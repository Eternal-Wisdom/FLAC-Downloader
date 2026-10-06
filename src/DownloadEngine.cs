using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    public sealed class EngineProgress
    {
        public string TrackKey { get; set; }
        public string JobId { get; set; }
        public string Peer { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public double TotalBytesPerSecond { get; set; }
        public int MovingTransfers { get; set; }
        public string Type { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }
        public string DownloadPath { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public int Total { get; set; }
        public double Percent { get; set; }
    }

    // Sockseek v3.0.5 is launched without shell evaluation or user-provided options.
    public sealed class DownloadEngine : IDisposable
    {
        public event Action<string> Log;
        public event Action<EngineProgress> Progress;
        private static readonly SemaphoreSlim processSlot = new SemaphoreSlim(1, 1);
        private readonly object gate = new object();
        private readonly string stateRoot;
        private readonly int sessionCooldownMs;
        private Process active;
        private bool running;
        private bool disposed;
        private int logCount;
        private int completed;
        private int failed;
        private int total;
        private string currentSecret = "";
        private readonly TransferTelemetry telemetry=new TransferTelemetry();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        public DownloadEngine() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlaylistFlac", "engine-runs")) { }
        internal DownloadEngine(string root) : this(root, DownloadTuning.SearchWindowMilliseconds) { }
        internal DownloadEngine(string root, int cooldownMs) { stateRoot = Path.GetFullPath(root); sessionCooldownMs = cooldownMs; }

        public Task<int> RunAsync(string enginePath, string csvPath, string outputDir, string username, string password, bool strictMatch, CancellationToken cancellation)
        {
            return RunWithIndexAsync(enginePath, csvPath, outputDir, username, password, strictMatch,
                LibraryLayout.PathFor(outputDir,"_index.csv"), Path.Combine(outputDir, "playlist.m3u8"), cancellation);
        }

        public Task<int> RunWithIndexAsync(string enginePath, string csvPath, string outputDir, string username, string password, bool strictMatch, string indexPath, string playlistPath, CancellationToken cancellation)
        {
            return RunWithIndexAsync(enginePath, csvPath, outputDir, username, password, strictMatch, indexPath, playlistPath, cancellation, DownloadTuning.Default);
        }

        public Task<int> RunWithIndexAsync(string enginePath, string csvPath, string outputDir, string username, string password, bool strictMatch, string indexPath, string playlistPath, CancellationToken cancellation, DownloadTuning tuning)
        {
            ValidateCredential(username, "Soulseek username");
            ValidateCredential(password, "Soulseek password");
            enginePath = Path.GetFullPath(enginePath);
            csvPath = Path.GetFullPath(csvPath);
            outputDir = Path.GetFullPath(outputDir);
            indexPath = Path.GetFullPath(indexPath);
            playlistPath = Path.GetFullPath(playlistPath);
            if (!File.Exists(enginePath)) throw new FileNotFoundException("The download engine is missing.", enginePath);
            if (!File.Exists(csvPath)) throw new FileNotFoundException("The imported playlist is missing.", csvPath);
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("DownloadEngine");
                if (running) throw new InvalidOperationException("A download is already running.");
                running = true;
                logCount = completed = failed = total = 0;
                telemetry.Clear();
                currentSecret = password;
            }
            return Task.Run(() => RunCore(enginePath, csvPath, outputDir, username, password, strictMatch, indexPath, playlistPath, cancellation, tuning ?? DownloadTuning.Default));
        }

        private int RunCore(string enginePath, string csvPath, string outputDir, string username, string password, bool strictMatch, string indexPath, string playlistPath, CancellationToken cancellation, DownloadTuning tuning)
        {
            string runDir = Path.Combine(stateRoot, Guid.NewGuid().ToString("N"));
            string configPath = Path.Combine(runDir, "session.conf");
            Process process = null;
            bool slotHeld = false;
            SearchSessionPacing pacing = null;
            try
            {
                if (cancellation.IsCancellationRequested) return 130;
                processSlot.Wait(cancellation);
                slotHeld = true;
                pacing = SearchSessionPacing.Acquire(stateRoot, sessionCooldownMs, cancellation, EmitLog);
                if (cancellation.IsCancellationRequested) return 130;
                Directory.CreateDirectory(outputDir);
                Directory.CreateDirectory(Path.GetDirectoryName(indexPath));
                Directory.CreateDirectory(Path.GetDirectoryName(playlistPath));
                CreatePrivateDirectory(runDir);
                // The enclosing quotes preserve leading/trailing spaces and embedded '='.
                File.WriteAllText(configPath, "username = \"" + username + "\"\r\npassword = \"" + password + "\"\r\n", new UTF8Encoding(false));
                var start = new ProcessStartInfo
                {
                    FileName = enginePath,
                    Arguments = BuildArguments(csvPath, outputDir, configPath, strictMatch, indexPath, playlistPath, tuning),
                    WorkingDirectory = runDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                process = new Process { StartInfo = start };
                lock (gate)
                {
                    if (disposed || cancellation.IsCancellationRequested) return 130;
                    if (!process.Start()) throw new IOException("The download engine could not start.");
                    active = process;
                }
                process.StandardInput.Close();
                EmitLog("Searching Soulseek for FLAC files. Tracks with no matching FLAC will be reported as unavailable.");
                using (cancellation.Register(() => KillActive()))
                {
                    Task outputReader = Task.Run(() => ReadOutput(process.StandardOutput, true));
                    Task errorReader = Task.Run(() => ReadOutput(process.StandardError, false));
                    process.WaitForExit();
                    Task.WaitAll(outputReader, errorReader);
                    if (cancellation.IsCancellationRequested || disposed)
                    {
                        EmitLog("Stopped. Completed files remain available; start again to retry unfinished tracks.");
                        return 130;
                    }
                    return process.ExitCode;
                }
            }
            catch (OperationCanceledException) { return 130; }
            finally
            {
                lock (gate) { if (active == process) active = null; }
                if (process != null) process.Dispose();
                try { if (File.Exists(configPath)) File.Delete(configPath); }
                catch (Exception) { EmitLog("Could not remove a temporary sign-in file. Close the app and remove: " + configPath); }
                try { if (Directory.Exists(runDir)) Directory.Delete(runDir, false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                lock (gate) { currentSecret = ""; running = false; }
                if (slotHeld)
                {
                    if (pacing != null) pacing.Dispose();
                    processSlot.Release();
                }
            }
        }

        internal static void CreatePrivateDirectory(string path)
        {
            var identity = WindowsIdentity.GetCurrent();
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(identity.User);
            security.AddAccessRule(new FileSystemAccessRule(identity.User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(path, security);
        }

        internal static void ValidateCredential(string value, string label)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException(label + " is required.");
            if (value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new ArgumentException(label + " cannot contain line breaks.");
        }

        internal static string BuildArguments(string csvPath, string outputDir, string configPath, bool strictMatch)
        {
            return BuildArguments(csvPath, outputDir, configPath, strictMatch, LibraryLayout.PathFor(outputDir,"_index.csv"), Path.Combine(outputDir, "playlist.m3u8"));
        }

        internal static string BuildArguments(string csvPath, string outputDir, string configPath, bool strictMatch, string indexPath, string playlistPath)
        {
            return BuildArguments(csvPath, outputDir, configPath, strictMatch, indexPath, playlistPath, DownloadTuning.Default);
        }

        internal static string BuildArguments(string csvPath, string outputDir, string configPath, bool strictMatch, string indexPath, string playlistPath, DownloadTuning tuning)
        {
            tuning = tuning ?? DownloadTuning.Default;
            var args = new List<string>
            {
                "--config", configPath,
                "--input", csvPath, "--input-type", "csv", "--song",
                "--artist-col", "artist", "--title-col", "title", "--album-col", "album", "--length-col", "length", "--time-format", "s",
                // The caller supplies a private per-pass folder. Source-title names can
                // collide in Sockseek, whose organizer overwrites an existing target.
                // Unique CSV row names are finalized safely by the wrapper afterwards.
                "--output-dir", outputDir, "--name-format", "{row}",
                "--format", "flac", "--pref-format", "flac", "--length-tol", "5",
                "--write-index", "--index-path", indexPath, "--skip-check-cond",
                "--write-playlist", "--playlist-path", playlistPath,
                "--concurrent-jobs", tuning.ParallelTracks.ToString(CultureInfo.InvariantCulture), "--concurrent-searches", "4", "--max-retries", "3", "--progress-json",
                "--searches-per-time", "4", "--searches-renew-time", "28",
                // Compare the complete search instead of committing to its first acceptable peer.
                // Quality preferences are soft: ordinary CD-quality FLAC remains a fallback.
                "--fast-search", tuning.QualityProfile=="Fast FLAC" ? "true" : "false", "--pref-min-bitdepth", tuning.QualityProfile=="Prefer high resolution" ? "24" : "0",
                "--pref", "acceptmissingprops=false",
                "--pref-min-bitrate", "0", "--pref-max-bitrate", "2147483647",
                "--pref-max-samplerate", "2147483647", "--max-stale-time", "20000"
            };
            if (strictMatch) { args.Add("--strict-title"); args.Add("--strict-artist"); }
            return String.Join(" ", args.ConvertAll(QuoteArgument));
        }

        // CommandLineToArgvW / Microsoft CRT escaping, including trailing backslashes.
        internal static string QuoteArgument(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') { result.Append('\\', slashes * 2 + 1); result.Append(ch); slashes = 0; continue; }
                result.Append('\\', slashes); slashes = 0; result.Append(ch);
            }
            result.Append('\\', slashes * 2); result.Append('"');
            return result.ToString();
        }

        private void ReadOutput(StreamReader reader, bool parseProgress)
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (parseProgress && line.StartsWith("{", StringComparison.Ordinal) && TryProgress(line)) continue;
                EmitLog(line);
            }
        }

        internal bool TryProgress(string line)
        {
            try
            {
                var envelope = json.DeserializeObject(line) as Dictionary<string, object>;
                if (envelope == null || !envelope.ContainsKey("type") || !envelope.ContainsKey("data")) return false;
                var data = envelope["data"] as Dictionary<string, object>;
                if (data == null) return false;
                string type = Text(envelope, "type");
                string status = "";
                if (type == "track_list")
                {
                    total = Number(data, "total");
                    completed = Number(data, "existing");
                    failed = Number(data, "notFound");
                    status = "Playlist ready";
                }
                else if (type == "track_state")
                {
                    status = Text(data, "terminalOutcome");
                    if (status == "Succeeded" || status == "1") completed++;
                    else if (status == "Failed" || status == "2" || status == "PartialSuccess" || status == "5") failed++;
                    string reason = Text(data, "failureReason");
                    if (!String.IsNullOrEmpty(reason) && reason != "None" && reason != "0") status += ": " + reason;
                    EmitLog(Text(data, "artist") + " — " + Text(data, "title") + ": " + status);
                }
                else if (type == "search_start") status = "Searching";
                else if (type == "download_start") status = "Queued / downloading";
                else if (type == "download_progress") status = "Downloading " + DecimalNumber(data, "percent").ToString("0", CultureInfo.InvariantCulture) + "%";
                else if (type == "progress" || type == "list_progress")
                {
                    total = Number(data, "total"); completed = Number(data, "downloaded"); failed = Number(data, "failed"); status = "Downloading";
                    if(total>0 && completed+failed>=total)telemetry.Clear();
                }
                else if (type == "extraction_failed") { status = "Playlist could not be read"; EmitLog(Text(data, "reason")); }
                else return false;
                string jobId=Text(data,"jobId");
                if(jobId.Length==0 && Text(data,"title").Length>0)jobId="query:"+Text(data,"artist").Length+":"+Text(data,"artist")+Text(data,"title");
                long bytes=LongNumber(data,"bytesTransferred"),size=LongNumber(data,"totalBytes");
                double rate=type=="download_progress" ? telemetry.Observe(jobId,bytes,size,DateTime.UtcNow) : 0;
                if(type=="download_progress" && rate>0)status+=" / "+TransferTelemetry.FormatRate(rate);
                if(type=="track_state")telemetry.Finish(jobId);
                if(type=="download_start" && Text(data,"username").Length>0)EmitLog("Source for "+Text(data,"title")+": "+Text(data,"username")+". Waiting for byte progress; source selection is handled by the engine.");
                var progress = new EngineProgress
                {
                    JobId=jobId,Peer=Text(data,"username"),BytesTransferred=bytes,TotalBytes=size,BytesPerSecond=rate,
                    TotalBytesPerSecond=telemetry.Speed(DateTime.UtcNow),MovingTransfers=telemetry.Moving(DateTime.UtcNow),
                    Type = type, Artist = Text(data, "artist"), Title = Text(data, "title"), Status = status,
                    DownloadPath = Text(data, "downloadPath"), Completed = completed, Failed = failed, Total = total,
                    Percent = total > 0 ? Math.Min(100.0, 100.0 * (completed + failed) / total) : 0
                };
                var callback = Progress;
                if (callback != null) callback(progress);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }

        private static string Text(Dictionary<string, object> data, string name) { object value; return data.TryGetValue(name, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : ""; }
        private static int Number(Dictionary<string, object> data, string name) { int number; return Int32.TryParse(Text(data, name), out number) ? number : 0; }
        private static long LongNumber(Dictionary<string, object> data,string name) {long number;return Int64.TryParse(Text(data,name),out number)?number:0;}
        private static double DecimalNumber(Dictionary<string, object> data, string name) { double number; return Double.TryParse(Text(data, name), NumberStyles.Any, CultureInfo.InvariantCulture, out number) ? number : 0; }

        internal static string Redact(string text, string secret)
        {
            if (String.IsNullOrEmpty(text)) return "";
            if (!String.IsNullOrEmpty(secret)) text = text.Replace(secret, "[hidden]");
            text = Regex.Replace(text, @"(?i)(spotify-(?:token|refresh|secret)|password|access_token|refresh_token|client_secret)\s*[:=]\s*.*", "$1=[hidden]");
            text = Regex.Replace(text, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
            text = Regex.Replace(text, @"[\x00-\x08\x0B-\x1F\x7F\u202A-\u202E\u2066-\u2069]", "");
            return text.Length > 2000 ? text.Substring(0, 2000) + "…" : text;
        }

        private void EmitLog(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return;
            int count = Interlocked.Increment(ref logCount);
            if (count > 5001) return;
            var callback = Log;
            if (callback != null) callback(count == 5001 ? "Further detailed log lines are hidden. Progress and the download report continue to update." : Redact(text, currentSecret));
        }

        private void KillActive()
        {
            lock (gate)
            {
                if (active == null) return;
                try { if (!active.HasExited) active.Kill(); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }

        public void Dispose() { lock (gate) { disposed = true; } KillActive(); }
    }
}
