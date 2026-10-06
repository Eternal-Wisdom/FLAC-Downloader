using System;
using System.Collections.Generic;
using PlaylistFlac;

internal static class ProgressStateTests
{
    public static void Run()
    {
        var a=new Track{Title="花",Artist="Artist",Album="Original",DurationSeconds=100};
        var b=new Track{Title="花",Artist="Artist",Album="Live",DurationSeconds=100};
        var p=new Playlist{Tracks=new List<Track>{a,a,b}};
        var state=new PlaylistProgressState(p,new Dictionary<string,string>{{IndexStore.Key(a),"Downloaded"},{IndexStore.Key(b),"Failed"}});
        Check(state.Total==3 && state.Completed==2 && state.Failed==1 && state.Ready==0,"restored duplicates");
        Check(state.Set(IndexStore.Key(b),"Searching") && state.Failed==0 && state.Ready==1,"retry clears old failure");
        Check(!state.Set(IndexStore.Key(a),"Downloading"),"late progress cannot replace completed state");
        state.Set(IndexStore.Key(b),"Downloaded");
        Check(state.Completed==3 && state.Ready==0,"playlist totals do not reset between passes");
        Check(!state.Set("unknown","Failed"),"unknown event ignored");
        var review=new PlaylistProgressState(new Playlist{Tracks=new List<Track>{a,a,b}},new Dictionary<string,string>{{IndexStore.Key(a),"Review versions"}});
        Check(review.Total==3 && review.Review==2 && review.Ready==1 && review.Completed==0,"ambiguous versions stay visible without inflating download queue");
        review.Set(IndexStore.Key(b),"Downloaded");
        Check(review.Completed==1 && review.Review==2 && review.Ready==0,"completed tracks do not clear review entries");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception("Progress state: "+message);}
}
