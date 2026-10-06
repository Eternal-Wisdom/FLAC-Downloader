using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    internal sealed class AliasLookup : IDisposable
    {
        private readonly HttpClient http;
        private static readonly SemaphoreSlim gate = new SemaphoreSlim(1,1);
        private static DateTime lastRequest = DateTime.MinValue;
        private readonly string cache;
        private readonly Func<CancellationToken,Task> testPacing;
        private readonly object memoryLock = new object();
        private readonly Dictionary<string,string> memory = new Dictionary<string,string>(StringComparer.Ordinal);
        private readonly Queue<string> memoryOrder = new Queue<string>();
        private int memoryCharacters;
        private int consecutiveFailures;
        private const int MaximumResponseCharacters = 2*1024*1024;
        private const int MaximumMemoryCharacters = 4*1024*1024;
        private const int MaximumMemoryEntries = 256;

        public AliasLookup(string cacheDirectory) : this(cacheDirectory,new HttpClientHandler { AllowAutoRedirect=false },null) { }
        internal AliasLookup(string cacheDirectory,HttpMessageHandler handler,Func<CancellationToken,Task> testPacing)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http=new HttpClient(handler);
            this.testPacing=testPacing;
            cache=cacheDirectory; Directory.CreateDirectory(cache);
            http.Timeout=TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PlaylistFLAC/1.1 (personal local desktop application)");
        }
        internal async Task<List<Track>> FindAsync(Track original, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(original==null) return new List<Track>();
            string isrc=(original.Isrc ?? "").Replace("-","").ToUpperInvariant();
            if(!Regex.IsMatch(isrc,@"^[A-Z]{2}[A-Z0-9]{3}[0-9]{7}$") || original.DurationSeconds<=0 || Double.IsNaN(original.DurationSeconds) || Double.IsInfinity(original.DurationSeconds)) return new List<Track>();
            string path=Path.Combine(cache,isrc+".json"), json=null;
            if(TryCached(isrc,path,out json)) return Parse(json,original);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Another caller may have filled the cache while this one waited at the global gate.
                ct.ThrowIfCancellationRequested();
                if(TryCached(isrc,path,out json)) return Parse(json,original);
                if(consecutiveFailures>=3) return new List<Track>();
                if(testPacing!=null) await testPacing(ct).ConfigureAwait(false);
                else {
                    int delay=(int)Math.Max(0,1200-(DateTime.UtcNow-lastRequest).TotalMilliseconds);
                    if(delay>0) await Task.Delay(delay,ct).ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();
                lastRequest=DateTime.UtcNow;
                try
                {
                    using(var response=await http.GetAsync("https://musicbrainz.org/ws/2/isrc/"+isrc+"?fmt=json&inc=artist-credits",ct).ConfigureAwait(false))
                    {
                        int status=(int)response.StatusCode;
                        if(status==429 || status==408 || status>=500)
                        { consecutiveFailures++; return new List<Track>(); }
                        consecutiveFailures=0;
                        if(response.StatusCode==HttpStatusCode.NotFound) json="{\"isrc\":\""+isrc+"\",\"recordings\":[]}";
                        else if(!response.IsSuccessStatusCode) return new List<Track>();
                        else json=await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch(OperationCanceledException)
                {
                    ct.ThrowIfCancellationRequested();
                    consecutiveFailures++; return new List<Track>();
                }
                catch(HttpRequestException)
                { ct.ThrowIfCancellationRequested(); consecutiveFailures++; return new List<Track>(); }
                ct.ThrowIfCancellationRequested();
                if(!IsValidResponse(json,isrc)) return new List<Track>();
                Remember(isrc,json);
                try { IndexStore.AtomicWrite(path,json); }
                catch(IOException) { }
                catch(UnauthorizedAccessException) { }
                return Parse(json,original);
            }
            finally { gate.Release(); }
        }

        private bool TryCached(string isrc,string path,out string json)
        {
            lock(memoryLock) { if(memory.TryGetValue(isrc,out json)) return true; }
            try
            {
                if(File.Exists(path) && DateTime.UtcNow-File.GetLastWriteTimeUtc(path)<TimeSpan.FromDays(7) && new FileInfo(path).Length<=MaximumResponseCharacters*4L)
                {
                    string stored=File.ReadAllText(path);
                    if(IsValidResponse(stored,isrc)) { Remember(isrc,stored); json=stored; return true; }
                }
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            json=null; return false;
        }

        private static bool IsValidResponse(string json,string isrc)
        {
            if(String.IsNullOrEmpty(json) || json.Length>MaximumResponseCharacters) return false;
            try
            {
                var data=new JavaScriptSerializer { MaxJsonLength=MaximumResponseCharacters }.DeserializeObject(json) as Dictionary<string,object>;
                object recordings;
                return data!=null && String.Equals(Text(data,"isrc"),isrc,StringComparison.OrdinalIgnoreCase) && data.TryGetValue("recordings",out recordings) && recordings is object[];
            }
            catch(ArgumentException) { return false; }
            catch(InvalidOperationException) { return false; }
        }

        private void Remember(string isrc,string json)
        {
            lock(memoryLock)
            {
                if(memory.ContainsKey(isrc)) return;
                while(memoryOrder.Count>0 && (memory.Count>=MaximumMemoryEntries || memoryCharacters+json.Length>MaximumMemoryCharacters))
                {
                    string oldest=memoryOrder.Dequeue();
                    memoryCharacters-=memory[oldest].Length; memory.Remove(oldest);
                }
                memory.Add(isrc,json); memoryOrder.Enqueue(isrc); memoryCharacters+=json.Length;
            }
        }
        internal static List<Track> Parse(string json,Track original)
        {
            var result=new List<Track>();
            if(original==null || original.DurationSeconds<=0 || Double.IsNaN(original.DurationSeconds) || Double.IsInfinity(original.DurationSeconds)) return result;
            var data=new JavaScriptSerializer { MaxJsonLength=2*1024*1024 }.DeserializeObject(json) as Dictionary<string,object>;
            object recordings;
            if(data==null || !data.TryGetValue("recordings",out recordings) || !(recordings is object[])) return result;
            if(!String.Equals(Text(data,"isrc"),(original.Isrc ?? "").Replace("-", ""),StringComparison.OrdinalIgnoreCase)) return result;
            foreach(var item in (object[])recordings)
            {
                var recording=item as Dictionary<string,object>; if(recording==null) continue;
                double ms; if(!Double.TryParse(Text(recording,"length"),NumberStyles.Any,CultureInfo.InvariantCulture,out ms) || ms<=0 || Double.IsNaN(ms) || Double.IsInfinity(ms) || Math.Abs(ms/1000-original.DurationSeconds)>5) continue;
                string title=Text(recording,"title"); if(String.IsNullOrWhiteSpace(title)) continue;
                object credits;
                if(!recording.TryGetValue("artist-credit",out credits) || !(credits is object[]) || ((object[])credits).Length==0) continue;
                var credit=((object[])credits)[0] as Dictionary<string,object>; if(credit==null) continue;
                var names=new List<string>{Text(credit,"name")};
                object artistObj;
                if(credit.TryGetValue("artist",out artistObj)) { var artist=artistObj as Dictionary<string,object>; if(artist!=null) { names.Add(Text(artist,"name")); names.Add(Text(artist,"sort-name")); } }
                names.Add(original.PrimaryArtist);
                foreach(string name in names.Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var candidate=new Track { Title=title, Artist=name, Album=original.Album, DurationSeconds=original.DurationSeconds, Isrc=original.Isrc, SpotifyId=original.SpotifyId, Artists=new[]{name} };
                    if(IndexStore.Key(candidate)!=IndexStore.Key(original) && !result.Any(x=>IndexStore.Key(x)==IndexStore.Key(candidate))) result.Add(candidate);
                }
            }
            return result.Take(4).ToList();
        }
        private static string Text(Dictionary<string,object> row,string name) { object value; return row.TryGetValue(name,out value) && value!=null ? Convert.ToString(value,CultureInfo.InvariantCulture) : ""; }
        public void Dispose() { http.Dispose(); }
    }
}
