using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PlaylistFlac;

internal static class AliasLookupTests
{
    private static int checks;
    private static readonly Func<CancellationToken,Task> NoPacing = ct => { ct.ThrowIfCancellationRequested(); return Task.FromResult(0); };

    public static void Run()
    {
        checks=0;
        string root=Path.Combine(Path.GetTempPath(),"PlaylistFlacAliasTests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { RunAsync(root).GetAwaiter().GetResult(); }
        finally
        {
            string absolute=Path.GetFullPath(root), temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!absolute.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("PlaylistFlacAliasTests-",StringComparison.Ordinal)) throw new Exception("Unsafe alias test cleanup path.");
            Directory.Delete(absolute,true);
        }
        Console.WriteLine("Alias lookup: "+checks+" checks passed.");
    }

#if ALIAS_TEST_RUNNER
    public static void Main() { Run(); }
#endif

    private static async Task RunAsync(string root)
    {
        await CacheTests(Path.Combine(root,"cache"));
        await InvalidCacheTests(Path.Combine(root,"invalid"));
        await NegativeTests(Path.Combine(root,"negative"));
        await IdentityTests(Path.Combine(root,"identity"));
        await BreakerTests(Path.Combine(root,"breaker"));
        await CancellationTests(Path.Combine(root,"cancel"));
        await ConcurrentTests(Path.Combine(root,"concurrent"));
    }

