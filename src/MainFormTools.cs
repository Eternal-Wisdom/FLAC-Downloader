using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PlaylistFlac
{
    internal sealed partial class MainForm
    {
        private void ShowImportMenu()
        {
            if(busy)return;
            var menu=UiStyle.Menu();
            menu.Items.Add("Import CSV file...",null,delegate {ImportCsv();});
            menu.Items.Add("Paste songs or CSV...",null,delegate {ShowTextImport();});
            menu.Items.Add("CSV format guide",null,delegate {OpenGuide("CSV-FORMAT.md");});
            menu.Closed+=delegate {BeginInvoke(new Action(menu.Dispose));};
            menu.Show(import,new Point(0,import.Height));
        }
        private void SaveImportedTracklist(Playlist list)
        {
            loadedCsv=ImportedTracklistCache.Save(Path.Combine(AppState.StateDirectory,"imports"),list);
        }
        private void ShowTextImport()
        {
            if(busy)return;
            Playlist imported=null;
            using(var window=new Form {Text="Paste songs",Size=new Size(660,500),MinimumSize=new Size(480,360),StartPosition=FormStartPosition.CenterParent,BackColor=Background,ForeColor=Color.White,Font=Font})
            {
                var hint=new Label {Text="One Artist - Title per line, or CSV with title and artist headers.\r\nNothing downloads until you choose Download FLAC.",Dock=DockStyle.Top,Height=54,Padding=new Padding(8)};
                var input=new TextBox {Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,BackColor=Surface,ForeColor=Color.White,MaxLength=2*1024*1024,AccessibleName="Pasted song list"};
                var footer=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(5)};
                var error=new Label {Dock=DockStyle.Bottom,Height=42,ForeColor=Color.Salmon,Padding=new Padding(8),AccessibleName="Import validation"};
                var add=new Button {Text="Import songs",AutoSize=true};var cancel=new Button {Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};
                add.Click+=delegate {
                    try {var parsed=PastedTracklist.Parse(input.Text);SaveImportedTracklist(parsed);imported=parsed;window.DialogResult=DialogResult.OK;window.Close();}
                    catch(Exception ex){if(!(ex is FormatException) && !(ex is IOException) && !(ex is UnauthorizedAccessException))throw;error.Text=ex.Message;}
                };
                footer.Controls.Add(cancel);footer.Controls.Add(add);window.Controls.Add(input);window.Controls.Add(error);window.Controls.Add(footer);window.Controls.Add(hint);window.CancelButton=cancel;
                if(window.ShowDialog(this)!=DialogResult.OK)return;
            }
            DisplayPlaylist(imported);SaveSettings();SetBusy(false);
        }
        private void CopyText(string text)
        {
            try {Clipboard.SetText(text);}
            catch(System.Runtime.InteropServices.ExternalException){ShowError("The clipboard is busy. Try copying again.");}
        }
        private void ShowDiagnostics()
        {
            string report=SafeDiagnostics.Create(allRows.Count,allRows.Count(r=>r.SubItems[4].Text=="Downloaded"),allRows.Count(r=>r.SubItems[4].Text=="Failed"),allRows.Count(r=>r.SubItems[4].Text=="Review versions"),File.Exists(EnginePath),SelectedParallelTracks,Convert.ToString(quality.SelectedItem),strict.Checked,busy);
            using(var window=new Form {Text="Safe diagnostics preview",Size=new Size(650,450),MinimumSize=new Size(460,320),StartPosition=FormStartPosition.CenterParent,BackColor=Background,ForeColor=Color.White,Font=Font})
            {
                var text=new TextBox {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Surface,ForeColor=Color.White,Text=report,AccessibleName="Privacy-safe diagnostics preview"};
                var footer=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=45,FlowDirection=FlowDirection.RightToLeft};
                var close=new Button {Text="Close",AutoSize=true,DialogResult=DialogResult.Cancel};var copy=new Button {Text="Copy diagnostics",AutoSize=true};copy.Click+=delegate {CopyText(report);};footer.Controls.Add(close);footer.Controls.Add(copy);
                var save=new Button {Text="Save diagnostics...",AutoSize=true,AccessibleName="Save privacy-safe diagnostics to a text file"};
                save.Click+=delegate {
                    using(var dialog=new SaveFileDialog {Title="Save privacy-safe diagnostics",Filter="Text files (*.txt)|*.txt",FileName="flac-downloader-diagnostics.txt",OverwritePrompt=true})
                        if(dialog.ShowDialog(window)==DialogResult.OK)try {IndexStore.AtomicWrite(dialog.FileName,report);}catch(Exception ex) {if(!(ex is IOException) && !(ex is UnauthorizedAccessException))throw;MessageBox.Show(window,"Diagnostics could not be saved. Choose a writable folder.","FLAC-Downloader",MessageBoxButtons.OK,MessageBoxIcon.Error);}
                };footer.Controls.Add(save);
                window.Controls.Add(text);window.Controls.Add(footer);window.CancelButton=close;window.ShowDialog(this);
            }
        }
        private void OpenGuide(string file)
        {
            string path=Path.Combine(AppState.Root,"docs",file);
            if(!File.Exists(path)){ShowError("This guide is missing. Extract the complete application folder again.");return;}
            try {Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
            catch(Exception ex){if(!(ex is System.ComponentModel.Win32Exception) && !(ex is InvalidOperationException))throw;ShowError("Windows could not open the guide. Read it from the application's docs folder.");}
        }
    }
}
