using System;
using System.Drawing;
using System.Reflection;
using System.Linq;
using System.Windows.Forms;
using PlaylistFlac;

internal static class WindowLayoutTests
{
    private static T Field<T>(Form form,string name) where T:Control
    { return (T)form.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form); }
    public static void Run()
    {
        using(var form=new MainForm(true))
        {
            if(!form.Text.EndsWith(typeof(MainForm).Assembly.GetName().Version.ToString(3)))throw new Exception("Window caption differs from assembly version.");
            form.ShowInTaskbar=false;form.Opacity=0;form.Show();form.LoadPreview();
            var tracks=Field<ListView>(form,"tracks");var title=Field<Label>(form,"playlistLabel");
            var source=Field<TextBox>(form,"source");
            var optional=Field<Panel>(form,"spotifySetup");var toggle=Field<Button>(form,"spotifySetupToggle");
            var library=Field<Button>(form,"libraryTools");
            var quality=Field<QualityChoice>(form,"quality");quality.SelectedItem="Balanced";
            if(Convert.ToString(quality.SelectedItem)!="Balanced" || !quality.Text.EndsWith(" ▾"))throw new Exception("The dropdown caption changed the saved quality profile.");
            foreach(string caption in new[]{"Check FLAC headers","Full audio check...","Preview and repair library...","Review saved retries...","Export unavailable songs as CSV..."})
                if(!library.ContextMenuStrip.Items.Cast<ToolStripItem>().Any(item=>item.Text==caption))throw new Exception("A library action is missing: "+caption);
            if(optional.Visible)throw new Exception("Spotify setup should not compete with CSV import by default.");
            toggle.PerformClick();if(!optional.Visible)throw new Exception("Optional Spotify setup cannot be expanded.");
            toggle.PerformClick();if(optional.Visible)throw new Exception("Optional Spotify setup cannot be collapsed.");
            foreach(var size in new[]{new Size(1240,900),new Size(850,650),new Size(1000,720),new Size(1240,900)})
            {
                form.ClientSize=size;Application.DoEvents();
                if(tracks.Width!=tracks.Parent.ClientSize.Width || tracks.Width<=0 || title.Width<=0 || tracks.Items.Count==0)
                    throw new Exception("Queue layout did not recover after resize.");
                foreach(string name in new[]{"start","stop","retry","libraryTools","browse","open"})
                {
                    var action=Field<Button>(form,name);
                    if(action.Left<0 || action.Right>action.Parent.ClientSize.Width || action.Bottom>action.Parent.ClientSize.Height)
                        throw new Exception("A core action is clipped at "+size+": "+name);
                    if(TextRenderer.MeasureText(action.Text,action.Font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Width>action.Width-42)
                        throw new Exception("A core action caption is truncated: "+name);
                }
                var settings=Field<Panel>(form,"settingsContent");var viewport=(Panel)settings.Parent;
                if(viewport.HorizontalScroll.Visible)throw new Exception("Account settings should scroll vertically, not sideways.");
            }
            // Reproduce a section receiving its final size after the container's
            // layout callback, without giving the container a second layout pass.
            foreach(Control section in new[]{tracks.Parent,source.Parent})
            {
                var container=section.Parent;int width=section.Width;
                container.SuspendLayout();
                try
                {
                    section.Width=0;section.Width=width;
                    if(tracks.Width!=tracks.Parent.ClientSize.Width || title.Width!=tracks.Parent.ClientSize.Width ||
                        source.Width!=Math.Max(430,source.Parent.ClientSize.Width)-254)
                        throw new Exception("Late section sizing left the track list or import controls collapsed.");
                }
                finally {container.ResumeLayout(false);}
            }
            form.WindowState=FormWindowState.Minimized;Application.DoEvents();
            form.WindowState=FormWindowState.Normal;Application.DoEvents();
            if(tracks.Width<=0 || tracks.Width!=tracks.Parent.ClientSize.Width || !tracks.Visible)
                throw new Exception("Track list did not recover after minimize and restore.");
            form.GetType().GetMethod("SetBusy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{true});
            if(library.Enabled)throw new Exception("Library tools remained active during a download operation.");
            form.GetType().GetMethod("SetBusy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{false});
            if(!library.Enabled)throw new Exception("Library tools did not recover after an operation.");
        }
        using(var empty=new MainForm(true))
        {
            empty.ShowInTaskbar=false;empty.Opacity=0;empty.ClientSize=new Size(850,650);empty.Show();Application.DoEvents();
            var prompt=Field<Panel>(empty,"emptyQueue");
            if(!prompt.Visible || !prompt.Controls.OfType<Button>().Any(b=>b.Visible && b.Bottom<=prompt.Height))
                throw new Exception("The empty queue must show a usable import action at minimum size.");
            if(Field<ListView>(empty,"tracks").Visible)throw new Exception("The empty-state prompt must not be covered by an empty list.");
        }
    }
}
