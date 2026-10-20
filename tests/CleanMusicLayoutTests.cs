using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using PlaylistFlac;

internal static class CleanMusicLayoutTests {
 internal static void Run() {
  string parent=Path.Combine(Path.GetTempPath(),"FlacCleanLayout-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(parent);
  try {
   var album=new Playlist{Name="Album",Source="https://open.spotify.com/album/6ERkW1jF8WvCHp3BGh3kDC",Tracks=new List<Track>{
    new Track{Title="Z first",Artist="Artist",Album="Album",DurationSeconds=10,DiscNumber=1,TrackNumber=1},
    new Track{Title="A second",Artist="Artist",Album="Album",DurationSeconds=20,DiscNumber=2,TrackNumber=1}}};
   string legacy=Path.Combine(parent,"Album - "+LibraryLayout.Id(album.Source).Substring(0,8));Directory.CreateDirectory(Path.Combine(legacy,"playlist"));
   var rows=new Dictionary<string,SavedTrack>();
   foreach(var t in album.Tracks){string audio=Path.Combine(legacy,"playlist",t.Title+".flac");File.WriteAllText(audio,t.Title);rows.Add(IndexStore.Key(t),new SavedTrack{Track=t,Path=audio,State=1});}
   IndexStore.Save(legacy,rows,album);
   // A stopped old build leaves a lock and a relative canonical index.
   File.WriteAllText(Path.Combine(legacy,".download.lock"),"");
   Check(AppState.PlaylistFolder(parent,album.Name,album.Source)==legacy,"old collection must be discovered");
   string modern=CollectionFolders.Organize(parent,album,CancellationToken.None);
   Check(modern==Path.Combine(parent,"Album") && !Directory.Exists(legacy),"friendly folder migration");
   Check(CollectionFolders.StorageIdentity(modern)==legacy,"retry identity survives rename");
   using(var downloader=new SmartDownloader()) {
    int code=downloader.RunAsync("must-not-run.exe",album,modern,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult();
    Check(code==0,"saved album must not download again");
   }
   Check(Directory.GetDirectories(modern).Length==0,"no work or playlist subfolders");
   Check(Directory.GetFiles(modern).Select(Path.GetFileName).OrderBy(x=>x).SequenceEqual(new[]{"01 - Z first.flac","02 - A second.flac"}),"album order follows import across discs");
   Check(File.ReadAllText(Path.Combine(modern,"01 - Z first.flac"))=="Z first","music bytes preserved");
   Check(LibraryStatus.Read(modern,album).Completed==2,"resume recognizes moved files");
   Check(AppState.PlaylistFolder(parent,album.Name,album.Source)==modern,"stable friendly folder");
   string other=AppState.PlaylistFolder(parent,"Album","another source");Check(other!=modern,"same-name collections cannot merge");
   using(var downloader=new SmartDownloader())Check(downloader.RunAsync("must-not-run.exe",album,modern,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult()==0,"second restart");
   var playlist=new Playlist{Name="Mix",Source="csv-fixture",Tracks=album.Tracks};string mix=AppState.PlaylistFolder(parent,"Mix",playlist.Source);Directory.CreateDirectory(mix);
   rows.Clear();foreach(var t in playlist.Tracks){string audio=Path.Combine(mix,"old"+rows.Count+".flac");File.WriteAllText(audio,t.Title);rows.Add(IndexStore.Key(t),new SavedTrack{Track=t,Path=audio,State=1});}IndexStore.Save(mix,rows,playlist);
   using(var downloader=new SmartDownloader())downloader.RunAsync("must-not-run.exe",playlist,mix,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult();
   Check(Directory.GetFiles(mix).Select(Path.GetFileName).OrderBy(x=>x).SequenceEqual(new[]{"A second.flac","Z first.flac"}),"playlists remain unnumbered");
   Check(!LibraryLayout.PathFor(modern,"_index.csv").StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"bookkeeping outside music root");
   Check(TrackFileActions.Resolve(modern,album,album.Tracks[1])==Path.Combine(modern,"02 - A second.flac"),"file actions follow numbering");
   Check(FlacAudit.CheckFolder(modern,CancellationToken.None).TotalFiles==2 && Directory.GetFiles(modern).Length==2,"audit reports stay outside music");
   Interrupted(parent);
   UnknownLegacy(parent);
   RenameRecovery(parent);
  } finally {if(Directory.Exists(parent))Directory.Delete(parent,true);}
 }
 private static void UnknownLegacy(string parent) {
  string source="https://open.spotify.com/album/0000000000000000000000",hash=LibraryLayout.Id(source).Substring(0,8);
  string old=Path.Combine(parent,"Saved album - "+hash);Directory.CreateDirectory(old);
  CollectionFolders.RegisterLegacy(parent,old,"Saved album",hash,true);
  var list=new Playlist{Name="Saved album",Source="legacy:"+hash};
  string modern=CollectionFolders.Organize(parent,list,CancellationToken.None);
  Check(CollectionFolders.IsAlbum(modern),"known legacy album kind retained");
  Check(AppState.PlaylistFolder(parent,"Saved album",source)==modern,"later real album import reuses organized folder");
  Check(CollectionFolders.StorageIdentity(modern)==old,"later source adoption preserves retry and storage identity");
 }
 private static void RenameRecovery(string parent) {
  var list=new Playlist{Name="Journal",Source="journal-fixture"};string from=AppState.PlaylistFolder(parent,list.Name,list.Source);
  Directory.CreateDirectory(from);LibraryLayout.Prepare(from,CancellationToken.None);
  string to=Path.Combine(parent,"After move");
  string registry=Path.Combine(AppState.StateDirectory,"collection-folders.json");
  var json=new JavaScriptSerializer();var entries=json.Deserialize<List<CollectionLocation>>(File.ReadAllText(registry));
  var entry=entries.Single(x=>x.Folder==from);entry.Folder=to;entry.MovingFrom=from;
  IndexStore.AtomicWrite(registry,json.Serialize(entries));Directory.Move(from,to);
  Check(CollectionFolders.Organize(parent,list,CancellationToken.None)==to,"restart completes interrupted folder journal");
  Check(File.ReadAllText(Path.Combine(LibraryLayout.Storage(to),"music-root.txt"))==to,"recovered root marker");
  string staging=Path.Combine(LibraryLayout.Work(to,".incoming"),"session");Directory.CreateDirectory(staging);
  string file=Path.Combine(staging,"1.flac");File.WriteAllText(file,"same audio");string adopted=LibraryLayout.Adopt(to,file);
  Check(LibraryLayout.Adopt(to,file)==adopted && File.ReadAllText(adopted)=="same audio","crash after adoption recovers deterministic target");
  Check(LibraryLayout.Adopt(to,file.ToUpperInvariant())==adopted,"staging recovery respects Windows path casing");
 }
 private static void Interrupted(string parent) {
  string root=Path.Combine(parent,"Interrupted");Directory.CreateDirectory(root);
  string incoming=Path.Combine(root,".incoming","old-session");Directory.CreateDirectory(incoming);
  string audio=Path.Combine(incoming,"1.flac");File.WriteAllText(audio,"preserve this completed transfer");
  var track=new Track{Title="Recovered",Artist="Artist",Album="Album",DurationSeconds=10};
  File.WriteAllText(Path.Combine(root,"_index.csv"),"filepath,artist,album,title,length,tracktype,state,failurereason\r\n./.incoming/old-session/1.flac,Artist,Album,Recovered,10,0,1,0\r\n");
  var playlist=new Playlist{Tracks=new List<Track>{track}};
  using(var downloader=new SmartDownloader())Check(downloader.RunAsync("must-not-run.exe",playlist,root,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult()==0,"completed staging transfer survives migration");
  Check(Directory.GetDirectories(root).Length==0 && Directory.GetFiles(root).Length==1 && File.ReadAllText(Path.Combine(root,"Recovered.flac"))=="preserve this completed transfer","interrupted layout is clean and lossless");
 }
 private static void Check(bool ok,string message){if(!ok)throw new Exception("Clean music layout: "+message);}
}
