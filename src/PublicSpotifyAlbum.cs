using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    // Read only the metadata already supplied by Spotify's public album embed.
    // Anonymous page tokens and audio previews are never used or retained.
    internal static class PublicSpotifyAlbum
    {
        private const int MaximumBytes=4*1024*1024;
        internal static async Task<Playlist> LoadAsync(string url,HttpClient http,CancellationToken ct)
        {
            string id=SpotifyImporter.ParseAlbumId(url);
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45)))
            using(var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,timeout.Token))
            using(var request=new HttpRequestMessage(HttpMethod.Get,"https://open.spotify.com/embed/album/"+id))
            {
                request.Headers.UserAgent.ParseAdd("FLAC-Downloader/1.18 (+public-album-metadata)");
                using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,linked.Token).ConfigureAwait(false))
                {
                    if(!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Spotify's public album page is unavailable (HTTP "+(int)response.StatusCode+"). Try again later or import a CSV. No developer account is needed for public album import.");
                    if(response.Content.Headers.ContentLength>MaximumBytes)throw new FormatException("The public album page is too large to import safely. Import a CSV instead.");
                    using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using(var buffer=new MemoryStream())
                    {
                        byte[] block=new byte[16384];int count;
                        while((count=await stream.ReadAsync(block,0,block.Length,linked.Token).ConfigureAwait(false))>0)
                        {if(buffer.Length+count>MaximumBytes)throw new FormatException("The public album page is too large to import safely. Import a CSV instead.");buffer.Write(block,0,count);}
                        linked.Token.ThrowIfCancellationRequested();
                        return ParseHtml(Encoding.UTF8.GetString(buffer.ToArray()),id);
                    }
                }
            }
        }

        internal static Playlist ParseHtml(string html,string id)
        {
            if(!Regex.IsMatch(id ?? "","^[A-Za-z0-9]{22}$"))throw new FormatException("Invalid Spotify album identity.");
            if(html==null || html.Length>MaximumBytes)throw new FormatException("The public album page is missing or too large.");
            var match=Regex.Match(html,@"<script\b(?=[^>]*\bid\s*=\s*(?:""__NEXT_DATA__""|'__NEXT_DATA__'))[^>]*>(.*?)</script\s*>",RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(2));
            if(!match.Success)throw new FormatException("Spotify did not supply a readable public album tracklist. The page may be restricted or its format may have changed. Import a CSV instead.");
            var json=new JavaScriptSerializer {MaxJsonLength=MaximumBytes,RecursionLimit=100};
            Dictionary<string,object> root;
            try {root=json.DeserializeObject(match.Groups[1].Value) as Dictionary<string,object>;}
            catch(ArgumentException) {throw new FormatException("Spotify supplied unreadable album metadata. Import a CSV instead.");}
            var entity=Object(Object(Object(Object(Object(root,"props"),"pageProps"),"state"),"data"),"entity");
            if(Text(entity,"type")!="album" || Text(entity,"id")!=id || Text(entity,"uri")!="spotify:album:"+id)
                throw new FormatException("Spotify returned a different album or an incomplete public page. Nothing was imported.");
            object preRelease;if(entity.TryGetValue("isPreRelease",out preRelease) && preRelease is bool && (bool)preRelease)
                throw new FormatException("This album is not fully released. Import its tracklist after release, or use a CSV.");
            object value;object[] rows=entity.TryGetValue("trackList",out value)?value as object[]:null;
            if(rows==null || rows.Length==0 || rows.Length>=100)
                throw new FormatException("Spotify did not provide a verifiable public tracklist for this album. Empty or very large album lists need a CSV or optional Web API import.");
            foreach(string key in new[]{"totalTracks","total_tracks"})
                if(entity.TryGetValue(key,out value) && Convert.ToInt32(value)!=rows.Length)
                    throw new FormatException("Spotify supplied only part of this album. Nothing was imported; use a CSV instead.");
            string name=Text(entity,"name");
            if(String.IsNullOrWhiteSpace(name))throw new FormatException("Spotify did not provide the album name.");
            string cover=Cover(Object(entity,"visualIdentity"));
            var result=new Playlist {Name=name,Source="https://open.spotify.com/album/"+id};
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(object item in rows)
            {
                var row=item as Dictionary<string,object>;
                string title=Text(row,"title"),artist=Text(row,"subtitle"),uri=Text(row,"uri");
                if(!Regex.IsMatch(uri,"^spotify:track:[A-Za-z0-9]{22}$") || !seen.Add(uri) || String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(artist))
                    throw new FormatException("Spotify supplied an incomplete or conflicting album track. Nothing was imported.");
                double milliseconds;
                if(!row.TryGetValue("duration",out value) || !Double.TryParse(Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out milliseconds) || Double.IsNaN(milliseconds) || Double.IsInfinity(milliseconds) || milliseconds<=0 || milliseconds>86400000)
                    throw new FormatException("An album track has no reliable duration. Nothing was imported.");
                string[] artists=artist.Split(new[]{",\u00a0"},StringSplitOptions.None).Select(a=>a.Trim()).ToArray();
                if(artists.Any(String.IsNullOrWhiteSpace))throw new FormatException("An album track has incomplete artist credits.");
                result.Tracks.Add(new Track {Title=title,Artist=String.Join(", ",artists),Artists=artists,Album=name,DurationSeconds=milliseconds/1000.0,SpotifyId=uri.Substring(14),CoverUrl=cover});
            }
            // Public embeds do not reliably expose ISRC, disc or original track numbers; leave them unknown.
            return result;
        }
        private static string Cover(Dictionary<string,object> visual)
        {
            object value;var images=visual!=null && visual.TryGetValue("image",out value)?value as object[]:null;
            if(images==null)return "";
            foreach(var item in images.Cast<Dictionary<string,object>>().OrderByDescending(i=> {object w;return i.TryGetValue("maxWidth",out w)?Convert.ToInt32(w):0;}))
            {
                Uri uri;string url=Text(item,"url");
                if(Uri.TryCreate(url,UriKind.Absolute,out uri) && (uri.Host=="i.scdn.co" || uri.Host.EndsWith(".spotifycdn.com",StringComparison.OrdinalIgnoreCase)))
                {string safe=CsvPlaylist.NormalizeCoverUrl(url);if(safe.Length>0)return safe;}
            }
            return "";
        }
        private static Dictionary<string,object> Object(Dictionary<string,object> obj,string key)
        {object value;return obj!=null && obj.TryGetValue(key,out value)?value as Dictionary<string,object>:null;}
        private static string Text(Dictionary<string,object> obj,string key)
        {object value;return obj!=null && obj.TryGetValue(key,out value)?Convert.ToString(value) ?? "":"";}
    }
}
