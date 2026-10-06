using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace PlaylistFlac
{
    // One engine session owns the search budget. A durable reservation prevents
    // fresh token buckets in retry passes or a quickly restarted app.
    internal sealed class SearchSessionPacing : IDisposable
    {
        private readonly object gate = new object();
        private readonly FileStream sessionLock;
        private readonly string deadlinePath;
        private readonly int windowMs, heartbeatMs;
        private readonly Action<string> log;
        private Timer timer;
        private bool disposed;

        private SearchSessionPacing(FileStream sessionLock, string path, int windowMs, Action<string> log)
        {
            this.sessionLock = sessionLock; deadlinePath = path; this.windowMs = windowMs; this.log = log;
            heartbeatMs = Math.Max(20, Math.Min(5000, windowMs / 4));
        }

        internal static SearchSessionPacing Acquire(string root, int windowMs, CancellationToken ct, Action<string> log)
        {
            Directory.CreateDirectory(root);
            FileStream handle = null;
            bool announced = false;
            while (handle == null)
            {
                ct.ThrowIfCancellationRequested();
                try { handle = new FileStream(Path.Combine(root, "search-session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException ex)
                {
                    int code = ex.HResult & 0xffff;
                    if (code != 32 && code != 33) throw;
                    if (!announced && log != null) log("Another Playlist FLAC download session is active. Waiting for its search budget.");
                    announced = true;
                    if (ct.WaitHandle.WaitOne(200)) ct.ThrowIfCancellationRequested();
                }
            }
            var lease = new SearchSessionPacing(handle, Path.Combine(root, "search-next-session.txt"), windowMs, log);
            try
            {
                string saved = File.Exists(lease.deadlinePath) ? File.ReadAllText(lease.deadlinePath) : null;
                int delay = RemainingDelay(saved, DateTime.UtcNow, windowMs + lease.heartbeatMs);
                if (delay > 0)
                {
                    if (log != null) log("Waiting " + (int)Math.Ceiling(delay / 1000.0) + " seconds before reconnecting to respect Soulseek search limits.");
                    if (ct.WaitHandle.WaitOne(delay)) ct.ThrowIfCancellationRequested();
                }
                ct.ThrowIfCancellationRequested();
                lease.WriteDeadline(windowMs + lease.heartbeatMs);
                lease.timer = new Timer(lease.Heartbeat, null, lease.heartbeatMs, lease.heartbeatMs);
                return lease;
            }
            catch { handle.Dispose(); throw; }
        }

        internal static int RemainingDelay(string saved, DateTime now, int maximumMs)
        {
            if (saved == null) return 0;
            long ticks;
            if (!Int64.TryParse(saved.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                return maximumMs;
            // Bound clock changes and malformed future dates, rather than hanging.
            return (int)Math.Ceiling(Math.Max(0, Math.Min(maximumMs, (new DateTime(ticks, DateTimeKind.Utc) - now).TotalMilliseconds)));
        }

        private void WriteDeadline(int milliseconds)
        {
            IndexStore.AtomicWrite(deadlinePath, DateTime.UtcNow.AddMilliseconds(milliseconds).Ticks.ToString(CultureInfo.InvariantCulture));
        }
        private void Heartbeat(object unused)
        {
            lock (gate)
            {
                if (disposed) return;
                try { WriteDeadline(windowMs + heartbeatMs); }
                catch (IOException) { if (log != null) log("Could not refresh the saved search cooldown."); }
                catch (UnauthorizedAccessException) { if (log != null) log("Could not refresh the saved search cooldown."); }
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (timer != null) timer.Dispose();
                try { WriteDeadline(windowMs); }
                catch (IOException) { if (log != null) log("Could not save the search cooldown."); }
                catch (UnauthorizedAccessException) { if (log != null) log("Could not save the search cooldown."); }
                finally { sessionLock.Dispose(); }
            }
        }
    }
}
