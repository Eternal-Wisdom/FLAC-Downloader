using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    internal sealed class AlbumRecovery
    {
        public string Incoming {get;set;}
        public List<Track> Tracks {get;set;}
    }
    internal sealed partial class SmartDownloader
    {
        private async Task<int> RunAlbumFolderPass(string exe,List<Track> pending,string user,string password,CancellationToken ct)
        {
            string state=Path.Combine(folder,".search"),search=Path.Combine(state,"album-search.csv");
            var artists=playlist.Tracks.Select(t=>t.PrimaryArtist).Where(a=>!String.IsNullOrWhiteSpace(a)).GroupBy(a=>a,StringComparer.OrdinalIgnoreCase).OrderByDescending(g=>g.Count()).ToList();
            if(artists.Count==0)return 1;
            // A blank title produces one actual album job, not an upgraded job per song.
            CsvPlaylist.Write(search,new Playlist {Name=playlist.Name,Tracks=new List<Track>{new Track{Title="",Artist=artists[0].Key,Album=playlist.Tracks[0].Album}}});
            AlbumFolderChoice choice=null;
            Say("Searching shared album folders before individual songs...");
            using(var budget=CancellationTokenSource.CreateLinkedTokenSource(ct))
            using(engine=new DownloadEngine{AlbumSearch=true}) {
                budget.CancelAfter(TimeSpan.FromSeconds(75));
                engine.Log+=Say;
                engine.AlbumResults+=json=>choice=AlbumFolderSearch.Select(json,recordings.Groups.Select(g=>g.Representative).Where(Unambiguous).ToList(),pending,tuning);
                int code;
                try {code=await engine.RunWithIndexAsync(exe,search,state,user,password,false,Path.Combine(state,"album-search-index.csv"),Path.Combine(state,Guid.NewGuid().ToString("N")+".m3u8"),budget.Token,tuning).ConfigureAwait(false);}
                finally {engine=null;}
                ct.ThrowIfCancellationRequested();
                if(code==130) {Say("Album folder search reached its time limit; continuing with individual songs.");return 1;}
                if(code!=0 && code!=1)return code;
            }
            if(choice==null) {Say("No album folder had confidently matching FLAC tracks; using individual song searches.");return 1;}
            Say("Selected one album folder matching "+choice.Coverage+" recordings; downloading "+choice.Files.Count+" missing tracks. Existing completed songs are skipped.");
            string incoming=Path.Combine(folder,".incoming",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(incoming);
            string input=Path.Combine(state,"album-files.txt");
            File.WriteAllLines(input,choice.Files.Select(f=>f.Link).ToArray());
            var recovery=new AlbumRecovery{Incoming=incoming,Tracks=choice.Files.Select(f=>f.Track).ToList()};
            IndexStore.AtomicWrite(Path.Combine(state,"album-map.json"),new JavaScriptSerializer().Serialize(recovery));
            try {
                using(engine=new DownloadEngine{DirectFiles=true}) {
                    engine.Log+=Say;
                    engine.Progress+=update=>{AlbumFolderSearch.MapProgress(choice,update);var callback=Progress;if(callback!=null)callback(update);};
                    try {return await engine.RunWithIndexAsync(exe,input,incoming,user,password,false,Path.Combine(state,"album-files-index.csv"),Path.Combine(incoming,"session.m3u8"),ct,tuning).ConfigureAwait(false);}
                    finally {engine=null;}
                }
            }
            finally {
                // Direct peer links are session-only. Recoverable bookkeeping contains no peers or remote paths.
                if(File.Exists(input))File.Delete(input);
                RecoverAlbumFiles();
                FinalizeNames();
            }
        }
        private void RecoverAlbumFiles()
        {
            string links=Path.Combine(folder,".search","album-files.txt");
            if(File.Exists(links))File.Delete(links);
            string map=Path.Combine(folder,".search","album-map.json");if(!File.Exists(map))return;
            AlbumRecovery recovery;
            try {recovery=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<AlbumRecovery>(File.ReadAllText(map));}
            catch(ArgumentException){Say("Album recovery map needs review; files were preserved.");return;}
            if(recovery==null || recovery.Tracks==null || recovery.Tracks.Count>10000 || String.IsNullOrEmpty(recovery.Incoming))return;
            string root=Path.GetFullPath(Path.Combine(folder,".incoming"))+Path.DirectorySeparatorChar;
            string incoming=Path.GetFullPath(recovery.Incoming);
            if(!incoming.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !String.Equals(Path.GetDirectoryName(incoming)+Path.DirectorySeparatorChar,root,StringComparison.OrdinalIgnoreCase) || !Directory.Exists(incoming) || (File.GetAttributes(incoming)&FileAttributes.ReparsePoint)!=0)return;
            for(int i=0;i<recovery.Tracks.Count;i++) {
                var track=recovery.Tracks[i];if(!Unambiguous(track) || Completed(track))continue;
                string path=Path.Combine(incoming,(i+1).ToString(CultureInfo.InvariantCulture)+".flac");
                if(!DuplicateLibrary.SafeFile(folder,path))continue;
                var header=FlacAudit.CheckFile(path,CancellationToken.None);
                if(header.IsValid && header.DurationSeconds.HasValue && Math.Abs(header.DurationSeconds.Value-track.DurationSeconds)<=5)
                    Remember(track,path,1,0);
            }
        }
    }
}
