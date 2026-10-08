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
        private sealed class Row {internal string Path;internal RetryEntry Entry;}
        internal RetryReview(string stateDirectory)
        {
            BackColor=System.Drawing.Color.FromArgb(17,21,26);ForeColor=System.Drawing.Color.White;Font=new System.Drawing.Font("Segoe UI",10);
            view.BackColor=System.Drawing.Color.FromArgb(25,31,38);view.ForeColor=ForeColor;view.BorderStyle=BorderStyle.None;
            Text="Saved retry lists — all collections";Width=980;Height=550;StartPosition=FormStartPosition.CenterParent;
            view.Columns.Add("Song",240);view.Columns.Add("Artist",170);view.Columns.Add("Next attempt",170);view.Columns.Add("Attempts",70);view.Columns.Add("Last reason",240);
            var controls=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=70};
            var pause=new Button {Text="Pause selected",AutoSize=true};var resume=new Button {Text="Resume in 15 minutes",AutoSize=true};var remove=new Button {Text="Remove selected",AutoSize=true};
            controls.Controls.AddRange(new Control[]{pause,resume,remove,new Label {Text="Automatic work still requires the selected collection and an armed, open app.",AutoSize=true}});
            controls.Padding=new Padding(12,10,12,8);controls.Height=100;
            foreach(Control c in controls.Controls) {var button=c as Button;if(button!=null){button.FlatStyle=FlatStyle.Flat;button.BackColor=System.Drawing.Color.FromArgb(33,41,50);button.ForeColor=ForeColor;button.Padding=new Padding(8,5,8,5);button.FlatAppearance.BorderColor=System.Drawing.Color.FromArgb(53,64,77);}}
            Controls.Add(view);Controls.Add(controls);
            pause.Click+=delegate {Change(0);};resume.Click+=delegate {Change(1);};remove.Click+=delegate {Change(2);};
            string directory=Path.Combine(stateDirectory,"retry-lists");
            if(!Directory.Exists(directory))return;
            foreach(string path in Directory.GetFiles(directory,"*.json").OrderBy(p=>p,StringComparer.Ordinal))
            {
                try
                {
                    foreach(var entry in Read(path))
                    {
                        var item=new ListViewItem(entry.Track.Title);item.SubItems.Add(entry.Track.Artist);item.SubItems.Add(entry.Paused?"Paused":entry.NextUtc.ToLocalTime().ToString("g"));item.SubItems.Add(entry.Attempts.ToString());item.SubItems.Add(entry.LastReason ?? "Unavailable; details not recorded");item.Tag=new Row {Path=path,Entry=entry};view.Items.Add(item);
                    }
                }
                catch(Exception ex) {if(!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException))throw;var item=new ListViewItem("Unreadable saved list");item.SubItems.Add("");item.SubItems.Add("Not changed");item.SubItems.Add("");item.SubItems.Add(ex.Message);view.Items.Add(item);}
            }
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
                    foreach(var item in group) {if(action==2)view.Items.Remove(item);else item.SubItems[2].Text=action==0?"Paused":DateTime.Now.AddMinutes(15).ToString("g");}
                }
            }
            catch(Exception ex) {MessageBox.Show(this,"Retry lists were not fully updated: "+ex.Message,"Saved retries");}
        }
    }
}
