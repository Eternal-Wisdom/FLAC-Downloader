using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PlaylistFlac
{
    internal sealed class ActivityLogWindow : Form
    {
        private readonly TextBox source;
        private readonly TextBox display;
        private readonly CheckBox follow;
        private readonly TextBox search;
        private string snapshot="";
        private string[] snapshotLines;

        internal ActivityLogWindow(TextBox source,Color background,Color foreground)
        {
            this.source=source;
            Text="Activity log";Size=new Size(800,440);StartPosition=FormStartPosition.CenterParent;
            display=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,
                BackColor=background,ForeColor=foreground,BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",10)};
            Controls.Add(display);
            follow=new CheckBox {Text="Follow new messages",Checked=true,Dock=DockStyle.Bottom,Height=30,ForeColor=foreground,BackColor=background,AccessibleName="Follow new activity messages"};
            follow.CheckedChanged+=delegate {if(follow.Checked)RefreshLog(source,EventArgs.Empty);};Controls.Add(follow);
            var header=new Panel {Dock=DockStyle.Top,Height=38,BackColor=background};
            var label=new Label {Text="Find",Dock=DockStyle.Left,Width=44,TextAlign=ContentAlignment.MiddleLeft,ForeColor=foreground};
            search=new TextBox {Dock=DockStyle.Fill,BackColor=background,ForeColor=foreground,AccessibleName="Search activity messages"};
            header.Padding=new Padding(6);header.Controls.Add(search);header.Controls.Add(label);Controls.Add(header);
            search.TextChanged+=delegate {ApplySearch();};
            KeyPreview=true;KeyDown+=delegate(object sender,KeyEventArgs e) {
                if(e.Control && e.KeyCode==Keys.F){search.Focus();search.SelectAll();e.SuppressKeyPress=true;}
                else if(e.KeyCode==Keys.Escape && search.TextLength>0){search.Clear();e.SuppressKeyPress=true;}
            };
            source.TextChanged+=RefreshLog;
            RefreshLog(source,EventArgs.Empty);
        }

        private void RefreshLog(object sender,EventArgs e)
        {
            if(IsDisposed || Disposing || (follow!=null && !follow.Checked))return;
            snapshot=source.Text;snapshotLines=null;ApplySearch();
        }

        private void ApplySearch()
        {
            string query=search.Text.Trim();
            if(query.Length>0 && snapshotLines==null)snapshotLines=snapshot.Split(new[]{Environment.NewLine},StringSplitOptions.None);
            string next=query.Length==0 ? snapshot : String.Join(Environment.NewLine,snapshotLines.Where(line=>line.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0));
            // Unrelated messages must not repaint the filtered view or disturb selected text.
            if(String.Equals(display.Text,next,StringComparison.Ordinal))return;
            display.Text=next;
            display.SelectionStart=display.TextLength;
            display.ScrollToCaret();
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing)source.TextChanged-=RefreshLog;
            base.Dispose(disposing);
        }
    }
}
