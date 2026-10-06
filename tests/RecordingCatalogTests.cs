using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PlaylistFlac;

internal static class RecordingCatalogTests
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"PlaylistFlacCatalog-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string library=Path.Combine(root,"library"),target=Path.Combine(root,"other");Directory.CreateDirectory(library);Directory.CreateDirectory(target);
            string source=Path.Combine(library,"Song.flac");var bytes=new byte[128];bytes[0]=102;bytes[1]=76;bytes[2]=97;bytes[3]=67;bytes[4]=128;bytes[7]=34;bytes[8]=16;bytes[10]=16;
            ulong packed=((ulong)44100<<44)|((ulong)1<<41)|((ulong)15<<36)|441000;
            for(int i=25;i>=18;i--){bytes[i]=(byte)packed;packed>>=8;}File.WriteAllBytes(source,bytes);
            var song=new Track {Title="Song",Artist="Artist",Album="Original",Isrc="USAAA2600001",DurationSeconds=180};
            var playlist=new Playlist {Tracks=new List<Track>{song}};var index=new Dictionary<string,SavedTrack>{{IndexStore.Key(song),new SavedTrack {Track=song,Path=source,State=1}}};
            var catalog=new RecordingCatalog(Path.Combine(root,"state"));catalog.Register(playlist,library,index,CancellationToken.None);
            catalog=new RecordingCatalog(Path.Combine(root,"state"));
            var reissue=new Track {Title="Song",Artist="Artist",Album="Greatest Hits",Isrc=song.Isrc,DurationSeconds=181};
            string reused=catalog.Reuse(reissue,target,CancellationToken.None);
            Check(reused!=null && File.ReadAllBytes(reused).SequenceEqual(bytes),"same recording on another album reused unchanged");
            Check(Path.GetFullPath(reused).StartsWith(target+Path.DirectorySeparatorChar),"reuse stays in destination");
            string integrated=Path.Combine(root,"integration");
            using(var downloader=new SmartDownloader()) {
                int code=downloader.RunAsync(Path.Combine(root,"engine-does-not-exist.exe"),new Playlist {Name="Reissue",Source="test",Tracks=new List<Track>{reissue}},integrated,"unused","unused",true,false,CancellationToken.None,null,null,null,Path.Combine(root,"state")).GetAwaiter().GetResult();
                Check(code==0,"reuse completes without launching an engine");
                Check(File.ReadAllText(Path.Combine(integrated,"playlist.m3u8")).Contains("Song.flac"),"reused song enters playable playlist with finalized name");
                Check(File.ReadAllBytes(source).SequenceEqual(bytes),"source remains unchanged");
            }
            string canceledFolder=Path.Combine(root,"canceled-run");
            using(var cancel=new CancellationTokenSource())
            using(var downloader=new SmartDownloader()) {
                downloader.Log+=line=> {if(line.StartsWith("Reused saved recording:",StringComparison.Ordinal))cancel.Cancel();};
                bool canceled=false;
                try {downloader.RunAsync(Path.Combine(root,"missing-engine.exe"),new Playlist {Tracks=new List<Track>{reissue,new Track {Title="Other",Artist="Artist",Isrc="USAAA2600002",DurationSeconds=180}}},canceledFolder,"unused","unused",true,false,cancel.Token,null,null,null,Path.Combine(root,"state")).GetAwaiter().GetResult();}
                catch(OperationCanceledException) {canceled=true;}
                var kept=IndexStore.Read(LibraryLayout.PathFor(canceledFolder,"_index.csv"));
                Check(canceled && kept.ContainsKey(IndexStore.Key(reissue)) && IndexStore.Done(kept[IndexStore.Key(reissue)]),"stop preserves completed reuse in the index");
            }
            reissue.Artist="Another Artist";Check(catalog.Reuse(reissue,target,CancellationToken.None)==null,"different artist rejected");reissue.Artist=song.Artist;
            reissue.Isrc="USAAA2600002";Check(catalog.Reuse(reissue,target,CancellationToken.None)==null,"conflicting code rejected");
            reissue.Isrc="";Check(catalog.Reuse(reissue,target,CancellationToken.None)==null,"unknown code requires search");reissue.Isrc=song.Isrc;
            reissue.DurationSeconds=200;Check(catalog.Reuse(reissue,target,CancellationToken.None)==null,"different version rejected");reissue.DurationSeconds=180;
            File.SetLastWriteTimeUtc(source,File.GetLastWriteTimeUtc(source).AddSeconds(10));Check(catalog.Reuse(reissue,target,CancellationToken.None)==null,"changed source invalidates cache");
            catalog.Register(playlist,library,index,CancellationToken.None);
            using(var cancel=new CancellationTokenSource()) {cancel.Cancel();bool stopped=false;try {catalog.Reuse(reissue,target,cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"cancellation stops reuse");}
            Directory.Delete(integrated,true);File.Delete(source);Check(catalog.Reuse(reissue,target,CancellationToken.None)==null,"all registered sources missing falls back to search");
        }
        finally {Directory.Delete(root,true);}
    }
    private static void Check(bool value,string reason){if(!value)throw new Exception("Recording catalog: "+reason);}
}
