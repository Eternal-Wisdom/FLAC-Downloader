using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PlaylistFlac;

internal static class CompletionCleanupTests {
 internal static void Run() {
  string parent=Path.Combine(Path.GetTempPath(),"FlacCompletion-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(parent);
  try {
   string root=Path.Combine(parent,"done");Directory.CreateDirectory(root);
   var track=new Track{Title="Song",Artist="Artist",DurationSeconds=10};var list=new Playlist{Tracks=new List<Track>{track}};
   string audio=Path.Combine(root,"Song.flac");File.WriteAllText(audio,"saved audio bytes");
   IndexStore.Save(root,new Dictionary<string,SavedTrack>{{IndexStore.Key(track),new SavedTrack{Track=track,Path=audio,State=1}}},list);
   foreach(string name in new[]{".incoming",".search","recovery"})Directory.CreateDirectory(LibraryLayout.Work(root,name));
   File.WriteAllText(Path.Combine(LibraryLayout.Work(root,".incoming"),"partial.flac"),"discard partial transfer");
   File.WriteAllText(Path.Combine(LibraryLayout.Work(root,".search"),"active-map.json"),"[]");
   File.WriteAllText(Path.Combine(LibraryLayout.Work(root,"recovery"),"duplicate.flac"),"discard committed backup");
   File.WriteAllText(LibraryLayout.PathFor(root,".active-search-index.csv"),"filepath,artist,album,title,length,tracktype,state,failurereason\r\n");
   string legacy=Path.Combine(root,"playlist");Directory.CreateDirectory(legacy);File.WriteAllText(Path.Combine(legacy,"Orphan.flac"),"preserve unindexed music");
   using(var downloader=new SmartDownloader())Check(downloader.RunAsync("must-not-run.exe",list,root,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult()==0,"completed run");
   foreach(string name in new[]{".incoming",".search","recovery"})Check(!Directory.Exists(LibraryLayout.Work(root,name)),name+" cleared on completion");
   Check(!File.Exists(LibraryLayout.PathFor(root,".active-search-index.csv")),"raw session index cleared");
   Check(File.ReadAllText(audio)=="saved audio bytes" && File.ReadAllText(Path.Combine(root,"Orphan.flac"))=="preserve unindexed music","saved and unindexed music preserved");
   Check(!Directory.Exists(legacy) && Directory.GetDirectories(root).Length==0,"old playlist layer flattened");
   Check(File.Exists(LibraryLayout.PathFor(root,"_index.csv")) && LibraryStatus.Read(root,list).Completed==1,"resume index retained");
   using(var held=LibraryLayout.Lock(root))Check(LibraryLayout.CleanupCompletedLocked(root,null)==0,"cleanup idempotent");
   string review=Path.Combine(parent,"review");Directory.CreateDirectory(review);
   var first=new Track{Title="Same",Artist="Artist",Album="Album",DurationSeconds=10,Isrc="USAAA2400001"};
   var second=new Track{Title="Same",Artist="Artist",Album="Album",DurationSeconds=10,Isrc="USAAA2400002"};
   Directory.CreateDirectory(LibraryLayout.Work(review,"recovery"));File.WriteAllText(Path.Combine(LibraryLayout.Work(review,"recovery"),"old.txt"),"temporary");
   using(var downloader=new SmartDownloader())Check(downloader.RunAsync("must-not-run.exe",new Playlist{Tracks=new List<Track>{first,second}},review,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult()==1,"settled review-only job needs no engine");
   Check(!Directory.Exists(LibraryLayout.Work(review,"recovery")),"settled job clears temporary data even when tracks need review");
   Guard(parent,track);
  } finally {if(Directory.Exists(parent))Directory.Delete(parent,true);}
 }
 private static void Guard(string parent,Track track) {
  string root=Path.Combine(parent,"guard");Directory.CreateDirectory(root);LibraryLayout.Prepare(root,CancellationToken.None);
  string recovery=LibraryLayout.Work(root,"recovery");Directory.CreateDirectory(recovery);string file=Path.Combine(recovery,"referenced.flac");File.WriteAllText(file,"still referenced");
  IndexStore.WriteIndex(LibraryLayout.PathFor(root,"_index.csv"),root,new Dictionary<string,SavedTrack>{{IndexStore.Key(track),new SavedTrack{Track=track,Path=file,State=1}}});
  bool rejected=false;using(var held=LibraryLayout.Lock(root))try{LibraryLayout.CleanupCompletedLocked(root,null);}catch(IOException){rejected=true;}
  Check(rejected && File.ReadAllText(file)=="still referenced","never delete a successful indexed file still in temporary storage");
 }
 private static void Check(bool ok,string why){if(!ok)throw new Exception("Completion cleanup: "+why);}
}
