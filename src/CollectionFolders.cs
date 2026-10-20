using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
namespace PlaylistFlac {
 internal sealed class CollectionLocation {
  public string Parent,Source,Folder,Original,MovingFrom,LegacyHash;
  public bool Album;
 }
 internal static class CollectionFolders {
  private static readonly object gate=new object();
  private static string Registry {get{return Path.Combine(AppState.StateDirectory,"collection-folders.json");}}
  private static FileStream heldRegistry;
  private static int registryDepth;
  private sealed class LeaseHandle:IDisposable {public void Dispose(){if(--registryDepth==0){heldRegistry.Dispose();heldRegistry=null;}}}
  private static IDisposable Lease() {
   if(registryDepth==0) {
    Directory.CreateDirectory(AppState.StateDirectory);string path=Path.Combine(AppState.StateDirectory,".collection-folders.lock");LibraryLayout.Check(path);
    try {heldRegistry=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    catch(IOException ex){throw new IOException("Another app instance is organizing collection folders. Wait for it to finish.",ex);}
   }
   registryDepth++;return new LeaseHandle();
  }
  private static List<CollectionLocation> Read() {
   LibraryLayout.Check(Registry);
   return File.Exists(Registry)?new JavaScriptSerializer().Deserialize<List<CollectionLocation>>(File.ReadAllText(Registry)):new List<CollectionLocation>();
  }
  private static void Save(List<CollectionLocation> records) {Directory.CreateDirectory(AppState.StateDirectory);IndexStore.AtomicWrite(Registry,new JavaScriptSerializer().Serialize(records));}
  internal static string StorageIdentity(string folder) {
   lock(gate) {
    var entry=Read().FirstOrDefault(x=>Same(x.Folder,folder)||Same(x.MovingFrom,folder));
    return entry==null?folder:entry.Original;
   }
  }
  private static bool Same(string a,string b) {return a!=null && b!=null && String.Equals(Path.GetFullPath(a).TrimEnd('\\','/'),Path.GetFullPath(b).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase);}
  private static string Clean(string name) {
   string clean=new string((name??"Playlist").Select(c=>Char.IsControl(c)||Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray()).Trim(' ','.');
   if(clean.Length>64)clean=clean.Substring(0,Char.IsHighSurrogate(clean[63])?63:64).Trim(' ','.');
   if(clean.Length==0)clean="Playlist";
   if(Regex.IsMatch(clean,@"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])\s*($|\.)",RegexOptions.IgnoreCase))clean="_"+clean;
   return clean;
  }
  internal static string Resolve(string parent,string name,string source) {
   lock(gate) using(var lease=Lease()) {
    parent=Path.GetFullPath(parent);string identity=source??name??"Playlist";
    var records=Read();var entry=records.FirstOrDefault(x=>Same(x.Parent,parent) && x.Source==identity);
    if(entry==null)entry=records.FirstOrDefault(x=>Same(x.Parent,parent) && x.LegacyHash==LibraryLayout.Id(identity).Substring(0,8) && x.Source=="legacy:"+x.LegacyHash);
    if(entry!=null){if(entry.Source!=identity){entry.Source=identity;Save(records);}return entry.MovingFrom!=null && Directory.Exists(entry.MovingFrom)?entry.MovingFrom:entry.Folder;}
    string clean=Clean(name),legacy=Path.Combine(parent,clean+" - "+LibraryLayout.Id(identity).Substring(0,8));
    string folder=Directory.Exists(legacy)?legacy:Available(parent,clean,records,null);
    records.Add(new CollectionLocation{Parent=parent,Source=identity,Folder=folder,Original=folder});Save(records);return folder;
   }
  }
  private static string Available(string parent,string clean,List<CollectionLocation> records,string own) {
   string candidate=Path.Combine(parent,clean);int suffix=2;
   while((Directory.Exists(candidate)||File.Exists(candidate)||records.Any(x=>Same(x.Folder,candidate))) && !Same(candidate,own))candidate=Path.Combine(parent,clean+" ("+(suffix++)+")");
   return candidate;
  }
  internal static string Organize(string parent,Playlist playlist,CancellationToken ct) {
   lock(gate) using(var lease=Lease()) {
    string from=Resolve(parent,playlist.Name,playlist.Source);var records=Read();var entry=records.First(x=>Same(x.Parent,parent)&&x.Source==(playlist.Source??playlist.Name??"Playlist"));
    if(!Directory.Exists(from))return from;
    if(SpotifyImporter.IsAlbumLink(playlist.Source) && !entry.Album){entry.Album=true;Save(records);}
    string to=entry.MovingFrom!=null?entry.Folder:Available(Path.GetFullPath(parent),Clean(playlist.Name),records,from);
    if(Same(from,to) && entry.MovingFrom==null)return from;
    string previous=entry.MovingFrom??from;
    using(var held=LibraryLayout.Lock(from)) {
     if(entry.MovingFrom!=null && Same(from,to)) {
      string marker=Path.Combine(LibraryLayout.Storage(from),"music-root.txt");
      if(File.Exists(marker) && Same(File.ReadAllText(marker),previous))IndexStore.AtomicWrite(marker,to);
     }
     LibraryLayout.Prepare(from,ct);ct.ThrowIfCancellationRequested();
     // Canonical paths become relative before the directory move. A restart can
     // therefore recognize saved music even if it occurs before journal completion.
     var index=IndexStore.Read(LibraryLayout.PathFor(from,"_index.csv"));IndexStore.Save(from,index,playlist);
     if(entry.MovingFrom==null){entry.MovingFrom=from;entry.Folder=to;Save(records);}
     ct.ThrowIfCancellationRequested();LibraryLayout.Check(to);
     if(!Same(from,to)){LibraryLayout.ReleaseLegacyLock(held);Directory.Move(from,to);}
     string storage=LibraryLayout.Storage(to);IndexStore.AtomicWrite(Path.Combine(storage,"music-root.txt"),to);
     foreach(var row in index.Values)if(!String.IsNullOrEmpty(row.Path) && row.Path.StartsWith(from.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))row.Path=to+row.Path.Substring(from.Length);
     IndexStore.Save(to,index,playlist);
     // Historical engine paths can still point into the old staging directory.
     string active=LibraryLayout.PathFor(to,".active-search-index.csv");
     if(File.Exists(active))RewritePaths(active,previous,to,false);
     foreach(string file in new[]{"album-map.json","active-map.json"}) {
      string map=Path.Combine(LibraryLayout.Work(to,".search"),file);if(File.Exists(map))RewritePaths(map,previous,to,true);
     }
     entry.MovingFrom=null;Save(records);return to;
    }
   }
  }
  internal static bool IsAlbum(string folder) {lock(gate){var entry=Read().FirstOrDefault(x=>Same(x.Folder,folder)||Same(x.MovingFrom,folder));return entry!=null && entry.Album;}}
  internal static void RegisterLegacy(string parent,string folder,string name,string hash,bool album) {
   lock(gate) using(var lease=Lease()) {
    if(!Regex.IsMatch(hash??"",@"^[a-f0-9]{8}$") || !Same(Path.GetDirectoryName(Path.GetFullPath(folder)),parent) || Path.GetFileName(folder)!=Clean(name)+" - "+hash)throw new IOException("Unknown legacy collection layout.");
    LibraryLayout.Check(folder);var records=Read();
    if(records.Any(x=>Same(x.Folder,folder)||Same(x.MovingFrom,folder)))return;
    records.Add(new CollectionLocation{Parent=Path.GetFullPath(parent),Source="legacy:"+hash,Folder=Path.GetFullPath(folder),Original=Path.GetFullPath(folder),LegacyHash=hash,Album=album});Save(records);
   }
  }
  private static void RewritePaths(string path,string from,string to,bool json) {
   string old=json?from.Replace("\\","\\\\"):from,modern=json?to.Replace("\\","\\\\"):to;
   string text=File.ReadAllText(path);IndexStore.AtomicWrite(path,Regex.Replace(text,Regex.Escape(old+ (json?"\\\\":"\\")),m=>modern+(json?"\\\\":"\\"),RegexOptions.IgnoreCase));
  }
 }
}
