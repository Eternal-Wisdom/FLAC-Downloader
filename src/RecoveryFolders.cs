using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace PlaylistFlac
{
    internal static class RecoveryFolders
    {
        internal const string Name=".playlist-flac-recovery";
        internal static string NewPath(string root,string kind)
        {
            if(kind!="naming" && kind!="duplicates" && kind!="artwork")throw new ArgumentException("Unknown recovery type");
            return Path.Combine(Path.GetFullPath(root),Name,"."+kind+"-backup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        }
        internal static string Prepare(string root)
        {
            root=Path.GetFullPath(root).TrimEnd('\\','/');
            if(root.Length<3 || !Directory.Exists(root) || (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recovery files need a real playlist folder.");
            string path=Path.Combine(root,Name);
            if(File.Exists(path) || (Directory.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0))throw new IOException("The recovery destination is not a safe directory.");
            Directory.CreateDirectory(path);File.SetAttributes(path,File.GetAttributes(path)|FileAttributes.Hidden);
            return path;
        }
        internal static int OrganizeLegacy(string root,CancellationToken ct)
        {
            root=Path.GetFullPath(root).TrimEnd('\\','/');
            if(root.Length<3 || !Directory.Exists(root) || (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a real playlist folder.");
            int moved=0;
            foreach(string directory in Directory.GetDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                string name=Path.GetFileName(directory);
                // Older duplicate maps contain absolute archive paths; leave those in place.
                if(!Regex.IsMatch(name,@"^\.naming-backup-\d{8}-\d{6}-[a-fA-F0-9]{8}$"))continue;
                VerifyTree(directory,ct);
                string destination=Path.Combine(Prepare(root),name);
                if(Directory.Exists(destination) || File.Exists(destination))continue;
                ct.ThrowIfCancellationRequested();
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("A recovery folder changed into a directory link.");
                Directory.Move(directory,destination);moved++;
            }
            return moved;
        }
        private static void VerifyTree(string directory,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recovery folders containing directory links cannot be organized.");
            foreach(string file in Directory.GetFiles(directory))
            {ct.ThrowIfCancellationRequested();if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recovery folders containing file links cannot be organized.");}
            foreach(string child in Directory.GetDirectories(directory))VerifyTree(child,ct);
        }
    }
}
