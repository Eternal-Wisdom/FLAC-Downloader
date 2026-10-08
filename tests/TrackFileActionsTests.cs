using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlaylistFlac;

internal static class TrackFileActionsTests
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"FlacFileActions-"+Guid.NewGuid().ToString("N"));
        string folder=Path.Combine(root,"collection");Directory.CreateDirectory(Path.Combine(folder,"playlist"));
        var song=new Track {Title="KING",Artist="Artist A",Album="Original",DurationSeconds=180};
        var reissue=new Track {Title="KING",Artist="Artist A",Album="Deluxe",DurationSeconds=180};
        var other=new Track {Title="KING",Artist="Artist B",Album="Other",DurationSeconds=180};
        var playlist=new Playlist {Tracks=new List<Track>{song,reissue,other}};
        string file=Path.Combine(folder,"playlist","KING - Artist A.flac"),second=Path.Combine(folder,"playlist","KING - Artist B.flac");
        File.WriteAllText(file,"synthetic audio");File.WriteAllText(second,"other synthetic audio");
        var index=new Dictionary<string,SavedTrack> {
            {IndexStore.Key(reissue),new SavedTrack {Track=reissue,State=1,Path=file}},
            {IndexStore.Key(other),new SavedTrack {Track=other,State=1,Path=second}}
        };
        try
        {
            IndexStore.Save(folder,index,playlist);
            Check(TrackFileActions.Resolve(folder,playlist,song)==file,"reissue resolves through saved identity, not guessed filename");
            Check(TrackFileActions.Resolve(folder,playlist,other)==second,"same title from another artist remains separate");
            Check(!TrackFileActions.SafeFile(folder,Path.Combine(root,"outside.flac")),"outside paths rejected");
            string outside=Path.Combine(root,"outside.flac");File.WriteAllText(outside,"outside");
            index[IndexStore.Key(song)]=new SavedTrack {Track=song,State=1,Path=outside};IndexStore.Save(folder,index,playlist);
            Check(TrackFileActions.Resolve(folder,playlist,song)==file,"an external saved path is never chosen");
            bool called=false,rejected=false;
            using(var held=new FileStream(Path.Combine(folder,".download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                try {TrackFileActions.Recycle(folder,playlist,song,file,p=>called=true);}catch(IOException){rejected=true;}
            }
            Check(rejected && !called && File.Exists(file),"another downloader's lock blocks removal");
            rejected=false;
            try {TrackFileActions.Recycle(folder,playlist,song,second,p=>called=true);}catch(IOException){rejected=true;}
            Check(rejected && !called,"changed confirmation target blocked");
            string before=File.ReadAllText(LibraryLayout.PathFor(folder,"_index.csv"));
            try {TrackFileActions.Recycle(folder,playlist,song,file,p=>{throw new OperationCanceledException();});}catch(OperationCanceledException){}
            Check(File.Exists(file) && before==File.ReadAllText(LibraryLayout.PathFor(folder,"_index.csv")),"cancel preserves file and index");
            // Substitute a reversible temporary move; tests never touch the user's Recycle Bin.
            TrackFileActions.Recycle(folder,playlist,song,file,p=>File.Move(p,Path.Combine(root,"recycled.flac")));
            Check(!File.Exists(file) && File.Exists(second) && File.Exists(outside),"only the resolved song is removed");
            Check(LibraryStatus.Read(folder,playlist).StatusFor(reissue)=="Ready","saved status refreshed after removal");
            Check(!File.ReadAllText(Path.Combine(folder,"playlist.m3u8")).Contains("KING - Artist A"),"playable list drops the removed file");
            File.Move(Path.Combine(root,"recycled.flac"),file);
            index.Remove(IndexStore.Key(song));index[IndexStore.Key(reissue)].Path=file;IndexStore.Save(folder,index,playlist);
            string duplicate=Path.Combine(folder,"playlist","duplicate.flac");File.WriteAllText(duplicate,"duplicate");
            index[IndexStore.Key(song)]=new SavedTrack {Track=song,State=1,Path=duplicate};IndexStore.Save(folder,index,playlist);
            rejected=false;
            try {TrackFileActions.Resolve(folder,playlist,song);}catch(IOException){rejected=true;}
            Check(rejected,"multiple saved files require explicit folder review");
        }
        finally {Directory.Delete(root,true);}
    }
    private static void Check(bool ok,string message) {if(!ok)throw new Exception("Track file actions: "+message);}
}
