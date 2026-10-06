using System;
using System.IO;
using System.Threading;
namespace PlaylistFlac {
 internal static class LibraryLayout {
  internal const string Name=".playlist-flac";
  private static readonly string[] Files={"_index.csv","playlist.csv","Covers.csv","FLAC-check.csv"};
  internal static string PathFor(string root,string name) {
   string directory=System.IO.Path.Combine(System.IO.Path.GetFullPath(root),Name);
   if(File.Exists(directory) || (Directory.Exists(directory) && (File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0))throw new IOException("The playlist metadata directory is not safe.");
   string modern=System.IO.Path.Combine(directory,name),legacy=System.IO.Path.Combine(root,name);
   if(!Directory.Exists(directory))return legacy;
   // Read legacy collections without changing files. Migration occurs under the download lock.
   if(!File.Exists(modern) && !Directory.Exists(modern) && (File.Exists(legacy)||Directory.Exists(legacy)))return legacy;
   return modern;
  }
  internal static void Prepare(string root,CancellationToken ct) {
   root=System.IO.Path.GetFullPath(root);if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a real playlist directory.");
   string directory=System.IO.Path.Combine(root,Name);PathFor(root,"_index.csv");Directory.CreateDirectory(directory);File.SetAttributes(directory,File.GetAttributes(directory)|FileAttributes.Hidden);
   foreach(string name in Files) {
    ct.ThrowIfCancellationRequested();string old=System.IO.Path.Combine(root,name),target=System.IO.Path.Combine(directory,name);
    if(File.Exists(old) && (File.Exists(target)||Directory.Exists(target)))throw new IOException("Both old and new playlist metadata exist; neither was overwritten: "+name);
    if(File.Exists(old) && (File.GetAttributes(old)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked metadata cannot be organized.");
   }
   foreach(string name in Files) {
    ct.ThrowIfCancellationRequested();string old=System.IO.Path.Combine(root,name),target=System.IO.Path.Combine(directory,name);
    if(!File.Exists(old))continue;if((File.GetAttributes(old)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked metadata cannot be organized.");
    if(File.Exists(target))throw new IOException("Both old and new playlist metadata exist; neither was overwritten: "+name);
    File.Move(old,target);
   }
   // Search maps and incoming files can contain absolute paths, so they stay in place.
   foreach(string name in new[]{".search",".incoming",".download.lock"}) {
    string path=System.IO.Path.Combine(root,name);if(File.Exists(path)||Directory.Exists(path))File.SetAttributes(path,File.GetAttributes(path)|FileAttributes.Hidden);
   }
  }
 }
}
