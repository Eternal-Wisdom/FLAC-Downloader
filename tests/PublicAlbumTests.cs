using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using PlaylistFlac;

internal static class PublicAlbumTests
{
    private const string Id="0000000000000000000000";
    private static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    private static void Reject(string html){try{PublicSpotifyAlbum.ParseHtml(html,Id);}catch(FormatException){return;}throw new Exception("Malformed public album accepted");}
    internal static string Html(int count=2)
    {
        var tracks=Enumerable.Range(0,count).Select(i=>new {uri="spotify:track:"+i.ToString().PadLeft(22,'0'),title=i==0?"日本語":"Second",subtitle="Artist,\u00a0Guest",duration=175500,entityType="track"}).ToArray();
        var data=new {props=new {pageProps=new {state=new {data=new {entity=new {type="album",id=Id,uri="spotify:album:"+Id,name="Fixture album",trackList=tracks,visualIdentity=new {image=new[]{new {url="https://i.scdn.co/image/fixture",maxWidth=640}}}}},settings=new {session=new {accessToken="DO-NOT-RETAIN-PUBLIC-PAGE-TOKEN"}}}}}};
        return "<html><script type='application/json' id='__NEXT_DATA__'>"+new JavaScriptSerializer().Serialize(data)+"</script></html>";
    }
    internal static void Run()
    {
        string html=Html();var album=PublicSpotifyAlbum.ParseHtml(html,Id);
        Check(album.Tracks.Count==2 && album.Tracks[0].Title=="日本語","public album Unicode/count");
        Check(album.Tracks[0].DurationSeconds==175.5 && album.Tracks[0].Artists.Length==2,"public duration and artist credits");
        Check(album.Tracks[0].Album=="Fixture album" && album.Tracks[0].CoverUrl=="https://i.scdn.co/image/fixture","album metadata/artwork");
        Check(album.Tracks.All(t=>t.DiscNumber==0 && t.TrackNumber==0 && String.IsNullOrEmpty(t.Isrc)),"unavailable metadata was invented");
        Check(!new JavaScriptSerializer().Serialize(album).Contains("DO-NOT-RETAIN"),"anonymous page token retained");
        Reject("<html>Sign in</html>");Reject(Html(0));Reject(Html(100));
        Reject(html.Replace("\"type\":\"album\"","\"type\":\"playlist\""));
        Reject(html.Replace("\"duration\":175500","\"duration\":0"));
        Reject(html.Replace("\"trackList\":","\"totalTracks\":3,\"trackList\":"));
        Reject(html.Replace("\"name\":\"Fixture album\"","\"isPreRelease\":true,\"name\":\"Fixture album\""));
        using(var handler=new Handler(html))
        using(var importer=new SpotifyImporter(handler))
        {
            Check(!importer.IsConnected,"public import started connected");
            var loaded=importer.LoadAsync("https://open.spotify.com/intl-ja/album/"+Id+"?si=share",CancellationToken.None).GetAwaiter().GetResult();
            Check(loaded.Tracks.Count==2 && handler.Calls==1 && !handler.Auth,"public album required authentication or extra requests");
            Check(handler.Uri=="https://open.spotify.com/embed/album/"+Id,"public import followed arbitrary URL");
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();bool rejected=false;
                try {importer.LoadAsync("spotify:album:"+Id,cancelled.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){rejected=true;}
                Check(rejected,"public album ignored cancellation");
            }
        }
        using(var handler=new Handler("blocked",(HttpStatusCode)429))
        using(var importer=new SpotifyImporter(handler))
        {
            bool rejected=false;try{importer.LoadAsync("spotify:album:"+Id,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidOperationException e){rejected=e.Message.Contains("429");}
            Check(rejected && handler.Calls==1,"rate-limited album was retried or silently accepted");
        }
    }
    private sealed class Handler:HttpMessageHandler
    {
        private readonly string text;private readonly HttpStatusCode status;
        internal int Calls;internal bool Auth;internal string Uri;
        internal Handler(string text,HttpStatusCode status=HttpStatusCode.OK){this.text=text;this.status=status;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();Calls++;Auth=request.Headers.Authorization!=null;Uri=request.RequestUri.AbsoluteUri;return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(text,System.Text.Encoding.UTF8,"text/html")});}
    }
}
