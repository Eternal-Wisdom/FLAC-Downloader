using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    internal sealed class SearchJob
    {
        public Track Original { get; set; }
        public Track Query { get; set; }
    }
    internal sealed class SmartDownloader : IDisposable
    {
        public event Action<string> Log;
        public event Action<EngineProgress> Progress;
        private DownloadEngine engine;
        private Dictionary<string,SavedTrack> index;
        private Playlist playlist;
        private string folder;
        private int running;
        private DownloadTuning tuning;
        private RecordingGroups recordings;
        private RecordingCatalog catalog;
        internal async Task<int> RunAsync(string enginePath,Playlist source,string output,string username,string password,bool strict,bool retryOnly,CancellationToken ct,ISet<string> selectedKeys=null,DownloadTuning downloadTuning=null,string coverCacheDirectory=null,string catalogDirectory=null)
        {
            ct.ThrowIfCancellationRequested();
            if(Interlocked.CompareExchange(ref running,1,0)!=0) throw new InvalidOperationException("This downloader is already running.");
            try
            {
                string outputFolder=Path.GetFullPath(output);
                Directory.CreateDirectory(outputFolder);
                // Keep this file between runs. Deleting it after closing would race with
                // another process that has just acquired the same playlist lock.
                using(var outputLock=AcquireOutputLock(outputFolder))
                {
                    ct.ThrowIfCancellationRequested();LibraryLayout.Prepare(outputFolder,ct);
                    playlist=source; folder=outputFolder; tuning=downloadTuning ?? DownloadTuning.Default;
                    catalog=null;
                    if(catalogDirectory!=null)try {catalog=new RecordingCatalog(catalogDirectory);}catch(Exception ex) {if(ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)Say("Recording reuse unavailable; the catalog was preserved: "+ex.Message);else throw;}
                    CsvPlaylist.Write(LibraryLayout.PathFor(folder,"playlist.csv"),source);
                    int code=await RunLockedAsync(enginePath,source,username,password,strict,retryOnly,ct,selectedKeys).ConfigureAwait(false);
                    if((code==0 || code==1) && coverCacheDirectory!=null && !ct.IsCancellationRequested)
                    {
                        Say("Adding missing album covers to completed downloads...");
                        await Task.Run(()=>ArtworkLibrary.FillAsync(folder,source,index,coverCacheDirectory,ct,Say),ct).ConfigureAwait(false);
                    }
                    if(catalog!=null && !ct.IsCancellationRequested && (code==0 || code==1))
                        catalog.Register(source,folder,index,ct);
                    return code;
                }
            }
            finally { Interlocked.Exchange(ref running,0); }
        }
        private static FileStream AcquireOutputLock(string outputFolder)
        {
            try { return new FileStream(Path.Combine(outputFolder,".download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
            catch(IOException ex)
            {
                int code=ex.HResult & 0xffff;
                if(code==32 || code==33) throw new IOException("Another download is already using this playlist folder. Stop that download or choose a different folder.",ex);
                throw;
            }
        }
        internal async Task<LibraryMaintenanceResult> FixLibraryAsync(Playlist source,string output,string coverCacheDirectory,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(Interlocked.CompareExchange(ref running,1,0)!=0) throw new InvalidOperationException("This downloader is already running.");
            try
            {
                string outputFolder=Path.GetFullPath(output);
                if(!Directory.Exists(outputFolder)) throw new DirectoryNotFoundException("This playlist has no saved music folder yet.");
                if(String.Equals(outputFolder.TrimEnd('\\','/'),Path.GetPathRoot(outputFolder).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase) ||
                    (File.GetAttributes(outputFolder) & FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Choose a playlist folder rather than a drive root or directory link.");
                using(var outputLock=AcquireOutputLock(outputFolder))
                {
                    LibraryLayout.Prepare(outputFolder,ct);
                    int organized=RecoveryFolders.OrganizeLegacy(outputFolder,ct);
                    if(organized>0)Say("Organized "+organized+" recovery folders under "+RecoveryFolders.Name+"; all backups were preserved.");
                    playlist=source;folder=outputFolder;recordings=RecordingGroups.Build(source.Tracks);
                    index=IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv"));Recover();
                    Say("Consolidating repeated recordings and updating conflicting song titles...");
                    var result=new LibraryMaintenanceResult();
                    result.Duplicates=await Task.Run(()=>DuplicateLibrary.Consolidate(folder,source,recordings,index,ct,Say),ct).ConfigureAwait(false);
                    result.Names=FinalizeNames(ct);
                    if(coverCacheDirectory!=null)
                        result.Covers=await ArtworkLibrary.FillAsync(folder,source,index,coverCacheDirectory,ct,Say).ConfigureAwait(false);
                    return result;
                }
            }
            finally { Interlocked.Exchange(ref running,0); }
        }
        private async Task<int> RunLockedAsync(string enginePath,Playlist source,string username,string password,bool strict,bool retryOnly,CancellationToken ct,ISet<string> selectedKeys)
        {
            string state=Path.Combine(folder,".search"); Directory.CreateDirectory(state);
            recordings=RecordingGroups.Build(source.Tracks);
            index=IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv")); Recover();
            Say("Checking existing files for duplicate recordings...");
            var cleanup=await Task.Run(()=>DuplicateLibrary.Consolidate(folder,source,recordings,index,ct,Say),ct).ConfigureAwait(false);
            if(cleanup.BackupDirectory!=null) Say("Duplicate recovery files: "+cleanup.BackupDirectory);
            FinalizeNames();
            int reviewCount=recordings.Groups.Count(g=>!Unambiguous(g.Representative));
            if(reviewCount>0) Say(reviewCount+" recording versions have conflicting IDs but identical search metadata. Existing files are kept; these entries need review and will not be automatically combined or downloaded.");
            var all=recordings.Groups.Select(g=>g.Representative).Where(Unambiguous).ToList();
            var isrcGroups=BuildIsrcGroups(source.Tracks);
            var pending=all.Where(t=>!Completed(t) && (selectedKeys==null || Members(t).Any(x=>selectedKeys.Contains(IndexStore.Key(x)))) && (!retryOnly || Members(t).Any(x=>index.ContainsKey(IndexStore.Key(x))))).ToList();
            if(catalog!=null) {
                int reused=0;
                try {foreach(var track in pending) {ct.ThrowIfCancellationRequested();string path=catalog.Reuse(track,folder,ct);if(path!=null){Remember(track,path,1,0);reused++;Say("Reused saved recording: "+track.Title);}}}
                finally {if(reused>0)FinalizeNames();}
                if(reused>0) {Say("Reused "+reused+" saved recordings across collections without downloading them again. Local copies preserve each playlist folder.");pending=pending.Where(t=>!Completed(t)).ToList();}
            }
            Say(source.Tracks.Count+" playlist entries represent "+recordings.Groups.Count+" recordings; repeated recordings share one download.");
            if(pending.Count==0) { Say(reviewCount>0?"Downloads are complete except for recording versions that need review.":retryOnly?"No attempted tracks need a retry yet.":"All these tracks are already downloaded."); return reviewCount>0?1:0; }
            var tried=new Dictionary<string,HashSet<string>>();
            foreach(var t in pending) tried[IndexStore.Key(t)]=new HashSet<string>();
            bool preserveRecoveryOnly=false;
            try
            {
                // A peer may simply have come online since the previous attempt.
                // Do not put a whole playlist of optional metadata lookups before
                // the first real search when the user presses Retry.
                {
                    Say("Searching for "+pending.Count+" missing recordings...");
                    var first=pending.Select(t=>new SearchJob {Original=t,Query=SearchNames.GetVariants(t).FirstOrDefault() ?? t}).ToList();
                    MarkTried(first,tried);
                    int code=await RunPass(enginePath,first,username,password,strict,ct).ConfigureAwait(false);
                    if(code==130 || ct.IsCancellationRequested) return 130;
                    if(code!=0 && code!=1) return code;
                }
                pending=pending.Where(t=>!Completed(t)).ToList();
                if(pending.Count==0) return reviewCount>0?1:0;
                Say("Looking up alternate recording names for "+pending.Count+" missing tracks...");
                var variants=new Dictionary<string,List<Track>>();
                using(var lookup=new AliasLookup(Path.Combine(state,"metadata")))
                using(var lookupBudget=CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                lookupBudget.CancelAfter(TimeSpan.FromSeconds(30));
                bool budgetReported=false;
                foreach(var t in pending)
                {
                    ct.ThrowIfCancellationRequested();
                    var candidates=new List<Track>();
                    try { if(!lookupBudget.IsCancellationRequested)candidates.AddRange(await lookup.FindAsync(t,lookupBudget.Token).ConfigureAwait(false)); }
                    catch(OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
                    catch(Exception ex) { if(ex is IOException || ex is System.Net.Http.HttpRequestException || ex is ArgumentException || ex is InvalidOperationException) Say("Recording-name lookup unavailable; using local aliases."); else throw; }
                    if(lookupBudget.IsCancellationRequested && !budgetReported) { Say("Optional recording-name lookup reached its 30-second limit; continuing with local aliases.");budgetReported=true; }
                    List<Track> sameIsrc;
                    if(!String.IsNullOrEmpty(t.Isrc) && isrcGroups.TryGetValue(t.Isrc,out sameIsrc))
                        candidates.AddRange(sameIsrc.Where(x=>x.DurationSeconds>0 && Math.Abs(x.DurationSeconds-t.DurationSeconds)<=5 && IndexStore.Key(x)!=IndexStore.Key(t)).Select(x=>new Track { Title=x.Title,Artist=x.PrimaryArtist,Album=t.Album,DurationSeconds=t.DurationSeconds }));
                    candidates.AddRange(SearchNames.GetVariants(t)); candidates.Add(t);
                    variants[IndexStore.Key(t)]=candidates.GroupBy(IndexStore.Key).Select(g=>g.First()).Where(x=>!tried[IndexStore.Key(t)].Contains(IndexStore.Key(x))).Take(5).ToList();
                }
                }
                for(int pass=0;pass<5;pass++)
                {
                    var jobs=pending.Where(t=>!Completed(t) && variants[IndexStore.Key(t)].Count>pass).Select(t=>new SearchJob {Original=t,Query=variants[IndexStore.Key(t)][pass]}).ToList();
                    if(jobs.Count==0) break;
                    Say("Alternate-name pass "+(pass+1)+": "+jobs.Count+" tracks. FLAC and duration checks remain active.");
                    int code=await RunPass(enginePath,jobs,username,password,strict,ct).ConfigureAwait(false);
                    if(code==130 || ct.IsCancellationRequested) return 130;
                    if(code!=0 && code!=1) return code;
                }
                int remaining=pending.Count(t=>!Completed(t)); Say(remaining+" tracks remain unavailable after alternate-name searches.");
                return remaining==0 && reviewCount==0?0:1;
            }
            catch(Exception ex) {preserveRecoveryOnly=!(ex is OperationCanceledException);throw;}
            finally
            {
                // Complete bookkeeping even when Stop cancels network work.
                // Unexpected storage/index failures must not trigger more writes.
                if(!preserveRecoveryOnly)FinalizeNames();
            }
        }
        private async Task<int> RunPass(string exe,List<SearchJob> jobs,string user,string password,bool strict,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string state=Path.Combine(folder,".search"), input=Path.Combine(state,"playlist.csv"), sessionIndex=LibraryLayout.PathFor(folder,".active-search-index.csv");
            string incoming=Path.Combine(folder,".incoming",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(incoming);
            File.SetAttributes(Path.Combine(folder,".incoming"),File.GetAttributes(Path.Combine(folder,".incoming")) | FileAttributes.Hidden);
            if(File.Exists(sessionIndex)) File.Delete(sessionIndex);
            IndexStore.AtomicWrite(Path.Combine(state,"active-map.json"),new JavaScriptSerializer {MaxJsonLength=16*1024*1024}.Serialize(jobs));
            CsvPlaylist.Write(input,new Playlist {Name=playlist.Name,Tracks=jobs.Select(j=>j.Query).ToList()});
            var progressJobs=BuildProgressLookup(jobs);
            using(engine=new DownloadEngine())
            {
                engine.Log+=Say;
                int sinceFinalize=0;
                engine.Progress+=update=>{
                    SearchJob job;
                    progressJobs.TryGetValue(new ProgressKey(update.Title,update.Artist),out job);
                    if(job!=null) {
                        if(update.Type=="track_state" && (update.Status=="Succeeded" || update.Status=="1") && !String.IsNullOrEmpty(update.DownloadPath))
                        {
                            string path=Path.GetFullPath(update.DownloadPath);
                            if(path.StartsWith(folder.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                            {
                                Remember(job.Original,path,1,0);
                                if(++sinceFinalize>=20) {
                                    FinalizeNames();sinceFinalize=0;
                                }
                            }
                        }
                        update.Title=job.Original.Title;update.Artist=job.Original.Artist;update.TrackKey=IndexStore.Key(job.Original);
                    }
                    // Stock progress events omit album/duration. Ambiguous names
                    // must wait for the full engine index, not claim another file.
                    else { update.Title="";update.Artist=""; }
                    var callback=Progress;if(callback!=null)callback(update);
                };
                bool failed=false;
                // The engine truncates its M3U with FileMode.Create, which Windows
                // rejects for an existing hidden file. Use a fresh per-pass path;
                // the canonical playlist is maintained separately by IndexStore.
                try { return await engine.RunWithIndexAsync(exe,input,incoming,user,password,strict,sessionIndex,Path.Combine(incoming,"session.m3u8"),ct,tuning).ConfigureAwait(false); }
                catch {failed=true;throw;}
                finally {
                    try {if(!failed){Merge(jobs);IndexStore.Save(folder,index,playlist);}}
                    finally {engine=null;}
                }
            }
        }
        private RenameResult FinalizeNames(CancellationToken ct=default(CancellationToken))
        {
            ct.ThrowIfCancellationRequested();
            if(recordings!=null) DuplicateLibrary.ReuseCompleted(folder,recordings,index);
            IndexStore.Save(folder,index,playlist);
            var result=LibraryNaming.RenameCompleted(folder,playlist,ct,Say,Path.Combine(folder,"playlist"));
            index=IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv"));
            return result;
        }
        private void Recover()
        {
            string map=Path.Combine(folder,".search","active-map.json");
            if(!File.Exists(map) || !File.Exists(LibraryLayout.PathFor(folder,".active-search-index.csv"))) return;
            var jobs=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Deserialize<List<SearchJob>>(File.ReadAllText(map));
            if(jobs!=null) Merge(jobs);
        }
        private void Merge(List<SearchJob> jobs)
        {
            var results=IndexStore.Read(LibraryLayout.PathFor(folder,".active-search-index.csv"));
            string root=folder.TrimEnd('\\')+"\\";
            // A full engine tuple can still collide after stripping artist
            // credits or choosing an alternate title. Its row cannot identify
            // which original recording succeeded, even when progress is ignored.
            var ambiguousQueries=new HashSet<string>(jobs.GroupBy(j=>IndexStore.Key(j.Query))
                .Where(g=>g.Select(j=>IndexStore.Key(j.Original)).Distinct().Skip(1).Any()).Select(g=>g.Key));
            foreach(var job in jobs)
            {
                SavedTrack result;
                string queryKey=IndexStore.Key(job.Query);
                if(ambiguousQueries.Contains(queryKey) || !Unambiguous(job.Original) || !results.TryGetValue(queryKey,out result) || Completed(job.Original))continue;
                if(!String.IsNullOrEmpty(result.Path) && !result.Path.StartsWith(root,StringComparison.OrdinalIgnoreCase))continue;
                Remember(job.Original,result.Path,result.State,result.Reason);
            }
        }
        private IEnumerable<Track> Members(Track track)
        {
            var group=recordings==null?null:recordings.GroupForKey(IndexStore.Key(track));
            return group==null?(IEnumerable<Track>)new[]{track}:group.Tracks;
        }
        private void Remember(Track original,string path,int state,int reason)
        {
            if(!Unambiguous(original)) return;
            foreach(var track in Members(original))
            {
                string key=IndexStore.Key(track); SavedTrack prior;
                // Keep distinct existing files visible until Consolidate can
                // archive them transactionally instead of losing their paths.
                if(index.TryGetValue(key,out prior) && IndexStore.Done(prior) && DuplicateLibrary.SafeFile(folder,prior.Path)) continue;
                index[key]=new SavedTrack {Track=track,Path=path,State=state,Reason=reason};
            }
        }
        private bool Unambiguous(Track track)
        {
            if(recordings==null) return true;
            var group=recordings.GroupForKey(IndexStore.Key(track));
            return group!=null && group.Tracks.All(t=>recordings.GroupForKey(IndexStore.Key(t))==group);
        }
        private bool Completed(Track t) { SavedTrack row; return index.TryGetValue(IndexStore.Key(t),out row) && IndexStore.Done(row) && DuplicateLibrary.SafeFile(folder,row.Path); }
        internal struct ProgressKey : IEquatable<ProgressKey>
        {
            private readonly string title, artist;
            internal ProgressKey(string title,string artist) { this.title=title; this.artist=artist; }
            public bool Equals(ProgressKey other) { return String.Equals(title,other.title,StringComparison.OrdinalIgnoreCase) && String.Equals(artist,other.artist,StringComparison.OrdinalIgnoreCase); }
            public override bool Equals(object other) { return other is ProgressKey && Equals((ProgressKey)other); }
            public override int GetHashCode() { unchecked { return (title==null?0:StringComparer.OrdinalIgnoreCase.GetHashCode(title))*397 ^ (artist==null?0:StringComparer.OrdinalIgnoreCase.GetHashCode(artist)); } }
        }
        internal static Dictionary<ProgressKey,SearchJob> BuildProgressLookup(IEnumerable<SearchJob> jobs)
        {
            var result=new Dictionary<ProgressKey,SearchJob>();
            foreach(var job in jobs)
            {
                var key=new ProgressKey(job.Query.Title,job.Query.Artist);
                SearchJob previous;
                if(!result.TryGetValue(key,out previous)) result.Add(key,job);
                else if(previous!=null && (IndexStore.Key(previous.Query)!=IndexStore.Key(job.Query) ||
                    (previous.Original!=null && job.Original!=null && IndexStore.Key(previous.Original)!=IndexStore.Key(job.Original)))) result[key]=null;
            }
            return result;
        }
        internal static Dictionary<string,List<Track>> BuildIsrcGroups(IEnumerable<Track> tracks)
        {
            var result=new Dictionary<string,List<Track>>(StringComparer.Ordinal);
            foreach(var track in tracks)
            {
                if(String.IsNullOrEmpty(track.Isrc))continue;
                List<Track> group;
                if(!result.TryGetValue(track.Isrc,out group)) { group=new List<Track>(); result.Add(track.Isrc,group); }
                group.Add(track);
            }
            return result;
        }
        private static void MarkTried(List<SearchJob> jobs,Dictionary<string,HashSet<string>> tried) { foreach(var job in jobs)tried[IndexStore.Key(job.Original)].Add(IndexStore.Key(job.Query)); }
        private void Say(string text) { var callback=Log;if(callback!=null)callback(text); }
        public void Dispose(){ if(engine!=null)engine.Dispose(); }
    }
}
