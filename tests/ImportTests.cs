using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlaylistFlac
{
    public static class ImportTests
    {
        private const string Id = "0000000000000000000000";
        private static int checks;

        public static void Run()
        {
            checks = 0;
            TestCsv();
            TestUrls();
            TestSpotifyPages();
            TestCallbackValidation();
            TestHttpFlow().GetAwaiter().GetResult();
            TestAlbumFlow().GetAwaiter().GetResult();
            Console.WriteLine("Importer: " + checks + " checks passed.");
        }

#if IMPORT_TEST_RUNNER
        public static void Main() { Run(); }
#endif

        private static void TestCsv()
        {
            string fixture = "\uFEFFTrack URI,Track Name,Artist Name(s),Album Name,Duration (ms)\r\n" +
                "spotify:track:" + Id + ",\"Hello, \"\"World\"\"\",Björk; A Guest,\"Album\r\nTwo\",185125\r\n" +
                "spotify:track:" + Id + ",Missing artist,,,190000\r\n" +
                ",Second,Another,Other,not-a-number\r\n\r\n";
            Playlist list = CsvPlaylist.Parse(fixture, "Example", "fixture");
            Equal(2, list.Tracks.Count, "Exportify valid rows");
            Equal(1, list.SkippedTracks, "missing artist is counted, blank row ignored");
            Equal("Hello, \"World\"", list.Tracks[0].Title, "quoted comma and escaped quote");
            Equal("Björk; A Guest", list.Tracks[0].Artist, "Unicode artist");
            Equal("Album\r\nTwo", list.Tracks[0].Album, "multiline field");
            Equal(185.125, list.Tracks[0].DurationSeconds, "milliseconds conversion");
            Equal(Id, list.Tracks[0].SpotifyId, "track URI conversion");
            Equal("", list.Tracks[0].CoverUrl, "old CSV without artwork stays compatible");
            Equal(0.0, list.Tracks[1].DurationSeconds, "invalid duration stays unknown");
            Equal(3723.5, CsvPlaylist.ParseDuration("1:02:03.5", false), "hours duration");
            Equal(243.0, CsvPlaylist.ParseDuration("4:03", false), "minutes duration");
            Equal(0.0, CsvPlaylist.ParseDuration("1:75", false), "invalid timestamp");
            Equal(0.0, CsvPlaylist.ParseDuration("NaN", false), "NaN duration");
            Equal(0.0, CsvPlaylist.ParseDuration("Infinity", false), "infinite duration");
            Equal(0.0, CsvPlaylist.ParseDuration("-20", false), "negative duration");
            Equal(1, CsvPlaylist.Parse("Title;Artist;Length\nExample;Person;4:03", "Semi", "fixture").Tracks.Count, "semicolon CSV");
            Equal("Tabbed", CsvPlaylist.Parse("title\tartist\nTabbed\tPerson", "Tab", "fixture").Tracks[0].Title, "tab separated");
            Throws<FormatException>(delegate { CsvPlaylist.Parse("title,album\nExample,Album", "Bad", "fixture"); }, "missing required header");
            Throws<FormatException>(delegate { CsvPlaylist.Parse("title,artist\n\"Unclosed,Artist", "Bad", "fixture"); }, "unclosed quoted field");
            Throws<FormatException>(delegate { CsvPlaylist.Parse("title,artist\n\"Title\"garbage,Artist", "Bad", "fixture"); }, "garbage after quote");
            var escaped = CsvPlaylist.Parse("Track Name,Artist Name(s),Artist URI(s),ISRC\n\"See You Again\",\"Tyler\\, The Creator, Kali Uchis\",\"spotify:artist:4V8LLVI7PbaPR0K2TGSxFF, spotify:artist:1U1el3k54VvEUzo3ybLPlM\",USQX91701275", "Escaped", "fixture").Tracks[0];
            Equal(@"Tyler\, The Creator, Kali Uchis", escaped.Artist, "original artist identity remains exact");
            Equal(2, escaped.Artists.Length, "Exportify escaped comma not a credit separator");
            Equal("Tyler, The Creator", escaped.PrimaryArtist, "primary artist unescaped");
            Equal("Kali Uchis", escaped.Artists[1], "secondary artist retained");
            Equal("USQX91701275", escaped.Isrc, "ISRC retained");
            Equal(1, CsvPlaylist.ParseArtistNames("Earth, Wind & Fire", "").Length, "generic CSV comma name preserved");
            Equal(1, CsvPlaylist.ParseArtistNames("First, Second", "spotify:artist:" + Id).Length, "mismatched artist ID count not guessed");
            Equal("Earth, Wind & Fire", CsvPlaylist.ParseArtistNames(@"Earth\, Wind & Fire", "spotify:artist:" + Id)[0], "single escaped artist with known ID");
            list.Tracks[0].Isrc = "TEST01234567";
            list.Tracks[0].Artists = new [] { "Björk", "A Guest" };
            list.Tracks[0].CoverUrl = "https://i.scdn.co/image/album?label=日本語&part=one,two";
            string path = Path.Combine(Path.GetTempPath(), "playlist-flac-import-test-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                CsvPlaylist.Write(path, list);
                Playlist restored = CsvPlaylist.Read(path);
                Equal(list.Tracks.Count, restored.Tracks.Count, "normalized CSV roundtrip count");
                Equal(list.Tracks[0].Title, restored.Tracks[0].Title, "normalized CSV roundtrip quotes");
                Equal(list.Tracks[0].DurationSeconds, restored.Tracks[0].DurationSeconds, "normalized CSV uses seconds");
                Equal(list.Tracks[0].Artist, restored.Tracks[0].Artist, "normalized CSV uses UTF8");
                Equal("", CsvPlaylist.ParseRows(File.ReadAllText(path), ',')[2][3], "unknown duration exports blank");
                Equal(list.Tracks[0].Isrc, restored.Tracks[0].Isrc, "ISRC roundtrip");
                Equal(list.Tracks[0].SpotifyId, restored.Tracks[0].SpotifyId, "Spotify ID roundtrip");
                Equal(2, restored.Tracks[0].Artists.Length, "structured artists roundtrip");
                Equal("Björk", restored.Tracks[0].PrimaryArtist, "primary artist roundtrip");
                Equal(CsvPlaylist.NormalizeCoverUrl(list.Tracks[0].CoverUrl), restored.Tracks[0].CoverUrl, "artwork URL roundtrip preserves Unicode and commas");
                Equal("", restored.Tracks[1].CoverUrl, "empty artwork URL roundtrip");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            foreach (string header in new [] { "Album Image URL", "cover_url", "artwork_url" })
            {
                Playlist withCover = CsvPlaylist.Parse("Track Name,Artist Name(s)," + header + "\n\"夜に, 駆ける\",YOASOBI,https://i.scdn.co/image/cover", "Covers", "fixture");
                Equal("https://i.scdn.co/image/cover", withCover.Tracks[0].CoverUrl, "artwork header " + header);
                Equal("夜に, 駆ける", withCover.Tracks[0].Title, "artwork does not change quoted Unicode titles");
            }
            foreach (string invalid in new [] { "http://i.scdn.co/image/cover", "file:///C:/cover.jpg", "https://name@i.scdn.co/image/cover", "https://i.scdn.co:8443/image/cover", "https://i.scdn.co/image/cover#fragment", "not a URL" })
                Equal("", CsvPlaylist.NormalizeCoverUrl(invalid), "unsafe artwork URL rejected " + invalid);
            Equal("https://example.test/cover.jpg", CsvPlaylist.NormalizeCoverUrl(" https://example.test/cover.jpg "), "valid general HTTPS import remains available for provider validation");
        }

        private static void TestUrls()
        {
            Equal(Id, SpotifyImporter.ParsePlaylistId(Id), "bare playlist ID");
            Equal(Id, SpotifyImporter.ParsePlaylistId("spotify:playlist:" + Id), "playlist URI");
            Equal(Id, SpotifyImporter.ParsePlaylistId("https://open.spotify.com/playlist/" + Id + "?si=foo"), "share URL");
            Equal(Id, SpotifyImporter.ParsePlaylistId(" https://open.spotify.com/intl-de/playlist/" + Id + "/ "), "localized URL");
            foreach (string invalid in new [] {
                "https://open.spotify.com.evil.example/playlist/" + Id,
                "https://open.spotify.com@evil.example/playlist/" + Id,
                "https://name@open.spotify.com/playlist/" + Id,
                "https://open.spotify.com:8443/playlist/" + Id,
                "https://open.spotify.com/album/" + Id,
                "spotify:track:" + Id,
                "https://open.spotify.com/playlist/" + Id + "/extra",
                "spotify:playlist:short", ""
            })
                Throws<FormatException>(delegate { SpotifyImporter.ParsePlaylistId(invalid); }, "reject invalid playlist link " + invalid);
            SpotifyImporter.ValidatePageUrl("https://api.spotify.com/v1/playlists/" + Id + "/items?offset=50&limit=50", Id);
            checks++;
            Throws<FormatException>(delegate { SpotifyImporter.ValidatePageUrl("https://evil.example/v1/playlists/" + Id + "/items", Id); }, "pagination cannot leak token to another host");
            Throws<FormatException>(delegate { SpotifyImporter.ValidatePageUrl("http://api.spotify.com/v1/playlists/" + Id + "/items", Id); }, "pagination cannot downgrade HTTPS");
            Throws<FormatException>(delegate { SpotifyImporter.ValidatePageUrl("https://api.spotify.com/v1/me", Id); }, "pagination cannot change resource");
        }

        private static void TestSpotifyPages()
        {
            var playlist = new Playlist(); string next;
            SpotifyImporter.ParseItemsPage("{\"items\":[" +
                "{\"item\":{\"type\":\"track\",\"id\":\"" + Id + "\",\"name\":\"Track one\",\"duration_ms\":212340,\"artists\":[{\"name\":\"One\"},{\"name\":\"Two\"}],\"album\":{\"name\":\"Album\"},\"external_ids\":{\"isrc\":\"TEST01234567\"}}}," +
                "{\"track\":{\"name\":\"Legacy track\",\"artists\":[{\"name\":\"Legacy artist\"}]}}," +
                "{\"item\":null},{\"item\":{\"type\":\"episode\",\"name\":\"Podcast\"}}," +
                "{\"is_local\":true,\"item\":{\"name\":\"Local\",\"artists\":[{\"name\":\"Artist\"}]}}," +
                "{\"item\":{\"name\":\"No artist\",\"artists\":[]}},null],\"next\":null}", playlist, out next);
            Equal(2, playlist.Tracks.Count, "current and legacy Spotify schemas");
            Equal(5, playlist.SkippedTracks, "unavailable, local, episode, and invalid Spotify records counted");
            Equal("One, Two", playlist.Tracks[0].Artist, "all artists retained");
            Equal(2, playlist.Tracks[0].Artists.Length, "Spotify structured artists");
            Equal("One", playlist.Tracks[0].PrimaryArtist, "Spotify primary artist");
            Equal("TEST01234567", playlist.Tracks[0].Isrc, "Spotify ISRC");
            Equal(212.34, playlist.Tracks[0].DurationSeconds, "Spotify milliseconds converted");
            Equal("", playlist.Tracks[1].Album, "missing album handled");
            Equal("", playlist.Tracks[1].CoverUrl, "missing Spotify artwork handled");
            Equal("", next, "null pagination handled");
            Throws<FormatException>(delegate { string ignored; SpotifyImporter.ParseItemsPage("{\"error\":\"oops\"}", playlist, out ignored); }, "missing Spotify items list rejected");
            Equal("https://i.scdn.co/image/640", SpotifyCover("[{\"url\":\"https://i.scdn.co/image/1600\",\"width\":1600,\"height\":1600},{\"url\":\"https://i.scdn.co/image/64\",\"width\":64,\"height\":64},{\"url\":\"https://i.scdn.co/image/640\",\"width\":640,\"height\":640}]"), "Spotify uses largest artwork at or below 1000 pixels");
            Equal("https://i.scdn.co/image/1000", SpotifyCover("[{\"url\":\"https://i.scdn.co/image/640\",\"width\":640,\"height\":640},{\"url\":\"https://i.scdn.co/image/1000\",\"width\":1000,\"height\":1000}]"), "Spotify image size limit is inclusive");
            Equal("https://i.scdn.co/image/1200", SpotifyCover("[{\"url\":\"https://i.scdn.co/image/2000\",\"width\":2000,\"height\":2000},{\"url\":\"https://i.scdn.co/image/1200\",\"width\":1200,\"height\":1200}]"), "Spotify falls back to smallest larger artwork");
            Equal("https://i.scdn.co/image/unknown", SpotifyCover("[{\"url\":\"http://i.scdn.co/image/640\",\"width\":640,\"height\":640},null,{\"url\":\"https://i.scdn.co/image/unknown\",\"width\":null,\"height\":null}]"), "Spotify skips unsafe artwork and accepts unknown dimensions as last fallback");
            Equal("", SpotifyCover("[]"), "Spotify empty artwork list");
        }

        private static string SpotifyCover(string images)
        {
            var playlist = new Playlist(); string next;
            SpotifyImporter.ParseItemsPage("{\"items\":[{\"item\":{\"name\":\"日本語の曲\",\"artists\":[{\"name\":\"Artist\"}],\"album\":{\"name\":\"Album\",\"images\":" + images + "}}}],\"next\":null}", playlist, out next);
            return playlist.Tracks[0].CoverUrl;
        }

        private static void TestCallbackValidation()
        {
            string code, error;
            True(SpotifyImporter.ParseCallbackRequest("GET /callback?code=abc%2Bdef&state=expected HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n", "expected", out code, out error), "valid callback");
            Equal("abc+def", code, "callback decodes code");
            True(!SpotifyImporter.ParseCallbackRequest("GET /callback?code=x&state=wrong HTTP/1.1", "expected", out code, out error), "reject wrong OAuth state");
            True(!SpotifyImporter.ParseCallbackRequest("GET /callback?code=x&state=expected&state=wrong HTTP/1.1", "expected", out code, out error), "reject duplicate state");
            True(!SpotifyImporter.ParseCallbackRequest("POST /callback?code=x&state=expected HTTP/1.1", "expected", out code, out error), "reject callback method");
            True(!SpotifyImporter.ParseCallbackRequest("GET /wrong?code=x&state=expected HTTP/1.1", "expected", out code, out error), "reject callback path");
            True(!SpotifyImporter.ParseCallbackRequest("GET /callback?code=x&error=denied&state=expected HTTP/1.1", "expected", out code, out error), "reject ambiguous callback");
            True(SpotifyImporter.ParseCallbackRequest("GET /callback?error=access_denied&state=expected HTTP/1.1", "expected", out code, out error), "accept authenticated denial");
            Equal("access_denied", error, "callback denial reason");
        }

        private static async Task TestHttpFlow()
        {
            var handler = new FixtureHandler();
            using (var importer = new SpotifyImporter(handler))
            {
                AuthenticateFixture(importer);
                Playlist playlist = await importer.LoadAsync("https://open.spotify.com/playlist/" + Id + "?note=/album/", CancellationToken.None);
                Equal(2, playlist.Tracks.Count, "HTTP paginated result");
                Equal("Fixture playlist", playlist.Name, "HTTP playlist name");
                Equal(1, handler.RefreshRequests, "401 refreshes once");
                Equal(2, handler.ItemRequests, "all Spotify pages requested");
                True(handler.CorrectAuthorization, "refreshed token applied to API requests only");
            }
            using (var importer = new SpotifyImporter(new StaticHandler(HttpStatusCode.Forbidden)))
            {
                AuthenticateFixture(importer);
                await ThrowsAsync<InvalidOperationException>(delegate { return importer.LoadAsync(Id, CancellationToken.None); }, "owner", "403 access explanation");
            }
            using (var importer = new SpotifyImporter(new StaticHandler((HttpStatusCode)429)))
            {
                AuthenticateFixture(importer);
                await ThrowsAsync<InvalidOperationException>(delegate { return importer.LoadAsync(Id, CancellationToken.None); }, "pause", "long Retry-After is bounded");
            }
            using (var importer = new SpotifyImporter(new StaticHandler(HttpStatusCode.OK)))
            using (var cancel = new CancellationTokenSource())
            {
                AuthenticateFixture(importer); cancel.Cancel();
                await ThrowsAsync<OperationCanceledException>(delegate { return importer.LoadAsync(Id, cancel.Token); }, "", "import honors cancellation");
            }
        }

        private static void AuthenticateFixture(SpotifyImporter importer)
        {
            SetField(importer, "clientId", new string('a', 32));
            SetField(importer, "accessToken", "fixture-old");
            SetField(importer, "refreshToken", "fixture-refresh");
            SetField(importer, "expiresAt", DateTime.UtcNow.AddHours(1));
        }

        private static async Task TestAlbumFlow()
        {
            Equal(Id, SpotifyImporter.ParseAlbumId("spotify:album:" + Id), "album URI");
            Equal(Id, SpotifyImporter.ParseAlbumId("https://open.spotify.com/intl-ja/album/" + Id + "?si=share"), "localized album share URL");
            Throws<FormatException>(delegate { SpotifyImporter.ParseAlbumId("https://evil.example/album/" + Id); }, "album host validated");
            Throws<FormatException>(delegate { SpotifyImporter.ParseAlbumId("https://open.spotify.com/track/" + Id); }, "album cannot be track");
            Throws<FormatException>(delegate { SpotifyImporter.ValidateAlbumPageUrl("https://evil.example/v1/albums/" + Id + "/tracks", Id); }, "album pagination host validated");
            Throws<FormatException>(delegate { SpotifyImporter.ValidateAlbumPageUrl("https://api.spotify.com/v1/albums/other/tracks", Id); }, "album pagination identity validated");
            var handler = new AlbumHandler();
            using (var importer = new SpotifyImporter(handler))
            {
                AuthenticateFixture(importer);
                var result = await importer.LoadAsync("https://open.spotify.com/album/" + Id, CancellationToken.None);
                Equal("Fixture album", result.Name, "album display name");
                Equal("https://open.spotify.com/album/" + Id, result.Source, "album has independent source folder identity");
                Equal(2, result.Tracks.Count, "all album pages loaded");
                Equal("日本語の曲", result.Tracks[0].Title, "album Unicode title");
                Equal("Second song", result.Tracks[1].Title, "album order preserved");
                Equal("Fixture album", result.Tracks[1].Album, "album metadata propagated to later pages");
                Equal("https://i.scdn.co/image/album", result.Tracks[1].CoverUrl, "album cover propagated to later pages");
                Equal(180.0, result.Tracks[0].DurationSeconds, "album duration preserved");
                Equal(1, handler.TrackRequests, "embedded first page avoids redundant request");
            }
            foreach (string invalidNext in new[] { "https://evil.example/tracks", "https://api.spotify.com/v1/albums/" + Id + "/tracks?offset=50" })
            using (var importer = new SpotifyImporter(new AlbumHandler { NextOverride = invalidNext }))
            {
                AuthenticateFixture(importer);
                await ThrowsAsync<FormatException>(delegate { return importer.LoadAsync("spotify:album:" + Id, CancellationToken.None); }, "", "album rejects host changes and pagination cycles");
            }
            using (var importer = new SpotifyImporter(new StaticHandler(HttpStatusCode.Forbidden)))
            {
                AuthenticateFixture(importer);
                await ThrowsAsync<InvalidOperationException>(delegate { return importer.LoadAsync("spotify:album:" + Id, CancellationToken.None); }, "album access", "album-specific access error");
            }
            using (var importer = new SpotifyImporter(new StaticHandler(HttpStatusCode.NotFound)))
            {
                AuthenticateFixture(importer);
                await ThrowsAsync<InvalidOperationException>(delegate { return importer.LoadAsync("spotify:album:" + Id, CancellationToken.None); }, "could not find", "album missing response");
            }
            using (var importer = new SpotifyImporter(new StaticHandler(HttpStatusCode.OK)))
            {
                AuthenticateFixture(importer);
                await ThrowsAsync<FormatException>(delegate { return importer.LoadAsync("spotify:album:" + Id, CancellationToken.None); }, "track list", "malformed album rejected");
            }
            using (var importer = new SpotifyImporter(new AlbumHandler()))
            using (var cancel = new CancellationTokenSource())
            {
                AuthenticateFixture(importer); cancel.Cancel();
                await ThrowsAsync<OperationCanceledException>(delegate { return importer.LoadAsync("spotify:album:" + Id, cancel.Token); }, "", "album cancellation");
            }
        }

        private sealed class AlbumHandler : HttpMessageHandler
        {
            public int TrackRequests;
            public string NextOverride;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                True(request.Headers.Authorization != null && request.Headers.Authorization.Parameter == "fixture-old", "album authenticated request");
                if (request.RequestUri.AbsolutePath.EndsWith("/tracks", StringComparison.Ordinal))
                {
                    TrackRequests++;
                    return Reply(HttpStatusCode.OK, "{\"items\":[{\"name\":\"Second song\",\"artists\":[{\"name\":\"Artist\"}]}],\"next\":" + (NextOverride == null ? "null" : "\"" + NextOverride + "\"") + "}");
                }
                return Reply(HttpStatusCode.OK, "{\"name\":\"Fixture album\",\"images\":[{\"url\":\"https://i.scdn.co/image/album\",\"width\":640}],\"tracks\":{\"items\":[{\"name\":\"日本語の曲\",\"duration_ms\":180000,\"artists\":[{\"name\":\"Artist\"}]}],\"next\":\"https://api.spotify.com/v1/albums/" + Id + "/tracks?offset=50\"}}");
            }
        }
        private static void SetField(object target, string name, object value) { target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value); }

        private sealed class FixtureHandler : HttpMessageHandler
        {
            public int RefreshRequests, ItemRequests;
            public bool CorrectAuthorization = true;
            private bool expired;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.RequestUri.Host == "accounts.spotify.com")
                {
                    RefreshRequests++;
                    CorrectAuthorization &= request.Headers.Authorization == null && request.Method == HttpMethod.Post;
                    string body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    CorrectAuthorization &= body.Contains("grant_type=refresh_token") && body.Contains("client_id=");
                    return Reply(HttpStatusCode.OK, "{\"access_token\":\"fixture-new\",\"expires_in\":3600}");
                }
                string bearer = request.Headers.Authorization == null ? "" : request.Headers.Authorization.Parameter;
                if (!expired)
                {
                    expired = true; CorrectAuthorization &= bearer == "fixture-old";
                    return Reply(HttpStatusCode.Unauthorized, "{}");
                }
                CorrectAuthorization &= bearer == "fixture-new";
                if (request.RequestUri.AbsolutePath.EndsWith("/items", StringComparison.Ordinal))
                {
                    ItemRequests++;
                    string next = ItemRequests == 1 ? "\"https://api.spotify.com/v1/playlists/" + Id + "/items?offset=50&limit=50\"" : "null";
                    return Reply(HttpStatusCode.OK, "{\"items\":[{\"item\":{\"name\":\"Song " + ItemRequests + "\",\"artists\":[{\"name\":\"Artist\"}]}}],\"next\":" + next + "}");
                }
                return Reply(HttpStatusCode.OK, "{\"name\":\"Fixture playlist\"}");
            }
        }

        private sealed class StaticHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode status;
            public StaticHandler(HttpStatusCode status) { this.status = status; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = new HttpResponseMessage(status) { Content = new StringContent("{}") };
                if ((int)status == 429) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
                return Task.FromResult(response);
            }
        }
        private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string body) { return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }); }
        private static void True(bool value, string message) { checks++; if (!value) throw new Exception("Import test failed: " + message); }
        private static void Equal<T>(T expected, T actual, string message) { True(EqualityComparer<T>.Default.Equals(expected, actual), message + " (expected " + expected + ", got " + actual + ")"); }
        private static void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); } catch (T) { checks++; return; }
            throw new Exception("Import test failed: " + message + " did not throw " + typeof(T).Name);
        }
        private static async Task ThrowsAsync<T>(Func<Task> action, string text, string message) where T : Exception
        {
            try { await action(); }
            catch (T ex) { True(ex.Message.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0, message); return; }
            throw new Exception("Import test failed: " + message + " did not throw " + typeof(T).Name);
        }
    }
}
