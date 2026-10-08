using System;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;

namespace PlaylistFlac
{
    internal static class TrackFileActions
    {
        internal static string Resolve(string folder, Playlist playlist, Track track)
        {
            var index=IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv"));
            var group=RecordingGroups.Build(playlist.Tracks).GroupForKey(IndexStore.Key(track));
            var keys=group==null ? new[]{IndexStore.Key(track)} : group.Tracks.Select(IndexStore.Key).ToArray();
            var paths=index.Where(p=>keys.Contains(p.Key) && (p.Value.State==1 || p.Value.State==3))
                .Select(p=>p.Value.Path).Where(p=>SafeFile(folder,p)).Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(paths.Count!=1)throw new IOException(paths.Count==0 ? "No downloaded file is available for this song in this collection." : "This song has multiple saved files. Open the collection folder to review them.");
            return paths[0];
        }

        internal static bool SafeFile(string folder, string file)
        {
            if(String.IsNullOrEmpty(file))return false;
            string root=Path.GetFullPath(folder).TrimEnd('\\','/');
            string path=Path.GetFullPath(file);
            if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) ||
               !String.Equals(Path.GetExtension(path),".flac",StringComparison.OrdinalIgnoreCase) || !File.Exists(path))return false;
            // Reject links at every level, including ancestors of the collection.
            for(string cursor=path;cursor!=null;cursor=Path.GetDirectoryName(cursor))
                if((File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)return false;
            return true;
        }

        internal static void Recycle(string folder, Playlist playlist, Track track, string expectedPath, Action<string> recycle=null)
        {
            using(var held=new FileStream(Path.Combine(folder,".download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                string path=Resolve(folder,playlist,track);
                if(!String.Equals(path,expectedPath,StringComparison.OrdinalIgnoreCase))throw new IOException("The saved file changed. Select the song again before removing it.");
                var index=IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv"));
                if(recycle==null)recycle=p=>FileSystem.DeleteFile(p,UIOption.AllDialogs,RecycleOption.SendToRecycleBin,UICancelOption.ThrowException);
                recycle(path);
                if(File.Exists(path))throw new IOException("The file was not removed. Its saved record was kept.");
                foreach(var saved in index.Values.Where(s=>String.Equals(s.Path,path,StringComparison.OrdinalIgnoreCase)))
                {saved.Path="";saved.State=0;saved.Reason=0;}
                IndexStore.Save(folder,index,playlist);
            }
        }
    }
}
