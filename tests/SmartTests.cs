using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using PlaylistFlac;

internal static class SmartTests
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "PlaylistFlacSmartTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CheckIndex(Path.Combine(root, "index"));
            CheckAliases();
            CheckMerge(Path.Combine(root, "merge"));
            CheckAmbiguousQueryMerge(Path.Combine(root, "ambiguous-query"));
            CheckAmbiguousAlbumMember(Path.Combine(root, "ambiguous-album"));
            CheckRecovery(Path.Combine(root, "recovery"));
            CheckLookups();
            CheckOutputLock(Path.Combine(root,"busy"));
            CheckCancelledLock(Path.Combine(root,"cancelled"));
            CheckFailedLock(Path.Combine(root,"failed"));
        }
        finally
        {
            string absolute = Path.GetFullPath(root);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("PlaylistFlacSmartTests-", StringComparison.Ordinal)) throw new Exception("Unsafe test cleanup path.");
            Directory.Delete(absolute, true);
        }
    }

    private static void CheckIndex(string folder)
    {
        Directory.CreateDirectory(folder);
        var original = new Track { Title = "夜, \"Light\" (feat. Guest)", Artist = "Lead, Guest", Album = "Album", DurationSeconds = 172.55 };
        var unknown = new Track { Title = "Unknown duration", Artist = "Artist", Album = "", DurationSeconds = 0 };
        var historic = new Track { Title = "Earlier playlist", Artist = "Artist", Album = "Previous", DurationSeconds = 200 };
        var playlist = new Playlist { Tracks = new List<Track> { original, unknown, original } };
        var rows = new Dictionary<string, SavedTrack>();
        foreach (Track track in new[] { original, unknown, historic })
        {
            string file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".flac");
            File.WriteAllBytes(file, new byte[0]);
            rows[IndexStore.Key(track)] = new SavedTrack { Track = track, Path = file, State = 1, Reason = 0 };
        }
        IndexStore.Save(folder, rows, playlist);
        var read = IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv"));
        Assert(read.Count == 3 && read.ContainsKey(IndexStore.Key(historic)), "Saving canonical index preserves unrelated historical rows.");
        Assert(read[IndexStore.Key(original)].Track.Title == original.Title && read[IndexStore.Key(original)].Track.Artist == original.Artist, "Canonical title/feat/credits survive CSV escaping.");
        Assert(IndexStore.Seconds(unknown) == -1 && IndexStore.Done(read[IndexStore.Key(unknown)]), "Unknown CSV duration maps to engine -1 identity.");
        Assert(IndexStore.Key(new Track {Title=unknown.Title,Artist=unknown.Artist,Album="",DurationSeconds=-1}) == IndexStore.Key(unknown), "Zero and engine -1 duration share identity.");
        string[] m3u = File.ReadAllLines(Path.Combine(folder, "playlist.m3u8")).Where(x => x.Length > 0 && !x.StartsWith("#")).ToArray();
        Assert(m3u.Length == 2 && m3u[0] != m3u[1], "Playback playlist keeps first-source order without replaying the same file twice.");
        string duplicate = Row(rows[IndexStore.Key(original)]) + Row(new SavedTrack {Track=original,Path="",State=2,Reason=9});
        File.WriteAllText(Path.Combine(folder, "duplicate.csv"), Header + duplicate, new UTF8Encoding(false));
        Assert(IndexStore.Done(IndexStore.Read(Path.Combine(folder, "duplicate.csv"))[IndexStore.Key(original)]), "A later failed duplicate cannot erase an existing completed path.");
    }

    private static void CheckAliases()
    {
        var bilingual = new Track {Title="夜の光 - Yoru no Hikari",Artist="Example Singer",Album="Original album",DurationSeconds=172,Isrc="JP-XXX-26-00001",SpotifyId="source-identity"};
        string firstFixture = AliasRecording("JPXXX2600001","夜の光","架空の歌手",172000);
        var aliases = AliasLookup.Parse(firstFixture,bilingual);
        Assert(aliases.Any(t => t.Title == "夜の光" && t.Artist == "架空の歌手"), "Synthetic ISRC response resolves Japanese title and native artist.");
        Assert(aliases.Any(t => t.Title == "夜の光" && t.Artist == "Example Singer"), "Synthetic ISRC response retains source artist option.");
        Assert(aliases.All(t => t.Album == bilingual.Album && t.DurationSeconds == bilingual.DurationSeconds && t.SpotifyId == bilingual.SpotifyId), "Aliases retain source album, exact source duration and identity.");
        var romanized = new Track {Title="Ao no Umi",Artist="Example Artist",Album="Example album",DurationSeconds=277,Isrc="JPXXX2600002"};
        Assert(AliasLookup.Parse(AliasRecording(romanized.Isrc,"青の海",romanized.Artist,277000),romanized).Any(t => t.Title == "青の海" && t.Artist == "Example Artist"), "Second synthetic ISRC resolves native title without guessed transliteration.");
        var original = new Track {Title="Original",Artist="Artist",Album="Album",DurationSeconds=180,Isrc="JPU902500397"};
        Assert(AliasLookup.Parse(Recording(185000,"JPU902500397"),original).Count > 0, "Five-second duration boundary is accepted.");
        Assert(AliasLookup.Parse(Recording(185001,"JPU902500397"),original).Count == 0, "More than five seconds is rejected.");
        Assert(AliasLookup.Parse(Recording(174999,"JPU902500397"),original).Count == 0, "Shorter wrong recording is rejected.");
        Assert(AliasLookup.Parse(Recording(180000,"TCJPM2019515"),original).Count == 0, "Wrong response ISRC cannot supply search aliases.");
        original.DurationSeconds = 0;
        Assert(AliasLookup.Parse(Recording(4000,"JPU902500397"),original).Count == 0, "Unknown source duration cannot validate a near-zero recording.");
        original.DurationSeconds = 180;
        Assert(AliasLookup.Parse(Recording("NaN","JPU902500397"),original).Count == 0, "Non-finite recording duration is rejected.");
        Assert(AliasLookup.Parse(Recording(null,"JPU902500397"),original).Count == 0, "Missing recording duration is rejected.");
    }

    private static string Recording(object length, string isrc)
    {
        return new JavaScriptSerializer().Serialize(new {isrc=isrc,recordings=new[] { new {title="Alternate title",length=length, artist_credit_placeholder=0} }}).Replace("\"artist_credit_placeholder\":0", "\"artist-credit\":[{\"name\":\"Artist\",\"artist\":{\"name\":\"Artist\",\"sort-name\":\"Artist\"}}]");
    }

    private static void CheckMerge(string folder)
    {
        Directory.CreateDirectory(folder);
        var original = new Track {Title="Original (feat. Guest)",Artist="Lead, Guest",Album="Album",DurationSeconds=180};
        var alias = new Track {Title="別名",Artist="Lead",Album="Album",DurationSeconds=180};
        string file = Path.Combine(folder,"alias.flac"); File.WriteAllBytes(file,new byte[0]);
        File.WriteAllText(LibraryLayout.PathFor(folder,".active-search-index.csv"),Header+Row(new SavedTrack {Track=alias,Path=file,State=1,Reason=0}));
        var canonical = new Dictionary<string,SavedTrack>();
        using(var downloader=new SmartDownloader())
        {
            Set(downloader,"folder",folder); Set(downloader,"index",canonical);
            Call(downloader,"Merge",new List<SearchJob> {new SearchJob {Original=original,Query=alias}});
            Assert(canonical.Count==1 && canonical.ContainsKey(IndexStore.Key(original)) && !canonical.ContainsKey(IndexStore.Key(alias)),"Alias result maps to original canonical tuple.");
            Assert(canonical[IndexStore.Key(original)].Track.Title==original.Title && canonical[IndexStore.Key(original)].Path==file,"Alias success retains original featured credit and maps file path.");
            File.WriteAllText(LibraryLayout.PathFor(folder,".active-search-index.csv"),Header+Row(new SavedTrack {Track=alias,Path="",State=2,Reason=9}));
            Call(downloader,"Merge",new List<SearchJob> {new SearchJob {Original=original,Query=alias}});
            Assert(IndexStore.Done(canonical[IndexStore.Key(original)]),"Later failed alias cannot downgrade a completed original.");
            canonical.Clear();
            string outside=Path.Combine(Path.GetDirectoryName(folder),"outside.flac");File.WriteAllBytes(outside,new byte[0]);
            File.WriteAllText(LibraryLayout.PathFor(folder,".active-search-index.csv"),Header+Row(new SavedTrack {Track=alias,Path=outside,State=1,Reason=0}));
            Call(downloader,"Merge",new List<SearchJob> {new SearchJob {Original=original,Query=alias}});
            Assert(canonical.Count==0,"Raw result outside playlist root is rejected.");
            canonical[IndexStore.Key(original)]=new SavedTrack {Track=original,Path=outside,State=1,Reason=0};
            bool externalComplete=(bool)typeof(SmartDownloader).GetMethod("Completed",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(downloader,new object[]{original});
            Assert(!externalComplete,"An external indexed file cannot suppress a download into the selected playlist folder.");
            File.WriteAllText(LibraryLayout.PathFor(folder,".active-search-index.csv"),Header+Row(new SavedTrack {Track=alias,Path=file,State=1,Reason=0}));
            Call(downloader,"Merge",new List<SearchJob> {new SearchJob {Original=original,Query=alias}});
            Assert(canonical[IndexStore.Key(original)].Path==file,"A valid local completion replaces an out-of-folder index reference.");
            Assert(File.Exists(outside) && new FileInfo(outside).Length==0,"Repairing the index never changes or removes the external file.");
        }
    }

    private static void CheckRecovery(string folder)
    {
        Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(folder,".search"));
        var original=new Track {Title="Original (feat. Guest)",Artist="Lead, Guest",Album="Album",DurationSeconds=0};
        var alias=new Track {Title="別名",Artist="Lead",Album="Album",DurationSeconds=0};
        var playlist=new Playlist {Name="Recovery",Tracks=new List<Track>{original}};
        string file=Path.Combine(folder,"alias.flac");File.WriteAllBytes(file,new byte[0]);
        File.WriteAllText(LibraryLayout.PathFor(folder,"_index.csv"),Header+Row(new SavedTrack {Track=original,Path="",State=2,Reason=9}));
        File.WriteAllText(LibraryLayout.PathFor(folder,".active-search-index.csv"),Header+Row(new SavedTrack {Track=alias,Path=file,State=1,Reason=0}));
        File.WriteAllText(Path.Combine(folder,".search","active-map.json"),new JavaScriptSerializer().Serialize(new List<SearchJob>{new SearchJob {Original=original,Query=alias}}));
        using(var downloader=new SmartDownloader())
        {
            int result=downloader.RunAsync("must-never-run.exe",playlist,folder,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult();
            Assert(result==0,"Completed interrupted pass recovers without starting backend or metadata lookup.");
        }
        var rows=IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv"));
        Assert(IndexStore.Done(rows[IndexStore.Key(original)]),"Recovery persists canonical success even when no pending tracks remain.");
        Assert(Path.GetFileName(rows[IndexStore.Key(original)].Path)==original.Title+".flac","Recovered aliases receive original source-title filename including feat.");
        Assert(String.Equals(Path.GetDirectoryName(rows[IndexStore.Key(original)].Path),Path.Combine(folder,"playlist"),StringComparison.OrdinalIgnoreCase),"Recovered files leave private staging and enter the visible playlist folder.");
        Assert(File.ReadAllText(Path.Combine(folder,"playlist.m3u8")).Contains(original.Title+".flac"),"Recovered playlist points to renamed canonical file.");
        AssertLockAvailable(folder,"Recovery releases the playlist lock after canonical naming finishes.");
    }

    private static void CheckAmbiguousQueryMerge(string folder)
    {
        Directory.CreateDirectory(folder);
        var duet=new Track {Title="Song",Artist="Lead, Guest",Artists=new[]{"Lead","Guest"},Album="Album",DurationSeconds=180,Isrc="USAAA2000001"};
        var solo=new Track {Title="Song",Artist="Lead",Artists=new[]{"Lead"},Album="Album",DurationSeconds=180,Isrc="USAAA2000002"};
        var duetQuery=SearchNames.GetVariants(duet).First();
        Assert(IndexStore.Key(duetQuery)==IndexStore.Key(solo),"Fixture has two distinct recordings whose primary-artist engine queries collide.");
        string file=Path.Combine(folder,"ambiguous.flac");File.WriteAllBytes(file,new byte[]{42});
        File.WriteAllText(LibraryLayout.PathFor(folder,".active-search-index.csv"),Header+Row(new SavedTrack {Track=duetQuery,Path=file,State=1,Reason=0}));
        var jobs=new List<SearchJob>{new SearchJob {Original=duet,Query=duetQuery},new SearchJob {Original=solo,Query=solo}};
        var canonical=new Dictionary<string,SavedTrack>();
        using(var downloader=new SmartDownloader())
        {
            Set(downloader,"folder",folder);Set(downloader,"index",canonical);Set(downloader,"recordings",RecordingGroups.Build(new[]{duet,solo}));
            Call(downloader,"Merge",jobs);
            Assert(canonical.Count==0,"An ambiguous engine-index row cannot mark either distinct source recording downloaded.");
            Assert(SmartDownloader.BuildProgressLookup(jobs)[new SmartDownloader.ProgressKey("Song","Lead")]==null,"Progress and engine-index merging both reject the same alias collision.");
            Call(downloader,"Merge",new List<SearchJob>{jobs[0]});
            Assert(canonical.Count==1 && canonical[IndexStore.Key(duet)].Path==file,"A later pass with one unambiguous owner can still recover the completed file.");
        }
        Assert(File.ReadAllBytes(file).SequenceEqual(new byte[]{42}),"Ambiguous results remain untouched for recovery or review.");
    }

    private static void CheckAmbiguousAlbumMember(string folder)
    {
        Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(folder,".search"));
        var representative=new Track {Title="Song",Artist="Artist",Album="Original",DurationSeconds=180,Isrc="USAAA2000001"};
        var alias=new Track {Title="Song",Artist="Artist",Album="Shared",DurationSeconds=180,Isrc="USAAA2000001"};
        var conflicting=new Track {Title="Song",Artist="Artist",Album="Shared",DurationSeconds=180,Isrc="USAAA2000002"};
        var playlist=new Playlist {Tracks=new List<Track>{representative,alias,conflicting}};
        var groups=RecordingGroups.Build(playlist.Tracks);
        Assert(groups.GroupForKey(IndexStore.Key(representative))!=null && groups.GroupForKey(IndexStore.Key(alias))==null,"Fixture has an unambiguous representative but an ambiguous member album key.");
        string file=Path.Combine(folder,"unclaimed.flac");File.WriteAllBytes(file,new byte[]{7,8});
        File.WriteAllText(LibraryLayout.PathFor(folder,".active-search-index.csv"),Header+Row(new SavedTrack {Track=representative,Path=file,State=1,Reason=0}));
        File.WriteAllText(Path.Combine(folder,".search","active-map.json"),new JavaScriptSerializer().Serialize(new[]{new SearchJob {Original=representative,Query=representative}}));
        using(var downloader=new SmartDownloader())
        {
            int result=downloader.RunAsync("must-never-run.exe",playlist,folder,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult();
            Assert(result==1,"Any ambiguous member makes the whole recording group require review without launching the backend.");
        }
        Assert(IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv")).Count==0,"Recovery cannot write success aliases for a partly ambiguous recording group.");
        Assert(File.ReadAllBytes(file).SequenceEqual(new byte[]{7,8}),"A partly ambiguous group's existing file must remain untouched.");
    }

    private static void CheckLookups()
    {
        var jobs=new List<SearchJob>();
        for(int i=0;i<3000;i++) jobs.Add(new SearchJob {Original=new Track {Title="Original "+i},Query=new Track {Title="Title "+i,Artist="Artist",Album="Album"}});
        SearchJob first=jobs[42];
        jobs.Add(new SearchJob {Original=new Track {Title="Later duplicate"},Query=new Track {Title="TITLE 42",Artist="ARTIST",Album="Different album"}});
        var nullJob=new SearchJob {Query=new Track {Title=null,Artist="Null artist"}};
        var emptyJob=new SearchJob {Query=new Track {Title="",Artist="Null artist"}};
        var delimiterOne=new SearchJob {Query=new Track {Title="one\ntwo",Artist="three"}};
        var delimiterTwo=new SearchJob {Query=new Track {Title="one",Artist="two\nthree"}};
        jobs.AddRange(new[]{nullJob,emptyJob,delimiterOne,delimiterTwo});
        var lookup=SmartDownloader.BuildProgressLookup(jobs);
        Assert(lookup[new SmartDownloader.ProgressKey("title 42","artist")]==null,"Progress without album metadata cannot choose between duplicate query labels.");
        Assert(Object.ReferenceEquals(lookup[new SmartDownloader.ProgressKey("Title 2999","Artist")],jobs[2999]),"Large-playlist progress can directly resolve its last query.");
        Assert(Object.ReferenceEquals(lookup[new SmartDownloader.ProgressKey(null,"Null artist")],nullJob) && Object.ReferenceEquals(lookup[new SmartDownloader.ProgressKey("","Null artist")],emptyJob),"Progress keys preserve the previous null-versus-empty distinction.");
        Assert(Object.ReferenceEquals(lookup[new SmartDownloader.ProgressKey("one\ntwo","three")],delimiterOne) && Object.ReferenceEquals(lookup[new SmartDownloader.ProgressKey("one","two\nthree")],delimiterTwo),"Embedded separators cannot cause query-key collisions.");
        var ambiguous=SmartDownloader.BuildProgressLookup(new[]{new SearchJob {Query=new Track {Title="Same",Artist="Artist",Album="First"}},new SearchJob {Query=new Track {Title="Same",Artist="Artist",Album="Second"}}});
        Assert(ambiguous[new SmartDownloader.ProgressKey("Same","Artist")]==null,"Progress events without album must never map a different recording's file to the first namesake.");
        var firstTrack=new Track {Title="First",Isrc="ABC"};
        var secondTrack=new Track {Title="Second",Isrc="ABC"};
        var lowerTrack=new Track {Title="Lowercase",Isrc="abc"};
        var groups=SmartDownloader.BuildIsrcGroups(new[]{firstTrack,lowerTrack,secondTrack,new Track {Isrc=null},new Track {Isrc=""}});
        Assert(groups.Count==2 && groups["ABC"].SequenceEqual(new[]{firstTrack,secondTrack}),"ISRC grouping preserves exact comparison and candidate source order.");
        Assert(Object.ReferenceEquals(groups["abc"][0],lowerTrack),"ISRC optimization does not change existing case-sensitive candidate selection.");
    }

    private static void CheckOutputLock(string folder)
    {
        Directory.CreateDirectory(folder);
        string lockPath=Path.Combine(folder,".download.lock");
        string indexPath=LibraryLayout.PathFor(folder,"_index.csv"), rawPath=LibraryLayout.PathFor(folder,".active-search-index.csv");
        string sourcePath=LibraryLayout.PathFor(folder,"playlist.csv");
        File.WriteAllText(indexPath,Header);File.WriteAllText(rawPath,"Existing interrupted data");File.WriteAllText(sourcePath,"Existing source playlist");
        using(var held=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        using(var downloader=new SmartDownloader())
        {
            bool rejected=false;
            try { downloader.RunAsync("must-never-run.exe",new Playlist(),folder,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult(); }
            catch(IOException ex) { rejected=ex.Message.Contains("already using this playlist folder"); }
            Assert(rejected,"Concurrent playlist access reports a meaningful busy error.");
            Assert(File.ReadAllText(indexPath)==Header && File.ReadAllText(rawPath)=="Existing interrupted data","Busy rejection precedes index or recovery-state writes.");
            Assert(File.ReadAllText(sourcePath)=="Existing source playlist","Busy rejection cannot overwrite the saved source playlist.");
            Assert(!Directory.Exists(Path.Combine(folder,".search")) && !Directory.Exists(Path.Combine(folder,".incoming")),"Busy rejection does not create working-state or staging folders.");
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();bool cancelled=false;
                try { downloader.RunAsync("must-never-run.exe",new Playlist(),folder,"fake","fake",true,false,cancellation.Token).GetAwaiter().GetResult(); }
                catch(OperationCanceledException) { cancelled=true; }
                Assert(cancelled,"Cancellation wins immediately while the playlist folder is busy.");
            }
        }
        using(var downloader=new SmartDownloader())
            Assert(downloader.RunAsync("must-never-run.exe",new Playlist(),folder,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult()==0,"Playlist can run after prior lock holder exits.");
        Assert(File.Exists(lockPath),"Lock file remains in place to avoid an unlink/reacquire race.");
        AssertLockAvailable(folder,"Successful early completion releases the output lock.");
    }

    private static void CheckCancelledLock(string folder)
    {
        Directory.CreateDirectory(folder);
        var track=new Track {Title="Missing",Artist="Artist",Album="Album",DurationSeconds=180,Isrc=""};
        var playlist=new Playlist {Tracks=new List<Track>{track}};
        File.WriteAllText(LibraryLayout.PathFor(folder,"_index.csv"),Header+Row(new SavedTrack {Track=track,Path="",State=2,Reason=9}));
        using(var cancellation=new CancellationTokenSource())
        using(var downloader=new SmartDownloader())
        {
            bool checkedHeld=false;
            downloader.Log+=message=>{
                if(message.StartsWith("Looking up alternate recording names",StringComparison.Ordinal))
                {
                    AssertLockHeld(folder);checkedHeld=true;cancellation.Cancel();
                }
            };
            bool cancelled=false;
            try { downloader.RunAsync("must-never-run.exe",playlist,folder,"fake","fake",true,true,cancellation.Token).GetAwaiter().GetResult(); }
            catch(OperationCanceledException) { cancelled=true; }
            Assert(cancelled && checkedHeld,"In-flight cancellation releases a genuinely acquired lock without starting network work.");
        }
        AssertLockAvailable(folder,"Cancellation releases the output lock after final bookkeeping.");
        Assert(IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv")).ContainsKey(IndexStore.Key(track)),"Cancelled run preserves canonical retry history.");
    }

    private static void CheckFailedLock(string folder)
    {
        Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(folder,".search"));
        string map=Path.Combine(folder,".search","active-map.json");
        File.WriteAllText(map,"invalid JSON");File.WriteAllText(LibraryLayout.PathFor(folder,".active-search-index.csv"),Header);
        using(var downloader=new SmartDownloader())
        {
            bool failed=false;
            try { downloader.RunAsync("must-never-run.exe",new Playlist(),folder,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult(); }
            catch(ArgumentException) { failed=true; }
            Assert(failed,"Test exercises recovery failure after acquiring the playlist lock.");
            AssertLockAvailable(folder,"Failed recovery releases the output lock.");
            File.WriteAllText(map,"[]");
            Assert(downloader.RunAsync("must-never-run.exe",new Playlist(),folder,"fake","fake",true,false,CancellationToken.None).GetAwaiter().GetResult()==0,"Downloader remains reusable after failed recovery.");
        }
    }

    private static void AssertLockAvailable(string folder,string message)
    {
        try { using(var handle=new FileStream(Path.Combine(folder,".download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) { } }
        catch(IOException) { throw new Exception("Smart downloader test failed: "+message); }
    }
    private static void AssertLockHeld(string folder)
    {
        bool held=false;
        try { using(var handle=new FileStream(Path.Combine(folder,".download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) { } }
        catch(IOException ex) { int code=ex.HResult & 0xffff;held=code==32 || code==33; }
        Assert(held,"Exclusive playlist lock spans alias processing and finalization.");
    }

    private static string AliasRecording(string isrc,string title,string artist,int length)
    {
        var credit=new Dictionary<string,object> {{"name",artist},{"artist",new Dictionary<string,object> {{"name",artist},{"sort-name",artist}}}};
        var recording=new Dictionary<string,object> {{"title",title},{"length",length},{"artist-credit",new[]{credit}}};
        return new JavaScriptSerializer().Serialize(new {isrc=isrc,recordings=new[]{recording}});
    }
    private const string Header="filepath,artist,album,title,length,tracktype,state,failurereason\r\n";
    private static string Row(SavedTrack row) {return String.Join(",",new[]{row.Path,row.Track.Artist,row.Track.Album,row.Track.Title,IndexStore.Seconds(row.Track).ToString(CultureInfo.InvariantCulture),"0",row.State.ToString(),row.Reason.ToString()}.Select(IndexStore.Quote))+"\r\n";}
    private static void Set(object instance,string name,object value) {instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(instance,value);}
    private static void Call(object instance,string name,object argument) {instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance,new[]{argument});}
    private static void Assert(bool condition,string message) {if(!condition)throw new Exception("Smart downloader test failed: "+message);}
}
