using System;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using PlaylistFlac;

internal static class QueueFilterTests
{
    internal static void Run()
    {
        var previous=SynchronizationContext.Current;
        try {using(var form=new MainForm(true))
        {
            form.ShowInTaskbar=false;form.Opacity=0;form.Show();form.LoadPreview();
            var list=Field<ListView>(form,"tracks");var search=Field<TextBox>(form,"queueSearch");var filter=Field<QueueStatusChoice>(form,"queueFilter");
            if(Convert.ToString(filter.SelectedItem)!="All songs")throw new Exception("The initial filter must have a visible All songs selection.");
            int total=list.Items.Count;
            search.Text="Open Water";if(list.Items.Count!=2)throw new Exception("Same-title artists were hidden incorrectly.");
            search.Text="nothing matches";if(list.Items.Count!=0)throw new Exception("Search did not filter.");
            typeof(MainForm).GetMethod("RestoreStatus",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{true});
            search.Clear();if(list.Items.Count!=total)throw new Exception("Hidden songs were lost during status restoration.");
            var hiddenTrack=(Track)list.Items[0].Tag;
            filter.SelectedItem="Completed";if(list.Items.Count!=0)throw new Exception("Ready songs shown as complete.");
            typeof(MainForm).GetMethod("OnProgress",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{new EngineProgress {TrackKey=IndexStore.Key(hiddenTrack),Type="download_complete",Status="Succeeded"}});
            var updateTimer=Stopwatch.StartNew();while(list.Items.Count==0 && updateTimer.ElapsedMilliseconds<2000){Application.DoEvents();Thread.Sleep(10);}
            if(list.Items.Count!=1 || ((Track)list.Items[0].Tag)!=hiddenTrack)throw new Exception("Live completion of a hidden song did not update the filter.");
            filter.SelectedItem="Waiting";if(list.Items.Count!=total-1)throw new Exception("Waiting filter lost ready songs.");
            if(!QueueFilter.Matches(new Track {Title="夜",Artist="例",Album="作品"},"Downloaded","作品","Completed"))throw new Exception("Unicode album search failed.");
            var large=new Playlist {Name="Synthetic library",Source="synthetic"};
            for(int i=0;i<5000;i++)large.Tracks.Add(new Track {Title="Synthetic "+i,Artist="Artist "+i,Album="Album",DurationSeconds=100});
            typeof(MainForm).GetMethod("DisplayPlaylist",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{large});
            filter.SelectedItem="All songs";var timer=Stopwatch.StartNew();
            for(int i=0;i<10;i++){search.Text="Synthetic 4999";if(list.Items.Count!=1)throw new Exception("Large list search failed.");search.Clear();}
            if(list.Items.Count!=5000 || timer.ElapsedMilliseconds>10000)throw new Exception("Large list filtering lost rows or exceeded ten seconds for twenty changes.");
            Console.WriteLine("Queue filter: 5,000 songs / 20 changes / "+timer.ElapsedMilliseconds+" ms");
            form.Close();
        }}finally {SynchronizationContext.SetSynchronizationContext(previous);}
    }
    private static T Field<T>(object form,string name){return (T)form.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);}
}
