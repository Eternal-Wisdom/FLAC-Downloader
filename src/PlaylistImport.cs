using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    public sealed class Track
    {
        public int DiscNumber { get; set; }
        public int TrackNumber { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public double DurationSeconds { get; set; }
        public string SpotifyId { get; set; }
        public string Isrc { get; set; }
        public string CoverUrl { get; set; }
        public string[] Artists { get; set; }
        public string PrimaryArtist { get { return Artists != null && Artists.Length > 0 ? Artists[0] : Artist; } }
    }

    public sealed class Playlist
    {
        public string Name { get; set; }
        public List<Track> Tracks { get; set; }
        public string Source { get; set; }
        public int SkippedTracks { get; set; }
        public Playlist() { Tracks = new List<Track>(); }
    }

    public static class CsvPlaylist
    {
        public static Playlist Read(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
                return Parse(reader.ReadToEnd(), Path.GetFileNameWithoutExtension(path), path);
        }

        public static Playlist Parse(string csv, string name, string source)
        {
            if (String.IsNullOrWhiteSpace(csv)) throw new FormatException("The playlist CSV is empty.");
            csv = csv.TrimStart('\uFEFF');
            char delimiter = DetectDelimiter(csv);
            var rows = ParseRows(csv, delimiter);
            if (rows.Count == 0) throw new FormatException("The playlist CSV is empty.");
            var headers = rows[0].Select(NormalizeHeader).ToList();
            int title = Column(headers, "title", "trackname", "tracktitle", "songname", "songtitle", "track", "name");
            int artist = Column(headers, "artist", "artists", "artistnames", "artistname", "trackartists", "trackartist", "albumartist");
            int album = Column(headers, "album", "albumname", "albumtitle");
            int duration = Column(headers, "length", "duration", "durationseconds", "lengthseconds", "durationms", "durationmilliseconds", "trackdurationms", "time");
            int id = Column(headers, "spotifyid", "trackuri", "spotifyuri", "trackid", "uri");
            int isrc = Column(headers, "isrc");
            int artistIds = Column(headers, "artisturis", "artisturi", "artistids", "artistid");
            int artistJson = Column(headers, "artistnamesjson");
            int cover = Column(headers, "albumimageurl", "coverurl", "artworkurl");
            int disc=Column(headers,"discnumber","disc"), position=Column(headers,"tracknumber","trackposition");
            if (title < 0 || artist < 0)
                throw new FormatException("CSV needs title and artist columns (Exportify's Track Name and Artist Name(s) also work).");
            var result = new Playlist { Name = name, Source = source };
            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                string trackTitle = Cell(row, title), trackArtist = Cell(row, artist);
                if (String.IsNullOrWhiteSpace(trackTitle) || String.IsNullOrWhiteSpace(trackArtist))
                {
                    if (row.Any(x => !String.IsNullOrWhiteSpace(x))) result.SkippedTracks++;
                    continue;
                }
                string rawId = Cell(row, id);
                if (rawId.StartsWith("spotify:track:", StringComparison.Ordinal)) rawId = rawId.Substring(14);
                if (!Regex.IsMatch(rawId, "^[A-Za-z0-9]{22}$")) rawId = "";
                result.Tracks.Add(new Track {
                    DiscNumber=ParsePosition(Cell(row,disc)),TrackNumber=ParsePosition(Cell(row,position)),
                    Title = trackTitle, Artist = trackArtist, Album = Cell(row, album), SpotifyId = rawId,
                    Isrc = Cell(row, isrc), CoverUrl = NormalizeCoverUrl(Cell(row, cover)), Artists = ReadArtistNames(trackArtist, Cell(row, artistIds), Cell(row, artistJson)),
                    DurationSeconds = ParseDuration(Cell(row, duration), duration >= 0 && (headers[duration].EndsWith("ms", StringComparison.Ordinal) || headers[duration].EndsWith("milliseconds", StringComparison.Ordinal)))
                });
            }
            return result;
        }

        private static int ParsePosition(string value) {int result;return Int32.TryParse(value,out result) && result>0 && result<=9999 ? result : 0;}

        public static void Write(string path, Playlist playlist)
        {
            if (playlist == null) throw new ArgumentNullException("playlist");
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("title,artist,album,length,isrc,spotify_id,artist_names_json,cover_url,disc_number,track_number");
                foreach (var track in playlist.Tracks)
                {
                    // Unknown durations are blank, so a downloader does not reject candidates as zero-length.
                    string length = track.DurationSeconds > 0 && !Double.IsNaN(track.DurationSeconds) && !Double.IsInfinity(track.DurationSeconds)
                        ? track.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture) : "";
                    string artists = new JavaScriptSerializer().Serialize(track.Artists ?? new [] { track.Artist ?? "" });
                    writer.WriteLine(Quote(track.Title) + "," + Quote(track.Artist) + "," + Quote(track.Album) + "," + length + "," + Quote(track.Isrc) + "," + Quote(track.SpotifyId) + "," + Quote(artists) + "," + Quote(NormalizeCoverUrl(track.CoverUrl))+","+track.DiscNumber.ToString(CultureInfo.InvariantCulture)+","+track.TrackNumber.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        // Importing a URL only preserves metadata. The artwork downloader separately restricts fetch hosts.
        internal static string NormalizeCoverUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out uri) || uri.Scheme != "https" ||
                !uri.IsDefaultPort || String.IsNullOrEmpty(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo) ||
                !String.IsNullOrEmpty(uri.Fragment)) return "";
            return uri.AbsoluteUri;
        }

        // Exportify escapes literal artist-name commas as \, and separates credits with unescaped commas.
        // A matching URI count confirms that this is a list; generic CSV artist strings stay intact.
        public static string[] ParseArtistNames(string names, string artistIds)
        {
            names = names ?? "";
            if (String.IsNullOrWhiteSpace(artistIds)) return new [] { names };
            string[] ids = Regex.Split(artistIds.Trim(), @"\s*[,;]\s*");
            if (ids.Any(x => !Regex.IsMatch(x, @"^(?:spotify:artist:)?[A-Za-z0-9]{22}$"))) return new [] { names };
            var parsed = new List<string>(); var part = new StringBuilder();
            for (int i = 0; i < names.Length; i++)
            {
                char ch = names[i];
                if (ch == '\\' && i + 1 < names.Length && (names[i + 1] == ',' || names[i + 1] == '\\')) part.Append(names[++i]);
                else if (ch == ',') { parsed.Add(part.ToString().Trim()); part.Clear(); }
                else part.Append(ch);
            }
            parsed.Add(part.ToString().Trim());
            return parsed.Count == ids.Length && parsed.All(x => x.Length > 0) ? parsed.ToArray() : new [] { names };
        }

        private static string[] ReadArtistNames(string display, string ids, string json)
        {
            if (!String.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var names = new JavaScriptSerializer().Deserialize<string[]>(json);
                    if (names != null && names.Length > 0 && names.All(x => !String.IsNullOrWhiteSpace(x))) return names;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            return ParseArtistNames(display, ids);
        }

        public static double ParseDuration(string value, bool milliseconds)
        {
            if (String.IsNullOrWhiteSpace(value)) return 0;
            double seconds;
            if (value.IndexOf(':') >= 0)
            {
                string[] parts = value.Split(':');
                if (parts.Length < 2 || parts.Length > 3) return 0;
                seconds = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    double part;
                    if (!Double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out part) || part < 0 || (i > 0 && part >= 60)) return 0;
                    seconds = seconds * 60 + part;
                }
            }
            else
            {
                if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return 0;
                if (milliseconds) seconds /= 1000.0;
            }
            return seconds > 0 && !Double.IsInfinity(seconds) && !Double.IsNaN(seconds) ? seconds : 0;
        }

        private static string NormalizeHeader(string value) { return Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", ""); }
        private static int Column(List<string> headers, params string[] aliases)
        {
            foreach (string alias in aliases) { int found = headers.IndexOf(alias); if (found >= 0) return found; }
            return -1;
        }
        private static string Cell(List<string> row, int index) { return index >= 0 && index < row.Count ? row[index].Trim() : ""; }
        private static string Quote(string text) { return "\"" + (text ?? "").Replace("\"", "\"\"") + "\""; }
        private static char DetectDelimiter(string csv)
        {
            int comma = 0, semicolon = 0, tab = 0; bool quoted = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char ch = csv[i];
                if (ch == '"') quoted = !quoted;
                if (!quoted)
                {
                    if (ch == '\r' || ch == '\n') break;
                    if (ch == ',') comma++; else if (ch == ';') semicolon++; else if (ch == '\t') tab++;
                }
            }
            return tab > comma && tab > semicolon ? '\t' : semicolon > comma ? ';' : ',';
        }
        internal static List<List<string>> ParseRows(string csv, char delimiter)
        {
            var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder();
            bool quoted = false, closed = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char ch = csv[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                        else { quoted = false; closed = true; }
                    }
                    else field.Append(ch);
                }
                else if (ch == delimiter) { row.Add(field.ToString()); field.Clear(); closed = false; }
                else if (ch == '\r' || ch == '\n')
                {
                    row.Add(field.ToString()); rows.Add(row); row = new List<string>(); field.Clear(); closed = false;
                    if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                }
                else if (ch == '"')
                {
                    if (field.ToString().Trim().Length != 0 || closed) throw new FormatException("Malformed CSV: unexpected quote near row " + (rows.Count + 1) + ".");
                    field.Clear(); quoted = true;
                }
                else
                {
                    if (closed && ch != ' ' && ch != '\t') throw new FormatException("Malformed CSV after a quoted field near row " + (rows.Count + 1) + ".");
                    if (!closed) field.Append(ch);
                }
            }
            if (quoted) throw new FormatException("Malformed CSV: a quoted field is not closed.");
            if (field.Length > 0 || row.Count > 0 || closed) { row.Add(field.ToString()); rows.Add(row); }
            return rows;
        }
    }

    public sealed class SpotifyImporter : IDisposable
    {
        public const string RedirectUri = "http://127.0.0.1:48723/callback";
        private readonly HttpClient http;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim tokenGate = new SemaphoreSlim(1, 1);
        private string clientId, accessToken, refreshToken;
        private DateTime expiresAt;
        private bool disposed;
        public bool IsConnected { get { return !disposed && !String.IsNullOrEmpty(accessToken); } }
        internal static string ClientIdForStorage(string value)
        {
            value=(value ?? "").Trim();
            return Regex.IsMatch(value,"^[a-fA-F0-9]{32}$") ? value : "";
        }

        public SpotifyImporter() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
        internal SpotifyImporter(HttpMessageHandler handler)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        }

        public async Task ConnectAsync(string clientId, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            clientId = (clientId ?? "").Trim();
            if(clientId.StartsWith("spak_",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("This is a Spotify Soloist API key. Playlist import requires the 32-character Client ID from a Spotify Web API app, not a Soloist key. You can also import a CSV tracklist.");
            if (!Regex.IsMatch(clientId, "^[a-fA-F0-9]{32}$")) throw new ArgumentException("Enter the 32-character Client ID from your Spotify developer app. A client secret is not needed.");
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token, timeout.Token))
            {
                var listener = new TcpListener(IPAddress.Loopback, 48723);
                try
                {
                    try { listener.Start(); }
                    catch (SocketException ex) { throw new InvalidOperationException("Cannot open the Spotify sign-in callback on port 48723. Close another copy of this app and try again.", ex); }
                    string verifier = RandomUrlToken(64), state = RandomUrlToken(32), challenge;
                    using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
                    string url = "https://accounts.spotify.com/authorize?" + Query(new Dictionary<string, string> {
                        {"client_id", clientId}, {"response_type", "code"}, {"redirect_uri", RedirectUri},
                        {"scope", "playlist-read-private playlist-read-collaborative"}, {"state", state},
                        {"code_challenge_method", "S256"}, {"code_challenge", challenge}
                    });
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    string code = await AwaitAuthorizationAsync(listener, state, linked.Token).ConfigureAwait(false);
                    await tokenGate.WaitAsync(linked.Token).ConfigureAwait(false);
                    try
                    {
                        var payload = new Dictionary<string, string> {
                            {"grant_type", "authorization_code"}, {"client_id", clientId}, {"code", code},
                            {"redirect_uri", RedirectUri}, {"code_verifier", verifier}
                        };
                        var data = await TokenRequestAsync(payload, linked.Token).ConfigureAwait(false);
                        string newToken = StringValue(data, "access_token");
                        if (String.IsNullOrEmpty(newToken)) throw new InvalidOperationException("Spotify did not return an access token. Please reconnect.");
                        this.clientId = clientId;
                        accessToken = newToken; refreshToken = StringValue(data, "refresh_token");
                        expiresAt = DateTime.UtcNow.AddSeconds(Math.Max(30, NumberValue(data, "expires_in", 3600) - 60));
                    }
                    finally { tokenGate.Release(); }
                }
                catch (OperationCanceledException)
                {
                    if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !lifetime.IsCancellationRequested)
                        throw new TimeoutException("Spotify sign-in timed out after 3 minutes. Click Connect and try again.");
                    throw;
                }
                finally { listener.Stop(); }
            }
        }

        public async Task<Playlist> LoadAsync(string url, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (IsAlbumLink(url)) return await LoadAlbumAsync(url, cancellationToken).ConfigureAwait(false);
            string id = ParsePlaylistId(url);
            if (!IsConnected) throw new InvalidOperationException("Connect to Spotify before importing a playlist, or import a CSV file.");
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                string root = "https://api.spotify.com/v1/playlists/" + id;
                var metadata = Deserialize(await ApiGetAsync(root + "?fields=name", linked.Token).ConfigureAwait(false));
                var result = new Playlist { Name = StringValue(metadata, "name"), Source = "https://open.spotify.com/playlist/" + id };
                if (String.IsNullOrWhiteSpace(result.Name)) result.Name = "Spotify playlist";
                string next = root + "/items?limit=50&offset=0";
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (!String.IsNullOrEmpty(next))
                {
                    ValidatePageUrl(next, id);
                    if (!visited.Add(next) || visited.Count > 1000) throw new InvalidOperationException("Spotify returned an invalid playlist pagination response. Please retry the import.");
                    string json = await ApiGetAsync(next, linked.Token).ConfigureAwait(false);
                    ParseItemsPage(json, result, out next);
                }
                return result;
            }
        }

        private static bool IsAlbumLink(string input)
        {
            input = (input ?? "").Trim();
            Uri uri;
            return input.StartsWith("spotify:album:", StringComparison.Ordinal) ||
                (Uri.TryCreate(input, UriKind.Absolute, out uri) && Regex.IsMatch(uri.AbsolutePath, "/album/"));
        }

        public static string ParseAlbumId(string input)
        {
            input = (input ?? "").Trim();
            var match = Regex.Match(input, "^spotify:album:([A-Za-z0-9]{22})$");
            if (match.Success) return match.Groups[1].Value;
            Uri uri;
            if (Uri.TryCreate(input, UriKind.Absolute, out uri) &&
                (uri.Scheme == "https" || uri.Scheme == "http") && uri.IsDefaultPort &&
                String.Equals(uri.Host, "open.spotify.com", StringComparison.OrdinalIgnoreCase) && String.IsNullOrEmpty(uri.UserInfo))
            {
                match = Regex.Match(uri.AbsolutePath, "^/(?:intl-[A-Za-z-]{2,10}/)?album/([A-Za-z0-9]{22})/?$");
                if (match.Success) return match.Groups[1].Value;
            }
            throw new FormatException("Paste a Spotify album link or spotify:album: URI.");
        }

        private async Task<Playlist> LoadAlbumAsync(string url, CancellationToken cancellationToken)
        {
            string id = ParseAlbumId(url);
            if (!IsConnected) throw new InvalidOperationException("Connect to Spotify before importing an album, or import a CSV file.");
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                string root = "https://api.spotify.com/v1/albums/" + id;
                var album = Deserialize(await ApiGetAsync(root, linked.Token).ConfigureAwait(false));
                var result = new Playlist { Name = StringValue(album, "name"), Source = "https://open.spotify.com/album/" + id };
                if (String.IsNullOrWhiteSpace(result.Name)) result.Name = "Spotify album";
                var page = ObjectValue(album, "tracks");
                if (page == null) throw new FormatException("Spotify returned an album without its track list.");
                string next;
                ParseAlbumTracksPage(new JavaScriptSerializer().Serialize(page), album, result, out next);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (!String.IsNullOrEmpty(next))
                {
                    ValidateAlbumPageUrl(next, id);
                    if (!visited.Add(next) || visited.Count > 1000) throw new FormatException("Spotify returned invalid album pagination.");
                    ParseAlbumTracksPage(await ApiGetAsync(next, linked.Token).ConfigureAwait(false), album, result, out next);
                }
                return result;
            }
        }

        internal static void ParseAlbumTracksPage(string json, Dictionary<string, object> album, Playlist target, out string next)
        {
            var page = Deserialize(json);
            var albumInfo = new Dictionary<string, object> { { "name", StringValue(album, "name") } };
            object images;
            if (album.TryGetValue("images", out images)) albumInfo["images"] = images;
            object items;
            if (!page.TryGetValue("items", out items) || !(items is object[])) throw new FormatException("Spotify returned an album page without its tracks.");
            var wrapped = new List<object>();
            foreach (object item in (object[])items)
            {
                var track = item as Dictionary<string, object>;
                if (track != null) track["album"] = albumInfo;
                wrapped.Add(new Dictionary<string, object> { { "item", track } });
            }
            ParseItemsPage(new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "items", wrapped }, { "next", StringValue(page, "next") } }), target, out next);
        }

        internal static void ValidateAlbumPageUrl(string url, string id)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
                !String.Equals(uri.Host, "api.spotify.com", StringComparison.OrdinalIgnoreCase) ||
                !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment) ||
                uri.AbsolutePath != "/v1/albums/" + id + "/tracks")
                throw new FormatException("Spotify returned an unexpected album pagination URL.");
        }

        public static string ParsePlaylistId(string input)
        {
            input = (input ?? "").Trim();
            if (Regex.IsMatch(input, "^[A-Za-z0-9]{22}$")) return input;
            var uriMatch = Regex.Match(input, "^spotify:playlist:([A-Za-z0-9]{22})$");
            if (uriMatch.Success) return uriMatch.Groups[1].Value;
            Uri uri;
            if (Uri.TryCreate(input, UriKind.Absolute, out uri) &&
                (uri.Scheme == "https" || uri.Scheme == "http") && uri.IsDefaultPort &&
                String.Equals(uri.Host, "open.spotify.com", StringComparison.OrdinalIgnoreCase) && String.IsNullOrEmpty(uri.UserInfo))
            {
                var match = Regex.Match(uri.AbsolutePath, "^/(?:intl-[A-Za-z-]{2,10}/)?playlist/([A-Za-z0-9]{22})/?$");
                if (match.Success) return match.Groups[1].Value;
            }
            throw new FormatException("Paste a Spotify playlist link, spotify:playlist: URI, or 22-character playlist ID. Album, track and shortened links are not playlist links.");
        }

        public static void ParseItemsPage(string json, Playlist target, out string next)
        {
            if (target == null) throw new ArgumentNullException("target");
            var data = Deserialize(json);
            object itemsObject;
            if (!data.TryGetValue("items", out itemsObject) || !(itemsObject is object[])) throw new FormatException("Spotify returned a playlist page without its items list.");
            foreach (object entry in (object[])itemsObject)
            {
                var wrapper = entry as Dictionary<string, object>;
                var track = ObjectValue(wrapper, "item") ?? ObjectValue(wrapper, "track");
                if (track == null || BooleanValue(wrapper, "is_local", false) || BooleanValue(track, "is_local", false) ||
                    (StringValue(track, "type") != "" && StringValue(track, "type") != "track"))
                { target.SkippedTracks++; continue; }
                string title = StringValue(track, "name");
                var artistNames = new List<string>();
                object artistsObject;
                if (track.TryGetValue("artists", out artistsObject) && artistsObject is object[])
                    foreach (object artist in (object[])artistsObject)
                    {
                        string name = StringValue(artist as Dictionary<string, object>, "name");
                        if (!String.IsNullOrWhiteSpace(name)) artistNames.Add(name);
                    }
                if (String.IsNullOrWhiteSpace(title) || artistNames.Count == 0) { target.SkippedTracks++; continue; }
                target.Tracks.Add(new Track {
                    DiscNumber=(int)Math.Max(0,Math.Min(9999,NumberValue(track,"disc_number",0))),TrackNumber=(int)Math.Max(0,Math.Min(9999,NumberValue(track,"track_number",0))),
                    Title = title, Artist = String.Join(", ", artistNames), Artists = artistNames.ToArray(), Album = StringValue(ObjectValue(track, "album"), "name"),
                    DurationSeconds = Math.Max(0, NumberValue(track, "duration_ms", 0) / 1000.0), SpotifyId = StringValue(track, "id"),
                    Isrc = StringValue(ObjectValue(track, "external_ids"), "isrc"), CoverUrl = SelectCoverUrl(ObjectValue(track, "album"))
                });
            }
            next = StringValue(data, "next");
        }

        private static string SelectCoverUrl(Dictionary<string, object> album)
        {
            object images;
            if (album == null || !album.TryGetValue("images", out images) || !(images is object[])) return "";
            string best = "", larger = "", unknown = "";
            double bestSize = 0, largerSize = Double.MaxValue;
            foreach (object item in (object[])images)
            {
                var image = item as Dictionary<string, object>;
                string url = CsvPlaylist.NormalizeCoverUrl(StringValue(image, "url"));
                if (url.Length == 0) continue;
                double width = NumberValue(image, "width", 0), height = NumberValue(image, "height", 0);
                double size = Math.Max(width, height);
                if (size <= 0) { if (unknown.Length == 0) unknown = url; }
                else if (size <= 1000 && size > bestSize) { best = url; bestSize = size; }
                else if (size > 1000 && size < largerSize) { larger = url; largerSize = size; }
            }
            return best.Length > 0 ? best : larger.Length > 0 ? larger : unknown;
        }

        internal static void ValidatePageUrl(string url, string playlistId)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
                !String.Equals(uri.Host, "api.spotify.com", StringComparison.OrdinalIgnoreCase) ||
                !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment) ||
                uri.AbsolutePath != "/v1/playlists/" + playlistId + "/items")
                throw new FormatException("Spotify returned an unexpected pagination URL.");
        }

        private async Task<string> ApiGetAsync(string url, CancellationToken ct)
        {
            bool refreshed = false; int rateRetries = 0;
            if (DateTime.UtcNow >= expiresAt) await RefreshAsync(ct).ConfigureAwait(false);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    using (var response = await http.SendAsync(request, ct).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
                        { refreshed = true; await RefreshAsync(ct).ConfigureAwait(false); continue; }
                        if ((int)response.StatusCode == 429 && rateRetries++ < 3)
                        { await DelayForRateLimitAsync(response, ct).ConfigureAwait(false); continue; }
                        if (response.StatusCode == HttpStatusCode.Forbidden)
                            throw new InvalidOperationException(url.Contains("/v1/albums/")
                                ? "Spotify refused album access. Check your app and allowed user account settings, or import an album CSV tracklist."
                                : "Spotify refused access. Its 2026 playlist API requires you to own the playlist or be a collaborator. Development-mode apps also require a Premium app owner and an allowed user account. Check those settings, or import an existing playlist CSV.");
                        if (response.StatusCode == HttpStatusCode.NotFound)
                            throw new InvalidOperationException("Spotify could not find this playlist or album for your account. Check the link and your access, or import a CSV.");
                        if ((int)response.StatusCode == 429)
                            throw new InvalidOperationException("Spotify is still rate-limiting requests. Wait a few minutes and try again.");
                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        { accessToken = null; throw new InvalidOperationException("Spotify sign-in expired. Please connect again."); }
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("Spotify returned HTTP " + (int)response.StatusCode + ". Try again later.");
                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task RefreshAsync(CancellationToken ct)
        {
            await tokenGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (String.IsNullOrEmpty(refreshToken))
                { accessToken = null; throw new InvalidOperationException("Spotify sign-in expired. Please connect again."); }
                var data = await TokenRequestAsync(new Dictionary<string, string> {
                    {"grant_type", "refresh_token"}, {"refresh_token", refreshToken}, {"client_id", clientId}
                }, ct).ConfigureAwait(false);
                string token = StringValue(data, "access_token");
                if (String.IsNullOrEmpty(token)) throw new InvalidOperationException("Spotify did not return an access token. Please reconnect.");
                accessToken = token;
                string replacement = StringValue(data, "refresh_token");
                if (!String.IsNullOrEmpty(replacement)) refreshToken = replacement;
                expiresAt = DateTime.UtcNow.AddSeconds(Math.Max(30, NumberValue(data, "expires_in", 3600) - 60));
            }
            finally { tokenGate.Release(); }
        }

        private async Task<Dictionary<string, object>> TokenRequestAsync(Dictionary<string, string> values, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                using (var body = new FormUrlEncodedContent(values))
                using (var response = await http.PostAsync("https://accounts.spotify.com/api/token", body, ct).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode == 429 && attempt < 3)
                    { await DelayForRateLimitAsync(response, ct).ConfigureAwait(false); continue; }
                    if (!response.IsSuccessStatusCode)
                    {
                        if (values["grant_type"] == "refresh_token" && response.StatusCode == HttpStatusCode.BadRequest)
                        { accessToken = null; refreshToken = null; }
                        throw new InvalidOperationException("Spotify could not complete sign-in (HTTP " + (int)response.StatusCode + "). Verify your app's Client ID and exact redirect URI: " + RedirectUri + ". Then connect again.");
                    }
                    return Deserialize(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }

        private static async Task DelayForRateLimitAsync(HttpResponseMessage response, CancellationToken ct)
        {
            double seconds = 3;
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue) seconds = response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                else if (response.Headers.RetryAfter.Date.HasValue) seconds = (response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
            }
            if (seconds > 60) throw new InvalidOperationException("Spotify has asked for a longer pause. Wait " + Math.Ceiling(seconds / 60) + " minute(s), then retry.");
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, seconds)), ct).ConfigureAwait(false);
        }

        private static async Task<string> AwaitAuthorizationAsync(TcpListener listener, string expectedState, CancellationToken ct)
        {
            using (ct.Register(delegate { listener.Stop(); }))
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                    catch (ObjectDisposedException) { ct.ThrowIfCancellationRequested(); throw; }
                    catch (SocketException) { ct.ThrowIfCancellationRequested(); throw; }
                    using (client)
                    using (ct.Register(delegate { client.Close(); }))
                    {
                        string request;
                        try { request = await ReadRequestAsync(client.GetStream(), ct).ConfigureAwait(false); }
                        catch (IOException) { ct.ThrowIfCancellationRequested(); continue; }
                        catch (ObjectDisposedException) { ct.ThrowIfCancellationRequested(); continue; }
                        string code, error;
                        bool accepted = ParseCallbackRequest(request, expectedState, out code, out error);
                        string text = accepted && String.IsNullOrEmpty(error) ? "Spotify sign-in received. You can close this tab and return to FLAC-Downloader." :
                            accepted ? "Spotify sign-in was declined. Return to FLAC-Downloader to try again." : "This callback was not accepted. Return to the Spotify sign-in tab.";
                        byte[] page = Encoding.UTF8.GetBytes("<!doctype html><html><head><meta charset=\"utf-8\"><title>FLAC-Downloader</title></head><body><p>" + text + "</p></body></html>");
                        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + (accepted ? "200 OK" : "400 Bad Request") + "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\nContent-Length: " + page.Length + "\r\n\r\n");
                        try
                        {
                            await client.GetStream().WriteAsync(header, 0, header.Length, ct).ConfigureAwait(false);
                            await client.GetStream().WriteAsync(page, 0, page.Length, ct).ConfigureAwait(false);
                        }
                        catch (IOException) { ct.ThrowIfCancellationRequested(); }
                        catch (ObjectDisposedException) { ct.ThrowIfCancellationRequested(); }
                        if (!accepted) continue;
                        if (!String.IsNullOrEmpty(error)) throw new InvalidOperationException("Spotify sign-in was declined or failed. Click Connect to try again.");
                        return code;
                    }
                }
            }
        }

        internal static bool ParseCallbackRequest(string request, string expectedState, out string code, out string error)
        {
            code = null; error = null;
            if (String.IsNullOrEmpty(request)) return false;
            string firstLine = request.Split(new [] { "\r\n" }, StringSplitOptions.None)[0];
            string[] pieces = firstLine.Split(' ');
            if (pieces.Length != 3 || pieces[0] != "GET" || !pieces[2].StartsWith("HTTP/1.", StringComparison.Ordinal) || !pieces[1].StartsWith("/callback?", StringComparison.Ordinal)) return false;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string part in pieces[1].Substring(10).Split('&'))
            {
                string[] pair = part.Split(new [] { '=' }, 2);
                if (pair.Length != 2) return false;
                string key = Uri.UnescapeDataString(pair[0]);
                if (values.ContainsKey(key)) return false;
                values.Add(key, Uri.UnescapeDataString(pair[1].Replace("+", " ")));
            }
            string state;
            if (!values.TryGetValue("state", out state) || !FixedTimeEquals(state, expectedState)) return false;
            values.TryGetValue("code", out code); values.TryGetValue("error", out error);
            return !String.IsNullOrEmpty(code) != !String.IsNullOrEmpty(error);
        }

        private static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new byte[1024]; var request = new StringBuilder();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(5000);
                using (timeout.Token.Register(delegate { stream.Dispose(); }))
                {
                    while (request.Length < 16384)
                    {
                        int count = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                        if (count == 0) return "";
                        request.Append(Encoding.ASCII.GetString(buffer, 0, count));
                        if (request.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) >= 0) return request.ToString();
                    }
                }
            }
            return "";
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }
        private static string Query(Dictionary<string, string> values) { return String.Join("&", values.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))); }
        private static string RandomUrlToken(int count) { var bytes = new byte[count]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Base64Url(bytes); }
        private static string Base64Url(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        private static Dictionary<string, object> Deserialize(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            var result = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (result == null) throw new FormatException("Spotify returned an unexpected response.");
            return result;
        }
        private static Dictionary<string, object> ObjectValue(Dictionary<string, object> data, string key) { object value; return data != null && data.TryGetValue(key, out value) ? value as Dictionary<string, object> : null; }
        private static string StringValue(Dictionary<string, object> data, string key) { object value; return data != null && data.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : ""; }
        private static bool BooleanValue(Dictionary<string, object> data, string key, bool fallback) { object value; return data != null && data.TryGetValue(key, out value) && value is bool ? (bool)value : fallback; }
        private static double NumberValue(Dictionary<string, object> data, string key, double fallback) { double value; return Double.TryParse(StringValue(data, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !Double.IsInfinity(value) && !Double.IsNaN(value) ? value : fallback; }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException("SpotifyImporter"); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; lifetime.Cancel(); http.Dispose();
            accessToken = null; refreshToken = null; clientId = null;
        }
    }
}
