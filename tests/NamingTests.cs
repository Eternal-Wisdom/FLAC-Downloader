using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PlaylistFlac
{
    public static class NamingTests
    {
        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "PlaylistFlac-NamingTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                CheckNames(Path.Combine(root, "names")); CheckRollback(Path.Combine(root, "rollback")); CheckCancel(Path.Combine(root, "cancel"));
                CheckSharedRecording(Path.Combine(root, "shared")); CheckDistinctAlbums(Path.Combine(root, "albums"));
                CheckUnicodeClashes(Path.Combine(root, "unicode")); CheckUndownloadedClash(Path.Combine(root, "future"));
                CheckUnindexedCollision(Path.Combine(root, "unindexed")); CheckConflictingSharedFile(Path.Combine(root, "conflicting"));
                CheckAmbiguousLegacyRow(Path.Combine(root, "ambiguous"));
                CheckHistoricalReferences(Path.Combine(root, "historical"));
            }
            finally
            {
                string absolute = Path.GetFullPath(root);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("PlaylistFlac-NamingTests-", StringComparison.Ordinal)) throw new Exception("Unsafe test cleanup path.");
                Directory.Delete(absolute, true);
            }
        }

        private static void CheckNames(string folder)
        {
            Directory.CreateDirectory(folder); Directory.CreateDirectory(Path.Combine(folder, "album"));
            var p = new Playlist();
            p.Tracks.Add(new Track { Title = "夜の歌 (feat. Guest)", Artist = "Lead, Guest", DurationSeconds = 123.9 });
            p.Tracks.Add(new Track { Title = "Same", Artist = "First", DurationSeconds = 10 });
            p.Tracks.Add(new Track { Title = "Same", Artist = "Second", DurationSeconds = 10 });
            p.Tracks.Add(new Track { Title = "AC/DC: Song?", Artist = "Earth, Wind & Fire", DurationSeconds = 20 });
            p.Tracks.Add(new Track { Title = "CON", Artist = "Artist", DurationSeconds = 5 });
            p.Tracks.Add(new Track { Title = "Wrong length", Artist = "Artist", DurationSeconds = 99 });
            var rows = new List<string> { Header };
            string[] names = { "album/Peer - Japanese.flac", "one.flac", "two.flac", "band.flac", "reserved.flac", "wrong.flac" };
            for (int i = 0; i < p.Tracks.Count; i++)
            {
                File.WriteAllBytes(Path.Combine(folder, names[i]), new byte[0]);
                rows.Add(Row("./" + names[i], p.Tracks[i], i == 5 ? 98 : (int)p.Tracks[i].DurationSeconds, 1));
            }
            rows.Add(Row("../outside.flac", p.Tracks[1], 10, 1));
            rows.Add(Row("./failed.flac", p.Tracks[1], 10, 2));
            File.WriteAllBytes(Path.Combine(folder, "Same.flac"), new byte[] { 42 });
            File.WriteAllBytes(Path.Combine(folder, "failed.flac"), new byte[0]);
            File.WriteAllText(LibraryLayout.PathFor(folder,"_index.csv"), String.Join("\n", rows) + "\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "playlist.m3u8"), "#EXTM3U\n" + String.Join("\n", names) + "\n", new UTF8Encoding(false));
            RenameResult result = LibraryNaming.RenameCompleted(folder, p, CancellationToken.None, null);
            Assert(result.Renamed == 5 && result.Skipped == 3, "Only completed, matched, in-folder tracks may be renamed.");
            Assert(File.Exists(Path.Combine(folder, "album/夜の歌 (feat. Guest).flac")), "Japanese and explicit featured credit must survive.");
            Assert(File.Exists(Path.Combine(folder, "Same (First).flac")) && File.Exists(Path.Combine(folder, "Same (Second).flac")), "Different artists with the same title must receive artist qualifiers.");
            Assert(File.ReadAllBytes(Path.Combine(folder, "Same.flac"))[0] == 42, "Existing files may never be overwritten.");
            Assert(File.Exists(Path.Combine(folder, "AC_DC_ Song_.flac")) && File.Exists(Path.Combine(folder, "_CON.flac")), "Windows invalid and reserved names must be sanitized.");
            string index = File.ReadAllText(LibraryLayout.PathFor(folder,"_index.csv"));
            string playlist = File.ReadAllText(Path.Combine(folder, "playlist.m3u8"));
            Assert(index.Contains("./Same (First).flac") && playlist.Contains("./album/夜の歌 (feat. Guest).flac"), "Both stored path formats must be updated.");
            Assert(Directory.Exists(result.BackupDirectory) && File.Exists(Path.Combine(result.BackupDirectory, "rename-map.csv")), "Original metadata and recovery mapping must be preserved.");
            RenameResult again = LibraryNaming.RenameCompleted(folder, p, CancellationToken.None, null);
            Assert(again.Renamed == 0 && again.Unchanged == 5 && File.ReadAllText(LibraryLayout.PathFor(folder,"_index.csv")) == index, "A repeated invocation must not rename or rewrite again.");
        }

        private static void CheckSharedRecording(string folder)
        {
            var p = new Playlist();
            p.Tracks.Add(new Track { Title = "KING", Artist = "Kanye", Album = "Album", DurationSeconds = 156, Isrc = "USAAA2400001" });
            p.Tracks.Add(new Track { Title = "King", Artist = "Kanye", Album = "Deluxe", DurationSeconds = 157, Isrc = "USAAA2400001" });
            p.Tracks.Add(new Track { Title = "ＫＩＮＧ", Artist = "Kanye", Album = "Greatest Hits", DurationSeconds = 156, Isrc = "USAAA2400001" });
            PrepareFiles(folder, p, new[] { "KING (3).flac", "KING (3).flac", "KING (3).flac" });
            var result = LibraryNaming.RenameCompleted(folder, p, CancellationToken.None, null);
            Assert(result.Renamed == 1 && File.Exists(Path.Combine(folder, "KING.flac")), "Several album aliases sharing one file must use a single stable representative name.");
            Assert(IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv")).Values.All(x => Path.GetFileName(x.Path) == "KING.flac"), "Every album alias must follow a shared file rename.");
            Assert(File.ReadAllLines(Path.Combine(folder, "playlist.m3u8")).Count(x => x == "./KING.flac") == 3, "Playlist order and repeated entries must be preserved while paths share one recording.");
            AssertRepeatUnchanged(folder, p, 1);
        }

        private static void CheckHistoricalReferences(string folder)
        {
            var current = new Playlist();
            current.Tracks.Add(new Track { Title = "KING", Artist = "Kanye", Album = "Current", DurationSeconds = 156 });
            PrepareFiles(folder, current, new[] { "old.flac" });
            var historical = new Track { Title = "King", Artist = "Kanye", Album = "Historical", DurationSeconds = 157 };
            var absolute = new Track { Title = "KING", Artist = "Kanye", Album = "Older compilation", DurationSeconds = 156 };
            var separate = new Track { Title = "KING", Artist = "Kanye", Album = "Independent historical file", DurationSeconds = 156 };
            var unsafeRow = new Track { Title = "KING", Artist = "Kanye", Album = "External reference", DurationSeconds = 156 };
            File.WriteAllBytes(Path.Combine(folder, "separate.flac"), new byte[] { 31, 32 });
            string indexPath = LibraryLayout.PathFor(folder,"_index.csv");
            File.AppendAllText(indexPath,
                Row("./OLD.flac", historical, 157, 3) + "\n" +
                Row(Path.GetFullPath(Path.Combine(folder, "old.flac")), absolute, 156, 1) + "\n" +
                Row("./separate.flac", separate, 156, 1) + "\n" +
                Row("../old.flac", unsafeRow, 156, 1) + "\n");
            File.AppendAllText(Path.Combine(folder, "playlist.m3u8"), "./OLD.flac\n./separate.flac\n../old.flac\n");
            string originalIndex = File.ReadAllText(indexPath);
            var result = LibraryNaming.RenameCompleted(folder, current, CancellationToken.None, null, "playlist");
            string renamed = Path.Combine(folder, "playlist", "KING.flac");
            var index = IndexStore.Read(indexPath);
            Assert(result.Renamed == 1 && File.Exists(renamed) && !File.Exists(Path.Combine(folder, "old.flac")), "One shared file must move once even when only its current album is imported.");
            Assert(new[] { current.Tracks[0], historical, absolute }.All(t => String.Equals(index[IndexStore.Key(t)].Path, renamed, StringComparison.OrdinalIgnoreCase)), "Historical relative, case-varied and absolute references must follow the physical file into its destination folder.");
            Assert(index[IndexStore.Key(historical)].State == 3 && index[IndexStore.Key(historical)].Track.Album == historical.Album, "Remapping historical paths must preserve their status and source metadata.");
            Assert(index[IndexStore.Key(separate)].Path == Path.Combine(folder, "separate.flac") && File.ReadAllBytes(Path.Combine(folder, "separate.flac")).SequenceEqual(new byte[] { 31, 32 }), "Independent historical files and their paths must remain untouched.");
            Assert(File.ReadAllText(indexPath).Contains("../old.flac") && File.ReadAllLines(Path.Combine(folder, "playlist.m3u8")).Count(x => x == "./playlist/KING.flac") == 2, "Only contained references to the moved file may be remapped in the index and playlist.");
            Assert(File.ReadAllText(Path.Combine(result.BackupDirectory, "_index.csv")) == originalIndex, "Recovery metadata must retain every original historical reference.");
            string updatedIndex = File.ReadAllText(indexPath);
            var again = LibraryNaming.RenameCompleted(folder, current, CancellationToken.None, null, "playlist");
            Assert(again.Renamed == 0 && again.Unchanged == 1 && File.ReadAllText(indexPath) == updatedIndex, "Following shared historical references must remain stable on repeated imports.");
        }

        private static void CheckDistinctAlbums(string folder)
        {
            var p = new Playlist();
            p.Tracks.Add(new Track { Title = "Song", Artist = "Artist", Album = "Original", DurationSeconds = 100, Isrc = "USAAA2400001" });
            p.Tracks.Add(new Track { Title = "Song", Artist = "Artist", Album = "Live album", DurationSeconds = 100, Isrc = "USAAA2400002" });
            PrepareFiles(folder, p, new[] { "one.flac", "two.flac" });
            var orphan = new Track { Title = "Song", Artist = "Artist", Album = "Unrelated album", DurationSeconds = 100 };
            File.WriteAllBytes(Path.Combine(folder, "unmatched.flac"), new byte[] { 33 });
            File.AppendAllText(LibraryLayout.PathFor(folder,"_index.csv"), Row("./unmatched.flac", orphan, 100, 1) + "\n");
            var result = LibraryNaming.RenameCompleted(folder, p, CancellationToken.None, null);
            Assert(result.Renamed == 2 && result.Skipped == 1, "Index matching must include album so unrelated rows cannot acquire a source track's name.");
            Assert(File.Exists(Path.Combine(folder, "Song (Artist - Original).flac")) && File.Exists(Path.Combine(folder, "Song (Artist - Live album).flac")), "Distinct recordings by the same artist must use album qualifiers when artist alone cannot distinguish them.");
            Assert(File.ReadAllBytes(Path.Combine(folder, "unmatched.flac"))[0] == 33, "Unmatched album rows must be left untouched.");
            AssertRepeatUnchanged(folder, p, 2);
        }

        private static void CheckUnicodeClashes(string folder)
        {
            var p = new Playlist();
            p.Tracks.Add(new Track { Title = "KING", Artist = "Kanye West, Ty Dolla $ign", Artists = new[] { "Kanye West", "Ty Dolla $ign" }, Album = "Vultures", DurationSeconds = 156 });
            p.Tracks.Add(new Track { Title = "ＫＩＮＧ", Artist = "Kanaria", Album = "KING", DurationSeconds = 134 });
            p.Tracks.Add(new Track { Title = "King  Kunta", Artist = "Kendrick Lamar", DurationSeconds = 234 });
            p.Tracks.Add(new Track { Title = "king kunta", Artist = "Other Artist", DurationSeconds = 220 });
            PrepareFiles(folder, p, new[] { "KING (2).flac", "KING (4).flac", "three.flac", "four.flac" });
            var result = LibraryNaming.RenameCompleted(folder, p, CancellationToken.None, null);
            Assert(result.Renamed == 4, "Old numeric names must be reconsidered when the playlist contains title clashes.");
            Assert(File.Exists(Path.Combine(folder, "KING (Kanye West).flac")) && File.Exists(Path.Combine(folder, "ＫＩＮＧ (Kanaria).flac")), "Width-insensitive title clashes must keep Japanese and English artists distinct and use primary artist labels.");
            Assert(File.Exists(Path.Combine(folder, "King  Kunta (Kendrick Lamar).flac")) && File.Exists(Path.Combine(folder, "king kunta (Other Artist).flac")), "Case and whitespace variations must still count as title clashes.");
            AssertRepeatUnchanged(folder, p, 4);
        }

        private static void CheckUndownloadedClash(string folder)
        {
            var p = new Playlist();
            p.Tracks.Add(new Track { Title = "KING", Artist = "Kanye", DurationSeconds = 156 });
            PrepareFiles(folder, p, new[] { "KING.flac" });
            p.Tracks.Add(new Track { Title = "KING", Artist = "Kanaria", DurationSeconds = 134 });
            var result = LibraryNaming.RenameCompleted(folder, p, CancellationToken.None, null);
            Assert(result.Renamed == 1 && File.Exists(Path.Combine(folder, "KING (Kanye).flac")), "Entire playlist titles must determine names even before another namesake downloads.");
            AssertRepeatUnchanged(folder, p, 1);
        }

        private static void CheckUnindexedCollision(string folder)
        {
            var p = new Playlist();
            p.Tracks.Add(new Track { Title = "Title", Artist = "First", DurationSeconds = 100 });
            p.Tracks.Add(new Track { Title = "Title", Artist = "Second", DurationSeconds = 100 });
            PrepareFiles(folder, p, new[] { "oldone.flac", "oldtwo.flac" });
            File.WriteAllBytes(Path.Combine(folder, "Title (First).flac"), new byte[] { 22 });
            Directory.CreateDirectory(Path.Combine(folder, "Title (First) (2).flac"));
            LibraryNaming.RenameCompleted(folder, p, CancellationToken.None, null);
            Assert(File.Exists(Path.Combine(folder, "Title (First) (3).flac")) && File.Exists(Path.Combine(folder, "Title (Second).flac")), "Numeric suffixes remain a safe final fallback for unrelated occupied paths.");
            Assert(File.ReadAllBytes(Path.Combine(folder, "Title (First).flac"))[0] == 22, "An unindexed file sharing the intended name must never be overwritten.");
            AssertRepeatUnchanged(folder, p, 2);
        }

        private static void CheckConflictingSharedFile(string folder)
        {
            var p = new Playlist();
            p.Tracks.Add(new Track { Title = "KING", Artist = "Kanye", DurationSeconds = 156 });
            p.Tracks.Add(new Track { Title = "KING", Artist = "Kanaria", DurationSeconds = 134 });
            PrepareFiles(folder, p, new[] { "shared.flac", "shared.flac" });
            string index = File.ReadAllText(LibraryLayout.PathFor(folder,"_index.csv")); bool failed = false;
            try { LibraryNaming.RenameCompleted(folder, p, CancellationToken.None, null); }
            catch (InvalidDataException) { failed = true; }
            Assert(failed && File.Exists(Path.Combine(folder, "shared.flac")) && File.ReadAllText(LibraryLayout.PathFor(folder,"_index.csv")) == index, "A single path assigned to different recordings must fail before changing files or metadata.");
        }

        private static void CheckAmbiguousLegacyRow(string folder)
        {
            var p = new Playlist();
            p.Tracks.Add(new Track { Title = "Song", Artist = "Artist", Album = "Album", DurationSeconds = 100, Isrc = "USAAA2400001" });
            PrepareFiles(folder, p, new[] { "legacy.flac" });
            p.Tracks.Add(new Track { Title = "Song", Artist = "Artist", Album = "Album", DurationSeconds = 100, Isrc = "USAAA2400002" });
            string index = File.ReadAllText(LibraryLayout.PathFor(folder,"_index.csv"));
            var result = LibraryNaming.RenameCompleted(folder, p, CancellationToken.None, null);
            Assert(result.Renamed == 0 && result.Skipped == 1 && File.Exists(Path.Combine(folder, "legacy.flac")) && File.ReadAllText(LibraryLayout.PathFor(folder,"_index.csv")) == index, "Conflicting recording IDs with an identical legacy index key cannot safely select a recording to rename.");
        }

        private static void PrepareFiles(string folder, Playlist playlist, string[] files)
        {
            Directory.CreateDirectory(folder);
            var rows = new List<string> { Header };
            for (int i = 0; i < playlist.Tracks.Count; i++)
            {
                if (!File.Exists(Path.Combine(folder, files[i]))) File.WriteAllBytes(Path.Combine(folder, files[i]), new byte[] { (byte)i });
                rows.Add(Row("./" + files[i], playlist.Tracks[i], (int)playlist.Tracks[i].DurationSeconds, 1));
            }
            File.WriteAllText(LibraryLayout.PathFor(folder,"_index.csv"), String.Join("\n", rows) + "\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "playlist.m3u8"), String.Join("\n", files) + "\n", new UTF8Encoding(false));
        }

        private static void AssertRepeatUnchanged(string folder, Playlist playlist, int files)
        {
            string indexPath = LibraryLayout.PathFor(folder,"_index.csv"), m3uPath = Path.Combine(folder, "playlist.m3u8");
            string index = File.ReadAllText(indexPath), m3u = File.ReadAllText(m3uPath);
            var marker = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(indexPath, marker); File.SetLastWriteTimeUtc(m3uPath, marker);
            var result = LibraryNaming.RenameCompleted(folder, playlist, CancellationToken.None, null);
            Assert(result.Renamed == 0 && result.Unchanged == files && result.BackupDirectory == null, "A second pass must keep stable filenames without creating another backup.");
            Assert(File.ReadAllText(indexPath) == index && File.ReadAllText(m3uPath) == m3u && File.GetLastWriteTimeUtc(indexPath) == marker && File.GetLastWriteTimeUtc(m3uPath) == marker, "An unchanged pass must not rewrite either metadata file.");
        }

        private static Playlist Prepare(string folder)
        {
            Directory.CreateDirectory(folder);
            var p = new Playlist(); p.Tracks.Add(new Track { Title = "Title", Artist = "Artist", DurationSeconds = 12 });
            File.WriteAllBytes(Path.Combine(folder, "Artist - Title.flac"), new byte[0]);
            File.WriteAllText(LibraryLayout.PathFor(folder,"_index.csv"), Header + "\n" + Row("./Artist - Title.flac", p.Tracks[0], 12, 1) + "\n");
            File.WriteAllText(Path.Combine(folder, "playlist.m3u8"), "Artist - Title.flac\n");
            return p;
        }

        private static void CheckRollback(string folder)
        {
            Playlist p = Prepare(folder); string index = File.ReadAllText(LibraryLayout.PathFor(folder,"_index.csv"));
            FileStream locked = null; bool failed = false;
            try
            {
                LibraryNaming.RenameCompleted(folder, p, CancellationToken.None, delegate(string message)
                { if (message.StartsWith("Applying", StringComparison.Ordinal)) locked = new FileStream(Path.Combine(folder, "playlist.m3u8"), FileMode.Open, FileAccess.Read, FileShare.None); });
            }
            catch (IOException) { failed = true; }
            finally { if (locked != null) locked.Dispose(); }
            Assert(failed && File.Exists(Path.Combine(folder, "Artist - Title.flac")) && !File.Exists(Path.Combine(folder, "Title.flac")), "A metadata commit failure must roll filenames back.");
            Assert(File.ReadAllText(LibraryLayout.PathFor(folder,"_index.csv")) == index, "An already committed index must be restored on playlist failure.");
        }

        private static void CheckCancel(string folder)
        {
            Playlist p = Prepare(folder); bool canceled = false;
            using (var cts = new CancellationTokenSource())
            {
                try { LibraryNaming.RenameCompleted(folder, p, cts.Token, delegate(string message) { cts.Cancel(); }); }
                catch (OperationCanceledException) { canceled = true; }
            }
            Assert(canceled && File.Exists(Path.Combine(folder, "Artist - Title.flac")), "Cancellation before applying must leave filenames unchanged.");
        }

        private const string Header = "filepath,artist,album,title,length,tracktype,state,failurereason";
        private static string Quote(string text) { return "\"" + (text ?? "").Replace("\"", "\"\"") + "\""; }
        private static string Row(string path, Track track, int length, int state)
        { return Quote(path) + "," + Quote(track.Artist) + "," + Quote(track.Album) + "," + Quote(track.Title) + "," + length + ",0," + state + ",0"; }
        private static void Assert(bool value, string message) { if (!value) throw new Exception("Naming test failed: " + message); }
    }
}
