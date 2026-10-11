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
            var sample=new Track {Title="夜",Artist="Example",Album=null};
            foreach(string query in new[]{"夜","example","夜\nExample","missing","  Example  ",""})
            {
                string prepared=query.Trim();bool expected=String.Join("\n",sample.Title,sample.Artist,sample.Album).IndexOf(prepared,StringComparison.OrdinalIgnoreCase)>=0;
                if(QueueFilter.MatchesPrepared(sample,"Ready",prepared,"All songs")!=expected)throw new Exception("Search semantics changed: "+query);
            }
            filter.SelectedItem="All songs";
            typeof(MainForm).GetMethod("OnProgress",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{new EngineProgress {TrackKey=IndexStore.Key(hiddenTrack),Status="Downloading"}});
            if(Field<bool>(form,"filterDirty"))throw new Exception("Unfiltered progress unnecessarily scheduled a queue scan.");
            var large=new Playlist {Name="Synthetic library",Source="synthetic"};
            for(int i=0;i<5000;i++)large.Tracks.Add(new Track {Title="Synthetic "+i,Artist="Artist "+i,Album="Album",DurationSeconds=100});
            typeof(MainForm).GetMethod("DisplayPlaylist",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{large});
            filter.SelectedItem="All songs";var timer=Stopwatch.StartNew();
            for(int i=0;i<10;i++){search.Text="Synthetic 4999";if(list.Items.Count!=1)throw new Exception("Large list search failed.");search.Clear();}
            if(list.Items.Count!=5000 || timer.ElapsedMilliseconds>10000)throw new Exception("Large list filtering lost rows or exceeded ten seconds for twenty changes.");
            Console.WriteLine("Queue filter: 5,000 songs / 20 changes / "+timer.ElapsedMilliseconds+" ms");
            int matches=0;var baseline=Stopwatch.StartNew();int collections=GC.CollectionCount(0);
            for(int pass=0;pass<100;pass++)foreach(var track in large.Tracks)if(String.Join("\n",track.Title,track.Artist,track.Album).IndexOf("Synthetic",StringComparison.OrdinalIgnoreCase)>=0)matches++;
            baseline.Stop();int oldCollections=GC.CollectionCount(0)-collections;
            var optimized=Stopwatch.StartNew();collections=GC.CollectionCount(0);int optimizedMatches=0;
            for(int pass=0;pass<100;pass++)foreach(var track in large.Tracks)if(QueueFilter.MatchesPrepared(track,"Ready","Synthetic","All songs"))optimizedMatches++;
            optimized.Stop();if(matches!=optimizedMatches)throw new Exception("Optimized search changed results.");
            Console.WriteLine("Search comparison: 500,000 checks; original "+baseline.ElapsedMilliseconds+" ms / "+oldCollections+" collections; optimized "+optimized.ElapsedMilliseconds+" ms / "+(GC.CollectionCount(0)-collections)+" collections.");
            form.Close();
        }}finally {SynchronizationContext.SetSynchronizationContext(previous);}
    }
    private static T Field<T>(object form,string name){return (T)form.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);}
}
