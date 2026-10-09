using System;
using System.Drawing;
using System.Windows.Forms;

namespace PlaylistFlac
{
    internal sealed class ActivityLogWindow : Form
    {
        private readonly TextBox source;
        private readonly TextBox display;

        internal ActivityLogWindow(TextBox source,Color background,Color foreground)
        {
            this.source=source;
            Text="Activity log";Size=new Size(800,440);StartPosition=FormStartPosition.CenterParent;
            display=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,
                BackColor=background,ForeColor=foreground,BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",10)};
            Controls.Add(display);
            source.TextChanged+=RefreshLog;
            RefreshLog(source,EventArgs.Empty);
        }

        private void RefreshLog(object sender,EventArgs e)
        {
            if(IsDisposed || Disposing)return;
            display.Text=source.Text;
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
