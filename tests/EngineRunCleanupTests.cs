using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CSharp;
using PlaylistFlac;

// Covers sign-in files left behind when a session is killed, and containment of the engine process.
internal static class EngineRunCleanupTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Engine run cleanup test failed: " + message); }

    private const string Secret = "password = \"synthetic-test-secret\"\r\n";

    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "FLAC-RunCleanupTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RemovesAbandonedSignInFiles(Path.Combine(root, "abandoned"));
            LeavesLiveSessionAlone(Path.Combine(root, "live"));
            LeavesUnrelatedEntriesAlone(Path.Combine(root, "unrelated"));
            QuietWhenNothingToClean(Path.Combine(root, "empty"));
            LockedFileDoesNotBlockOtherCleanup(Path.Combine(root, "locked"));
            LeavesJunctionTargetsAlone(Path.Combine(root, "junctions"));
            NextEngineRunRemovesAbandonedFiles(Path.Combine(root, "run"));
            EngineEndsWhenItsJobCloses();
            EngineEndsWhenParentIsKilled(Path.Combine(root, "hard-kill"));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static void LeavesJunctionTargetsAlone(string root)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        Directory.CreateDirectory(root);
        string state = Path.Combine(root, "state"), target = Path.Combine(root, "target");
        Directory.CreateDirectory(state); Directory.CreateDirectory(target);
        string secret = Path.Combine(target, EngineRunCleanup.SessionFileName);
        File.WriteAllText(secret, Secret);
        string link = Path.Combine(state, Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo { FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"), Arguments = "/d /c mklink /J \"" + link + "\" \"" + target + "\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(start))
        {
            Check(process.WaitForExit(10000) && process.ExitCode == 0, "test junction is created");
        }
        try
        {
            Check(EngineRunCleanup.RemoveAbandonedRunsIfIdle(state, null) == 0, "run folder junction is skipped");
            Check(File.ReadAllText(secret) == Secret, "linked target config is preserved");
            string nested = PlantRun(target, Secret, null);
            Check(EngineRunCleanup.RemoveAbandonedRunsIfIdle(link, null) == 0, "state root junction is skipped");
            Check(File.Exists(Path.Combine(nested, EngineRunCleanup.SessionFileName)), "configs below linked root are preserved");
        }
        finally { Directory.Delete(link, false); }
    }

    private static void LockedFileDoesNotBlockOtherCleanup(string root)
    {
        string locked = PlantRun(root, Secret, null);
        string other = PlantRun(root, Secret, null);
        string path = Path.Combine(locked, EngineRunCleanup.SessionFileName);
        string logs = "";
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Check(EngineRunCleanup.RemoveAbandonedRunsIfIdle(root, s => logs += s) == 1, "locked file does not prevent other cleanup");
            Check(File.Exists(path) && !Directory.Exists(other), "locked file survives while other abandoned file is removed");
        }
        Check(!logs.Contains(root) && !logs.Contains("synthetic-test-secret"), "cleanup diagnostics expose neither paths nor secrets");
        Check(EngineRunCleanup.RemoveAbandonedRunsIfIdle(root, null) == 1, "a later attempt removes the unlocked file");
    }

    private static string PlantRun(string stateRoot, string sessionText, string extraFile)
    {
        string directory = Path.Combine(stateRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (sessionText != null) File.WriteAllText(Path.Combine(directory, EngineRunCleanup.SessionFileName), sessionText);
        if (extraFile != null) File.WriteAllText(Path.Combine(directory, extraFile), "not a sign-in file");
        return directory;
    }

    private static void RemovesAbandonedSignInFiles(string stateRoot)
    {
        Directory.CreateDirectory(stateRoot);
        string withSecret = PlantRun(stateRoot, Secret, null);
        string empty = PlantRun(stateRoot, null, null);
        string withExtra = PlantRun(stateRoot, Secret, "engine-notes.txt");
        string logs = "";
        int removed = EngineRunCleanup.RemoveAbandonedRunsIfIdle(stateRoot, line => logs += line + "\n");
        Check(removed == 2, "both abandoned sign-in files are counted (" + removed + ")");
        Check(!Directory.Exists(withSecret), "a run folder holding only the sign-in file is removed");
        Check(!Directory.Exists(empty), "an empty abandoned run folder is removed");
        Check(Directory.Exists(withExtra) && !File.Exists(Path.Combine(withExtra, EngineRunCleanup.SessionFileName)), "the sign-in file goes even when the engine left other files");
        Check(File.Exists(Path.Combine(withExtra, "engine-notes.txt")), "other engine files are not deleted");
        Check(logs.Contains("Removed 2 temporary sign-in files"), "the user is told what was removed");
        Check(!logs.Contains("synthetic-test-secret"), "the log never carries the secret");
        Check(EngineRunCleanup.RemoveAbandonedRunsIfIdle(stateRoot, null) == 0, "a second pass finds nothing");
    }

    private static void LeavesLiveSessionAlone(string stateRoot)
    {
        Directory.CreateDirectory(stateRoot);
        string live = PlantRun(stateRoot, Secret, null);
        string sessionFile = Path.Combine(live, EngineRunCleanup.SessionFileName);
        using (SearchSessionPacing.Acquire(stateRoot, 20, CancellationToken.None, null))
        {
            Check(EngineRunCleanup.RemoveAbandonedRunsIfIdle(stateRoot, null) == 0, "nothing is removed while another session holds the lease");
            Check(File.Exists(sessionFile), "a live session's sign-in file is untouched");
        }
        Check(EngineRunCleanup.RemoveAbandonedRunsIfIdle(stateRoot, null) == 1, "the same folder is removed once the lease is free");
        Check(!File.Exists(sessionFile), "released session's file is removed");
    }

    private static void LeavesUnrelatedEntriesAlone(string stateRoot)
    {
        Directory.CreateDirectory(stateRoot);
        string notes = Path.Combine(stateRoot, "notes");
        Directory.CreateDirectory(notes);
        File.WriteAllText(Path.Combine(notes, EngineRunCleanup.SessionFileName), "kept");
        string upper = Path.Combine(stateRoot, "ABCDEF0123456789ABCDEF0123456789");
        Directory.CreateDirectory(upper);
        File.WriteAllText(Path.Combine(upper, EngineRunCleanup.SessionFileName), "kept");
        string shortName = Path.Combine(stateRoot, Guid.NewGuid().ToString("N").Substring(1));
        Directory.CreateDirectory(shortName);
        File.WriteAllText(Path.Combine(shortName, EngineRunCleanup.SessionFileName), "kept");
        string pacing = Path.Combine(stateRoot, "search-next-session.txt");
        File.WriteAllText(pacing, "123");
        // A genuine abandoned run makes the sweep actually run, so the name filter is what protects the rest.
        string abandoned = PlantRun(stateRoot, Secret, null);
        // On a case-insensitive file system the upper-case name is still not a run folder this app creates.
        Check(EngineRunCleanup.RemoveAbandonedRunsIfIdle(stateRoot, null) == 1, "only the app's own run folder names are candidates");
        Check(!Directory.Exists(abandoned), "the genuine abandoned run is still removed");
        Check(File.Exists(Path.Combine(notes, EngineRunCleanup.SessionFileName)), "an unrelated folder is untouched");
        Check(File.Exists(Path.Combine(upper, EngineRunCleanup.SessionFileName)), "a differently-cased name is untouched");
        Check(File.Exists(Path.Combine(shortName, EngineRunCleanup.SessionFileName)), "a near-miss name is untouched");
        Check(File.ReadAllText(pacing) == "123", "the saved search cooldown is untouched");
    }

    private static void QuietWhenNothingToClean(string stateRoot)
    {
        Check(EngineRunCleanup.RemoveAbandonedRunsIfIdle(stateRoot, null) == 0, "a missing state folder is fine");
        Check(!Directory.Exists(stateRoot), "startup cleanup does not create the state folder");
        Directory.CreateDirectory(stateRoot);
        Check(EngineRunCleanup.RemoveAbandonedRunsIfIdle(stateRoot, null) == 0, "an empty state folder is fine");
        Check(!File.Exists(Path.Combine(stateRoot, SearchSessionPacing.LockFileName)), "startup cleanup does not create the lease file when there is nothing to clean");
    }

    // The real engine path: an abandoned folder is gone after the next run, and the run cleans up after itself.
    private static void NextEngineRunRemovesAbandonedFiles(string root)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        Directory.CreateDirectory(root);
        string stateRoot = Path.Combine(root, "sessions");
        Directory.CreateDirectory(stateRoot);
        string abandoned = PlantRun(stateRoot, Secret, null);
        string exe = Path.Combine(root, "quiet engine.exe");
        using (var provider = new CSharpCodeProvider())
        {
            var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = exe };
            options.ReferencedAssemblies.Add("System.dll");
            var result = provider.CompileAssemblyFromSource(options, "class Quiet { static int Main() { return 0; } }");
            Check(!result.Errors.HasErrors, "mock engine compiles: " + (result.Errors.Count > 0 ? result.Errors[0].ToString() : ""));
        }
        string csv = Path.Combine(root, "tracks.csv");
        File.WriteAllText(csv, "title,artist,album,length\r\nSong,Artist,Album,180\r\n");
        string logs = "";
        using (var engine = new DownloadEngine(stateRoot, 20))
        {
            engine.Log += line => logs += line + "\n";
            int code = engine.RunAsync(exe, csv, Path.Combine(root, "music"), "user", "synthetic-test-secret", true, CancellationToken.None).GetAwaiter().GetResult();
            Check(code == 0, "mock engine run completes (" + code + ")");
        }
        Check(!Directory.Exists(abandoned), "the next engine run removes an abandoned run folder");
        Check(logs.Contains("Removed 1 temporary sign-in file"), "the engine run reports the cleanup");
        Check(Directory.GetDirectories(stateRoot).Length == 0, "the run leaves no run folder of its own");
    }

    // Windows ends every process in a kill-on-close job when the last handle closes, which is what happens
    // to the shared job when the app is force-closed. Closing a private job here stands in for that.
    private static void EngineEndsWhenParentIsKilled(string root)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        Directory.CreateDirectory(root);
        string exe = Path.Combine(root, "parent.exe"), marker = Path.Combine(root, "child.txt");
        string source = @"using System; using System.IO; using System.Diagnostics; using System.Reflection; using System.Threading;
