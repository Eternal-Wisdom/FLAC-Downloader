using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    public static class ParallelDownloadTests
    {
        // This invokes the bundled engine with its local mock client, never Soulseek.
        // Mock files share a single peer/slot: download_start means an outstanding
        // transfer request (possibly peer-queued), not simultaneous network bytes.
        public static void Run(string enginePath)
        {
            enginePath = Path.GetFullPath(enginePath);
            if (!File.Exists(enginePath)) throw new FileNotFoundException("Bundled engine is required for the offline parallel test.", enginePath);
            string root = Path.Combine(Path.GetTempPath(), "PlaylistFlac-ParallelTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string library = Path.Combine(root, "mock-library"), csv = LibraryLayout.PathFor(root,"playlist.csv");
                Directory.CreateDirectory(library);
                string[] names = { "Amber", "Birch", "Cobalt", "Delta", "Ember", "Fern", "Garnet", "Hazel", "Indigo", "Juniper" };
                var expected = new Dictionary<string, byte[]>();
                var input = new StringBuilder("title,artist,album,length\r\n");
                for (int i = 0; i < names.Length; i++)
                {
                    string title = "Parallel " + names[i], filename = "OfflineArtist - " + title + ".flac";
                    // Placeholder fixture bytes are distinct; this test checks transport,
                    // filenames and filtering, rather than FLAC decoding or provenance.
                    byte[] bytes = new byte[4096 + i];
                    for (int j = 0; j < bytes.Length; j++) bytes[j] = (byte)((j * 31 + i) % 251);
                    File.WriteAllBytes(Path.Combine(library, filename), bytes); expected.Add(title, bytes);
                    input.AppendLine(title + ",OfflineArtist,OfflineAlbum," + MockDuration(filename));
                }
                string lossyName = "OfflineArtist - LossyOnly.mp3";
                File.WriteAllBytes(Path.Combine(library, lossyName), Encoding.ASCII.GetBytes("ID3 offline fixture"));
                input.AppendLine("LossyOnly,OfflineArtist,OfflineAlbum," + MockDuration(lossyName));
                File.WriteAllText(csv, input.ToString(), new UTF8Encoding(false));

                int four = CheckCapacity(enginePath, root, library, csv, expected, 4);
                int eight = CheckCapacity(enginePath, root, library, csv, expected, 8);
                Check(four == 4 && eight == 8 && eight > four,
                    "The actual engine must allow eight outstanding transfer jobs when configured for eight, versus four when configured for four.");
            }
            finally
            {
                string full = Path.GetFullPath(root), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("PlaylistFlac-ParallelTests-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing unexpected parallel-test cleanup path.");
                Directory.Delete(full, true);
            }
        }

        private static int CheckCapacity(string enginePath, string root, string library, string csv, Dictionary<string, byte[]> expected, int capacity)
        {
            string output = Path.Combine(root, "capacity-" + capacity);
            Directory.CreateDirectory(output);
            var arguments = new[] {
                "--no-config", "--username", "offline-test", "--password", "offline-test",
                "--mock-files-dir", library, "--mock-files-no-read-tags", "--mock-files-slow",
                "--input", csv, "--input-type", "csv", "--song",
                "--artist-col", "artist", "--title-col", "title", "--album-col", "album", "--length-col", "length", "--time-format", "s",
                "--output-dir", output, "--name-format", "{row}",
                "--format", "flac", "--pref-format", "flac", "--strict-title", "--strict-artist", "--length-tol", "5",
                "--concurrent-jobs", capacity.ToString(CultureInfo.InvariantCulture), "--concurrent-searches", capacity.ToString(CultureInfo.InvariantCulture),
                "--searches-per-time", "64", "--searches-renew-time", "1", "--max-retries", "1", "--progress-json",
                "--write-index", "--index-path", LibraryLayout.PathFor(output,"_index.csv"),
                "--write-playlist", "--playlist-path", Path.Combine(output, "playlist.m3u8")
            };
            string stdout, stderr; int exit;
            var info = new ProcessStartInfo(enginePath, String.Join(" ", arguments.Select(Quote)))
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(info))
            {
                process.StandardInput.Close();
                Task<string> readOut = process.StandardOutput.ReadToEndAsync(), readError = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(45000)) { process.Kill(); process.WaitForExit(); throw new Exception("Offline parallel test timed out at capacity " + capacity + "."); }
                process.WaitForExit(); stdout = readOut.Result; stderr = readError.Result; exit = process.ExitCode;
            }
            Check(exit == 1, "Expected one unavailable MP3-only track (engine exit 1), got " + exit + ". " + Tail(stderr));
            var json = new JavaScriptSerializer();
            var outstanding = new HashSet<string>();
            var completed = new Dictionary<string, string>();
            var failed = new HashSet<string>();
            int peak = 0, progressEvents = 0;
            using (var reader = new StringReader(stdout))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith("{", StringComparison.Ordinal)) continue;
                    var envelope = json.DeserializeObject(line) as Dictionary<string, object>;
                    if (envelope == null || !envelope.ContainsKey("data")) continue;
                    var data = envelope["data"] as Dictionary<string, object>;
                    if (data == null) continue;
                    string type = Text(envelope, "type"), title = Text(data, "title");
                    if (type == "download_start")
                    {
                        Check(expected.ContainsKey(title), "A non-FLAC-only track must never start transferring.");
                        outstanding.Add(title); peak = Math.Max(peak, outstanding.Count);
                    }
                    else if (type == "download_progress") progressEvents++;
                    else if (type == "track_state")
                    {
                        outstanding.Remove(title);
                        string state = Text(data, "terminalOutcome");
                        if (state == "Succeeded" || state == "1") completed[title] = Text(data, "downloadPath");
                        if (state == "Failed" || state == "2") failed.Add(title);
                    }
                }
            }
            Check(peak == capacity, "Outstanding mock transfer jobs peaked at " + peak + " rather than configured " + capacity + ". " + Tail(stderr));
            Check(outstanding.Count == 0 && progressEvents > 0, "Transfer progress and terminal cleanup must both be emitted.");
            Check(completed.Count == expected.Count && failed.SetEquals(new[] { "LossyOnly" }), "All ten FLAC fixtures must complete; only the MP3-only track may fail.");
            Check(completed.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == expected.Count, "Every completed track must have a distinct output path.");
            string prefix = Path.GetFullPath(output).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (var item in completed)
            {
                string path = Path.GetFullPath(item.Value);
                Check(path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase), "Completed files must remain FLAC paths inside the test output.");
                Check(File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(expected[item.Key]), "Fixture bytes must survive without truncation or collision: " + item.Key);
            }
            Check(Directory.GetFiles(output, "*.mp3", SearchOption.AllDirectories).Length == 0, "FLAC filtering must not copy MP3 fixtures.");
            Check(Directory.GetFiles(output, "*.incomplete", SearchOption.AllDirectories).Length == 0, "Completed runs must not retain incomplete fixture files.");
            Check(File.Exists(LibraryLayout.PathFor(output,"_index.csv")) && File.Exists(Path.Combine(output, "playlist.m3u8")), "The actual engine must write its index and playback playlist.");
            return peak;
        }

        private static int MockDuration(string filename)
        {
            int hash = 0;
            foreach (char c in filename) hash = unchecked(hash * 31 + c);
            return Math.Abs(hash % 1000) + 1;
        }
        private static string Text(Dictionary<string, object> data, string name)
        { object value; return data.TryGetValue(name, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : ""; }
        private static string Tail(string text) { return text.Length <= 1600 ? text : text.Substring(text.Length - 1600); }
        private static string Quote(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1).Append(c); slashes = 0; continue; }
                result.Append('\\', slashes).Append(c); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception("Parallel download test failed: " + message); }
    }
}
