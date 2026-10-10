using System;
using System.Drawing;
using System.Windows.Forms;

namespace PlaylistFlac
{
    internal sealed class QueueStatusChoice : Button
    {
        private static readonly string[] Choices={"All songs","Downloading","Waiting","Missing","Completed","Review"};
        private string selected="All songs";
        internal event EventHandler SelectedItemChanged;
        internal object SelectedItem
        {
            get {return selected;}
            set {string next=Convert.ToString(value);if(Array.IndexOf(Choices,next)<0)throw new ArgumentException("Unknown queue filter.");if(next==selected)return;selected=next;Text=selected+" ▾";if(SelectedItemChanged!=null)SelectedItemChanged(this,EventArgs.Empty);}
        }
        internal QueueStatusChoice()
        {
            Text=selected+" ▾";FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=1;AccessibleName="Filter songs by status";
            var menu=new ContextMenuStrip();
            foreach(var choice in Choices) {string value=choice;var item=menu.Items.Add(value);item.Click+=delegate {SelectedItem=value;};}
            Click+=delegate {menu.Show(this,new Point(0,Height));};Disposed+=delegate {menu.Dispose();};
        }
    }
}
