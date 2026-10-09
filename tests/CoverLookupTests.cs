using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PlaylistFlac;

internal static class CoverLookupTests
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"PlaylistFlacCoverLookupTests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { RunAsync(root).GetAwaiter().GetResult(); }
        finally { Directory.Delete(root,true); }
    }
    private static async Task RunAsync(string root)
    {
        byte[] png=ArtworkTestData.Image();
        var track=new Track {Title="夜",Artist="Artist",Album="Album",CoverUrl="https://i.scdn.co/image/example"};
        var handler=new Handler((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(png)}));
        using(var lookup=new CoverLookup(Path.Combine(root,"cached"),handler,null))
        {
            var both=await Task.WhenAll(lookup.FindAsync(track,CancellationToken.None),lookup.FindAsync(track,CancellationToken.None));
            Check(handler.Count==1 && both.All(x=>x!=null && x.Bytes.SequenceEqual(png)),"Concurrent same-album lookups fetch one image and use its cache.");
            using(var cancelled=new CancellationTokenSource()) {cancelled.Cancel();bool stopped=false;try{await lookup.FindAsync(track,cancelled.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"Cancellation precedes cached results.");}
        }
        var offline=new Handler((request,ct)=>{throw new Exception("Persistent cache should avoid network.");});
        using(var lookup=new CoverLookup(Path.Combine(root,"cached"),offline,null))
            Check((await lookup.FindAsync(track,CancellationToken.None)).Bytes.SequenceEqual(png) && offline.Count==0,"A new lookup instance reuses the verified disk cache.");
        string image=Directory.GetFiles(Path.Combine(root,"cached"),"*.img").Single();File.WriteAllBytes(image,new byte[]{0});
        var repair=new Handler((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(png)}));
        using(var lookup=new CoverLookup(Path.Combine(root,"cached"),repair,null))
            Check((await lookup.FindAsync(track,CancellationToken.None)).Bytes.SequenceEqual(png) && repair.Count==1,"Corrupt image cache refreshes rather than being embedded.");
        var invalid=new Handler((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("not an image")}));
        using(var lookup=new CoverLookup(Path.Combine(root,"invalid"),invalid,null))
        {Check(await lookup.FindAsync(track,CancellationToken.None)==null,"Non-image payload is rejected.");Check(await lookup.FindAsync(track,CancellationToken.None)==null && invalid.Count==1,"Permanent misses are cached.");}
        var unsafeHandler=new Handler((request,ct)=>{throw new Exception("Unsafe URL reached the network.");});
        using(var lookup=new CoverLookup(Path.Combine(root,"unsafe"),unsafeHandler,null))
        {
            foreach(string url in new[]{"http://i.scdn.co/image/a","https://i.scdn.co.evil.example/a","https://user:pass@i.scdn.co/a","https://127.0.0.1/a","https://i.scdn.co:444/a"})
                Check(await lookup.FindAsync(new Track{CoverUrl=url},CancellationToken.None)==null,"Only trusted HTTPS artwork hosts are fetched.");
            Check(unsafeHandler.Count==0,"Unsafe direct hosts never receive requests.");
        }
        var redirect=new Handler((request,ct)=> {var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri("https://example.com/wrong");return Task.FromResult(response);});
        using(var lookup=new CoverLookup(Path.Combine(root,"redirect"),redirect,null))
            Check(await lookup.FindAsync(track,CancellationToken.None)==null && redirect.Count==1,"Cross-service redirects are rejected before fetching their destination.");
        var throttle=new Handler((request,ct)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)));
        using(var lookup=new CoverLookup(Path.Combine(root,"throttle"),throttle,null))
        {await lookup.FindAsync(track,CancellationToken.None);await lookup.FindAsync(new Track{CoverUrl="https://i.scdn.co/image/other"},CancellationToken.None);Check(throttle.Count==1,"Rate-limit response backs off the service instead of retrying each track.");}
        DateTime clock=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
        var delayed=new Handler((request,ct)=>{
            var response=new HttpResponseMessage((HttpStatusCode)503);
            response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return Task.FromResult(response);
        });
        using(var lookup=new CoverLookup(Path.Combine(root,"retry-after"),delayed,null,()=>clock))
        {
            await lookup.FindAsync(track,CancellationToken.None);clock=clock.AddMinutes(9);
            await lookup.FindAsync(track,CancellationToken.None);Check(delayed.Count==1,"Retry-After suppresses requests throughout the provider cooldown.");
            clock=clock.AddMinutes(1);await lookup.FindAsync(track,CancellationToken.None);Check(delayed.Count==2,"Transient failures are retried after cooldown, not cached as permanent misses.");
        }
        var repeated=new Handler((request,ct)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)));
        using(var lookup=new CoverLookup(Path.Combine(root,"backoff"),repeated,null,()=>clock))
        {
            await lookup.FindAsync(track,CancellationToken.None);clock=clock.AddMinutes(2);
            await lookup.FindAsync(track,CancellationToken.None);clock=clock.AddMinutes(2);
            await lookup.FindAsync(track,CancellationToken.None);Check(repeated.Count==2,"Repeated throttling doubles cooldown instead of polling at a fixed interval.");
            clock=clock.AddMinutes(2);await lookup.FindAsync(track,CancellationToken.None);Check(repeated.Count==3,"Backoff permits another request at its due time.");
        }
        using(var dated=new HttpResponseMessage((HttpStatusCode)429))
        {
            dated.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(new DateTimeOffset(clock.AddMinutes(20)));
            Check(CoverLookup.RetryDelay(dated,clock).Value==TimeSpan.FromMinutes(20),"HTTP-date Retry-After is respected.");
        }
        string release="76df3287-6cda-33eb-8e9a-044b5e15ffdd";
        string json="{\"isrc\":\"USAAA2000001\",\"recordings\":[{\"length\":180000,\"artist-credit\":[{\"name\":\"Artist\"}],\"releases\":[{\"id\":\""+release+"\",\"title\":\"Album\"}]}]}";
        var exact=new Track{Title="Song",Artist="Artist",Album="Album",Isrc="USAAA2000001",DurationSeconds=180};
        Check(CoverLookup.MatchingReleases(json,exact).SequenceEqual(new[]{release}),"Exact recording, album, artist and duration produce a candidate release.");
        Check(CoverLookup.MatchingReleases(json,new Track{Artist="Other",Album="Album",Isrc=exact.Isrc,DurationSeconds=180}).Count==0,"Wrong artist cannot borrow cover art.");
        Check(CoverLookup.MatchingReleases(json,new Track{Artist="Artist",Album="Deluxe",Isrc=exact.Isrc,DurationSeconds=180}).Count==0,"A different album edition stays separate.");
        Check(CoverLookup.MatchingReleases(json,new Track{Artist="Artist",Album="Album",Isrc=exact.Isrc,DurationSeconds=190}).Count==0,"Conflicting duration is rejected.");
        var fallback=new Handler((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=request.RequestUri.Host=="musicbrainz.org"?(HttpContent)new StringContent(json):new ByteArrayContent(png)}));
        using(var lookup=new CoverLookup(Path.Combine(root,"fallback"),fallback,ct=>Task.FromResult(0)))
        {var found=await lookup.FindAsync(exact,CancellationToken.None);Check(found!=null && found.ReleaseId==release && fallback.Count==2,"Verified ISRC metadata leads to the matching release front cover.");}
        string recordingId="8f3471b5-7e6a-48da-86a9-c1c07a0f47ae";
        string partial="{\"isrc\":\"USAAA2000001\",\"recordings\":[{\"id\":\""+recordingId+"\"}]}";
        string full="{\"id\":\""+recordingId+"\",\"isrcs\":[\"USAAA2000001\"],\"length\":180000,\"artist-credit\":[{\"name\":\"Artist\"}],\"releases\":[{\"id\":\""+release+"\",\"title\":\"Album\"}]}";
        int paced=0;
        var hydration=new Handler((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=request.RequestUri.Host!="musicbrainz.org"?(HttpContent)new ByteArrayContent(png):new StringContent(request.RequestUri.AbsolutePath.Contains("/recording/")?full:partial)}));
        using(var lookup=new CoverLookup(Path.Combine(root,"hydration"),hydration,ct=>{paced++;return Task.FromResult(0);}))
        {
            Check(await lookup.FindAsync(exact,CancellationToken.None)!=null && hydration.Count==3 && paced==2,"Omitted releases are resolved through a paced recording lookup.");
            Check(await lookup.FindAsync(exact,CancellationToken.None)!=null && hydration.Count==3,"Completed metadata and artwork are cached.");
        }
        foreach(string bad in new[]{full.Replace("USAAA2000001","USAAA2000002"),full.Replace(recordingId,release),"invalid JSON"})
        {
            var mismatch=new Handler((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(request.RequestUri.AbsolutePath.Contains("/recording/")?bad:partial)}));
            using(var lookup=new CoverLookup(Path.Combine(root,Guid.NewGuid().ToString("N")),mismatch,ct=>Task.FromResult(0)))
                Check(await lookup.FindAsync(exact,CancellationToken.None)==null && mismatch.Count==2,"Unverified recording details cannot supply artwork.");
        }
        string many="{\"isrc\":\"USAAA2000001\",\"recordings\":["+String.Join(",",Enumerable.Repeat("{\"id\":\""+recordingId+"\"}",10))+ "]}";
        var bounded=new Handler((request,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(request.RequestUri.AbsolutePath.Contains("/recording/")?"{}":many)}));
        using(var lookup=new CoverLookup(Path.Combine(root,"bounded"),bounded,ct=>Task.FromResult(0)))
            Check(await lookup.FindAsync(exact,CancellationToken.None)==null && bounded.Count==4,"One ISRC lookup makes at most three recording follow-ups.");
        var transient=new Handler((request,ct)=>Task.FromResult(new HttpResponseMessage(request.RequestUri.AbsolutePath.Contains("/recording/")?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){Content=new StringContent(partial)}));
        using(var lookup=new CoverLookup(Path.Combine(root,"transient-hydration"),transient,ct=>Task.FromResult(0)))
            Check(await lookup.FindAsync(exact,CancellationToken.None)==null && Directory.GetFiles(Path.Combine(root,"transient-hydration"),"isrc*.json").Length==0,"Transient recording lookup failure is not cached as missing artwork.");
    }
    private sealed class Handler:HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> callback;
        internal int Count;
        internal Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> callback){this.callback=callback;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Interlocked.Increment(ref Count);return callback(request,ct);}
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception("Cover lookup: "+message);}
}

internal static class ArtworkTestData
{
    internal static byte[] Image()
    {using(var bitmap=new Bitmap(8,8))using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);return stream.ToArray();}}
    internal static byte[] Flac(byte seed=1)
    {
        var bytes=new byte[74];bytes[0]=102;bytes[1]=76;bytes[2]=97;bytes[3]=67;bytes[4]=128;bytes[7]=34;bytes[8]=16;bytes[10]=16;
        ulong packed=((ulong)44100<<44)|((ulong)1<<41)|((ulong)15<<36)|(ulong)441000;
        for(int i=25;i>=18;i--){bytes[i]=(byte)packed;packed>>=8;}
        bytes[42]=255;bytes[43]=248;for(int i=44;i<bytes.Length;i++)bytes[i]=(byte)(i+seed);return bytes;
    }
}