class Parent { static void Main(string[] a) {
var t=Assembly.LoadFrom(a[0]).GetType(""PlaylistFlac.ChildProcessJob"");
var job=Activator.CreateInstance(t,true);
var child=Process.Start(new ProcessStartInfo { FileName=Path.Combine(Environment.SystemDirectory,""ping.exe""), Arguments=""-n 60 127.0.0.1"", UseShellExecute=false, CreateNoWindow=true });
try {
if(!(bool)t.GetMethod(""TryAssign"",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(job,new object[]{child})) return;
File.WriteAllText(a[1]+"".tmp"",child.Id.ToString()); File.Move(a[1]+"".tmp"",a[1]); Thread.Sleep(60000);
} finally { ((IDisposable)job).Dispose(); }
} }";
        using (var provider = new CSharpCodeProvider())
        {
            var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = exe };
            options.ReferencedAssemblies.Add("System.dll");
            Check(!provider.CompileAssemblyFromSource(options, source).Errors.HasErrors, "hard-kill helper compiles");
        }
        var start = new ProcessStartInfo { FileName = exe, Arguments = "\"" + typeof(DownloadEngine).Assembly.Location + "\" \"" + marker + "\"", UseShellExecute = false, CreateNoWindow = true };
        using (var parent = Process.Start(start))
        {
            Process child = null;
            try
            {
                var wait = Stopwatch.StartNew();
                while (!File.Exists(marker) && !parent.HasExited && wait.ElapsedMilliseconds < 10000) Thread.Sleep(25);
                Check(File.Exists(marker), "parent assigned its child before reporting readiness");
                child = Process.GetProcessById(Int32.Parse(File.ReadAllText(marker)));
                Check(!child.HasExited, "child is alive before parent termination");
                parent.Kill(); Check(parent.WaitForExit(10000), "test parent terminates");
                Check(child.WaitForExit(10000), "hard-killing the parent also ends its contained child");
            }
            finally
            {
                if (!parent.HasExited) { parent.Kill(); parent.WaitForExit(10000); }
                if (child != null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
            }
        }
    }

    private static void EngineEndsWhenItsJobCloses()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        using (var job = new ChildProcessJob())
        {
            Check(job.IsActive, "a job object can be created");
            var start = new ProcessStartInfo { FileName = Path.Combine(Environment.SystemDirectory, "ping.exe"), Arguments = "-n 60 127.0.0.1", UseShellExecute = false, CreateNoWindow = true };
            using (var child = Process.Start(start))
            {
                try
                {
                    Check(job.TryAssign(child), "the child process joins the job");
                    Check(!child.HasExited, "the child is still running while the job is open");
                    job.Dispose();
                    Check(child.WaitForExit(10000), "the child ends when the job closes");
                }
                finally { try { if (!child.HasExited) child.Kill(); } catch (InvalidOperationException) { } }
            }
            Check(!job.IsActive, "a closed job is no longer active");
        }
    }
}
