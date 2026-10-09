using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    internal sealed class CoverLookupResult
    {
        public byte[] Bytes { get; set; }
        public string SourceUrl { get; set; }
        public string ReleaseId { get; set; }
    }

    // Artwork is looked up from an exact exported album image first. The optional
    // catalogue fallback requires recording ID, primary artist, duration and album.
    internal sealed class CoverLookup : IDisposable
    {
        private const int MaximumImageBytes = 10 * 1024 * 1024;
        private const int MaximumJsonBytes = 2 * 1024 * 1024;
        private readonly HttpClient http;
        private readonly string cacheDirectory;
        private readonly Func<CancellationToken, Task> testPacing;
        private readonly Func<DateTime> utcNow;
        private readonly SemaphoreSlim requests = new SemaphoreSlim(4, 4);
        private readonly object sync = new object();
        private readonly Dictionary<string, SemaphoreSlim> keyGates = new Dictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> blockedUntil = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> failures = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly SemaphoreSlim musicBrainzGate = new SemaphoreSlim(1, 1);
        private static DateTime lastMusicBrainzRequest = DateTime.MinValue;

        private sealed class FetchResult
        {
            internal byte[] Bytes;
            internal string SourceUrl;
            internal bool PermanentMiss;
        }

        public CoverLookup(string cacheDirectory)
            : this(cacheDirectory, new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }, null) { }

        internal CoverLookup(string cacheDirectory, HttpMessageHandler handler, Func<CancellationToken, Task> pacing)
            : this(cacheDirectory, handler, pacing, () => DateTime.UtcNow) { }

        internal CoverLookup(string cacheDirectory, HttpMessageHandler handler, Func<CancellationToken, Task> pacing, Func<DateTime> clock)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            this.cacheDirectory = Path.GetFullPath(cacheDirectory);
            Directory.CreateDirectory(this.cacheDirectory);
            testPacing = pacing;
            utcNow = clock;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FLAC-Downloader/1.14 (https://github.com/Eternal-Wisdom/FLAC-Downloader)");
        }

        public async Task<CoverLookupResult> FindAsync(Track track, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (track == null) return null;
            Uri direct;
            if (Uri.TryCreate(track.CoverUrl, UriKind.Absolute, out direct) && Allowed(direct, "spotify"))
            {
                CoverLookupResult exact = await ImageAsync(direct, "", "spotify", ct).ConfigureAwait(false);
                if (exact != null) return exact;
            }
            string isrc = (track.Isrc ?? "").Replace("-", "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(isrc, @"^[A-Z]{2}[A-Z0-9]{3}[0-9]{7}$") ||
                String.IsNullOrWhiteSpace(track.Album) || String.IsNullOrWhiteSpace(track.PrimaryArtist) ||
                track.DurationSeconds <= 0 || Double.IsNaN(track.DurationSeconds) || Double.IsInfinity(track.DurationSeconds)) return null;
            string json = await RecordingsAsync(isrc, ct).ConfigureAwait(false);
            if (json == null) return null;
            foreach (string release in MatchingReleases(json, track).Take(3))
            {
                ct.ThrowIfCancellationRequested();
                var uri = new Uri("https://coverartarchive.org/release/" + release + "/front-500");
                CoverLookupResult result = await ImageAsync(uri, release, "archive", ct).ConfigureAwait(false);
                if (result != null) return result;
            }
            return null;
        }

        private async Task<CoverLookupResult> ImageAsync(Uri uri, string release, string service, CancellationToken ct)
        {
            string key = Hash(uri.AbsoluteUri), prefix = Path.Combine(cacheDirectory, key);
            SemaphoreSlim gate = KeyGate(key);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                CoverLookupResult cached;
                if (TryImageCache(prefix, uri.AbsoluteUri, release, service, out cached)) return cached;
                FetchResult fetched = await FetchAsync(uri, MaximumImageBytes, service, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (fetched.Bytes != null && !FlacArtwork.IsValidCoverImage(fetched.Bytes))
                    fetched = new FetchResult { PermanentMiss = true };
                if (fetched.Bytes == null && !fetched.PermanentMiss) return null;
                var result = fetched.Bytes == null ? null : new CoverLookupResult { Bytes = fetched.Bytes, SourceUrl = fetched.SourceUrl, ReleaseId = release };
                SaveImageCache(prefix, uri.AbsoluteUri, result);
                return result;
            }
            finally { gate.Release(); }
        }

        private bool TryImageCache(string prefix, string requested, string release, string service, out CoverLookupResult result)
        {
            result = null;
            try
            {
                string meta = prefix + ".json";
                if (!File.Exists(meta) || new FileInfo(meta).Length > 16384) return false;
                var data = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(meta)) as Dictionary<string, object>;
                if (data == null || Text(data, "RequestedUrl") != requested) return false;
                DateTime timestamp;
                if (!DateTime.TryParse(Text(data, "CreatedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp)) return false;
                TimeSpan age = DateTime.UtcNow - timestamp.ToUniversalTime();
                if (age < TimeSpan.Zero || age > TimeSpan.FromDays(Text(data, "Found") == "True" ? 30 : 1)) return false;
                if (Text(data, "Found") != "True") return true;
                Uri source;
                if (!Uri.TryCreate(Text(data, "SourceUrl"), UriKind.Absolute, out source) || !Allowed(source, service) || Text(data, "ReleaseId") != release) return false;
                string imagePath = prefix + ".img";
                if (!File.Exists(imagePath) || new FileInfo(imagePath).Length > MaximumImageBytes) return false;
                byte[] bytes = File.ReadAllBytes(imagePath);
                if (Hash(bytes) != Text(data, "ImageHash") || !FlacArtwork.IsValidCoverImage(bytes)) return false;
                result = new CoverLookupResult { Bytes = bytes, SourceUrl = source.AbsoluteUri, ReleaseId = release };
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private void SaveImageCache(string prefix, string requested, CoverLookupResult result)
        {
            try
            {
                if (result != null) AtomicBytes(prefix + ".img", result.Bytes);
                string json = new JavaScriptSerializer().Serialize(new {
                    RequestedUrl = requested, CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Found = result != null, SourceUrl = result == null ? "" : result.SourceUrl,
                    ReleaseId = result == null ? "" : result.ReleaseId, ImageHash = result == null ? "" : Hash(result.Bytes)
                });
                AtomicBytes(prefix + ".json", Encoding.UTF8.GetBytes(json));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private async Task<string> RecordingsAsync(string isrc, CancellationToken ct)
        {
            // Older cached ISRC responses can lack releases even when requested.
            string path = Path.Combine(cacheDirectory, "isrc-v2-" + isrc + ".json"), json;
            if (TryRecordingsCache(path, isrc, out json)) return json;
            await musicBrainzGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                if (TryRecordingsCache(path, isrc, out json)) return json;
                if (Blocked("musicbrainz")) return null;
                await PaceMusicBrainzAsync(ct).ConfigureAwait(false);
                FetchResult result = await FetchAsync(new Uri("https://musicbrainz.org/ws/2/isrc/" + isrc + "?fmt=json&inc=artists%2Breleases"), MaximumJsonBytes, "musicbrainz", ct).ConfigureAwait(false);
                if (result.Bytes == null && !result.PermanentMiss) return null;
                json = result.Bytes == null ? "{\"isrc\":\"" + isrc + "\",\"recordings\":[]}" : Encoding.UTF8.GetString(result.Bytes);
                if (!ValidRecordings(json, isrc)) return null;
                json = await CompleteReleasesAsync(json, isrc, ct).ConfigureAwait(false);
                if (json == null) return null;
                ct.ThrowIfCancellationRequested();
                try { AtomicBytes(path, Encoding.UTF8.GetBytes(json)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return json;
            }
            finally { musicBrainzGate.Release(); }
        }

        private async Task PaceMusicBrainzAsync(CancellationToken ct)
        {
            if (testPacing != null) await testPacing(ct).ConfigureAwait(false);
            else
            {
                int delay = (int)Math.Max(0, 1100 - (DateTime.UtcNow - lastMusicBrainzRequest).TotalMilliseconds);
                if (delay > 0) await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            lastMusicBrainzRequest = DateTime.UtcNow;
        }

        // Some live ISRC responses omit releases. Resolve at most three identified
        // recordings, under the same provider gate and pacing as the ISRC request.
        private async Task<string> CompleteReleasesAsync(string json, string isrc, CancellationToken ct)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumJsonBytes };
            var data = (Dictionary<string, object>)serializer.DeserializeObject(json);
            var recordings = (object[])data["recordings"];
            int requestsMade = 0;
            for (int i = 0; i < recordings.Length && requestsMade < 3; i++)
            {
                var recording = recordings[i] as Dictionary<string, object>;
                object releases; Guid id;
                if (recording == null || recording.TryGetValue("releases", out releases) && releases is object[] ||
                    !Guid.TryParse(Text(recording, "id"), out id)) continue;
                if (Blocked("musicbrainz")) return null;
                await PaceMusicBrainzAsync(ct).ConfigureAwait(false);
                requestsMade++;
                FetchResult result = await FetchAsync(new Uri("https://musicbrainz.org/ws/2/recording/" + id.ToString("D") + "?fmt=json&inc=artists%2Breleases%2Bisrcs"), MaximumJsonBytes, "musicbrainz", ct).ConfigureAwait(false);
                if (result.Bytes == null) { if (!result.PermanentMiss) return null; continue; }
                try
                {
                    var full = serializer.DeserializeObject(Encoding.UTF8.GetString(result.Bytes)) as Dictionary<string, object>;
                    object codes; Guid returned;
                    if (full != null && Guid.TryParse(Text(full, "id"), out returned) && returned == id &&
                        full.TryGetValue("isrcs", out codes) && codes is object[] &&
                        ((object[])codes).Any(code => String.Equals(Convert.ToString(code), isrc, StringComparison.OrdinalIgnoreCase)))
                        recordings[i] = full;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            try { return serializer.Serialize(data); }
            catch (InvalidOperationException) { return null; }
        }

        private static bool TryRecordingsCache(string path, string isrc, out string json)
        {
            json = null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaximumJsonBytes) return false;
                TimeSpan age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                if (age < TimeSpan.Zero || age > TimeSpan.FromDays(1)) return false;
                string stored = File.ReadAllText(path);
                if (!ValidRecordings(stored, isrc)) return false;
                json = stored; return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static bool ValidRecordings(string json, string isrc)
        {
            try
            {
                var data = new JavaScriptSerializer { MaxJsonLength = MaximumJsonBytes }.DeserializeObject(json) as Dictionary<string, object>;
                object recordings;
                return data != null && String.Equals(Text(data, "isrc"), isrc, StringComparison.OrdinalIgnoreCase) &&
                    data.TryGetValue("recordings", out recordings) && recordings is object[];
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        internal static List<string> MatchingReleases(string json, Track track)
        {
            var result = new List<string>();
            try
            {
                string isrc = (track.Isrc ?? "").Replace("-", "").Trim();
                if (!ValidRecordings(json, isrc)) return result;
                var data = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = MaximumJsonBytes }.DeserializeObject(json);
                foreach (object item in (object[])data["recordings"])
                {
                    var recording = item as Dictionary<string, object>;
                    if (recording == null) continue;
                    double length;
                    if (!Double.TryParse(Text(recording, "length"), NumberStyles.Any, CultureInfo.InvariantCulture, out length) ||
                        length <= 0 || Double.IsNaN(length) || Double.IsInfinity(length) || Math.Abs(length / 1000 - track.DurationSeconds) > 5) continue;
                    object credits, releases;
                    if (!recording.TryGetValue("artist-credit", out credits) || !(credits is object[]) || ((object[])credits).Length == 0) continue;
                    var credit = ((object[])credits)[0] as Dictionary<string, object>;
                    if (credit == null) continue;
                    var names = new List<string> { Text(credit, "name") };
                    object artistObject;
                    if (credit.TryGetValue("artist", out artistObject))
                    {
                        var artist = artistObject as Dictionary<string, object>;
                        if (artist != null) { names.Add(Text(artist, "name")); names.Add(Text(artist, "sort-name")); }
                    }
                    if (!names.Any(name => Normalize(name) == Normalize(track.PrimaryArtist))) continue;
                    if (!recording.TryGetValue("releases", out releases) || !(releases is object[])) continue;
                    foreach (object releaseObject in (object[])releases)
                    {
                        var release = releaseObject as Dictionary<string, object>;
                        if (release == null || Normalize(Text(release, "title")) != Normalize(track.Album)) continue;
                        Guid id;
                        if (!Guid.TryParse(Text(release, "id"), out id)) continue;
                        string value = id.ToString("D");
                        if (!result.Contains(value)) result.Add(value);
                        if (result.Count == 3) return result;
                    }
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return result;
        }

        private async Task<FetchResult> FetchAsync(Uri start, int maximumBytes, string service, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Blocked(service)) return new FetchResult();
            await requests.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Blocked(service)) return new FetchResult();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    try
                    {
                        Uri current = start;
                        for (int redirect = 0; redirect <= 4; redirect++)
                        {
                            if (!Allowed(current, service)) return new FetchResult { PermanentMiss = true };
                            // .NET Framework can otherwise limit a host to two
                            // connections even though the artwork queue allows four.
                            var point=ServicePointManager.FindServicePoint(current);
                            if(point.ConnectionLimit<4)point.ConnectionLimit=4;
                            using (var request = new HttpRequestMessage(HttpMethod.Get, current))
                            using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                            {
                                int status = (int)response.StatusCode;
                                if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                                {
                                    Uri next;
                                    if (redirect == 4 || response.Headers.Location == null || !Uri.TryCreate(current, response.Headers.Location, out next) || !Allowed(next, service))
                                        return new FetchResult { PermanentMiss = true };
                                    current = next; continue;
                                }
                                if (status == 429 || status == 408 || status >= 500)
                                { Failed(service, status == 429 || status == 503, RetryDelay(response, utcNow())); return new FetchResult(); }
                                if (status != 200) return new FetchResult { PermanentMiss = true };
                                if (response.Content == null || response.Content.Headers.ContentLength > maximumBytes)
                                    return new FetchResult { PermanentMiss = true };
                                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                using (var output = new MemoryStream())
                                {
                                    var buffer = new byte[32768]; int read;
                                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                                    {
                                        if (output.Length + read > maximumBytes) return new FetchResult { PermanentMiss = true };
                                        output.Write(buffer, 0, read);
                                    }
                                    timeout.Token.ThrowIfCancellationRequested();
                                    lock (sync) { failures[service] = 0; }
                                    return new FetchResult { Bytes = output.ToArray(), SourceUrl = current.AbsoluteUri };
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); Failed(service, false); }
                    catch (HttpRequestException) { ct.ThrowIfCancellationRequested(); Failed(service, false); }
                    catch (IOException) { ct.ThrowIfCancellationRequested(); Failed(service, false); }
                }
                return new FetchResult();
            }
            finally { requests.Release(); }
        }

        private bool Blocked(string service)
        { lock (sync) { DateTime until; return blockedUntil.TryGetValue(service, out until) && utcNow() < until; } }

        internal static TimeSpan? RetryDelay(HttpResponseMessage response, DateTime now)
        {
            var retry = response.Headers.RetryAfter;
            if (retry == null) return null;
            TimeSpan delay = retry.Delta ?? (retry.Date.HasValue ? retry.Date.Value.UtcDateTime - now : TimeSpan.Zero);
            return TimeSpan.FromSeconds(Math.Max(0, Math.Min(86400, delay.TotalSeconds)));
        }

        private void Failed(string service, bool throttle, TimeSpan? serverDelay = null)
        {
            lock (sync)
            {
                int count; failures.TryGetValue(service, out count); failures[service] = ++count;
                if (throttle || count >= 3 || serverDelay.HasValue)
                {
                    double seconds = Math.Min(21600, 120 * Math.Pow(2, Math.Min(8, count - 1)));
                    if (serverDelay.HasValue) seconds = Math.Max(seconds, serverDelay.Value.TotalSeconds);
                    DateTime until = utcNow().AddSeconds(seconds), existing;
                    if (!blockedUntil.TryGetValue(service, out existing) || until > existing) blockedUntil[service] = until;
                }
            }
        }

        private SemaphoreSlim KeyGate(string key)
        {
            lock (sync)
            {
                SemaphoreSlim gate;
                if (!keyGates.TryGetValue(key, out gate)) keyGates.Add(key, gate = new SemaphoreSlim(1, 1));
                return gate;
            }
        }

        private static bool Allowed(Uri uri, string service)
        {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment)) return false;
            string host = uri.DnsSafeHost.ToLowerInvariant();
            if (service == "spotify") return host == "i.scdn.co" || host == "image-cdn-ak.spotifycdn.com" || host == "image-cdn-fa.spotifycdn.com";
            if (service == "musicbrainz") return host == "musicbrainz.org";
            return service == "archive" && (host == "coverartarchive.org" || host == "archive.org" || host.EndsWith(".archive.org", StringComparison.Ordinal));
        }

        private static string Normalize(string value)
        { return Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ").ToUpperInvariant(); }

        private static string Text(Dictionary<string, object> data, string key)
        { object value; return data.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : ""; }

        private static string Hash(string value) { return Hash(Encoding.UTF8.GetBytes(value)); }
        private static string Hash(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

        private static void AtomicBytes(string path, byte[] bytes)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void Dispose() { http.Dispose(); }
    }
}
