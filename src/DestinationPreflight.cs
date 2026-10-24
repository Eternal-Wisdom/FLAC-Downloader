using System;
using System.Collections.Generic;
using System.IO;

namespace PlaylistFlac
{
    internal static class DestinationPreflight
    {
        internal static List<string> CheckStorage(string music, string staging, Func<string,long?> freeSpace = null)
        {
            var warnings = new List<string>();
            foreach(var location in new[]{new[]{"Music folder",music},new[]{"Temporary download storage",staging}})
            {
                try
                {
                    foreach(string warning in Check(location[1],freeSpace==null ? (long?)null : freeSpace(location[1])))
                        warnings.Add(location[0]+": "+warning);
                }
                catch(IOException ex) {throw new IOException(location[0]+": "+ex.Message,ex);}
            }
            return warnings;
        }

        internal static List<string> Check(string folder, long? availableBytes = null)
        {
            string full = Path.GetFullPath(folder);
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
                throw new IOException("Choose a normal music folder rather than a Windows device path.");
            if (Path.Combine(full, "Song.flac").Length >= 260)
                throw new IOException("The destination path is too long. Choose a shorter music folder before downloading.");
            for (string current=full; current!=null; current=Path.GetDirectoryName(current))
            {
                if (File.Exists(current)) throw new IOException("The destination contains a file where a folder is required.");
                if (Directory.Exists(current) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Choose a destination without linked folders. This protects music and recovery files.");
            }
            var warnings = new List<string>();
            if (Path.Combine(full,new string('x',150)+".flac").Length >= 260)
                warnings.Add("Long song names may exceed Windows path limits here. A shorter destination folder is recommended.");
            string[] parts=full.Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries);
            foreach(string part in parts)
                if(part.Equals("OneDrive",StringComparison.OrdinalIgnoreCase) || part.StartsWith("OneDrive - ",StringComparison.OrdinalIgnoreCase) || part.Equals("Dropbox",StringComparison.OrdinalIgnoreCase))
                {warnings.Add("A cloud-synced destination can lock or rename files during downloads. A local music folder is recommended.");break;}
            if(full.StartsWith(@"\\",StringComparison.Ordinal))
                warnings.Add("A network destination can lose connections or lock files. Keep the connection available during downloads.");
            if(!availableBytes.HasValue && !full.StartsWith(@"\\",StringComparison.Ordinal))
            {
                var drive=new DriveInfo(Path.GetPathRoot(full));
                if(!drive.IsReady)throw new IOException("The destination drive is not ready. Reconnect it or choose another folder.");
                availableBytes=drive.AvailableFreeSpace;
            }
            if(availableBytes.HasValue)
            {
                if(availableBytes.Value<64L*1024*1024)
                    throw new IOException("Less than 64 MiB is free on the destination drive. Free space or choose another drive before downloading.");
                if(availableBytes.Value<1024L*1024*1024)
                    warnings.Add("Less than 1 GiB is free on the destination drive. A large batch may run out of space.");
            }
            return warnings;
        }
    }
}
