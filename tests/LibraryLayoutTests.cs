using System;
using System.IO;
using System.Threading;
using PlaylistFlac;
internal static class LibraryLayoutTests {
 internal static void Run() {
  string root=Path.Combine(Path.GetTempPath(),"PlaylistFlacLayout-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try {
   string music=Path.Combine(root,"playlist");Directory.CreateDirectory(music);string audio=Path.Combine(music,"example.flac");File.WriteAllText(audio,"unchanged");
   string csv="filepath,artist,album,title,length,tracktype,state,failurereason\r\n./playlist/example.flac,Example,Album,Example,10,0,1,0\r\n";File.WriteAllText(Path.Combine(root,"_index.csv"),csv);
   if(LibraryLayout.PathFor(root,"_index.csv")!=Path.Combine(root,"_index.csv"))throw new Exception("Legacy read moved data");
   LibraryLayout.Prepare(root,CancellationToken.None);
   string modern=LibraryLayout.PathFor(root,"_index.csv");if(File.ReadAllText(modern)!=csv || File.Exists(Path.Combine(root,"_index.csv")))throw new Exception("Metadata migration failed");
   foreach(var saved in IndexStore.Read(modern).Values)if(saved.Path!=audio)throw new Exception("Moved index resolved music incorrectly");
   LibraryLayout.Prepare(root,CancellationToken.None);if(File.ReadAllText(audio)!="unchanged")throw new Exception("Music changed");
   File.WriteAllText(Path.Combine(root,"_index.csv"),"conflicting metadata");bool rejected=false;try{LibraryLayout.Prepare(root,CancellationToken.None);}catch(IOException){rejected=true;}
   if(!rejected || File.ReadAllText(modern)!=csv || !File.Exists(Path.Combine(root,"_index.csv")))throw new Exception("Conflicting indexes were not preserved");
  }finally{Directory.Delete(root,true);}
 }
}
