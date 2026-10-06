using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlaylistFlac
{
    internal sealed class ArtworkLibraryResult
    {
        internal int Added, Existing, Unavailable, Review, Invalid;
        internal string RecoveryDirectory, ReportPath;
    }

    internal sealed class LibraryMaintenanceResult
    {
        internal DuplicateCleanupResult Duplicates;
        internal RenameResult Names;
        internal ArtworkLibraryResult Covers;
    }

    internal static class ArtworkLibrary
    {
        private sealed class PreparedCover
        {
            internal string Path, State;
            internal Track Track;
            internal CoverLookupResult Cover;
        }
        // The caller holds the same playlist lock as the downloader. Only files
        // confidently owned by a current recording can receive its artwork.
        internal static async Task<ArtworkLibraryResult> FillAsync(string folder, Playlist playlist,
            Dictionary<string, SavedTrack> index, string cacheDirectory, CancellationToken ct, Action<string> log)
        {
            using (var lookup = new CoverLookup(cacheDirectory))
                return await FillAsync(folder, playlist, index, lookup.FindAsync, ct, log).ConfigureAwait(false);
        }

        internal static async Task<ArtworkLibraryResult> FillAsync(string folder, Playlist playlist,
            Dictionary<string, SavedTrack> index, Func<Track,CancellationToken,Task<CoverLookupResult>> find,
            CancellationToken ct, Action<string> log,int parallelCovers=4)
        {
            ct.ThrowIfCancellationRequested();
            if(parallelCovers<1 || parallelCovers>4)throw new ArgumentOutOfRangeException("parallelCovers");
            string root = Path.GetFullPath(folder).TrimEnd('\\','/');
            if (root.Length < 3 || !Directory.Exists(root) ||
                String.Equals(root, Path.GetPathRoot(Path.GetFullPath(folder)).TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Choose an existing playlist folder that is not a drive root or directory link.");
            var recordings = RecordingGroups.Build(playlist.Tracks);
            var owners = new Dictionary<string, List<Track>>(StringComparer.OrdinalIgnoreCase);
            var recordingForPath = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var reviewPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in recordings.Groups)
            {
                bool ambiguous = group.Tracks.Any(t=>recordings.GroupForKey(IndexStore.Key(t)) != group);
                foreach (var track in group.Tracks)
                {
                    SavedTrack row;
                    if (!index.TryGetValue(IndexStore.Key(track), out row) || !IndexStore.Done(row) || !DuplicateLibrary.SafeFile(root,row.Path)) continue;
                    string path = Path.GetFullPath(row.Path), previous;
                    if (ambiguous || (recordingForPath.TryGetValue(path,out previous) && previous != group.Key)) reviewPaths.Add(path);
                    recordingForPath[path] = group.Key;
                    List<Track> candidates;
                    if (!owners.TryGetValue(path,out candidates)) owners.Add(path,candidates = new List<Track>());
                    candidates.Add(track);
                }
            }
            LibraryLayout.Prepare(root,ct);
            var result = new ArtworkLibraryResult { ReportPath = LibraryLayout.PathFor(root,"Covers.csv") };
            var report = new StringBuilder("path,title,artist,album,status,source\r\n");
            string recovery = RecoveryFolders.NewPath(root,"artwork");
            int processed = 0;
            var entries=owners.ToArray();
            var pending=new Queue<Task<PreparedCover>>();
            using(var pipeline=CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                int scheduled=0;
                Exception failure=null;
                try
                {
                    while(scheduled<entries.Length && pending.Count<parallelCovers)
                    {
                        var next=PrepareAsync(entries[scheduled++],reviewPaths,find,pipeline.Token);
                        pending.Enqueue(next);
                        if(next.IsFaulted)await next.ConfigureAwait(false);
                    }
                    while(pending.Count>0)
                    {
                        foreach(var waiting in pending)if(waiting.IsFaulted)await waiting.ConfigureAwait(false);
                        var prepared=await pending.Dequeue().ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        foreach(var waiting in pending)if(waiting.IsFaulted)await waiting.ConfigureAwait(false);
                        var track=prepared.Track;
                        string state=prepared.State,source="";
                        if(state=="Review recording identity")result.Review++;
                        else if(state=="Invalid FLAC; unchanged")result.Invalid++;
                        else if(state=="Already has cover")result.Existing++;
                        else if(prepared.Cover==null) {result.Unavailable++;state="No matching cover available";}
                        else
                        {
                            if(!DuplicateLibrary.SafeFile(root,prepared.Path))throw new IOException("A library file changed location while covers were being repaired.");
                            source=prepared.Cover.SourceUrl ?? "";
                            try
                            {
                                RecoveryFolders.Prepare(root);
                                var written=FlacArtwork.AddFrontCover(prepared.Path,prepared.Cover.Bytes,recovery,ct);
                                if(written.Changed)
                                {
                                    result.Added++;result.RecoveryDirectory=recovery;
                                    File.SetAttributes(recovery,File.GetAttributes(recovery)|FileAttributes.Hidden);
                                    state="Cover added";
                                }
                                else {result.Existing++;state="Already has cover";}
                            }
                            catch(InvalidDataException ex)
                            {
                                result.Invalid++;state="Invalid FLAC or image; unchanged";
                                if(log!=null)log("Cover skipped for "+track.Title+": "+ex.Message);
                            }
                        }
                        report.AppendLine(String.Join(",",new[]{prepared.Path,track.Title,track.Artist,track.Album,state,source}.Select(IndexStore.Quote)));
                        if(++processed%25==0 && log!=null)log("Covers checked: "+processed+" of "+owners.Count+"; "+result.Added+" added.");
                        if(scheduled<entries.Length)pending.Enqueue(PrepareAsync(entries[scheduled++],reviewPaths,find,pipeline.Token));
                    }
                }
                catch(Exception ex){failure=ex;}
                // C# 5 cannot await in finally. Drain here before rethrowing so
                // the output lock and HTTP client outlive every pending request.
                pipeline.Cancel();
                try {await Task.WhenAll(pending).ConfigureAwait(false);}catch(Exception ex){if(failure==null)failure=ex;}
                if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            ct.ThrowIfCancellationRequested();
            if (File.Exists(result.ReportPath) && (File.GetAttributes(result.ReportPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The cover report must not be a file link.");
            IndexStore.AtomicWrite(result.ReportPath,report.ToString());
            if (log != null)
            {
                log("Cover repair: " + result.Added + " added, " + result.Existing + " already present, " + result.Unavailable + " unavailable, " + (result.Review + result.Invalid) + " need review.");
                if (result.RecoveryDirectory != null) log("Original artwork metadata is recoverable from " + result.RecoveryDirectory + ".");
            }
            return result;
        }

        private static async Task<PreparedCover> PrepareAsync(KeyValuePair<string,List<Track>> entry,
            HashSet<string> reviewPaths,Func<Track,CancellationToken,Task<CoverLookupResult>> find,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var prepared=new PreparedCover{Path=entry.Key,Track=entry.Value[0]};
            if(reviewPaths.Contains(entry.Key)){prepared.State="Review recording identity";return prepared;}
            try {if(FlacArtwork.ReadFrontCover(entry.Key,ct)){prepared.State="Already has cover";return prepared;}}
            catch(InvalidDataException){prepared.State="Invalid FLAC; unchanged";return prepared;}
            var candidates=entry.Value.OrderByDescending(t=>!String.IsNullOrEmpty(t.CoverUrl))
                .GroupBy(t=>(t.CoverUrl ?? "")+"\n"+(t.Album ?? "")+"\n"+(t.Isrc ?? ""))
                .Select(g=>g.First()).Take(3);
            foreach(var candidate in candidates)
            {
                prepared.Cover=await find(candidate,ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if(prepared.Cover!=null && prepared.Cover.Bytes!=null){prepared.Track=candidate;return prepared;}
            }
            prepared.Cover=null;
            return prepared;
        }
    }
}
