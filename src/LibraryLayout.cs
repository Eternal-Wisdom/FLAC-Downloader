using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace PlaylistFlac {
 internal static class LibraryLayout {
  internal const string Name=".playlist-flac";
  private static readonly string[] Files={"_index.csv","playlist.csv","Covers.csv","FLAC-check.csv","FLAC-audio-check.csv",".active-search-index.csv",".active-playlist.m3u8","playlist.m3u8"};
  internal static string Storage(string root) {
   string directory=Path.Combine(AppState.StateDirectory,"collections",Id(CollectionFolders.StorageIdentity(Path.GetFullPath(root)).TrimEnd('\\','/').ToUpperInvariant()).Substring(0,24));
   Check(directory);return directory;
  }
  internal static string Id(string value) {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
  internal static void Check(string path) {
   for(string cursor=Path.GetFullPath(path);cursor!=null;cursor=Path.GetDirectoryName(cursor))
    if((File.Exists(cursor)||Directory.Exists(cursor)) && (File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked library paths are not supported.");
  }
  internal static string PathFor(string root,string name) {
   if(Path.GetFileName(name)!=name)throw new ArgumentException("Expected a metadata filename.");
   string directory=Storage(root),modern=Path.Combine(directory,name),legacy=Path.Combine(root,name),old=Path.Combine(root,Name,name);
   Check(old);Check(legacy);Check(modern);
   if(File.Exists(modern)||Directory.Exists(modern))return modern;
   if(File.Exists(old)||Directory.Exists(old))return old;
   if(File.Exists(legacy)||Directory.Exists(legacy))return legacy;
   return File.Exists(Path.Combine(directory,"music-root.txt"))?modern:legacy;
  }
  internal static string Work(string root,string name) {
   string path=Path.Combine(Storage(root),name);Check(path);return path;
  }
  internal static string MetadataPath(string root,string path) {
   path=Path.GetFullPath(path);string storage=Storage(root)+Path.DirectorySeparatorChar;
   if(!path.StartsWith(storage,StringComparison.OrdinalIgnoreCase) && !path.StartsWith(Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Metadata leaves the collection storage.");
   Check(path);return path;
  }
  internal static string MusicRoot(string index) {
   string directory=Path.GetDirectoryName(Path.GetFullPath(index)),marker=Path.Combine(directory,"music-root.txt");
   if(File.Exists(marker)){Check(marker);return Path.GetFullPath(File.ReadAllText(marker));}
   return Path.GetFileName(directory)==Name && Path.GetFileName(index)=="_index.csv"?Path.GetDirectoryName(directory):directory;
  }
  internal static void Prepare(string root,CancellationToken ct) {
   root=Path.GetFullPath(root);Check(root);string directory=Storage(root);Directory.CreateDirectory(directory);
   string marker=Path.Combine(directory,"music-root.txt");
   if(File.Exists(marker) && !String.Equals(File.ReadAllText(marker),root,StringComparison.OrdinalIgnoreCase))throw new IOException("Collection storage belongs to another music folder.");
   foreach(string name in Files) {
    ct.ThrowIfCancellationRequested();string target=Path.Combine(directory,name);
    foreach(string old in new[]{Path.Combine(root,Name,name),Path.Combine(root,name)}) {
     Check(old);if(!File.Exists(old))continue;
     if(File.Exists(target)||Directory.Exists(target))throw new IOException("Conflicting metadata was preserved: "+name);
     File.Move(old,target);
    }
   }
   if(!File.Exists(marker))IndexStore.AtomicWrite(marker,root);
   // Preserve old maps while migrating their staging paths with a journal.
   string search=Path.Combine(root,".search"),to=Work(root,".search");
   if(Directory.Exists(search)) {Check(search);RecoveryFolders.VerifyTree(search,ct);MoveTree(search,to,ct);}
   string recovery=Path.Combine(root,RecoveryFolders.Name);
   string recoveryJournal=Work(root,"recovery-migration.txt");
   if(Directory.Exists(recovery)) {
    if(!File.Exists(recoveryJournal))IndexStore.AtomicWrite(recoveryJournal,recovery);
    MoveTree(recovery,Work(root,"recovery"),ct);
   }
   if(File.Exists(recoveryJournal)) {RecoveryFolders.RelocateMaps(Work(root,"recovery"),recovery,Work(root,"recovery"));File.Delete(recoveryJournal);}
   MigrateIncoming(root,ct);
   RecoveryFolders.OrganizeLegacy(root,ct);
   string oldMetadata=Path.Combine(root,Name);if(Directory.Exists(oldMetadata) && Directory.GetFileSystemEntries(oldMetadata).Length==0)Directory.Delete(oldMetadata);
  }
  private static void MigrateIncoming(string root,CancellationToken ct) {
   string from=Path.Combine(root,".incoming"),to=Work(root,".incoming"),journal=Work(root,"incoming-migration.txt");
   if(!Directory.Exists(from) && !File.Exists(journal))return;
   if(!File.Exists(journal))File.WriteAllText(journal,from,new UTF8Encoding(false));
   if(Directory.Exists(from))MoveTree(from,to,ct);
   foreach(string name in new[]{"_index.csv",".active-search-index.csv"}) {
    string path=PathFor(root,name);if(!File.Exists(path))continue;
    var rows=IndexStore.Read(path);bool changed=false;
    foreach(var row in rows.Values)if(!String.IsNullOrEmpty(row.Path) && row.Path.StartsWith(from+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) {
     row.Path=to+row.Path.Substring(from.Length);changed=true;
    }
    if(name=="_index.csv") {
     foreach(var row in rows.Values)if((row.State==1||row.State==3) && !String.IsNullOrEmpty(row.Path) && row.Path.StartsWith(to+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) {row.Path=Adopt(root,row.Path);changed=true;}
     if(changed)IndexStore.SaveIndex(root,rows);
    } else if(changed)IndexStore.WriteIndex(path,root,rows);
   }
   string map=Path.Combine(Work(root,".search"),"album-map.json");
   if(File.Exists(map))IndexStore.AtomicWrite(map,File.ReadAllText(map).Replace(from.Replace("\\","\\\\"),to.Replace("\\","\\\\")));
   File.Delete(journal);
  }
  internal static void MoveTree(string from,string to,CancellationToken ct) {
   ct.ThrowIfCancellationRequested();Check(from);Check(to);RecoveryFolders.VerifyTree(from,ct);Directory.CreateDirectory(to);
   foreach(string file in Directory.GetFiles(from)) {
    ct.ThrowIfCancellationRequested();string target=Path.Combine(to,Path.GetFileName(file));Check(target);
    if(File.Exists(target)||Directory.Exists(target))throw new IOException("Conflicting recovery files were preserved: "+Path.GetFileName(file));
    File.Move(file,target);
   }
   foreach(string child in Directory.GetDirectories(from))MoveTree(child,Path.Combine(to,Path.GetFileName(child)),ct);
   if(Directory.GetFileSystemEntries(from).Length==0)Directory.Delete(from);
  }
  internal static void ReleaseLegacyLock(IDisposable held) {((CollectionLock)held).ReleaseLegacy();}
  internal static IDisposable Lock(string root) {return new CollectionLock(root);}
  private sealed class CollectionLock:IDisposable {
   private FileStream modern,legacy;
   internal CollectionLock(string root) {
    try {
     Check(root);string old=Path.Combine(root,".download.lock");Check(old);
     if(File.Exists(old))legacy=new FileStream(old,FileMode.Open,FileAccess.ReadWrite,FileShare.Delete);
     Directory.CreateDirectory(Storage(root));modern=new FileStream(Work(root,".download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    } catch(IOException ex) {Dispose();int code=ex.HResult & 0xffff;if(code==32||code==33)throw new IOException("Another download is already using this playlist folder. Stop it before organizing files.",ex);throw;} catch {Dispose();throw;}
   }
   internal void ReleaseLegacy(){if(legacy!=null){legacy.Dispose();legacy=null;}}
   public void Dispose(){if(modern!=null)modern.Dispose();ReleaseLegacy();}
  }
  // Completed staging files are adopted before entering the canonical music index.
  // The deterministic target permits recovery if the process stops after the move.
  internal static string Adopt(string root,string path) {
   if(String.IsNullOrEmpty(path))return path;
   path=Path.GetFullPath(path);string incoming=Work(root,".incoming")+Path.DirectorySeparatorChar;
   string legacy=Path.Combine(Path.GetFullPath(root),".incoming")+Path.DirectorySeparatorChar;
   if(path.StartsWith(legacy,StringComparison.OrdinalIgnoreCase) && !File.Exists(path))path=incoming+path.Substring(legacy.Length);
   if(!path.StartsWith(incoming,StringComparison.OrdinalIgnoreCase))return path;
   Check(path);if(!String.Equals(Path.GetExtension(path),".flac",StringComparison.OrdinalIgnoreCase))throw new IOException("Only completed FLAC files can enter the library.");
   string target=Path.Combine(root,".completed-"+Id(path.ToUpperInvariant())+".flac");Check(target);
   if(File.Exists(path)) {if(File.Exists(target))throw new IOException("A recovered transfer conflicts with a saved file; both were preserved.");File.Move(path,target);}
   return target;
  }
  internal static void RemoveEmptyLegacy(string root) {
   foreach(string name in new[]{"playlist",".incoming"}) {
    string directory=Path.Combine(root,name);if(!Directory.Exists(directory))continue;Check(directory);
    RemoveEmpty(directory);
   }
   string old=Path.Combine(root,".download.lock");if(File.Exists(old)){Check(old);File.Delete(old);}
  }
  // Call only while holding the collection lock, after canonical bookkeeping commits.
  internal static int CleanupCompletedLocked(string root,Action<string> log) {
   root=Path.GetFullPath(root);Check(root);Prepare(root,CancellationToken.None);
   var index=IndexStore.Read(PathFor(root,"_index.csv"));
   foreach(var row in index.Values)
    if((row.State==1 || row.State==3) && !String.IsNullOrEmpty(row.Path) && File.Exists(row.Path) && !DuplicateLibrary.SafeFile(root,row.Path))
     throw new IOException("A saved recording still references temporary or external storage. Temporary files were kept for review.");
   string[] directories={Work(root,".incoming"),Work(root,".search"),Work(root,"recovery")};
   foreach(string directory in directories)if(Directory.Exists(directory))RecoveryFolders.VerifyTree(directory,CancellationToken.None);
   string[] files={Work(root,".active-search-index.csv"),Work(root,".active-playlist.m3u8")};
   foreach(string file in files)Check(file);
   int count=0;
   foreach(string directory in directories)if(Directory.Exists(directory))count+=DeleteTemporaryTree(directory);
   foreach(string file in files)if(File.Exists(file)){Check(file);File.Delete(file);count++;}
   RemoveEmptyLegacy(root);
   if(log!=null)log("Cleared "+count+" temporary and recovery files. Saved songs and download history are kept.");
   return count;
  }
  private static int DeleteTemporaryTree(string directory) {
   Check(directory);int count=0;
   foreach(string file in Directory.GetFiles(directory)){Check(file);File.Delete(file);count++;}
   foreach(string child in Directory.GetDirectories(directory))count+=DeleteTemporaryTree(child);
   Check(directory);Directory.Delete(directory);return count;
  }
  private static void RemoveEmpty(string directory) {
   Check(directory);foreach(string child in Directory.GetDirectories(directory))RemoveEmpty(child);
   if(Directory.GetFileSystemEntries(directory).Length==0)Directory.Delete(directory);
  }
 }
}
