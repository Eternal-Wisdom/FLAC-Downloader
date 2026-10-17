using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PlaylistFlac
{
    internal static class ImportedTracklistCache
    {
        private static string FileName(string source)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source ?? ""))).Replace("-","").ToLowerInvariant()+".csv";
        }
        internal static string Save(string directory,Playlist list)
        {
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,FileName(list.Source));CsvPlaylist.Write(path,list);return path;
        }
        internal static Playlist Read(string directory,string path,string source,string name)
        {
            bool supported=Regex.IsMatch(source ?? "",@"^pasted:[0-9a-f]{64}$") || Regex.IsMatch(source ?? "",@"^https://open\.spotify\.com/album/[A-Za-z0-9]{22}$");
            if(!supported || !String.Equals(Path.GetFullPath(path),Path.Combine(Path.GetFullPath(directory),FileName(source)),StringComparison.OrdinalIgnoreCase))
                return CsvPlaylist.Read(path);
            var list=CsvPlaylist.Read(path);list.Source=source;list.Name=String.IsNullOrWhiteSpace(name)?"Imported songs":name;return list;
        }
    }
}