    private static async Task CacheTests(string folder)
    {
        Directory.CreateDirectory(folder);
        Track track=Original(1);
        string path=Path.Combine(folder,track.Isrc+".json");
        File.WriteAllText(path,Response(track.Isrc));
        var handler=new FixtureHandler();
        using(var lookup=new AliasLookup(folder,handler,NoPacing))
        {
            Check((await lookup.FindAsync(track,CancellationToken.None)).Count>0,"valid disk cache yields aliases");
            Check(handler.Requests==0,"disk cache skips HTTP");
            File.Delete(path);
            Check((await lookup.FindAsync(track,CancellationToken.None)).Count>0,"memory cache survives missing disk file");
            Check(handler.Requests==0,"memory cache skips repeated HTTP");
            Track longer=Original(1); longer.DurationSeconds=250;
            Check((await lookup.FindAsync(longer,CancellationToken.None)).Count==0,"raw cache rechecks duration for each original");
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();
                await ExpectCancellation(()=>lookup.FindAsync(track,cancellation.Token),"cached lookup preserves cancellation");
            }
        }
    }

    private static async Task InvalidCacheTests(string folder)
    {
        Directory.CreateDirectory(folder);
        string[] invalid={"{broken-json", "{\"recordings\":[]}", Response(Original(99).Isrc), "{\"isrc\":\"USAAA0000001\",\"recordings\":null}"};
        for(int i=0;i<invalid.Length;i++)
        {
            string child=Path.Combine(folder,i.ToString()); Directory.CreateDirectory(child);
            Track track=Original(1); string path=Path.Combine(child,track.Isrc+".json");
            File.WriteAllText(path,invalid[i]);
            var handler=new FixtureHandler(); handler.Enqueue(HttpStatusCode.OK,Response(track.Isrc));
            using(var lookup=new AliasLookup(child,handler,NoPacing))
            {
                Check((await lookup.FindAsync(track,CancellationToken.None)).Count>0,"invalid disk cache fetched again "+i);
                Check(handler.Requests==1,"invalid disk cache causes exactly one request "+i);
                Check(File.ReadAllText(path)==Response(track.Isrc),"invalid disk cache replaced "+i);
            }
        }
        string stale=Path.Combine(folder,"stale"); Directory.CreateDirectory(stale);
        Track old=Original(2); string oldPath=Path.Combine(stale,old.Isrc+".json");
        File.WriteAllText(oldPath,Response(old.Isrc)); File.SetLastWriteTimeUtc(oldPath,DateTime.UtcNow.AddDays(-8));
        var refresh=new FixtureHandler(); refresh.Enqueue(HttpStatusCode.OK,Response(old.Isrc));
        using(var lookup=new AliasLookup(stale,refresh,NoPacing))
        {
            await lookup.FindAsync(old,CancellationToken.None);
            Check(refresh.Requests==1,"cache older than seven days refreshes");
        }
    }

    private static async Task NegativeTests(string folder)
    {
        Track track=Original(3); var handler=new FixtureHandler(); handler.Enqueue(HttpStatusCode.NotFound,"{}");
        using(var lookup=new AliasLookup(folder,handler,NoPacing))
        {
            Check((await lookup.FindAsync(track,CancellationToken.None)).Count==0,"404 has no aliases");
            Check((await lookup.FindAsync(track,CancellationToken.None)).Count==0,"negative cache reused");
            Check(handler.Requests==1,"404 requests only once per session");
        }
        var freshHandler=new FixtureHandler();
        using(var lookup=new AliasLookup(folder,freshHandler,NoPacing))
        {
            Check((await lookup.FindAsync(track,CancellationToken.None)).Count==0,"negative disk cache accepted");
            Check(freshHandler.Requests==0,"404 disk cache reused next session");
        }
    }

    private static async Task IdentityTests(string folder)
    {
        Track track=Original(4); var handler=new FixtureHandler();
        handler.Enqueue(HttpStatusCode.OK,Response(Original(5).Isrc));
        handler.Enqueue(HttpStatusCode.OK,Response(track.Isrc));
        using(var lookup=new AliasLookup(folder,handler,NoPacing))
        {
            Check((await lookup.FindAsync(track,CancellationToken.None)).Count==0,"mismatched response rejected");
            Check(!File.Exists(Path.Combine(folder,track.Isrc+".json")),"mismatched response not written");
            Check((await lookup.FindAsync(track,CancellationToken.None)).Count>0,"mismatched response not remembered");
            Check(handler.Requests==2,"identity mismatch can retry");
        }
    }

    private static async Task BreakerTests(string folder)
    {
        var handler=new FixtureHandler();
        handler.Enqueue(HttpStatusCode.OK,Response(Original(1).Isrc));
        handler.EnqueueFailure(new HttpRequestException("Offline fixture"));
        handler.EnqueueFailure(new TaskCanceledException("Timeout fixture"));
        handler.Enqueue(HttpStatusCode.ServiceUnavailable,"{}");
        using(var lookup=new AliasLookup(folder,handler,NoPacing))
        {
            await lookup.FindAsync(Original(1),CancellationToken.None);
            for(int i=2;i<40;i++) Check((await lookup.FindAsync(Original(i),CancellationToken.None)).Count==0,"unavailable service returns local-only "+i);
            Check(handler.Requests==4,"three transient failures stop additional network reads");
            Check((await lookup.FindAsync(Original(1),CancellationToken.None)).Count>0,"circuit breaker leaves cached aliases usable");
            using(var canceled=new CancellationTokenSource())
            {
                canceled.Cancel();
                await ExpectCancellation(()=>lookup.FindAsync(Original(98),canceled.Token),"open circuit preserves cancellation");
            }
        }
        var rates=new FixtureHandler();
        rates.Enqueue((HttpStatusCode)429,"{}"); rates.Enqueue((HttpStatusCode)429,"{}"); rates.Enqueue((HttpStatusCode)429,"{}");
        using(var lookup=new AliasLookup(Path.Combine(folder,"rates"),rates,NoPacing))
        {
            for(int i=1;i<=6;i++) await lookup.FindAsync(Original(i),CancellationToken.None);
            Check(rates.Requests==3,"three rate limits open circuit");
        }
        var reset=new FixtureHandler();
        reset.Enqueue(HttpStatusCode.ServiceUnavailable,"{}"); reset.Enqueue(HttpStatusCode.NotFound,"{}");
        reset.Enqueue(HttpStatusCode.ServiceUnavailable,"{}"); reset.Enqueue(HttpStatusCode.ServiceUnavailable,"{}");
        reset.Enqueue(HttpStatusCode.OK,Response(Original(5).Isrc));
        using(var lookup=new AliasLookup(Path.Combine(folder,"reset"),reset,NoPacing))
        {
            for(int i=1;i<5;i++) await lookup.FindAsync(Original(i),CancellationToken.None);
            Check((await lookup.FindAsync(Original(5),CancellationToken.None)).Count>0,"successful response resets consecutive failures");
            Check(reset.Requests==5,"breaker counts consecutive failures only");
        }
    }

    private static async Task CancellationTests(string folder)
    {
        using(var cancellation=new CancellationTokenSource())
        {
            var handler=new FixtureHandler();
            handler.EnqueueFailure(new HttpRequestException("First failure"));
            handler.Enqueue((request,ct)=> { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); });
            handler.EnqueueFailure(new HttpRequestException("Second failure"));
            handler.Enqueue(HttpStatusCode.OK,Response(Original(4).Isrc));
            using(var lookup=new AliasLookup(folder,handler,NoPacing))
            {
                await lookup.FindAsync(Original(1),CancellationToken.None);
                await ExpectCancellation(()=>lookup.FindAsync(Original(2),cancellation.Token),"active request cancellation reaches caller");
                await lookup.FindAsync(Original(3),CancellationToken.None);
                Check((await lookup.FindAsync(Original(4),CancellationToken.None)).Count>0,"caller cancellation does not count as service failure");
                Check(handler.Requests==4,"cancellation leaves gate usable");
            }
        }
    }

    private static async Task ConcurrentTests(string folder)
    {
        var handler=new FixtureHandler();
        handler.Enqueue(async (request,ct)=> { await Task.Delay(20,ct); return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(Response(Original(1).Isrc)) }; });
        using(var lookup=new AliasLookup(folder,handler,NoPacing))
        {
            var results=await Task.WhenAll(lookup.FindAsync(Original(1),CancellationToken.None),lookup.FindAsync(Original(1),CancellationToken.None));
            Check(results[0].Count>0 && results[1].Count>0,"concurrent lookups both return aliases");
            Check(handler.Requests==1,"concurrent identical ISRC performs one HTTP read");
            Check(handler.ValidRequests,"MusicBrainz origin and identifying user agent retained");
        }
    }

    private static Track Original(int n) { return new Track { Title="Original",Artist="Artist",Artists=new [] {"Artist"},Album="Album",DurationSeconds=200,Isrc="USAAA"+n.ToString("0000000") }; }
    private static string Response(string isrc) { return "{\"isrc\":\""+isrc+"\",\"recordings\":[{\"title\":\"Alternate title\",\"length\":200000,\"artist-credit\":[{\"name\":\"Artist\"}]}]}"; }
    private static void Check(bool condition,string name) { checks++; if(!condition) throw new Exception("Alias lookup test failed: "+name); }
    private static async Task ExpectCancellation(Func<Task<List<Track>>> action,string name)
    {
        try { await action(); }
        catch(OperationCanceledException) { checks++; return; }
        throw new Exception("Alias lookup test failed: "+name);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>>> replies=new Queue<Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>>>();
        internal int Requests;
        internal bool ValidRequests=true;
        internal void Enqueue(HttpStatusCode status,string body) { Enqueue((request,ct)=>Task.FromResult(new HttpResponseMessage(status) { Content=new StringContent(body) })); }
        internal void Enqueue(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply) { replies.Enqueue(reply); }
        internal void EnqueueFailure(Exception error) { Enqueue((request,ct)=> { var failed=new TaskCompletionSource<HttpResponseMessage>(); failed.SetException(error); return failed.Task; }); }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requests++;
            ValidRequests &= request.RequestUri.Scheme=="https" && request.RequestUri.Host=="musicbrainz.org" && request.Headers.UserAgent.ToString().Contains("PlaylistFLAC/") && request.Headers.Authorization==null;
            if(replies.Count==0) throw new Exception("Unexpected fixture HTTP request.");
            return replies.Dequeue()(request,ct);
        }
    }
}
