using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PlaylistFlac;

internal static class ArtworkLibraryTests
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"PlaylistFlacArtworkLibraryTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{CheckFill(root).GetAwaiter().GetResult();CheckMaintenance(Path.Combine(root,"maintenance"));}
        finally{Directory.Delete(root,true);}
    }
    private static async Task CheckFill(string root)
    {
        byte[] image=ArtworkTestData.Image();
        var a=new Track{Title="KING",Artist="Artist A",Album="Album",Isrc="USAAA2000001",DurationSeconds=100,CoverUrl="https://i.scdn.co/image/album"};
        var alias=new Track{Title="KING",Artist="Artist A",Album="Deluxe",Isrc=a.Isrc,DurationSeconds=100,CoverUrl=a.CoverUrl};
        var b=new Track{Title="KING",Artist="Artist B",Album="Other",DurationSeconds=120};
        var p=new Playlist{Tracks=new List<Track>{a,alias,b}};
        string one=Path.Combine(root,"one.flac"),two=Path.Combine(root,"two.flac");File.WriteAllBytes(one,ArtworkTestData.Flac());File.WriteAllBytes(two,ArtworkTestData.Flac(2));
        string hash=FlacIdentity.AudioHash(one,CancellationToken.None);
        FlacArtwork.AddFrontCover(two,image,Path.Combine(root,"old-recovery"),CancellationToken.None);
        var index=new Dictionary<string,SavedTrack>();index[IndexStore.Key(a)]=new SavedTrack{Track=a,Path=one,State=1};index[IndexStore.Key(alias)]=new SavedTrack{Track=alias,Path=one,State=1};index[IndexStore.Key(b)]=new SavedTrack{Track=b,Path=two,State=1};
        int fetched=0;Func<Track,CancellationToken,Task<CoverLookupResult>> lookup=(t,ct)=>{fetched++;return Task.FromResult(new CoverLookupResult{Bytes=image,SourceUrl=t.CoverUrl});};
        var result=await ArtworkLibrary.FillAsync(root,p,index,lookup,CancellationToken.None,null);
        Check(result.Added==1 && result.Existing==1 && fetched==1,"Shared recording aliases receive one cover write; existing covers avoid lookups.");
        Check(hash==FlacIdentity.AudioHash(one,CancellationToken.None) && File.Exists(result.ReportPath),"Cover repair preserves encoded audio and writes its report.");
        var repeated=await ArtworkLibrary.FillAsync(root,p,index,lookup,CancellationToken.None,null);Check(repeated.Added==0 && repeated.Existing==2 && fetched==1,"Repeated repair is stable and does no image lookup.");
        string conflict=Path.Combine(root,"conflict.flac");File.WriteAllBytes(conflict,ArtworkTestData.Flac(3));index[IndexStore.Key(a)].Path=conflict;index[IndexStore.Key(alias)].Path=conflict;index[IndexStore.Key(b)].Path=conflict;
        var review=await ArtworkLibrary.FillAsync(root,p,index,lookup,CancellationToken.None,null);Check(review.Review==1 && fetched==1 && !FlacArtwork.HasFrontCover(conflict),"A file assigned to different recordings stays untouched for review.");
        index.Remove(IndexStore.Key(alias));index.Remove(IndexStore.Key(b));File.WriteAllBytes(conflict,new byte[]{0,1,2});
        var invalid=await ArtworkLibrary.FillAsync(root,new Playlist{Tracks=new List<Track>{a}},index,lookup,CancellationToken.None,null);
        Check(invalid.Invalid==1 && fetched==1,"Malformed songs are flagged before spending a cover lookup or changing bytes.");
        File.WriteAllBytes(conflict,ArtworkTestData.Flac(3));
        using(var held=new FileStream(conflict,FileMode.Open,FileAccess.Read,FileShare.None))
        {
            bool failed=false;try{await ArtworkLibrary.FillAsync(root,new Playlist{Tracks=new List<Track>{a}},index,lookup,CancellationToken.None,null);}catch(IOException){failed=true;}
            Check(failed && fetched==1,"A storage read failure stops repair before artwork requests or writes.");
        }
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{await ArtworkLibrary.FillAsync(root,p,index,lookup,cancel.Token,null);}catch(OperationCanceledException){stopped=true;}Check(stopped,"Pre-cancelled repair does no work.");}
    }
    private static void CheckMaintenance(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder,"playlist"));
        var a=new Track{Title="KING",Artist="Artist A",Album="Original",DurationSeconds=100,Isrc="USAAA2000001"};var deluxe=new Track{Title="KING",Artist="Artist A",Album="Deluxe",DurationSeconds=100,Isrc=a.Isrc};var b=new Track{Title="KING",Artist="Artist B",Album="Other",DurationSeconds=120};
        var p=new Playlist{Tracks=new List<Track>{a,deluxe,b}};var index=new Dictionary<string,SavedTrack>();int i=0;foreach(var t in p.Tracks){string path=Path.Combine(folder,"playlist","old"+(++i)+".flac");File.WriteAllBytes(path,ArtworkTestData.Flac((byte)i));index[IndexStore.Key(t)]=new SavedTrack{Track=t,Path=path,State=1};}IndexStore.Save(folder,index,p);
        using(var held=new FileStream(Path.Combine(folder,".download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var downloader=new SmartDownloader())
        {bool locked=false;try{downloader.FixLibraryAsync(p,folder,null,CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){locked=true;}Check(locked && File.Exists(Path.Combine(folder,"playlist","old1.flac")),"Maintenance honors the downloader lock before moving files.");}
        using(var downloader=new SmartDownloader())
        {var result=downloader.FixLibraryAsync(p,folder,null,CancellationToken.None).GetAwaiter().GetResult();Check(result.Duplicates.ArchivedFiles==1 && result.Names.Renamed==2,"One action consolidates album repeats and renames both different artists.");}
        var after=IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv"));Check(after[IndexStore.Key(a)].Path==after[IndexStore.Key(deluxe)].Path,"Both albums link to one retained recording.");
        Check(File.Exists(Path.Combine(folder,"playlist","KING (Artist A).flac")) && File.Exists(Path.Combine(folder,"playlist","KING (Artist B).flac")),"Same-title songs receive artist qualifiers without numbering.");
        Check(Directory.GetFiles(Directory.GetDirectories(Path.Combine(folder,RecoveryFolders.Name),".duplicates-backup-*").Single(),"*.flac").Length==1,"The extra audio remains recoverable.");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception("Artwork library: "+message);}
}
