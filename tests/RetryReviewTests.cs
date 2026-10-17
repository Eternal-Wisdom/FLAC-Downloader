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
        string damaged=Path.Combine(directory,"damaged.json");
        string damagedJson=serializer.Serialize(new object[]{new RetryEntry {Track=song,Key=IndexStore.Key(song),NextUtc=DateTime.UtcNow.AddMinutes(15)},null});
        File.WriteAllText(damaged,damagedJson);
        try
        {
            using(var form=new RetryReview(root))
            {
                IntPtr formHandle=form.Handle;var view=form.Controls.OfType<ListView>().Single();IntPtr viewHandle=view.Handle;
                Check(view.Items.Count==3,"valid, syntactically corrupt, and structurally damaged queues shown together");
                Check(view.Items.Cast<ListViewItem>().Count(i=>i.Text=="Unreadable saved list")==2,"the whole damaged queue is blocked instead of exposing a partial editable list");
                var search=(TextBox)typeof(RetryReview).GetField("search",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                var filterButton=(Button)typeof(RetryReview).GetField("filter",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                foreach(int width in new[]{980,650,980}){form.ClientSize=new System.Drawing.Size(width,400);Application.DoEvents();Check(filterButton.Left>=0 && filterButton.Right<=filterButton.Parent.ClientSize.Width && search.Right<filterButton.Left,"retry filter remains visible after resize");}
                var filter=typeof(RetryReview).GetMethod("SetRetryFilter",BindingFlags.NonPublic|BindingFlags.Instance);
                search.Text="日本語";Check(view.Items.Count==1,"Unicode search limits visible retries");
                search.Text="no match";Check(view.Items.Count==0,"empty search preserves underlying entries");search.Clear();
                filter.Invoke(form,new object[]{"Unreadable"});Check(view.Items.Count==2,"unreadable filter excludes editable entries");
                filter.Invoke(form,new object[]{"Scheduled"});Check(view.Items.Count==1,"scheduled filter excludes corrupt lists");
                filter.Invoke(form,new object[]{"All retries"});Check(view.Items.Count==3,"filter reset restores all entries");
                var row=view.Items.Cast<ListViewItem>().Single(i=>i.Text==song.Title);row.Selected=true;
                Check(view.SelectedItems.Count==1,"selection retained without starting work");
                var change=typeof(RetryReview).GetMethod("Change",BindingFlags.NonPublic|BindingFlags.Instance);
                change.Invoke(form,new object[]{0});Check(serializer.Deserialize<List<RetryEntry>>(File.ReadAllText(path))[0].Paused,"pause saved");
                filter.Invoke(form,new object[]{"Paused"});Check(view.Items.Count==1,"pause moves entry to paused view");
                change.Invoke(form,new object[]{1});Check(view.Items.Count==0,"resume leaves paused filter");
                filter.Invoke(form,new object[]{"All retries"});row.Selected=true;
                var resumed=serializer.Deserialize<List<RetryEntry>>(File.ReadAllText(path))[0];Check(!resumed.Paused && resumed.NextUtc>DateTime.UtcNow.AddMinutes(14),"resume waits fifteen minutes");
                change.Invoke(form,new object[]{2});Check(serializer.Deserialize<List<RetryEntry>>(File.ReadAllText(path)).Count==0,"remove saved");
                Check(File.ReadAllText(broken)=="{broken","corrupt queue preserved");
                Check(File.ReadAllText(damaged)==damagedJson,"structurally damaged queue preserved while another queue is edited");
            }
        }
        finally {Directory.Delete(root,true);}
    }
    private static void Check(bool value,string reason) {if(!value)throw new Exception("Retry review: "+reason);}
}
