using System;
using System.Drawing;
using System.Windows.Forms;

namespace PlaylistFlac
{
    internal sealed class QualityChoice : Button
    {
        private string selected="Prefer high resolution";
        internal object SelectedItem {get {return selected;}set {selected=DownloadTuning.NormalizeQuality(Convert.ToString(value));Text=selected+" ▾";AccessibleDescription="Selected profile: "+selected;}}
        internal QualityChoice()
        {
            SelectedItem=selected;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=1;
            AccessibleName="Quality and speed preference";
            var choices=UiStyle.Menu();
            foreach(string profile in new[]{"Fast FLAC","Balanced","Prefer high resolution"}) {
                string choice=profile;var item=new ToolStripMenuItem(profile);choices.Items.Add(item);item.Click+=delegate {SelectedItem=choice;};
                item.ToolTipText=profile=="Fast FLAC" ? "Prefer faster peers while keeping FLAC-only matching." : profile=="Balanced" ? "Balance advertised quality and peer speed." : "Prefer higher bit depth and sample rate; availability and speed vary.";
            }
            choices.Opening+=delegate {foreach(ToolStripMenuItem item in choices.Items)item.Checked=item.Text==selected;};
            ContextMenuStrip=choices;
            Click+=delegate {choices.Show(this,new Point(0,Height));};
            Disposed+=delegate {choices.Dispose();};
        }
    }
}
