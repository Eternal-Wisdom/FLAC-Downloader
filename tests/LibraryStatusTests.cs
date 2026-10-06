using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    internal static class LibraryStatusTests
    {
        internal static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "PlaylistFlac-StatusTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { CheckSnapshots(root); CheckRecordingVersions(Path.Combine(root,"versions")); CheckAmbiguousActiveQuery(Path.Combine(root,"ambiguous-query")); }
            finally
            {
                string absolute = Path.GetFullPath(root), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("PlaylistFlac-StatusTests-", StringComparison.Ordinal)) throw new Exception("Unsafe status test cleanup path.");
                Directory.Delete(absolute, true);
            }
        }

        private static void CheckRecordingVersions(string root)
        {
            Directory.CreateDirectory(root);
            var first=new Track {Title="KING",Artist="Kanye West",Album="Standard",DurationSeconds=126.8,Isrc="QZQAY2608133"};
            var deluxe=new Track {Title="KING",Artist="Kanye West",Album="Deluxe",DurationSeconds=126.6,Isrc="QZQAY2608133"};
            var japanese=new Track {Title="KING",Artist="Kanaria",Album="KING",DurationSeconds=135.5,Isrc="JPX562000062"};
            var explicitTrack=new Track {Title="Version",Artist="Artist",Album="Same album",DurationSeconds=200,Isrc="USUG12602467"};
            var clean=new Track {Title="Version",Artist="Artist",Album="Same album",DurationSeconds=200,Isrc="USUG12602480"};
            var playlist=new Playlist {Tracks=new List<Track>{first,deluxe,japanese,explicitTrack,clean}};
            string file=Path.Combine(root,"kept.flac");File.WriteAllBytes(file,new byte[0]);
            var index=new Dictionary<string,SavedTrack>();index[IndexStore.Key(deluxe)]=Saved(deluxe,file,1);index[IndexStore.Key(explicitTrack)]=Saved(explicitTrack,file,1);
            IndexStore.Save(root,index,playlist);
            var snapshot=LibraryStatus.Read(root,playlist,false);
            Assert(snapshot.StatusFor(first)=="Downloaded" && snapshot.StatusFor(deluxe)=="Downloaded", "An existing album copy satisfies matching recording aliases without extra downloads.");
            Assert(snapshot.StatusFor(japanese)=="Ready", "A same-title song by another artist remains a separate download.");
            Assert(snapshot.StatusFor(explicitTrack)=="Review versions" && snapshot.StatusFor(clean)=="Review versions", "Conflicting recording IDs cannot inherit one legacy file as both completed versions.");
        }

        private static void CheckSnapshots(string root)
        {
            Track done = Track("Done"), missing = Track("Missing"), failed = Track("Failed"), ready = Track("Ready");
            var playlist = new Playlist { Tracks = new List<Track> { done, missing, failed, ready, done } };
            string completedPath = Path.Combine(root, "complete.flac"); File.WriteAllBytes(completedPath, new byte[0]);
            var index = new Dictionary<string, SavedTrack>();
            index[IndexStore.Key(done)] = Saved(done, completedPath, 1);
            index[IndexStore.Key(missing)] = Saved(missing, Path.Combine(root, "absent.flac"), 1);
            index[IndexStore.Key(failed)] = Saved(failed, "", 2);
            IndexStore.Save(root, index, playlist);
            string originalIndex = File.ReadAllText(LibraryLayout.PathFor(root,"_index.csv"));
            LibrarySnapshot snapshot = LibraryStatus.Read(root, playlist);
            Assert(snapshot.Completed == 2 && snapshot.Failed == 1 && snapshot.Ready == 2 && snapshot.Total == 5, "Counts must include duplicate display rows and match the playlist total.");
            Assert(snapshot.StatusFor(missing) == "Ready" && snapshot.Statuses.Count == 4, "A missing successful file must be ready to download again.");
            bool readOnly = false;
            try { ((IDictionary<string, string>)snapshot.Statuses)[IndexStore.Key(done)] = "Ready"; }
            catch (NotSupportedException) { readOnly = true; }
            Assert(readOnly, "Snapshot statuses must not be mutable.");

            Directory.CreateDirectory(Path.Combine(root, ".search"));
            Track alias = Track("Alternate title");
            string newPath = Path.Combine(root, "fresh.flac"); File.WriteAllBytes(newPath, new byte[0]);
            WriteRaw(root, new[] { Saved(alias, newPath, 1), Saved(done, "", 2) });
            WriteMap(root, new[] { Mapping(root, failed, alias), Mapping(root, done, done) });
            LibrarySnapshot fresh = LibraryStatus.Read(root, playlist);
            Assert(fresh.StatusFor(failed) == "Downloaded" && fresh.Completed == 3 && fresh.Failed == 0, "A current successful raw result may recover a canonical failure.");
            Assert(fresh.StatusFor(done) == "Downloaded", "A stale raw failure must not replace a completed canonical file.");
            Assert(snapshot.Failed == 1 && snapshot.StatusFor(failed) == "Failed", "An old snapshot must stay unchanged after new data arrives.");
            Assert(LibraryStatus.Read(root, playlist, false).StatusFor(failed) == "Failed", "The active overlay must be optional.");

            var mismatched = Mapping(root, failed, alias); mismatched.OriginalKey = "unrelated";
            WriteMap(root, new[] { mismatched });
            Assert(LibraryStatus.Read(root, playlist).StatusFor(failed) == "Failed", "A mismatched explicit original key must not be applied.");
            mismatched = Mapping(root + "-other", failed, alias); WriteMap(root, new[] { mismatched });
            Assert(LibraryStatus.Read(root, playlist).StatusFor(failed) == "Failed", "An explicit owner for another folder must not be applied.");
            WriteMap(root, new[] { Mapping(root, failed, alias) });
            WriteRaw(root, new[] { Saved(alias, Path.Combine(root, "missing-raw.flac"), 1) });
            Assert(LibraryStatus.Read(root, playlist).StatusFor(failed) == "Failed", "A raw success without its file must not erase failure history.");

            File.WriteAllText(LibraryLayout.PathFor(root,".active-search-index.csv"), "filepath,artist,album,title,length,tracktype,state,failurereason\n\"partial", Encoding.UTF8);
            Assert(LibraryStatus.Read(root, playlist).Completed == 2, "A partial raw CSV must fall back to the canonical index.");
            File.WriteAllText(Path.Combine(root, ".search", "active-map.json"), "[{\"Original\":", Encoding.UTF8);
            Assert(LibraryStatus.Read(root, playlist).Completed == 2, "A partial map must fall back to the canonical index.");
            Assert(File.ReadAllText(LibraryLayout.PathFor(root,"_index.csv")) == originalIndex, "Reading snapshots must never rewrite the saved index.");
            Assert(LibraryStatus.Read(Path.Combine(root, "not-created"), playlist).Ready == 5, "An unused destination must be entirely ready.");
        }

        private static void CheckAmbiguousActiveQuery(string root)
        {
            Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, ".search"));
            var duet = new Track { Title = "Song", Artist = "Lead, Guest", Artists = new[] { "Lead", "Guest" }, Album = "Album", DurationSeconds = 180, Isrc = "USAAA2000001" };
            var solo = new Track { Title = "Song", Artist = "Lead", Artists = new[] { "Lead" }, Album = "Album", DurationSeconds = 180, Isrc = "USAAA2000002" };
            var query = new Track { Title = "Song", Artist = "Lead", Album = "Album", DurationSeconds = 180 };
            var playlist = new Playlist { Tracks = new List<Track> { duet, solo } };
            string file = Path.Combine(root, "unclaimed.flac"); File.WriteAllBytes(file, new byte[] { 42 });
            var index = new Dictionary<string, SavedTrack>(); index[IndexStore.Key(duet)] = Saved(duet, "", 2);
            IndexStore.Save(root, index, playlist);
            string originalIndex = File.ReadAllText(LibraryLayout.PathFor(root,"_index.csv"));
            WriteRaw(root, new[] { Saved(query, file, 1) });
            WriteMap(root, new[] { Mapping(root, duet, query), Mapping(root, solo, query) });
            var ambiguous = LibraryStatus.Read(root, playlist);
            Assert(ambiguous.Completed == 0 && ambiguous.StatusFor(duet) == "Failed" && ambiguous.StatusFor(solo) == "Ready",
                "One ambiguous engine query cannot mark two distinct source recordings downloaded or erase canonical failure history.");
            WriteMap(root, new[] { Mapping(root, duet, query) });
            var unique = LibraryStatus.Read(root, playlist);
            Assert(unique.Completed == 1 && unique.StatusFor(duet) == "Downloaded" && unique.StatusFor(solo) == "Ready",
                "A unique active mapping still overlays its original without claiming another recording.");
            Assert(File.ReadAllText(LibraryLayout.PathFor(root,"_index.csv")) == originalIndex && File.ReadAllBytes(file)[0] == 42,
                "Ambiguity checks must never mutate the library or completed audio.");
        }

        private static Track Track(string title) { return new Track { Title = title, Artist = "Artist", Album = "Album", DurationSeconds = 100 }; }
        private static SavedTrack Saved(Track track, string path, int state) { return new SavedTrack { Track = track, Path = path, State = state, Reason = state == 2 ? 9 : 0 }; }
        private static MapRow Mapping(string root, Track original, Track query) { return new MapRow { Original = original, Query = query, OriginalKey = IndexStore.Key(original), Owner = root }; }
        private static void WriteMap(string root, MapRow[] jobs) { File.WriteAllText(Path.Combine(root, ".search", "active-map.json"), new JavaScriptSerializer().Serialize(jobs), Encoding.UTF8); }
        private static void WriteRaw(string root, SavedTrack[] entries)
        {
            var output = new StringBuilder("filepath,artist,album,title,length,tracktype,state,failurereason\r\n");
            foreach (SavedTrack row in entries)
                output.AppendLine(String.Join(",", new[] { IndexStore.Quote(row.Path), IndexStore.Quote(row.Track.Artist), IndexStore.Quote(row.Track.Album), IndexStore.Quote(row.Track.Title), IndexStore.Seconds(row.Track).ToString(), "0", row.State.ToString(), row.Reason.ToString() }));
            File.WriteAllText(LibraryLayout.PathFor(root,".active-search-index.csv"), output.ToString(), Encoding.UTF8);
        }
        private sealed class MapRow
        {
            public Track Original { get; set; }
            public Track Query { get; set; }
            public string OriginalKey { get; set; }
            public string Owner { get; set; }
        }
        private static void Assert(bool value, string message) { if (!value) throw new Exception("Library status test failed: " + message); }
    }
}
