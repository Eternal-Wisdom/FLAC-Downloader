using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using PlaylistFlac;

internal static class RetryReviewTests
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"PlaylistFlacRetryReview-"+Guid.NewGuid().ToString("N"));string directory=Path.Combine(root,"retry-lists");Directory.CreateDirectory(directory);
        var song=new Track {Title="日本語",Artist="Demo artist",Album="Demo",DurationSeconds=180};
        string path=Path.Combine(directory,"demo.json");var serializer=new JavaScriptSerializer();
        File.WriteAllText(path,serializer.Serialize(new[]{new RetryEntry {Track=song,Key=IndexStore.Key(song),NextUtc=DateTime.UtcNow.AddMinutes(15)}}));
        string broken=Path.Combine(directory,"broken.json");File.WriteAllText(broken,"{broken");
        try
        {
            using(var form=new RetryReview(root))
            {
                IntPtr formHandle=form.Handle;var view=form.Controls.OfType<ListView>().Single();IntPtr viewHandle=view.Handle;
                Check(view.Items.Count==2,"valid and corrupt queues shown together");
                var row=view.Items.Cast<ListViewItem>().Single(i=>i.Text==song.Title);row.Selected=true;
                Check(view.SelectedItems.Count==1,"selection retained without starting work");
                var change=typeof(RetryReview).GetMethod("Change",BindingFlags.NonPublic|BindingFlags.Instance);
                change.Invoke(form,new object[]{0});Check(serializer.Deserialize<List<RetryEntry>>(File.ReadAllText(path))[0].Paused,"pause saved");
                change.Invoke(form,new object[]{1});var resumed=serializer.Deserialize<List<RetryEntry>>(File.ReadAllText(path))[0];Check(!resumed.Paused && resumed.NextUtc>DateTime.UtcNow.AddMinutes(14),"resume waits fifteen minutes");
                change.Invoke(form,new object[]{2});Check(serializer.Deserialize<List<RetryEntry>>(File.ReadAllText(path)).Count==0,"remove saved");
                Check(File.ReadAllText(broken)=="{broken","corrupt queue preserved");
            }
        }
        finally {Directory.Delete(root,true);}
    }
    private static void Check(bool value,string reason) {if(!value)throw new Exception("Retry review: "+reason);}
}
