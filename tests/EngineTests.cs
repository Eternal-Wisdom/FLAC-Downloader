using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using PlaylistFlac;

internal static class EngineTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Engine test failed: " + message); }

    public static void Run()
    {
        Check(DownloadEngine.QuoteArgument("") == "\"\"", "empty argument");
        Check(DownloadEngine.QuoteArgument("a b") == "\"a b\"", "spaces");
        Check(DownloadEngine.QuoteArgument("a\"b") == "\"a\\\"b\"", "embedded quote");
        Check(DownloadEngine.QuoteArgument("C:\\folder\\") == "\"C:\\folder\\\\\"", "trailing backslash");
        bool rejected = false;
        try { DownloadEngine.ValidateCredential("password\r\non-complete=evil", "Password"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "config injection rejected");
        string arguments = DownloadEngine.BuildArguments("C:\\playlist & songs.csv", "C:\\music", "C:\\session.conf", true);
        Check(arguments.Contains("\"--format\" \"flac\"") && arguments.Contains("\"--strict-title\"") && arguments.Contains("\"--skip-check-cond\""), "required FLAC and resume options");
        Check(!arguments.Contains("yt-dlp") && !arguments.Contains("on-complete"), "no lossy fallback or shell action");
        Check(arguments.Contains("\"--name-format\" \"{row}\"") && !arguments.Contains("{stitle}"), "unique private row filenames prevent upstream title collisions before safe finalization");
        Check(arguments.Contains("\"--searches-per-time\" \"4\"") && arguments.Contains("\"--searches-renew-time\" \"28\""), "bounded four-search batches");
        Check(arguments.Contains("\"--concurrent-jobs\" \"20\"") && arguments.Contains("\"--concurrent-searches\" \"4\""), "twenty parallel tracks by default");
        Check(arguments.Contains("\"--fast-search\" \"false\"") && !arguments.Contains("--fast-search-min-up-speed"), "complete search ranking instead of first acceptable response");
        Check(arguments.Contains("\"--pref-min-bitdepth\" \"24\"") && !arguments.Contains("\"--min-bitdepth\""), "high-resolution preference is soft so CD-quality FLAC remains eligible");
        Check(arguments.Contains("\"--pref\" \"acceptmissingprops=false\"") && !arguments.Contains("\"--strict-conditions\""), "unknown quality remains fallback rather than satisfying high-resolution preference");
        Check(arguments.Contains("\"--pref-max-samplerate\" \"2147483647\"") && arguments.Contains("\"--pref-max-bitrate\" \"2147483647\"") && arguments.Contains("\"--pref-min-bitrate\" \"0\""), "high-resolution and efficiently compressed lossless files avoid legacy preference ceilings");
        Check(arguments.Contains("\"--max-stale-time\" \"20000\""), "stalled peer timeout retained");
        foreach (int limit in new[] { 8, 20, 32 })
        {
            string tuned = DownloadEngine.BuildArguments("in.csv", "out", "config", true, "index.csv", "playlist.m3u8", new DownloadTuning(limit));
            Check(tuned.Contains("\"--concurrent-jobs\" \"" + limit + "\""), "chosen parallel capacity reaches engine");
            Check(tuned.Contains("\"--searches-renew-time\" \"28\"") && tuned.Contains("\"--format\" \"flac\""), "higher capacity keeps pacing and format restrictions");
        }
        Check(new DownloadTuning(0).ParallelTracks == 20 && new DownloadTuning(10000).ParallelTracks == 20 && new DownloadTuning(-1).ParallelTracks == 20, "old or invalid settings use bounded default");
        Check(arguments.Contains("\"--strict-artist\"") && arguments.Contains("\"--length-tol\" \"5\"") && !arguments.Contains("desperate"), "strict identity and length validation retained");
        string customArguments = DownloadEngine.BuildArguments("C:\\playlist.csv", "C:\\music", "C:\\session.conf", true, "C:\\retry state\\alternate-index.csv", "C:\\retry state\\alternate.m3u8");
        Check(customArguments.Contains("\"C:\\retry state\\alternate-index.csv\"") && customArguments.Contains("\"C:\\retry state\\alternate.m3u8\"") && !customArguments.Contains("C:\\music\\_index.csv"), "isolated retry output paths");
        string redacted = DownloadEngine.Redact("Failed: mysecret spotify-refresh=abc", "mysecret");
        Check(!redacted.Contains("mysecret") && !redacted.Contains("abc"), "password and token redaction");
        using (var engine = new DownloadEngine())
        {
            EngineProgress latest = null;
            engine.Progress += p => latest = p;
            Check(engine.TryProgress("{\"type\":\"track_list\",\"data\":{\"total\":3,\"existing\":1,\"notFound\":0}}"), "playlist event");
            Check(engine.TryProgress("{\"type\":\"track_state\",\"data\":{\"artist\":\"Artist\",\"title\":\"Song\",\"terminalOutcome\":\"Succeeded\",\"downloadPath\":\"track.flac\"}}"), "completion event");
            Check(latest.Completed == 2 && latest.Total == 3 && latest.DownloadPath == "track.flac", "completion totals");
            Check(!engine.TryProgress("{broken"), "malformed JSON fallback");
        }
        RunFakeProcess();
        CheckPacing();
    }

    private static void RunFakeProcess()
    {
        string root = Path.Combine(Path.GetTempPath(), "PlaylistFlacEngineTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string exe = Path.Combine(root, "fake engine.exe");
            string source = @"using System; using System.IO; using System.Threading; class Fake { static int Main(string[] a) { string config = a[Array.IndexOf(a,""--config"")+1]; string output = a[Array.IndexOf(a,""--output-dir"")+1]; string text = File.ReadAllText(config); if(!text.Contains(""password = \""testsecret\"""")) return 20; if(Array.IndexOf(a,""--format"")<0 || a[Array.IndexOf(a,""--format"")+1] != ""flac"") return 21; File.WriteAllText(Path.Combine(output,""started""),config); Console.WriteLine(""password=testsecret""); Console.WriteLine(""{\""type\"":\""track_list\"",\""data\"":{\""total\"":1,\""existing\"":0}}""); if (output.EndsWith(""cancel"")) Thread.Sleep(30000); Console.WriteLine(""{\""type\"":\""track_state\"",\""data\"":{\""terminalOutcome\"":\""Succeeded\"",\""title\"":\""Song\""}}""); return 0; } }";
            using (var provider = new CSharpCodeProvider())
            {
                var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = exe };
                options.ReferencedAssemblies.Add("System.dll");
                var result = provider.CompileAssemblyFromSource(options, source);
                Check(!result.Errors.HasErrors, "mock compile: " + (result.Errors.Count > 0 ? result.Errors[0].ToString() : ""));
            }
            string csv = Path.Combine(root, "playlist & tracks.csv");
            File.WriteAllText(csv, "title,artist,album,length\r\nSong,Artist,Album,180\r\n");
            string output = Path.Combine(root, "done");
            using (var engine = new DownloadEngine(Path.Combine(root, "sessions"), 500))
            {
                string logs = "";
                engine.Log += line => logs += line;
                int exit = engine.RunAsync(exe, csv, output, "testuser", "testsecret", true, CancellationToken.None).GetAwaiter().GetResult();
                Check(exit == 0, "successful mock exit");
                Check(!logs.Contains("testsecret"), "child secret redacted");
                Check(!File.Exists(File.ReadAllText(Path.Combine(output, "started"))), "temporary credentials removed after success");
            }
            var gap = Stopwatch.StartNew();
            output = Path.Combine(root, "cancel");
            using (var cancellation = new CancellationTokenSource())
            using (var engine = new DownloadEngine(Path.Combine(root, "sessions"), 500))
            {
                Task<int> run = engine.RunWithIndexAsync(exe, csv, output, "testuser", "testsecret", true, Path.Combine(root, "retry-state", "index.csv"), Path.Combine(root, "retry-state", "playlist.m3u8"), cancellation.Token);
                var timer = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(output, "started")) && timer.ElapsedMilliseconds < 12000) Thread.Sleep(20);
                Check(File.Exists(Path.Combine(output, "started")), "mock started before cancellation");
                Check(gap.ElapsedMilliseconds >= 450, "configured gap between child sessions (short test clock)");
                cancellation.Cancel();
                Check(run.GetAwaiter().GetResult() == 130, "cancellation exit");
                Check(!File.Exists(File.ReadAllText(Path.Combine(output, "started"))), "temporary credentials removed after cancellation");
            }
            output = Path.Combine(root, "cancel-during-cooldown");
            using (var cancellation = new CancellationTokenSource())
            using (var engine = new DownloadEngine(Path.Combine(root, "sessions"), 500))
            {
                var timer = Stopwatch.StartNew();
                Task<int> run = engine.RunAsync(exe, csv, output, "testuser", "testsecret", true, cancellation.Token);
                cancellation.CancelAfter(100);
                Check(run.GetAwaiter().GetResult() == 130 && timer.ElapsedMilliseconds < 3000, "cooldown cancels promptly");
                Check(!Directory.Exists(output), "cancelled cooldown does not start a child or create output");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckPacing()
    {
        DateTime now = DateTime.UtcNow;
        Check(DownloadTuning.SearchWindowMilliseconds == 28000, "production session cooldown covers the entire search window");
        Check(SearchSessionPacing.RemainingDelay(null, now, 33000) == 0, "first session has no artificial delay");
        Check(SearchSessionPacing.RemainingDelay(now.AddSeconds(-1).Ticks.ToString(), now, 33000) == 0, "expired reservation");
        Check(SearchSessionPacing.RemainingDelay(now.AddSeconds(28).Ticks.ToString(), now, 33000) == 28000, "saved future reservation");
        Check(SearchSessionPacing.RemainingDelay("broken", now, 33000) == 33000, "corrupt pacing state waits conservatively");
        Check(SearchSessionPacing.RemainingDelay(DateTime.MaxValue.Ticks.ToString(), now, 33000) == 33000, "clock changes cannot create an unbounded wait");
        string root = Path.Combine(Path.GetTempPath(), "PlaylistFlacPacingTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = SearchSessionPacing.Acquire(root, 500, CancellationToken.None, null))
            using (var cancelled = new CancellationTokenSource(60))
            {
                bool blocked = false;
                try { using (SearchSessionPacing.Acquire(root, 500, cancelled.Token, null)) { } }
                catch (OperationCanceledException) { blocked = true; }
                Check(blocked, "a competing session cannot allocate another fresh search bucket");
            }
            Check(File.Exists(Path.Combine(root, "search-next-session.txt")), "reservation survives disposal on disk");
            using (var cancelled = new CancellationTokenSource(60))
            {
                bool blocked = false;
                try { using (SearchSessionPacing.Acquire(root, 500, cancelled.Token, null)) { } }
                catch (OperationCanceledException) { blocked = true; }
                Check(blocked, "a fresh instance reads persisted cooldown and can cancel promptly");
            }
            File.WriteAllText(Path.Combine(root, "search-next-session.txt"), now.AddSeconds(-1).Ticks.ToString());
            using (SearchSessionPacing.Acquire(root, 500, CancellationToken.None, null)) { }
        }
        finally
        {
            string full = Path.GetFullPath(root), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("PlaylistFlacPacingTest-", StringComparison.Ordinal)) throw new Exception("Unsafe pacing-test cleanup path");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }

    public static void CheckRealEngine(string enginePath)
    {
        string help = Capture(enginePath, "--help", null);
        Check(help.Contains("--format") && help.Contains("--progress-json"), "installed backend help");
        string root = Path.Combine(Path.GetTempPath(), "PlaylistFlacRealEngineTest-" + Guid.NewGuid().ToString("N"));
        DownloadEngine.CreatePrivateDirectory(root);
        try
        {
            string csv = Path.Combine(root, "test & playlist.csv");
            string config = Path.Combine(root, "test.conf");
            string output = Path.Combine(root, "output");
            string mock = Path.Combine(root, "empty-local-library");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(mock);
            File.WriteAllText(csv, "title,artist,album,length\r\nOffline Test Song,Test Artist,Test Album,180\r\n");
            File.WriteAllText(config, "username = testuser\r\npassword = testsecret\r\n");
            string arguments = DownloadEngine.BuildArguments(csv, output, config, true) + " --print jobs --mock-files-dir " + DownloadEngine.QuoteArgument(mock);
            string text = Capture(enginePath, arguments, root);
            Check(text.Contains("Offline Test Song"), "real engine accepts all wrapper flags and extracts normalized CSV without network searches");
        }
        finally { Directory.Delete(root, true); }
    }

    private static string Capture(string enginePath, string arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(enginePath, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        if (workingDirectory != null) info.WorkingDirectory = workingDirectory;
        using (var process = Process.Start(info))
        {
            process.StandardInput.Close();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20000)) { process.Kill(); throw new Exception("Engine test timed out."); }
            process.WaitForExit();
            Check(process.ExitCode == 0, "installed backend exit: " + stderr.Result);
            return stdout.Result + "\n" + stderr.Result;
        }
    }
}
