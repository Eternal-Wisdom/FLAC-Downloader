using System;
using System.Drawing;
using System.Reflection;
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
            form.ShowInTaskbar=false;form.Opacity=0;form.Show();form.LoadPreview();
            var tracks=Field<ListView>(form,"tracks");var title=Field<Label>(form,"playlistLabel");
            var source=Field<TextBox>(form,"source");
            foreach(var size in new[]{new Size(1240,900),new Size(850,650),new Size(1000,720),new Size(1240,900)})
            {
                form.ClientSize=size;Application.DoEvents();
                if(tracks.Width!=tracks.Parent.ClientSize.Width || tracks.Width<=0 || title.Width<=0 || tracks.Items.Count==0)
                    throw new Exception("Queue layout did not recover after resize.");
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
                        source.Width!=Math.Max(430,source.Parent.ClientSize.Width)-242)
                        throw new Exception("Late section sizing left the track list or import controls collapsed.");
                }
                finally {container.ResumeLayout(false);}
            }
            form.WindowState=FormWindowState.Minimized;Application.DoEvents();
            form.WindowState=FormWindowState.Normal;Application.DoEvents();
            if(tracks.Width<=0 || tracks.Width!=tracks.Parent.ClientSize.Width || !tracks.Visible)
                throw new Exception("Track list did not recover after minimize and restore.");
        }
    }
}
