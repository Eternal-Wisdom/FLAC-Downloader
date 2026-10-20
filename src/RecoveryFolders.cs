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
            return Path.Combine(LibraryLayout.Work(root,"recovery"),kind+"-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        }
        internal static string Prepare(string root)
        {
            root=Path.GetFullPath(root).TrimEnd('\\','/');
            if(root.Length<3 || !Directory.Exists(root) || (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recovery files need a real playlist folder.");
            string path=LibraryLayout.Work(root,"recovery");
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
                if(!Regex.IsMatch(name,@"^\.(naming|duplicates|artwork)-backup-\d{8}-\d{6}-[a-fA-F0-9]{8}$"))continue;
                VerifyTree(directory,ct);
                string destination=Path.Combine(Prepare(root),name);
                string journal=destination+".moving";
                if((Directory.Exists(destination)||File.Exists(destination)) && !File.Exists(journal))continue;
                ct.ThrowIfCancellationRequested();
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("A recovery folder changed into a directory link.");
                if(!File.Exists(journal))IndexStore.AtomicWrite(journal,directory);
                LibraryLayout.MoveTree(directory,destination,ct);RelocateMaps(destination,directory,destination);File.Delete(journal);moved++;
            }
            string recovery=LibraryLayout.Work(root,"recovery");
            if(Directory.Exists(recovery))foreach(string journal in Directory.GetFiles(recovery,"*.moving",SearchOption.TopDirectoryOnly)) {
                ct.ThrowIfCancellationRequested();string from=File.ReadAllText(journal),destination=journal.Substring(0,journal.Length-7);
                if(!String.Equals(Path.GetDirectoryName(Path.GetFullPath(from)),root,StringComparison.OrdinalIgnoreCase) || Path.GetFileName(from)!=Path.GetFileName(destination))throw new IOException("A recovery relocation journal needs review.");
                if(!Directory.Exists(from) && Directory.Exists(destination)){RelocateMaps(destination,from,destination);File.Delete(journal);}
            }
            return moved;
        }
        internal static void RelocateMaps(string directory,string from,string to) {
            foreach(string map in Directory.GetFiles(directory,"duplicate-map.csv",SearchOption.AllDirectories)) {
                string original=File.ReadAllText(map),updated=original.Replace(from,to);
                if(updated==original)continue;
                string preserved=map+".original";if(!File.Exists(preserved))File.WriteAllBytes(preserved,File.ReadAllBytes(map));
                IndexStore.AtomicWrite(map,updated);
            }
        }
        internal static void VerifyTree(string directory,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recovery folders containing directory links cannot be organized.");
            foreach(string file in Directory.GetFiles(directory))
            {ct.ThrowIfCancellationRequested();if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recovery folders containing file links cannot be organized.");}
            foreach(string child in Directory.GetDirectories(directory))VerifyTree(child,ct);
        }
    }
}
