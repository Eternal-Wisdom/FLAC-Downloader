using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PlaylistFlac
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--restore-artwork")
            {
                try { FlacArtwork.RestoreMetadata(args[1],CancellationToken.None); MessageBox.Show("Original artwork metadata restored.","FLAC-Downloader"); }
                catch(Exception ex) { Environment.ExitCode=1; MessageBox.Show("Artwork recovery stopped: "+ex.Message,"FLAC-Downloader"); }
                return;
            }
            if (args.Length > 0 && args[0] == "--self-test")
            {
                MessageBox.Show("Tests are run separately with scripts/Test.ps1 from the source package.", "FLAC-Downloader");
                Environment.ExitCode = 2; return;
            }
            bool rendering = args.Length > 1 && args[0] == "--render-preview";
            if (!rendering)
            {
                try { EnvironmentGuard.EnsureWritableState(AppState.StateDirectory); }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is UnauthorizedAccessException) && !(ex is System.Security.SecurityException)) throw;
                    MessageBox.Show("The app cannot save its settings here. Extract the entire app folder to a writable location, such as Downloads, and open it there.", "FLAC-Downloader", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.ExitCode = 1; return;
                }
            }
            using (var form = new MainForm(args.Length>1 && args[0]=="--render-preview"))
            {
                if (args.Length > 1 && args[0] == "--render-preview")
                {
                    if(args.Length==4)form.ClientSize=new Size(Int32.Parse(args[2]),Int32.Parse(args[3]));
                    form.Show(); form.LoadPreview(); Application.DoEvents();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(args[1]); }
                    form.Close(); return;
                }
                if(args.Contains("--resume")) form.Shown += async delegate { await form.ResumeDownload(); };
                Application.Run(form);
            }
        }

    }

    internal sealed class Preferences
    {
        public string Username { get; set; }
        public string EncryptedPassword { get; set; }
        public bool RememberPassword { get; set; }
        public string ClientId { get; set; }
        public string OutputDirectory { get; set; }
        public string LastCsv { get; set; }
        public bool StrictMatch { get; set; }
        public int ParallelTracks { get; set; }
        public string QualityProfile { get; set; }
        public bool AutoRetryMissing { get; set; }
        public Preferences() { StrictMatch = true; ParallelTracks = 20; AutoRetryMissing = true; }
    }

    internal static class AppState
    {
        public static string Root { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static string StateDirectory { get { return Path.Combine(Root, "state"); } }
        public static Preferences Load()
        {
            try { return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(StateDirectory, "settings.json"))) ?? new Preferences(); }
            catch { return new Preferences(); }
        }
        public static void Save(Preferences settings)
        {
            Directory.CreateDirectory(StateDirectory);
            string target = Path.Combine(StateDirectory, "settings.json");
            string temp = target + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(settings), new UTF8Encoding(false));
            if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
        }
        public static string Protect(string value)
        { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); }
        public static string Unprotect(string value)
        { string plain; return TryUnprotect(value, out plain) ? plain : ""; }
        internal static bool TryUnprotect(string value, out string plain)
        {
            plain = "";
            if (String.IsNullOrEmpty(value)) return true;
            try { plain = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); return true; }
            catch (FormatException) { return false; }
            catch (CryptographicException) { return false; }
        }
        public static string PlaylistFolder(string root, string name, string source)
        {
            var bad = new HashSet<char>(Path.GetInvalidFileNameChars());
            string clean = new string((name ?? "Playlist").Select(c => bad.Contains(c) || Char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
            if (clean.Length > 64) clean = clean.Substring(0, 64).Trim(' ', '.');
            if (clean.Length == 0) clean = "Playlist";
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source ?? name ?? "Playlist"))).Replace("-", "").Substring(0, 8).ToLowerInvariant();
            return Path.Combine(Path.GetFullPath(root), clean + " - " + hash);
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color Background = Color.FromArgb(17, 21, 26);
        private static readonly Color Surface = Color.FromArgb(25, 31, 38);
        private static readonly Color Inset = Color.FromArgb(33, 41, 50);
        private static readonly Color Muted = Color.FromArgb(157, 171, 187);
        private static readonly Color Accent = Color.FromArgb(136, 231, 185);
        private TextBox source, username, password, clientId, destination, activity;
        private Button load, import, connect, start, stop, browse, open, verify, retry, fix;
        private CheckBox remember, strict;
        private CheckBox autoRetry;
        private QualityChoice quality;
        private Button waiting;
        private Label transferStatus;
        private DateTime lastTransferEvent;
        private readonly Dictionary<string,string> failureReasons=new Dictionary<string,string>();
        private readonly Dictionary<string,TrackDetails> sourceDetails=new Dictionary<string,TrackDetails>();
        private string runProfile;
        private bool runStrict;
        private bool retriesArmed;
        private int waitingRetries;
        private readonly System.Windows.Forms.Timer retryTimer = new System.Windows.Forms.Timer { Interval=30000 };
        private RadioButton parallel8, parallel20, parallel32;
        private Label playlistLabel, countLabel, status, spotifyStatus, engineStatus;
        private ListView tracks;
        private ProgressBar progress;
        private readonly SpotifyImporter spotify = new SpotifyImporter();
        private SmartDownloader engine;
        private Playlist playlist;
        private RecordingGroups recordingGroups;
        private CancellationTokenSource operation;
        private bool busy, preview, closeRequested, retryReviewOpen;
        private string currentFolder, loadedCsv;
        private readonly List<string> logLines = new List<string>();
        private readonly object logGate = new object();
        private readonly Queue<string> pendingLogs = new Queue<string>();
        private readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer { Interval=200 };
        private readonly Dictionary<string,List<ListViewItem>> rowsByKey = new Dictionary<string,List<ListViewItem>>();
        private readonly Dictionary<string,List<ListViewItem>> rowsByQuery = new Dictionary<string,List<ListViewItem>>(StringComparer.OrdinalIgnoreCase);
        private PlaylistProgressState progressState;

        public MainForm(bool previewMode=false)
        {
            preview=previewMode;
            using(var stream=typeof(MainForm).Assembly.GetManifestResourceStream("PlaylistFlac.app.ico")) {if(stream!=null)Icon=new Icon(stream);}
            Text = "FLAC-Downloader 1.13"; BackColor = Background; ForeColor = Color.White;
            Font = new Font("Segoe UI", 10); ClientSize = new Size(1200, 838);
            MinimumSize = new Size(800, 600); StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Build(); FitWindow(); if(!previewMode)LoadSettings();
            autoRetry.CheckedChanged += delegate { retriesArmed=autoRetry.Checked; SaveSettings(); };
            retryTimer.Tick += async delegate { await RetryWaiting(); }; retryTimer.Start();
            uiTimer.Tick += delegate { FlushLog(); if(busy && lastTransferEvent!=DateTime.MinValue && (DateTime.UtcNow-lastTransferEvent).TotalSeconds>4) transferStatus.Text="Waiting for transfer progress"; }; uiTimer.Start();
            FormClosed += delegate { uiTimer.Dispose(); retryTimer.Dispose(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; closeRequested = true; if (operation != null) operation.Cancel(); status.Text = "Stopping before closing..."; return; }
                if (engine != null) engine.Dispose(); if (!preview) SaveSettings(); spotify.Dispose();
            };
        }

        private void FitWindow()
        {
            var original = Controls.Cast<Control>().ToArray();
            var side = original.OfType<Panel>().Single();
            var header = new Panel { Dock=DockStyle.Fill };
            var importPanel = new Panel { Dock=DockStyle.Fill };
            var queue = new Panel { Dock=DockStyle.Fill };
            var footer = new Panel { Dock=DockStyle.Fill };
            foreach(var c in original) {
                c.Anchor=AnchorStyles.Top|AnchorStyles.Left;
                if(c==side)continue;
                if(c.Top<140)header.Controls.Add(c);
                else if(c.Top<220)importPanel.Controls.Add(c);
                else if(c==tracks || c==playlistLabel || c==countLabel)queue.Controls.Add(c);
                else footer.Controls.Add(c);
            }
            var layout = new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(24,16,24,16),ColumnCount=2,RowCount=4,BackColor=Background};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,336));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,90));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,262));
            Controls.Add(layout);layout.Controls.Add(header,0,0);layout.SetColumnSpan(header,2);layout.Controls.Add(importPanel,0,1);layout.Controls.Add(queue,0,2);layout.Controls.Add(footer,0,3);
            var settingsViewport=new Panel {Dock=DockStyle.Fill,AutoScroll=true,BackColor=Surface,Margin=new Padding(20,8,0,0)};
            side.Anchor=AnchorStyles.Top|AnchorStyles.Left;side.Location=Point.Empty;side.Size=new Size(294,660);settingsViewport.Controls.Add(side);
            layout.Controls.Add(settingsViewport,1,1);layout.SetRowSpan(settingsViewport,3);
            foreach(Control c in side.Controls) {if(c.Width>270)c.Width=268;}
            parallel8.Left=137;parallel20.Left=187;parallel32.Left=237;parallel32.Width=50;
            connect.Width=158;
            var help=side.Controls.OfType<Button>().First(c=>c.Text=="Setup help");help.Left=190;help.Width=104;help.Text="Help";
            header.Controls.OfType<Label>().First(c=>c.Text=="PLAYLIST / FLAC").Text="FLAC-DOWNLOADER";
            var title=header.Controls.OfType<Label>().First(c=>c.Font.Size>20);title.Text="Your music, beautifully organized.";title.Font=new Font("Segoe UI",20,FontStyle.Bold);
            var subtitle=header.Controls.OfType<Label>().First(c=>c.Text.StartsWith("Import a tracklist"));subtitle.Text="Playlists and albums · Lossless audio · Artwork and smart filenames";
            var brand=header.Controls.OfType<Label>().First(c=>c.Text=="FLAC-DOWNLOADER");brand.SetBounds(0,0,160,20);
            title.SetBounds(0,20,800,36);subtitle.SetBounds(0,56,850,20);
            var badge=header.Controls.OfType<Label>().First(c=>c.Text.StartsWith("FLAC ONLY"));badge.Text="LOSSLESS / FLAC";
            var importLabel=importPanel.Controls.OfType<Label>().Single();importLabel.Text="ADD MUSIC   •   Paste a Spotify link or drop a CSV";
            var outputLabel=footer.Controls.OfType<Label>().First(c=>c.Text.StartsWith("SAVE PLAYLIST"));outputLabel.Text="SAVE MUSIC TO";
            var rows=new ImageList {ImageSize=new Size(1,34)};tracks.SmallImageList=rows;tracks.Disposed+=delegate {rows.Dispose();};
            Action arrange=delegate {
                int w=Math.Max(430,importPanel.ClientSize.Width);
                badge.SetBounds(Math.Max(0,header.Width-170),2,170,22);
                importLabel.SetBounds(0,2,w,22);source.SetBounds(0,31,w-242,30);load.Text="Load link";load.SetBounds(w-232,29,112,36);import.Text="CSV";import.SetBounds(w-112,29,112,36);
                playlistLabel.SetBounds(0,8,queue.Width,30);countLabel.SetBounds(0,42,queue.Width,28);tracks.SetBounds(0,76,queue.Width,Math.Max(60,queue.Height-84));
                int available=Math.Max(410,tracks.ClientSize.Width-20);tracks.Columns[0].Width=38;tracks.Columns[3].Width=52;tracks.Columns[4].Width=112;tracks.Columns[1].Width=(available-202)*55/100;tracks.Columns[2].Width=available-202-tracks.Columns[1].Width;
                outputLabel.SetBounds(0,0,w,22);destination.SetBounds(0,27,w-212,30);browse.Text="Select";browse.SetBounds(w-202,25,94,36);open.Text="Open";open.SetBounds(w-100,25,100,36);
                start.SetBounds(0,72,164,40);stop.SetBounds(172,72,76,40);retry.SetBounds(256,72,136,40);
                verify.SetBounds(0,120,124,34);fix.SetBounds(132,120,124,34);waiting.SetBounds(264,120,148,34);
                status.SetBounds(0,162,w,22);progress.SetBounds(0,190,w,4);transferStatus.SetBounds(0,200,w,21);
                autoRetry.SetBounds(0,228,w,28);autoRetry.Text="Automatically retry unavailable songs while open";
                activity.Visible=false;
            };
            var activityButton=ButtonAt(header,"Activity",0,0,100,false);activityButton.Click+=delegate {
                using(var dialog=new Form {Text="Activity log",Size=new Size(800,440),BackColor=Background,StartPosition=FormStartPosition.CenterParent}) {
                    var log=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Surface,ForeColor=Muted,BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",10),Text=activity.Text};dialog.Controls.Add(log);dialog.ShowDialog(this);
                }
            };
            bool arranging=false;
            Action arrangeCurrent=delegate {
                if(arranging)return;
                arranging=true;
                try {arrange();activityButton.SetBounds(Math.Max(0,header.Width-108),32,108,32);}
                finally {arranging=false;}
            };
            // TableLayoutPanel.Layout can run before its children's final sizes
            // are assigned (notably on minimize/restore). Reflow each section
            // when its actual size changes, so zero-width controls recover.
            layout.Layout+=delegate {arrangeCurrent();};
            foreach(var section in new Control[]{header,importPanel,queue,footer})
                section.ClientSizeChanged+=delegate {arrangeCurrent();};
            MinimumSize=new Size(850,650);
            var area=Screen.FromControl(this).WorkingArea;ClientSize=new Size(Math.Min(1240,area.Width-64),Math.Min(900,area.Height-80));arrange();

        }

        private Label LabelAt(Control parent, string text, int x, int y, int width, int height, float size, Color color, bool bold)
        {
            var c = new Label { Text = text, UseMnemonic = false, Location = new Point(x, y), Size = new Size(width, height), ForeColor = color, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), BackColor = Color.Transparent };
            parent.Controls.Add(c); return c;
        }
        private TextBox TextAt(Control parent, int x, int y, int width, bool secret)
        {
            var c = new TextBox { Location = new Point(x, y), Width = width, Height = 30, BorderStyle = BorderStyle.FixedSingle, BackColor = Inset, ForeColor = Color.White, Font = new Font("Segoe UI", 11), UseSystemPasswordChar = secret };
            parent.Controls.Add(c); return c;
        }
        private Button ButtonAt(Control parent, string text, int x, int y, int width, bool primary)
        {
            var c = new Button { Image=UiIcons.Create(text,primary ? Background : Accent), ImageAlign=ContentAlignment.MiddleLeft, TextImageRelation=TextImageRelation.ImageBeforeText, Padding=new Padding(5,0,5,0), Text = text, Location = new Point(x, y), Size = new Size(width, 35), FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Inset, ForeColor = primary ? Background : Color.White, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10, FontStyle.Bold), UseVisualStyleBackColor = false };
            c.FlatAppearance.BorderSize = 1; c.FlatAppearance.BorderColor=primary ? Accent : Color.FromArgb(53,64,77); c.FlatAppearance.MouseOverBackColor=primary ? Color.FromArgb(166,245,209) : Color.FromArgb(46,57,70); c.Disposed+=delegate {if(c.Image!=null)c.Image.Dispose();}; parent.Controls.Add(c); return c;
        }
        private void Build()
        {
            LabelAt(this, "PLAYLIST / FLAC", 28, 22, 300, 25, 10, Accent, true);
            LabelAt(this, "Playlists & albums. In FLAC.", 26, 52, 820, 48, 27, Color.White, true);
            LabelAt(this, "Import a tracklist. Find FLAC matches on Soulseek. Keep a playlist you can play anywhere.", 29, 104, 800, 25, 10, Muted, false);
            var tag = LabelAt(this, "FLAC ONLY  /  NO CONVERSION", 886, 32, 290, 25, 9, Accent, true); tag.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            LabelAt(this, "01  ADD A PLAYLIST OR ALBUM", 29, 151, 450, 25, 10, Muted, true);
            source = TextAt(this, 30, 183, 525, false); source.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            source.AccessibleName = "Spotify playlist or album URL";
            load = ButtonAt(this, "Load link", 565, 180, 105, false); load.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            import = ButtonAt(this, "Import CSV", 680, 180, 120, false); import.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            load.Click += async delegate { await LoadLink(); };
            import.Click += delegate { ImportCsv(); };
            source.KeyDown += async delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter && !busy) { e.SuppressKeyPress = true; await LoadLink(); } };

            playlistLabel = LabelAt(this, "Your queue starts here", 29, 237, 590, 30, 16, Color.White, true);
            playlistLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            countLabel = LabelAt(this, "Load a playlist or album link, or import a tracklist.", 30, 275, 770, 28, 10, Muted, false);
            tracks = new ListView { Location = new Point(30, 308), Size = new Size(770, 270), View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BackColor = Surface, ForeColor = Color.White, BorderStyle = BorderStyle.None, OwnerDraw = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = new Font("Segoe UI", 10), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            tracks.Columns.Add("#", 42); tracks.Columns.Add("TRACK", 275); tracks.Columns.Add("ARTIST", 220); tracks.Columns.Add("TIME", 65); tracks.Columns.Add("STATUS", 140);
            tracks.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e) { using (var brush = new SolidBrush(Inset)) using (var font = new Font("Segoe UI", 8, FontStyle.Bold)) { e.Graphics.FillRectangle(brush, e.Bounds); TextRenderer.DrawText(e.Graphics, e.Header.Text, font, e.Bounds, Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); } };
            tracks.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { if (tracks.View != View.Details) e.DrawDefault = true; };
            tracks.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e) { using (var b = new SolidBrush(e.Item.Selected ? Color.FromArgb(40,65,64) : (e.ItemIndex%2==0 ? Surface : Color.FromArgb(28,35,43)))) e.Graphics.FillRectangle(b, e.Bounds); var r = e.Bounds; r.X += 8; r.Width -= 8; TextRenderer.DrawText(e.Graphics, e.SubItem.Text, tracks.Font, r, e.ColumnIndex == 4 ? UiIcons.StatusColor(e.SubItem.Text) : (e.ColumnIndex==0 || e.ColumnIndex==3 ? Muted : Color.White), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); };
            Controls.Add(tracks);
            tracks.MultiSelect=true;
            var trackMenu=new ContextMenuStrip();
            var downloadSelected=trackMenu.Items.Add("Download selected songs");
            downloadSelected.Click+=async delegate {
                if(busy || playlist==null)return;
                var selected=new HashSet<string>(tracks.SelectedItems.Cast<ListViewItem>().Select(i=>IndexStore.Key((Track)i.Tag)));
                if(selected.Count>0)await Download(false,selected);
            };
            var copyTitle=trackMenu.Items.Add("Copy song and artist");
            copyTitle.Click+=delegate {if(tracks.SelectedItems.Count>0)Clipboard.SetText(String.Join(Environment.NewLine,tracks.SelectedItems.Cast<ListViewItem>().Select(i=>((Track)i.Tag).Title+" — "+((Track)i.Tag).Artist)));};
            trackMenu.Opening+=delegate {downloadSelected.Enabled=!busy && tracks.SelectedItems.Count>0;copyTitle.Enabled=tracks.SelectedItems.Count>0;};
            tracks.ContextMenuStrip=trackMenu;
            trackMenu.Items.Add(new ToolStripSeparator());
            var showFile=trackMenu.Items.Add("Show in folder");
            var recycleFile=trackMenu.Items.Add("Move to Recycle Bin...");
            showFile.Click+=delegate {SelectedFileAction(false);};
            recycleFile.Click+=delegate {SelectedFileAction(true);};
            trackMenu.Opening+=delegate {showFile.Enabled=recycleFile.Enabled=!busy && !preview && tracks.SelectedItems.Count==1;};
            tracks.MouseDown+=delegate(object sender,MouseEventArgs e) {
                if(e.Button!=MouseButtons.Right)return;
                var clicked=tracks.GetItemAt(e.X,e.Y);
                if(clicked==null || !clicked.Selected) {
                    foreach(var item in tracks.SelectedItems.Cast<ListViewItem>().ToList())item.Selected=false;
                    if(clicked!=null){clicked.Selected=true;clicked.Focused=true;}
                }
            };
            var detailsItem=trackMenu.Items.Add("Track and source details");
            detailsItem.Click+=delegate {ShowTrackDetails();};
            trackMenu.Opening+=delegate {detailsItem.Enabled=tracks.SelectedItems.Count==1;};
            tracks.DoubleClick+=delegate {ShowTrackDetails();};
            tracks.KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;ShowTrackDetails();}};
            AllowDrop=true;
            DragEnter+=delegate(object sender,DragEventArgs e) {if(!busy && (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText)))e.Effect=DragDropEffects.Copy;};
            DragDrop+=delegate(object sender,DragEventArgs e) {
                if(busy)return;
                try {
                    if(e.Data.GetDataPresent(DataFormats.FileDrop)) {
                        var files=(string[])e.Data.GetData(DataFormats.FileDrop);
                        if(files.Length!=1 || !String.Equals(Path.GetExtension(files[0]),".csv",StringComparison.OrdinalIgnoreCase)) {ShowError("Drop one CSV tracklist at a time.");return;}
                        var imported=CsvPlaylist.Read(files[0]);DisplayPlaylist(imported);loadedCsv=files[0];SaveSettings();
                    }
                    else if(e.Data.GetDataPresent(DataFormats.UnicodeText))source.Text=Convert.ToString(e.Data.GetData(DataFormats.UnicodeText)).Trim();
                } catch(Exception ex) {ShowError("The dropped tracklist could not be loaded: "+ex.Message);}
            };

            var side = new Panel { Location = new Point(828, 150), Size = new Size(344, 650), BackColor = Surface, Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
            Controls.Add(side);
            LabelAt(side, "02  CONNECT & CHOOSE", 22, 18, 300, 25, 10, Muted, true);
            LabelAt(side, "Soulseek account", 22, 56, 300, 28, 15, Color.White, true);
            var accountHelp = ButtonAt(side, "Soulseek account setup", 22, 91, 298, false);
            accountHelp.Click += delegate {
                MessageBox.Show(this,
                    "1. Get SoulseekQt from https://www.slsknet.org/news/node/1\r\n" +
                    "2. Open it and log in with a new username and a unique password. An unused name is registered on successful login.\r\n" +
                    "3. If the name is taken, choose another. Website/forum logins are separate.\r\n" +
                    "4. Disconnect SoulseekQt or Nicotine+ before using the same account here.\r\n" +
                    "5. Enter your credentials below, import a tracklist, and start when ready.\r\n\r\n" +
                    "The full guide is included in docs/SOULSEEK-ACCOUNT.md. Never post your password or private settings in a bug report.",
                    "Soulseek account setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            LabelAt(side, "Username", 22, 133, 295, 20, 9, Muted, false);
            username = TextAt(side, 22, 156, 298, false);
            LabelAt(side, "Password", 22, 193, 295, 20, 9, Muted, false);
            password = TextAt(side, 22, 216, 298, true);
            remember = new CheckBox { Text = "Remember password on this Windows account", Location = new Point(22, 255), Size = new Size(299, 41), Font = new Font("Segoe UI", 9), ForeColor = Muted };
            side.Controls.Add(remember);
            remember.CheckedChanged += delegate { if (!remember.Checked && !preview) SaveSettings(); };
            strict = new CheckBox { Text = "Require title + artist matches", Location = new Point(22, 303), Size = new Size(299, 28), Checked = true, ForeColor = Color.White, Font = new Font("Segoe UI", 10) }; side.Controls.Add(strict);
            LabelAt(side, "Tighter matching can leave more tracks missing.", 22, 333, 300, 21, 9, Muted, false);
            LabelAt(side, "Parallel tracks", 22, 361, 130, 23, 10, Color.White, true);
            parallel8 = new RadioButton { Text = "8", Location = new Point(159, 357), Size = new Size(47, 29), ForeColor = Color.White, AccessibleName = "8 parallel tracks" };
            parallel20 = new RadioButton { Text = "20", Location = new Point(209, 357), Size = new Size(54, 29), ForeColor = Color.White, Checked = true, AccessibleName = "20 parallel tracks (recommended)" };
            parallel32 = new RadioButton { Text = "32", Location = new Point(266, 357), Size = new Size(54, 29), ForeColor = Color.White, AccessibleName = "32 parallel tracks" };
            side.Controls.AddRange(new Control[] { parallel8, parallel20, parallel32 });
            LabelAt(side, "Quality / speed preference",22,392,300,20,9,Muted,false);
            quality=new QualityChoice {Location=new Point(22,417),Size=new Size(298,28),BackColor=Inset,ForeColor=Color.White};side.Controls.Add(quality);
            LabelAt(side, "Spotify link import", 22, 456, 300, 27, 15, Color.White, true);
            spotifyStatus = LabelAt(side, "Optional with CSV. Requires your Spotify app ID.", 22, 493, 300, 40, 9, Muted, false);
            clientId = TextAt(side, 22, 537, 298, false); clientId.AccessibleName = "Spotify developer client ID";
            connect = ButtonAt(side, "Connect Spotify", 22, 577, 166, false);
            var help = ButtonAt(side, "Setup help", 198, 577, 122, false);
            connect.Click += async delegate { await ConnectSpotify(); };
            help.Click += delegate { ShowHelp(); };
            engineStatus = LabelAt(side, File.Exists(EnginePath) ? "ENGINE READY  /  Sockseek 3.0.5" : "Download engine is missing. Restore engine folder.", 22, 624, 305, 24, 9, File.Exists(EnginePath) ? Accent : Color.Salmon, true);

            var outputLabel = LabelAt(this, "SAVE PLAYLIST FOLDERS TO", 30, 597, 770, 21, 9, Muted, true); outputLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            destination = TextAt(this, 30, 625, 540, false); destination.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            browse = ButtonAt(this, "Choose", 580, 622, 100, false); browse.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            open = ButtonAt(this, "Open folder", 690, 622, 110, false); open.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            browse.Click += delegate { using (var d = new FolderBrowserDialog { Description = "Choose where playlist folders should be saved", SelectedPath = destination.Text }) if (d.ShowDialog(this) == DialogResult.OK) { destination.Text = d.SelectedPath; SaveSettings(); RestoreStatus(); } };
            destination.Leave += delegate { if(!busy) RestoreStatus(); };
            open.Click += delegate { try { var path = currentFolder ?? Path.GetFullPath(destination.Text); if(currentFolder!=null && Directory.Exists(Path.Combine(currentFolder,"playlist")))path=Path.Combine(currentFolder,"playlist"); Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex.Message); } };
            start = ButtonAt(this, "Download FLAC", 30, 670, 150, true); start.Enabled = false; start.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            stop = ButtonAt(this, "Stop", 190, 670, 75, false); stop.Enabled = false; stop.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            retry = ButtonAt(this, "Retry missing", 275, 670, 110, false); retry.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; retry.Enabled = false;
            verify = ButtonAt(this, "Check files", 395, 670, 100, false); verify.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            fix = ButtonAt(this, "Fix library", 505, 670, 110, false); fix.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; fix.Enabled = false;
            start.Click += async delegate { await Download(false); };
            retry.Click += async delegate { await Download(true); };
            stop.Click += delegate { retriesArmed=false; if (operation != null) { status.Text = "Stopping..."; operation.Cancel(); stop.Enabled = false; } };
            verify.Click += async delegate { await CheckFiles(); };
            fix.Click += async delegate { await FixLibrary(); };
            status = LabelAt(this, "Ready to import", 625, 670, 175, 44, 9, Accent, false); status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            progress = new ProgressBar { Location = new Point(30, 718), Size = new Size(770, 5), Style = ProgressBarStyle.Continuous, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right }; Controls.Add(progress);
            activity = new TextBox { Location = new Point(30, 735), Size = new Size(770, 62), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Background, ForeColor = Muted, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 9), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right }; Controls.Add(activity);
            waiting=ButtonAt(this,"Saved retries",630,805,170,false);waiting.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            waiting.Click+=delegate {
                retryReviewOpen=true;retryTimer.Stop();
                try {using(var window=new RetryReview(AppState.StateDirectory))window.ShowDialog(this); if(playlist!=null && currentFolder!=null){waitingRetries=new RetryList(AppState.StateDirectory,currentFolder,playlist).Count;UpdateSummary();}}
                catch(Exception ex) {ShowError("Saved retries could not be read: "+ex.Message);}
                finally {retryReviewOpen=false;retryTimer.Start();}
            };
            transferStatus=LabelAt(this,"Transfer speed appears when bytes arrive",30,727,770,22,9,Muted,false);transferStatus.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
            activity.Location=new Point(30,751);activity.Height=46;
            autoRetry=new CheckBox {Text="Retry unavailable songs automatically while open",Location=new Point(30,805),Size=new Size(590,28),ForeColor=Muted,Anchor=AnchorStyles.Bottom|AnchorStyles.Left};Controls.Add(autoRetry);
            Log("Ready. Playlist metadata supplies the search list; audio comes from Soulseek peers.");
            Log("Fix library adds missing covers, consolidates duplicates and distinguishes same-title songs by artist.");
        }
        private string EnginePath { get { return Path.Combine(AppState.Root, "engine", "sockseek.exe"); } }
        private string preservedPassword;
        private void LoadSettings()
        {
            var s = AppState.Load(); username.Text = s.Username ?? ""; clientId.Text = s.ClientId ?? "";
            autoRetry.Checked=s.AutoRetryMissing; quality.SelectedItem=DownloadTuning.NormalizeQuality(s.QualityProfile);
            destination.Text = String.IsNullOrWhiteSpace(s.OutputDirectory) ? Path.Combine(AppState.Root, "Music") : s.OutputDirectory;
            remember.Checked = s.RememberPassword;
            string savedPassword;
            if (s.RememberPassword && !AppState.TryUnprotect(s.EncryptedPassword, out savedPassword))
            {
                password.Text = "";
                preservedPassword = s.EncryptedPassword;
                Log("The saved password cannot be opened on this Windows account. Enter your Soulseek password again, or turn off Remember password.");
            }
            else password.Text = s.RememberPassword ? AppState.Unprotect(s.EncryptedPassword) : "";
            strict.Checked = s.StrictMatch;
            int parallel = DownloadTuning.NormalizeParallelTracks(s.ParallelTracks); parallel8.Checked = parallel == 8; parallel20.Checked = parallel == 20; parallel32.Checked = parallel == 32;
            loadedCsv = s.LastCsv;
            if (!String.IsNullOrEmpty(loadedCsv) && File.Exists(loadedCsv))
                try { DisplayPlaylist(CsvPlaylist.Read(loadedCsv)); } catch (Exception ex) { Log("The previous tracklist could not be loaded: " + ex.Message); }
        }
        private void SaveSettings()
        {
            if(preview)return;
            if(!String.IsNullOrEmpty(password.Text))preservedPassword=null;
            try { AppState.Save(new Preferences { Username = username.Text.Trim(), ClientId = SpotifyImporter.ClientIdForStorage(clientId.Text), OutputDirectory = destination.Text.Trim(), LastCsv = loadedCsv, StrictMatch = strict.Checked, ParallelTracks = SelectedParallelTracks, AutoRetryMissing=autoRetry.Checked, QualityProfile=Convert.ToString(quality.SelectedItem), RememberPassword = remember.Checked, EncryptedPassword = remember.Checked ? (String.IsNullOrEmpty(password.Text) && preservedPassword != null ? preservedPassword : AppState.Protect(password.Text)) : null }); }
            catch (Exception ex) { Log("Settings could not be saved: " + ex.Message); }
        }
        private void SetBusy(bool value)
        {
            busy = value;
            foreach (Control c in new Control[] { load, import, connect, browse, verify, source, username, password, clientId, destination, strict, remember, parallel8, parallel20, parallel32, quality, waiting }) c.Enabled = !value;
            start.Enabled = !value && playlist != null && playlist.Tracks.Count > 0 && File.Exists(EnginePath);
            retry.Enabled = start.Enabled;
            fix.Enabled = !value && playlist != null && playlist.Tracks.Count > 0;
            stop.Enabled = value; progress.Style = ProgressBarStyle.Continuous;
            if (!value) { if (operation != null) operation.Dispose(); operation = null; if (closeRequested && !IsDisposed) BeginInvoke(new Action(Close)); }
        }
        private void Log(string message)
        {
            if (IsDisposed || Disposing) return;
            if (String.IsNullOrWhiteSpace(message)) return;
            lock(logGate) { pendingLogs.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + message); while(pendingLogs.Count>160)pendingLogs.Dequeue(); }
        }
        private void FlushLog()
        {
            lock(logGate) { if(pendingLogs.Count==0)return; while(pendingLogs.Count>0)logLines.Add(pendingLogs.Dequeue()); }
            if (logLines.Count > 160) logLines.RemoveRange(0, logLines.Count - 160);
            activity.Text = String.Join(Environment.NewLine, logLines); activity.SelectionStart = activity.TextLength; activity.ScrollToCaret();
        }
        private void ShowError(string message) { Log(message); status.Text = "Needs attention"; MessageBox.Show(this, message, "FLAC-Downloader", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        private void ShowHelp()
        {
            using (var d = new Form { Text = "Connect Spotify", Size = new Size(665, 545), StartPosition = FormStartPosition.CenterParent, BackColor = Background, ForeColor = Color.White, Font = Font, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog })
            {
                LabelAt(d, "One-time Spotify setup", 26, 23, 580, 35, 20, Color.White, true);
                LabelAt(d, "1. Open the Spotify developer dashboard and create an app.\n\n2. Enable Web API and add this redirect URI exactly:\n\n\n3. Paste its Client ID into the main window, then Connect Spotify.\n   Sign in and approve playlist access in your browser.\n\nDevelopment apps require the app owner to have Premium. Spotify\ncurrently limits playlist contents to playlists you own or collaborate on.\n\nFor other playlists, import a CSV tracklist with artist and title columns.\nThe app uses Spotify only for metadata, never for audio.", 28, 80, 600, 326, 10, Muted, false);
                var redirect = TextAt(d, 30, 163, 555, false); redirect.Text = "http://127.0.0.1:48723/callback"; redirect.ReadOnly = true;
                var dashboard = ButtonAt(d, "Open developer dashboard", 28, 419, 270, true); dashboard.Click += delegate { Process.Start(new ProcessStartInfo("https://developer.spotify.com/dashboard") { UseShellExecute = true }); };
                var docs = ButtonAt(d, "Read guide", 310, 419, 120, false); docs.Click += delegate { OpenReadme(); };
                var close = ButtonAt(d, "Close", 444, 419, 140, false); close.Click += delegate { d.Close(); }; d.ShowDialog(this);
            }
        }
        private void OpenReadme() { string path = Path.Combine(AppState.Root, "START HERE.txt"); if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        private async Task ConnectSpotify()
        {
            if (busy) return;
            if (String.IsNullOrWhiteSpace(clientId.Text)) { ShowHelp(); return; }
            operation = new CancellationTokenSource(TimeSpan.FromMinutes(4)); SetBusy(true); status.Text = "Waiting for Spotify sign-in...";
            try { await spotify.ConnectAsync(clientId.Text.Trim(), operation.Token); spotifyStatus.Text = "Connected for this session. Playlist and album links are ready."; spotifyStatus.ForeColor = Accent; Log("Spotify connected. Tokens remain in memory for this session."); status.Text = "Spotify connected"; SaveSettings(); }
            catch (OperationCanceledException) { status.Text = "Sign-in cancelled"; Log("Spotify sign-in cancelled or timed out. You can try again."); }
            catch (Exception ex) { ShowError(ex.Message); }
            finally { SetBusy(false); }
        }
        private async Task LoadLink()
        {
            if (busy) return;
            if (String.IsNullOrWhiteSpace(source.Text)) { ShowError("Paste a Spotify playlist or album link first, or choose Import CSV."); return; }
            if (!spotify.IsConnected) { await ConnectSpotify(); if (!spotify.IsConnected) return; }
            operation = new CancellationTokenSource(); SetBusy(true); status.Text = "Reading tracklist...";
            try { var result = await spotify.LoadAsync(source.Text.Trim(), operation.Token); DisplayPlaylist(result); loadedCsv = null; SaveSettings(); }
            catch (OperationCanceledException) { status.Text = "Import cancelled"; }
            catch (Exception ex) { ShowError(ex.Message + "\r\n\r\nYou can also import this tracklist as a CSV."); }
            finally { SetBusy(false); }
        }
        private void ImportCsv()
        {
            using (var dialog = new OpenFileDialog { Filter = "CSV tracklists (*.csv)|*.csv", Title = "Import a playlist tracklist" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { DisplayPlaylist(CsvPlaylist.Read(dialog.FileName)); loadedCsv = dialog.FileName; SaveSettings(); SetBusy(false); }
                catch (Exception ex) { ShowError(ex.Message); }
            }
        }
        private void DisplayPlaylist(Playlist p)
        {
            sourceDetails.Clear();runProfile=null;
            retriesArmed=false; waitingRetries=0;
            playlist = p; recordingGroups=RecordingGroups.Build(p.Tracks); currentFolder = null; playlistLabel.Text = p.Name; tracks.BeginUpdate(); tracks.Items.Clear(); rowsByKey.Clear();rowsByQuery.Clear();
            int i = 0;
            foreach (var group in recordingGroups.Groups)
            {
                Track t=group.Representative;
                var row = new ListViewItem((++i).ToString("00")); row.SubItems.Add(t.Title); row.SubItems.Add(t.Artist);
                int seconds = (int)Math.Max(0, t.DurationSeconds); row.SubItems.Add(seconds > 0 ? (seconds / 60).ToString() + ":" + (seconds % 60).ToString("00") : "--"); row.SubItems.Add("Ready"); row.Tag = t; tracks.Items.Add(row);
                foreach(string key in group.Tracks.Select(IndexStore.Key).Distinct()) AddRow(rowsByKey,key,row);
                AddRow(rowsByQuery,QueryKey(t.Artist,t.Title),row);
            }
            tracks.EndUpdate(); countLabel.Text = p.Tracks.Count + " tracks  /  FLAC only" + (p.SkippedTracks > 0 ? "  /  " + p.SkippedTracks + " unavailable or incomplete entries skipped" : "");
            progress.Value = 0; status.Text = "Ready to download"; start.Enabled = !busy && p.Tracks.Count > 0 && File.Exists(EnginePath); retry.Enabled = start.Enabled;
            fix.Enabled = !busy && p.Tracks.Count > 0;
            RestoreStatus(true);
            if(!preview && currentFolder!=null)
            {
                try {waitingRetries=new RetryList(AppState.StateDirectory,currentFolder,p).Count;UpdateSummary();}
                catch(Exception ex){Log("The saved retry list could not be loaded: "+ex.Message);}
            }
            Log("Loaded " + p.Tracks.Count + " tracks from " + p.Name + ".");
        }
        private async Task Download(bool retryOnly, ISet<string> selectedKeys=null)
        {
            if (busy || playlist == null) return;
            if (String.IsNullOrWhiteSpace(username.Text) || String.IsNullOrEmpty(password.Text)) { ShowError("Enter your Soulseek username and password in the right panel. If Nicotine+ uses this account, disconnect it first or use a different account here."); return; }
            if (String.IsNullOrWhiteSpace(destination.Text)) { ShowError("Choose a download folder first."); return; }
            if(selectedKeys==null)retriesArmed=autoRetry.Checked;
            operation = new CancellationTokenSource(); failureReasons.Clear(); sourceDetails.Clear(); runProfile=Convert.ToString(quality.SelectedItem);runStrict=strict.Checked; lastTransferEvent=DateTime.MinValue; transferStatus.Text="Searching / waiting for peers"; SetBusy(true); SaveSettings();
            try
            {
                currentFolder = AppState.PlaylistFolder(destination.Text.Trim(), playlist.Name, playlist.Source);
                engine = new SmartDownloader(); engine.Log += Log; engine.Progress += OnProgress;
                status.Text = retryOnly ? "Retrying missing tracks..." : "Connecting to Soulseek...";
                Log("One download per matching recording, including album reissues. Filenames use song titles; the artist is added only when titles clash.");
                Log("Profile: "+quality.SelectedItem+". Fast FLAC accepts an early matching source; Balanced compares results without preferring 24-bit. Higher resolution remains a soft preference.");
                Log("Up to " + SelectedParallelTracks + " tracks can search, queue, or download at once. Completed-search ranking, FLAC and duration checks stay active.");
                int exit = await engine.RunAsync(EnginePath, playlist, currentFolder, username.Text.Trim(), password.Text, strict.Checked, retryOnly, operation.Token, selectedKeys, new DownloadTuning(SelectedParallelTracks,Convert.ToString(quality.SelectedItem)),Path.Combine(AppState.StateDirectory,"covers"),Path.Combine(AppState.StateDirectory,"catalog"));
                if (operation.IsCancellationRequested || exit == 130) { retriesArmed=false; status.Text = "Stopped; run again to retry"; Log("Stopped. Completed files stay in the playlist folder."); return; }
                if (exit == 2) { retriesArmed=false; ShowError("The download engine could not start this job. Check the activity log for its error."); return; }
                if (exit != 0 && exit != 1) { retriesArmed=false; ShowError("The download engine exited unexpectedly (code " + exit + "). Check the activity log."); return; }
                var savedRetries=new RetryList(AppState.StateDirectory,currentFolder,playlist);
                savedRetries.Update(playlist,LibraryStatus.Read(currentFolder,playlist,false).Statuses,selectedKeys ?? new HashSet<string>(playlist.Tracks.Select(IndexStore.Key)),DateTime.UtcNow,failureReasons);
                waitingRetries=savedRetries.Count;
                if(waitingRetries>0)Log(waitingRetries+" unavailable entries saved to the retry list. Automatic checks begin after 15 minutes and back off to six hours; Stop pauses them.");
                status.Text = "Checking FLAC headers...";
                var audit = await Task.Run(() => FlacAudit.CheckFolder(currentFolder, operation.Token));
                Log(audit.ValidHeaders + " FLAC headers checked; " + audit.InvalidHeaders + " unreadable or invalid files. Report: FLAC-check.csv. This does not verify original source quality.");
                status.Text = exit == 0 ? "Download run finished" : "Run ended; some tracks are missing or need review";
                if (audit.InvalidHeaders > 0) status.Text = "Check files: " + audit.InvalidHeaders + " need review";
                if (exit == 1) Log("Some tracks are missing or need review. Retry missing tracks when more peers are online. Entries marked Review versions have conflicting identities and are kept separate.");
            }
            catch (OperationCanceledException) { retriesArmed=false; status.Text = "Stopped; run again to retry"; Log("Cancelled. Completed files are kept."); }
            catch (Exception ex) { retriesArmed=false; ShowError(ex.Message); }
            finally
            {
                if (engine != null) { engine.Dispose(); engine = null; }
                if(!preview && currentFolder!=null && progressState!=null)
                {
                    try
                    {
                        var pending=new RetryList(AppState.StateDirectory,currentFolder,playlist);
                        var states=playlist.Tracks.Select(IndexStore.Key).Distinct().ToDictionary(k=>k,k=>progressState.Status(k));
                        pending.Update(playlist,states,null,DateTime.UtcNow,failureReasons);waitingRetries=pending.Count;
                    }
                    catch(Exception ex){retriesArmed=false;Log("Retry list could not be saved: "+ex.Message);}
                }
                if (!IsDisposed && !Disposing) { transferStatus.Text="Transfers stopped"; SetBusy(false); RestoreStatus(); FlushLog(); }
            }
        }
        private async Task RetryWaiting()
        {
            if(busy || preview || closeRequested || retryReviewOpen || !retriesArmed || !autoRetry.Checked || playlist==null)return;
            if(String.IsNullOrWhiteSpace(username.Text) || String.IsNullOrEmpty(password.Text) || !File.Exists(EnginePath))return;
            try
            {
                string folder=AppState.PlaylistFolder(destination.Text.Trim(),playlist.Name,playlist.Source);
                var list=new RetryList(AppState.StateDirectory,folder,playlist);
                var due=list.Due(DateTime.UtcNow,8);
                if(due.Count==0)return;
                Log("Checking "+due.Count+" waiting songs for newly available FLAC peers.");
                await Download(true,due);
            }
            catch(Exception ex){retriesArmed=false;Log("Automatic retry paused: "+ex.Message);}
        }
        private void OnProgress(EngineProgress update)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<EngineProgress>(OnProgress), update); } catch (InvalidOperationException) { } return; }
            if(update.Type=="download_progress") {
                lastTransferEvent=DateTime.UtcNow;
                transferStatus.Text=TransferTelemetry.FormatRate(update.TotalBytesPerSecond)+" observed / "+update.MovingTransfers+" transfers with recent byte progress";
            }
            List<ListViewItem> affected=null;
            if(!String.IsNullOrEmpty(update.TrackKey)) rowsByKey.TryGetValue(update.TrackKey,out affected);
            if(affected==null && !String.IsNullOrEmpty(update.Title)) rowsByQuery.TryGetValue(QueryKey(update.Artist,update.Title),out affected);
            if(affected!=null && progressState!=null)
            {
                string next=update.Status ?? "";
                if(next=="Succeeded" || next=="1")next="Downloaded";
                else if(next.StartsWith("Failed",StringComparison.Ordinal) || next.StartsWith("PartialSuccess",StringComparison.Ordinal) || next=="2" || next=="5")next="Failed";
                else if(next=="None" || next=="0" || next.Length==0) return;
                foreach (ListViewItem row in affected)
                {
                    string key=IndexStore.Key((Track)row.Tag);
                    TrackDetails details;if(!sourceDetails.TryGetValue(key,out details)){details=new TrackDetails();sourceDetails.Add(key,details);}details.Observe(update);
                    if(next=="Failed")failureReasons[key]=update.Status; progressState.Set(key,next); row.SubItems[4].Text=progressState.Status(key);
                }
                UpdateSummary();
                status.Text=next=="Failed" ? "A track is unavailable; continuing" : next=="Downloaded" ? "Downloaded "+progressState.Completed+" of "+progressState.Total : next;
            }
        }
        private static string QueryKey(string artist,string title) { artist=artist ?? "";return artist.Length+":"+artist+(title ?? ""); }
        private void ShowTrackDetails()
        {
            if(tracks.SelectedItems.Count!=1)return;
            var row=tracks.SelectedItems[0];var track=(Track)row.Tag;
            TrackDetails details;if(!sourceDetails.TryGetValue(IndexStore.Key(track),out details))details=new TrackDetails();
            using(var window=new Form {Text="Track and source details",StartPosition=FormStartPosition.CenterParent,Size=new Size(650,540),MinimumSize=new Size(440,340),BackColor=Background,ForeColor=Color.White})
            using(var text=new TextBox {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Surface,ForeColor=Color.White,Font=new Font("Segoe UI",10),BorderStyle=BorderStyle.None,Text=details.Describe(track,row.SubItems[4].Text,runProfile ?? Convert.ToString(quality.SelectedItem),runProfile==null ? strict.Checked : runStrict)})
            {text.AccessibleName="Track, selected source, and matching policy";window.Controls.Add(text);window.ShowDialog(this);}
        }
        private static void AddRow(Dictionary<string,List<ListViewItem>> map,string key,ListViewItem row)
        { List<ListViewItem> group;if(!map.TryGetValue(key,out group)){group=new List<ListViewItem>();map.Add(key,group);}group.Add(row); }
        private void SelectedFileAction(bool remove)
        {
            if(busy || preview || playlist==null || tracks.SelectedItems.Count!=1)return;
            try
            {
                var track=(Track)tracks.SelectedItems[0].Tag;
                string folder=AppState.PlaylistFolder(destination.Text.Trim(),playlist.Name,playlist.Source);
                string file=TrackFileActions.Resolve(folder,playlist,track);
                if(!remove) {Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+file+"\"") {UseShellExecute=true});return;}
                if(MessageBox.Show(this,"Move this file to the Recycle Bin?\r\n\r\n"+file+"\r\n\r\nRepeated playlist entries may share this file. Automatic retries will pause. The song stays in your tracklist and can be downloaded again.","Remove downloaded file",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                retriesArmed=false;
                TrackFileActions.Recycle(folder,playlist,track,file);
                status.Text="File removed; automatic retries paused";
            }
            catch(OperationCanceledException) {status.Text="File removal cancelled";}
            catch(Exception ex) {ShowError(ex.Message);}
            finally {RestoreStatus();}
        }
        private void RestoreStatus(bool force=false)
        {
            if(playlist==null || (busy && !force))return;
            IDictionary<string,string> saved=null;
            try {
                currentFolder=AppState.PlaylistFolder(destination.Text.Trim(),playlist.Name,playlist.Source);
                if(!preview) { var snapshot=LibraryStatus.Read(currentFolder,playlist);saved=snapshot.Statuses.ToDictionary(x=>x.Key,x=>x.Value); }
            } catch(Exception ex) { if(ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is FormatException) Log("Saved status could not be read: "+ex.Message);else throw; }
            var displayPlaylist=new Playlist {Tracks=recordingGroups.Groups.Select(g=>g.Representative).ToList()};
            progressState=new PlaylistProgressState(displayPlaylist,saved);
            tracks.BeginUpdate();foreach(ListViewItem row in tracks.Items)row.SubItems[4].Text=progressState.Status(IndexStore.Key((Track)row.Tag));tracks.EndUpdate();
            UpdateSummary();
        }
        private void UpdateSummary()
        {
            if(progressState==null)return;
            countLabel.Text=progressState.Total+" songs  /  "+(playlist.Tracks.Count-progressState.Total)+" repeats skipped  /  "+progressState.Completed+" downloaded  /  "+progressState.Failed+" missing  /  "+progressState.Ready+" left";
            if(progressState.Review>0)countLabel.Text+="  /  "+progressState.Review+" need review";
            if(playlist.SkippedTracks>0)countLabel.Text+="  /  "+playlist.SkippedTracks+" skipped";
            if(waitingRetries>0)countLabel.Text+="  /  "+waitingRetries+" waiting to retry";
            progress.Style=ProgressBarStyle.Continuous;progress.Maximum=Math.Max(1,progressState.Total);progress.Value=Math.Min(progress.Maximum,progressState.Completed);
        }
        internal Task ResumeDownload() { return Download(false); }
        private int SelectedParallelTracks { get { return parallel8.Checked ? 8 : parallel32.Checked ? 32 : 20; } }
        private async Task FixLibrary()
        {
            if(busy || playlist==null) return;
            operation=new CancellationTokenSource();SetBusy(true);
            try
            {
                currentFolder=AppState.PlaylistFolder(destination.Text.Trim(),playlist.Name,playlist.Source);
                engine=new SmartDownloader();engine.Log+=Log;
                status.Text="Fixing covers and filenames...";
                Log("Fixing saved music without starting Soulseek downloads. Extra duplicates and original artwork metadata remain recoverable.");
                var result=await Task.Run(()=>engine.FixLibraryAsync(playlist,currentFolder,Path.Combine(AppState.StateDirectory,"covers"),operation.Token));
                Log("Library updated: "+result.Duplicates.ArchivedFiles+" duplicate copies moved to recovery; "+result.Names.Renamed+" filenames updated; "+result.Covers.Added+" covers added.");
                if(result.Duplicates.BackupDirectory!=null)Log("Duplicate recovery folder: "+result.Duplicates.BackupDirectory);
                status.Text="Library fixes finished";
            }
            catch(OperationCanceledException) {status.Text="Library fixes stopped";Log("Stopped. Finished changes and their recovery information are kept.");}
            catch(Exception ex) {ShowError("Library fixes stopped: "+ex.Message);}
            finally
            {
                if(engine!=null){engine.Dispose();engine=null;}
                if(!IsDisposed && !Disposing){SetBusy(false);RestoreStatus();FlushLog();}
            }
        }
        private async Task CheckFiles()
        {
            if (busy) return;
            string folder = currentFolder;
            if (String.IsNullOrEmpty(folder)) using (var dialog = new FolderBrowserDialog { Description = "Choose a folder to check for FLAC headers", SelectedPath = destination.Text }) { if (dialog.ShowDialog(this) != DialogResult.OK) return; folder = dialog.SelectedPath; }
            operation = new CancellationTokenSource(); SetBusy(true); status.Text = "Checking FLAC headers...";
            try { var result = await Task.Run(() => FlacAudit.CheckFolder(folder, operation.Token)); currentFolder = folder; Log(result.ValidHeaders + " valid FLAC headers, " + result.InvalidHeaders + " files need review. Report saved to " + result.ReportPath); status.Text = result.TotalFiles + " files checked"; }
            catch (OperationCanceledException) { status.Text = "Check cancelled"; }
            catch (Exception ex) { ShowError(ex.Message); }
            finally { SetBusy(false); }
        }
        public void LoadPreview()
        {
            preview = true;destination.Text=@"C:\Music";
            username.Text="";password.Text="";clientId.Text="";remember.Checked=false;
            var p = new Playlist { Name = "A playlist, ready to find", Source = "preview", Tracks = new List<Track>() };
            p.Tracks.Add(new Track { Title = "Evening Light", Artist = "Example Artist", Album = "Demo only", DurationSeconds = 243 });
            p.Tracks.Add(new Track { Title = "Evening Light", Artist = "Example Artist", Album = "Demo deluxe", DurationSeconds = 243 });
            p.Tracks.Add(new Track { Title = "Open Water", Artist = "Another Artist", Album = "Demo only", DurationSeconds = 188 });
            p.Tracks.Add(new Track { Title = "Open Water", Artist = "Different Artist", Album = "Another recording", DurationSeconds = 204 });
            p.Tracks.Add(new Track { Title = "Slow Motion", Artist = "Example Artist", Album = "Demo only", DurationSeconds = 276 });
            DisplayPlaylist(p);status.Text="Illustrative preview; no music downloaded";
            FlushLog();
        }
    }
}
