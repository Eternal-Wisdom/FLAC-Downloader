using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PlaylistFlac
{
    internal sealed class RetryReview : Form
    {
        private readonly ListView view=new ListView {Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=true};
        private readonly List<ListViewItem> allRows=new List<ListViewItem>();
        private readonly TextBox search=new TextBox {Left=12,Top=9,Width=650,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};
        private readonly Button filter=new Button {Text="All retries ▾",Top=7,Width=160,Height=30,Anchor=AnchorStyles.Top|AnchorStyles.Right};
        private readonly Label counts=new Label {Left=12,Top=40,AutoSize=true};
        private string selectedFilter="All retries";
        private Button pause,resume,remove;
        private sealed class Row {internal string Path;internal RetryEntry Entry;}
        internal RetryReview(string stateDirectory)
        {
            BackColor=System.Drawing.Color.FromArgb(17,21,26);ForeColor=System.Drawing.Color.White;Font=new System.Drawing.Font("Segoe UI",10);
            view.BackColor=System.Drawing.Color.FromArgb(25,31,38);view.ForeColor=ForeColor;view.BorderStyle=BorderStyle.None;
            Text="Saved retry lists — all collections";Width=980;Height=550;StartPosition=FormStartPosition.CenterParent;
            view.Columns.Add("Song",240);view.Columns.Add("Artist",170);view.Columns.Add("Next attempt",170);view.Columns.Add("Attempts",70);view.Columns.Add("Last reason",240);
            var controls=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=70};
            pause=new Button {Text="Pause selected",AutoSize=true};resume=new Button {Text="Resume in 15 minutes",AutoSize=true};remove=new Button {Text="Remove selected",AutoSize=true};
            controls.Controls.AddRange(new Control[]{pause,resume,remove,new Label {Text="Automatic work still requires the selected collection and an armed, open app.",AutoSize=true}});
            controls.Padding=new Padding(12,10,12,8);controls.Height=100;
            foreach(Control c in controls.Controls) {var button=c as Button;if(button!=null){button.FlatStyle=FlatStyle.Flat;button.BackColor=System.Drawing.Color.FromArgb(33,41,50);button.ForeColor=ForeColor;button.Padding=new Padding(8,5,8,5);button.FlatAppearance.BorderColor=System.Drawing.Color.FromArgb(53,64,77);}}
            var header=new Panel {Dock=DockStyle.Top,Height=65,Width=ClientSize.Width};header.Controls.AddRange(new Control[]{search,filter,counts});
            search.Anchor=filter.Anchor=AnchorStyles.Top|AnchorStyles.Left;
            Action arrange=delegate{search.Width=Math.Max(100,header.ClientSize.Width-200);filter.Left=header.ClientSize.Width-176;};header.Resize+=delegate{arrange();};arrange();
            search.BackColor=view.BackColor;search.ForeColor=ForeColor;search.BorderStyle=BorderStyle.FixedSingle;search.AccessibleName="Find saved retries";UiStyle.Hint(search,"Find song, artist, or failure reason · Ctrl+F");
            filter.FlatStyle=FlatStyle.Flat;filter.BackColor=System.Drawing.Color.FromArgb(33,41,50);filter.ForeColor=ForeColor;
            var menu=UiStyle.Menu();foreach(string name in new[]{"All retries","Scheduled","Paused","Unreadable"}){string choice=name;var item=new ToolStripMenuItem(name);menu.Items.Add(item);item.Click+=delegate{SetRetryFilter(choice);};}
            menu.Opening+=delegate{foreach(ToolStripMenuItem item in menu.Items)item.Checked=item.Text==selectedFilter;};filter.ContextMenuStrip=menu;filter.Click+=delegate{menu.Show(filter,new System.Drawing.Point(0,filter.Height));};Disposed+=delegate{menu.Dispose();};
            search.TextChanged+=delegate{ApplyFilter();};view.SelectedIndexChanged+=delegate{UpdateActions();};
            KeyPreview=true;KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Control && e.KeyCode==Keys.F){search.Focus();search.SelectAll();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Escape && search.Focused){search.Clear();e.SuppressKeyPress=true;}};
            Controls.Add(view);Controls.Add(header);Controls.Add(controls);MinimumSize=new System.Drawing.Size(650,400);
            pause.Click+=delegate {Change(0);};resume.Click+=delegate {Change(1);};remove.Click+=delegate {Change(2);};
            string directory=Path.Combine(stateDirectory,"retry-lists");
            if(!Directory.Exists(directory)){ApplyFilter();return;}
            foreach(string path in Directory.GetFiles(directory,"*.json").OrderBy(p=>p,StringComparer.Ordinal))
            {
                try
                {
                    foreach(var entry in Read(path))
                    {
                        var item=new ListViewItem(entry.Track.Title);item.SubItems.Add(entry.Track.Artist);item.SubItems.Add(entry.Paused?"Paused":entry.NextUtc.ToLocalTime().ToString("g"));item.SubItems.Add(entry.Attempts.ToString());item.SubItems.Add(FailureExplanation.Describe(entry.LastReason));item.Tag=new Row {Path=path,Entry=entry};allRows.Add(item);
                    }
                }
                catch(Exception ex) {if(!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException))throw;var item=new ListViewItem("Unreadable saved list");item.SubItems.Add("");item.SubItems.Add("Not changed");item.SubItems.Add("");item.SubItems.Add(ex.Message);allRows.Add(item);}
            }
            ApplyFilter();
        }
        private void SetRetryFilter(string value){selectedFilter=value;filter.Text=value+" ▾";ApplyFilter();}
        private void UpdateActions(){bool selected=view.SelectedItems.Cast<ListViewItem>().Any(i=>i.Tag is Row);pause.Enabled=resume.Enabled=remove.Enabled=selected;}
        private void ApplyFilter()
        {
            string query=(search.Text ?? "").Trim();var selected=new HashSet<ListViewItem>(view.SelectedItems.Cast<ListViewItem>());
            var visible=allRows.Where(i=> {
                var row=i.Tag as Row;
                bool match=selectedFilter=="All retries" || (selectedFilter=="Unreadable"?row==null:row!=null && row.Entry.Paused==(selectedFilter=="Paused"));
                return match && (query.Length==0 || String.Join("\n",i.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x=>x.Text)).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);
            }).ToArray();
            view.BeginUpdate();try{view.Items.Clear();view.Items.AddRange(visible);foreach(var item in visible)item.Selected=selected.Contains(item);}finally{view.EndUpdate();}
            counts.Text=visible.Length+" of "+allRows.Count+" saved entries · Filtering only changes this view";UpdateActions();
        }
        private static List<RetryEntry> Read(string path) {return RetryList.ReadValidated(path);}
        private void Change(int action)
        {
            try
            {
                var selected=view.SelectedItems.Cast<ListViewItem>().Where(i=>i.Tag is Row).ToList();
                foreach(var group in selected.GroupBy(i=>((Row)i.Tag).Path))
                {
                    var entries=Read(group.Key);var keys=new HashSet<string>(group.Select(i=>((Row)i.Tag).Entry.Key));
                    if(action==2)entries.RemoveAll(e=>e!=null && keys.Contains(e.Key));
                    else foreach(var entry in entries.Where(e=>e!=null && keys.Contains(e.Key))) {entry.Paused=action==0;if(action==1)entry.NextUtc=DateTime.UtcNow.AddMinutes(15);}
                    string temp=group.Key+"."+Guid.NewGuid().ToString("N")+".tmp";
                    try {File.WriteAllText(temp,new JavaScriptSerializer {MaxJsonLength=32*1024*1024}.Serialize(entries),new UTF8Encoding(false));File.Replace(temp,group.Key,null);}
                    finally {if(File.Exists(temp))File.Delete(temp);}
                    foreach(var item in group) {if(action==2)allRows.Remove(item);else {var saved=entries.Single(e=>e.Key==((Row)item.Tag).Entry.Key);((Row)item.Tag).Entry=saved;item.SubItems[2].Text=saved.Paused?"Paused":saved.NextUtc.ToLocalTime().ToString("g");}}
                }
            }
            catch(Exception ex) {MessageBox.Show(this,"Retry lists were not fully updated: "+ex.Message,"Saved retries");}
            ApplyFilter();
        }
    }
}
