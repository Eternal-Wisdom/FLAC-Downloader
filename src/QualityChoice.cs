using System;
using System.Drawing;
using System.Windows.Forms;

namespace PlaylistFlac
{
    internal sealed class QualityChoice : Button
    {
        internal object SelectedItem {get {return Text;}set {Text=DownloadTuning.NormalizeQuality(Convert.ToString(value));}}
        internal QualityChoice()
        {
            Text="Prefer high resolution";FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
            AccessibleName="Quality and speed preference";
            var choices=new ContextMenuStrip();
            foreach(string profile in new[]{"Fast FLAC","Balanced","Prefer high resolution"}) {
                string selected=profile;var item=choices.Items.Add(profile);item.Click+=delegate {SelectedItem=selected;};
            }
            Click+=delegate {choices.Show(this,new Point(0,Height));};
            Disposed+=delegate {choices.Dispose();};
        }
    }
}
