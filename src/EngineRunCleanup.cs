using System;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;

namespace PlaylistFlac
{
    // Each engine run writes a private session.conf (it holds the Soulseek password) into its own
    // run folder and removes it in a finally block. A hard kill (End Task, a crash, a power loss)
    // skips that block and leaves the file behind.
    //
    // Every engine run holds the cross-process search-session lease for its whole life, and removes
    // its run folder before releasing it. So whoever holds the lease knows that any other run folder
    // under the same root has no owning app session. An uncontained orphan engine may still exist.
    // Cleanup removes its temporary config, not the process. The startup path only runs
    // when it can take the lease itself, and never touches anything except Guid-named run folders.
    internal static class EngineRunCleanup
    {
        internal const string SessionFileName = "session.conf";
        private static readonly Regex RunFolder = new Regex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

        // The caller must already hold the search-session lease for stateRoot.
        // Returns the number of leftover sign-in files removed. Never throws: cleanup is best-effort
        // and must not stop a download or the app from starting.
        internal static int RemoveAbandonedRuns(string stateRoot, Action<string> log)
        {
            int removed = 0, stuck = 0;
            try
            {
                if (!Directory.Exists(stateRoot) || (File.GetAttributes(stateRoot) & FileAttributes.ReparsePoint) != 0) return 0;
                foreach (string directory in Directory.GetDirectories(stateRoot))
                {
                    if (!RunFolder.IsMatch(Path.GetFileName(directory))) continue;
                    try
                    {
                        // Never follow a link out of the private state folder.
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                        string sessionFile = Path.Combine(directory, SessionFileName);
                        if (File.Exists(sessionFile))
                        {
                            if ((File.GetAttributes(sessionFile) & FileAttributes.ReparsePoint) != 0) continue;
                            File.Delete(sessionFile); removed++;
                        }
                        try { Directory.Delete(directory, false); }
                        catch (IOException) { /* The engine left other files here; they hold no sign-in data. */ }
                    }
                    catch (IOException) { stuck++; }
                    catch (UnauthorizedAccessException) { stuck++; }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            if (log != null)
            {
                if (removed > 0) log("Removed " + removed + " temporary sign-in file" + (removed == 1 ? "" : "s") + " left by an interrupted session.");
                if (stuck > 0) log("Could not remove a temporary sign-in file from an interrupted session. Cleanup will be tried again next time.");
            }
            return removed;
        }

        // Startup variant: does nothing unless an abandoned run folder exists and no other session
        // is active. A live session's folder is never touched because its owner holds the lease.
        internal static int RemoveAbandonedRunsIfIdle(string stateRoot, Action<string> log)
        {
            try
            {
                if (!HasRunFolders(stateRoot)) return 0;
                using (FileStream lease = SearchSessionPacing.TryOpenLease(stateRoot))
                {
                    if (lease == null) return 0;
                    return RemoveAbandonedRuns(stateRoot, log);
                }
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
            catch (SecurityException) { return 0; }
        }

        private static bool HasRunFolders(string stateRoot)
        {
            if (!Directory.Exists(stateRoot) || (File.GetAttributes(stateRoot) & FileAttributes.ReparsePoint) != 0) return false;
            foreach (string directory in Directory.GetDirectories(stateRoot))
                if (RunFolder.IsMatch(Path.GetFileName(directory))) return true;
            return false;
        }
    }
}
