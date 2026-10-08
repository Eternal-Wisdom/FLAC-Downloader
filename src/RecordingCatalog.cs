using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    // Conservative cross-collection reuse. Unknown recording codes are never
    // matched by title alone. Reused files remain inside the destination's safety boundary.
    internal sealed class CatalogRecording
    {
        public Track Track {get;set;}
        public string Path {get;set;}
        public long Size {get;set;}
        public DateTime ModifiedUtc {get;set;}
        public long ModifiedTicks {get;set;}
        public string Signature {get;set;}
    }
    internal sealed class RecordingCatalog
    {
        private readonly string path;
        private readonly List<CatalogRecording> entries;
        private sealed class Candidate { internal CatalogRecording Entry; internal RecordingDescriptor Identity; }
        private Dictionary<string,List<Candidate>> byIsrc;
        private void BuildLookup()
        {
            var lookup=new Dictionary<string,List<Candidate>>(StringComparer.Ordinal);
            foreach(var entry in entries)
            {
                if(entry==null || entry.Track==null)continue;
                var identity=new RecordingDescriptor(entry.Track);
                if(identity.Isrc.Length==0 || !identity.KnownLength || !identity.HasMetadata)continue;
                List<Candidate> bucket;
                if(!lookup.TryGetValue(identity.Isrc,out bucket))lookup.Add(identity.Isrc,bucket=new List<Candidate>());
                bucket.Add(new Candidate {Entry=entry,Identity=identity});
            }
            byIsrc=lookup;
        }
        internal IEnumerable<CatalogRecording> Matching(Track track)
        {
            var identity=new RecordingDescriptor(track);
            List<Candidate> bucket;
            if(identity.Isrc.Length==0 || !identity.KnownLength || !identity.HasMetadata || !byIsrc.TryGetValue(identity.Isrc,out bucket))return Enumerable.Empty<CatalogRecording>();
            return bucket.Where(c=>c.Identity.MetadataKey==identity.MetadataKey && Math.Abs(c.Identity.Length-identity.Length)<=2).Select(c=>c.Entry);
        }
        internal RecordingCatalog(string directory)
        {
            path=System.IO.Path.Combine(directory,"recordings.json");
            entries=File.Exists(path)?new JavaScriptSerializer {MaxJsonLength=32*1024*1024}.Deserialize<List<CatalogRecording>>(File.ReadAllText(path)):new List<CatalogRecording>();
            if(entries==null)throw new FormatException("The recording catalog could not be read.");
            BuildLookup();
        }
        internal string Reuse(Track track,string folder,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var matching=Matching(track);
            foreach(var entry in matching)
            {
                ct.ThrowIfCancellationRequested();
                if(!Valid(entry))continue;
                string incoming=System.IO.Path.Combine(folder,".incoming");Directory.CreateDirectory(incoming);
                string target=System.IO.Path.Combine(incoming,"reuse-"+Guid.NewGuid().ToString("N")+".flac");
                try
                {
                    using(var input=new FileStream(entry.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
                    using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {
                        var buffer=new byte[128*1024];int count;
                        while((count=input.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();output.Write(buffer,0,count);}
                    }
                    if(!Valid(entry) || new FileInfo(target).Length!=entry.Size || FlacIdentity.QuickSignature(target)!=entry.Signature)throw new IOException("A saved recording changed during reuse.");
                    return target;
                }
                catch {if(File.Exists(target))File.Delete(target);throw;}
            }
            return null;
        }
        private static bool Valid(CatalogRecording entry)
        {
            if(String.IsNullOrEmpty(entry.Path) || String.IsNullOrEmpty(entry.Signature) || !System.IO.Path.IsPathRooted(entry.Path) || !String.Equals(System.IO.Path.GetExtension(entry.Path),".flac",StringComparison.OrdinalIgnoreCase))return false;
            string cursor=System.IO.Path.GetFullPath(entry.Path);
            while(cursor!=null) {if((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)return false;cursor=System.IO.Path.GetDirectoryName(cursor);}
            var file=new FileInfo(entry.Path);
            return file.Exists && file.Length==entry.Size && file.LastWriteTimeUtc.Ticks==entry.ModifiedTicks && FlacIdentity.QuickSignature(entry.Path)==entry.Signature;
        }
        internal void Register(Playlist playlist,string folder,IDictionary<string,SavedTrack> index,CancellationToken ct)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            using(var catalogLock=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
            if(File.Exists(path)) {
                var current=new JavaScriptSerializer {MaxJsonLength=32*1024*1024}.Deserialize<List<CatalogRecording>>(File.ReadAllText(path));
                if(current==null)throw new FormatException("The recording catalog could not be read.");
                entries.Clear();entries.AddRange(current);
            }
            foreach(var track in playlist.Tracks)
            {
                ct.ThrowIfCancellationRequested();SavedTrack saved;
                if(!index.TryGetValue(IndexStore.Key(track),out saved) || !IndexStore.Done(saved) || !DuplicateLibrary.SafeFile(folder,saved.Path))continue;
                if(new RecordingDescriptor(track).Isrc.Length==0)continue;
                var file=new FileInfo(saved.Path);
                var known=entries.FirstOrDefault(e=>e!=null && String.Equals(e.Path,file.FullName,StringComparison.OrdinalIgnoreCase) && e.Size==file.Length && e.ModifiedTicks==file.LastWriteTimeUtc.Ticks);
                if(known!=null && known.Track!=null && !String.IsNullOrEmpty(known.Signature) && new RecordingDescriptor(known.Track).Isrc==new RecordingDescriptor(track).Isrc && new RecordingDescriptor(known.Track).MetadataKey==new RecordingDescriptor(track).MetadataKey)continue;
                string signature=FlacIdentity.QuickSignature(saved.Path);if(signature==null)continue;
                entries.RemoveAll(e=>e!=null && String.Equals(e.Path,saved.Path,StringComparison.OrdinalIgnoreCase));
                entries.Add(new CatalogRecording {Track=track,Path=file.FullName,Size=file.Length,ModifiedUtc=file.LastWriteTimeUtc,ModifiedTicks=file.LastWriteTimeUtc.Ticks,Signature=signature});
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            IndexStore.AtomicWrite(path,new JavaScriptSerializer {MaxJsonLength=32*1024*1024}.Serialize(entries));
            BuildLookup();
            }
        }
    }
}
