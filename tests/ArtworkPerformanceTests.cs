using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PlaylistFlac;

internal static class ArtworkPerformanceTests
{
    internal static string Result;
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"PlaylistFlacArtworkPerformanceTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {RunAsync(root).GetAwaiter().GetResult();}
        finally {Directory.Delete(root,true);}
    }
    private static async Task RunAsync(string root)
    {
        byte[] image=ArtworkTestData.Image();
        var one=new Scenario(Path.Combine(root,"sequential"),8,image,200);
        var four=new Scenario(Path.Combine(root,"parallel"),8,image,200);
        var clock=Stopwatch.StartNew();
        var sequential=await ArtworkLibrary.FillAsync(one.Folder,one.Playlist,one.Index,one.Find,CancellationToken.None,null,1);
        long oldMs=clock.ElapsedMilliseconds;clock.Restart();
        var parallel=await ArtworkLibrary.FillAsync(four.Folder,four.Playlist,four.Index,four.Find,CancellationToken.None,null,4);
        long newMs=clock.ElapsedMilliseconds;
        Check(sequential.Added==8 && parallel.Added==8 && one.Peak==1 && four.Peak==4,"The bounded queue reaches four lookups without exceeding its limit.");
        for(int i=0;i<8;i++)Check(File.ReadAllBytes(Path.Combine(one.Folder,i+".flac")).SequenceEqual(File.ReadAllBytes(Path.Combine(four.Folder,i+".flac"))),"Both modes produce exactly the same cover metadata and audio bytes.");
        Result="8 missing-cover fixtures, 200 ms simulated network latency per lookup: sequential "+oldMs+" ms; four-lookahead "+newMs+" ms; "+((double)oldMs/Math.Max(1,newMs)).ToString("0.00",CultureInfo.InvariantCulture)+"x faster. Peak lookups: "+four.Peak+". This is an offline benchmark, not a real-network speed guarantee.";

        // The first lookup cannot finish until every later lookup has started.
        // A FIFO window stalls at four and fails; completion-order processing
        // keeps replenishing its bounded window and completes all eight.
        var staggered=new Scenario(Path.Combine(root,"staggered"),8,image,10);
        var releaseFirst=new TaskCompletionSource<bool>();int started=0;
        using(var deadline=new CancellationTokenSource(5000))
        {
            Func<Track,CancellationToken,Task<CoverLookupResult>> lookup=async (track,ct)=>
            {
                if(Interlocked.Increment(ref started)==8)releaseFirst.TrySetResult(true);
                if(track.Title=="Song 0")using(ct.Register(()=>releaseFirst.TrySetCanceled()))await releaseFirst.Task.ConfigureAwait(false);
                return await staggered.Find(track,ct).ConfigureAwait(false);
            };
            var result=await ArtworkLibrary.FillAsync(staggered.Folder,staggered.Playlist,staggered.Index,lookup,deadline.Token,null,4);
            Check(started==8 && result.Added==8,"A blocked first lookup cannot stop later lookups from filling available slots.");
            Check(staggered.Active==0 && staggered.Peak<=4,"Completion-order processing stays bounded and drains all work.");
            for(int i=0;i<8;i++)Check(File.ReadAllBytes(Path.Combine(staggered.Folder,i+".flac")).SequenceEqual(File.ReadAllBytes(Path.Combine(one.Folder,i+".flac"))),"Completion order leaves the resulting files identical.");
        }

        var cancelled=new Scenario(Path.Combine(root,"cancelled"),8,image,Timeout.Infinite);
        using(var stop=new CancellationTokenSource())
        {
            var run=ArtworkLibrary.FillAsync(cancelled.Folder,cancelled.Playlist,cancelled.Index,cancelled.Find,stop.Token,null,4);
            var deadline=Stopwatch.StartNew();
            while(Volatile.Read(ref cancelled.Started)<4 && deadline.ElapsedMilliseconds<3000)await Task.Delay(10);
            Check(Volatile.Read(ref cancelled.Started)==4,"Cancellation fixture fills the network window.");
            stop.Cancel();bool ended=false;try{await run;}catch(OperationCanceledException){ended=true;}
            Check(ended && Volatile.Read(ref cancelled.Active)==0,"Stop drains every pending lookup before the library operation returns.");
            for(int i=0;i<8;i++)Check(File.ReadAllBytes(Path.Combine(cancelled.Folder,i+".flac")).SequenceEqual(ArtworkTestData.Flac((byte)i)),"Cancelled lookahead cannot modify a song before its cover is ready.");
        }
        var failure=new Scenario(Path.Combine(root,"failure"),8,image,Timeout.Infinite);
        Func<Track,CancellationToken,Task<CoverLookupResult>> failing=(track,ct)=>track.Title=="Song 3"?Fault():failure.Find(track,ct);
        bool rejected=false;try{await ArtworkLibrary.FillAsync(failure.Folder,failure.Playlist,failure.Index,failing,CancellationToken.None,null,4);}catch(IOException){rejected=true;}
        Check(rejected && failure.Active==0,"A failed worker cancels and drains other outstanding requests.");
        Check(!Directory.GetFiles(failure.Folder,"*",SearchOption.AllDirectories).Any(p=>p.EndsWith(".flac-metadata")),"Failure before the first cover commits no song or artwork recovery metadata.");
        var lateFailure=new Scenario(Path.Combine(root,"late-failure"),8,image,Timeout.Infinite);
        using(var stop=new CancellationTokenSource(5000))
        {
            Func<Track,CancellationToken,Task<CoverLookupResult>> lateFailing=async (track,ct)=>
            {
                if(track.Title=="Song 3"){await Task.Delay(20,ct);throw new IOException("Delayed lookup failure");}
                return await lateFailure.Find(track,ct);
            };
            rejected=false;try{await ArtworkLibrary.FillAsync(lateFailure.Folder,lateFailure.Playlist,lateFailure.Index,lateFailing,stop.Token,null,4);}catch(IOException){rejected=true;}
            Check(rejected && !stop.IsCancellationRequested && lateFailure.Active==0,"A later asynchronous failure interrupts a blocked first lookup and drains other requests.");
        }
    }
    private static Task<CoverLookupResult> Fault(){var task=new TaskCompletionSource<CoverLookupResult>();task.SetException(new IOException("Simulated lookup failure"));return task.Task;}
    private sealed class Scenario
    {
        internal string Folder;
        internal Playlist Playlist=new Playlist();
        internal Dictionary<string,SavedTrack> Index=new Dictionary<string,SavedTrack>();
        internal int Active,Peak,Started;
        private byte[] image;private int latency;
        internal Scenario(string folder,int count,byte[] image,int latency)
        {
            Folder=folder;this.image=image;this.latency=latency;Directory.CreateDirectory(folder);
            for(int i=0;i<count;i++)
            {
                var track=new Track{Title="Song "+i,Artist="Artist",Album="Album "+i,DurationSeconds=100,CoverUrl="https://i.scdn.co/image/"+i};Playlist.Tracks.Add(track);
                string path=Path.Combine(folder,i+".flac");File.WriteAllBytes(path,ArtworkTestData.Flac((byte)i));Index[IndexStore.Key(track)]=new SavedTrack{Track=track,Path=path,State=1};
            }
        }
        internal async Task<CoverLookupResult> Find(Track track,CancellationToken ct)
        {
            int active=Interlocked.Increment(ref Active);Interlocked.Increment(ref Started);
            int old;do{old=Volatile.Read(ref Peak);if(old>=active)break;}while(Interlocked.CompareExchange(ref Peak,active,old)!=old);
            try{await Task.Delay(latency,ct).ConfigureAwait(false);return new CoverLookupResult{Bytes=image,SourceUrl=track.CoverUrl};}
            finally{Interlocked.Decrement(ref Active);}
        }
    }
    private static void Check(bool value,string message){if(!value)throw new Exception("Artwork pipeline: "+message);}
}
