using System;
using System.IO;

namespace PlaylistFlac
{
    internal static class EnvironmentGuard
    {
        internal static void EnsureWritableState(string directory)
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, ".write-check-" + Guid.NewGuid().ToString("N"));
            // A unique file never overwrites settings or another instance's probe.
            using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            { file.WriteByte(0); file.Flush(true); }
        }
    }
}
