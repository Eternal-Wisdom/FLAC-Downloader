using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PlaylistFlac
{
    public static class DuplicateLibraryTests
    {
        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "PlaylistFlac-DuplicateTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                CheckAlbumCopies(Path.Combine(root, "albums"));
                CheckMissingRepresentative(Path.Combine(root, "missing"));
                CheckCancellation(Path.Combine(root, "cancel"));
                CheckCommitRollback(Path.Combine(root, "rollback"));
                CheckOutsideIndex(Path.Combine(root, "outside"));
                CheckHistoricalReference(Path.Combine(root, "historical"));
                CheckEncodedAudio(Path.Combine(root, "audio"));
                CheckPlaybackOrder(Path.Combine(root, "order"));
                CheckDriveRootRejected(root);
            }
            finally
            {
                string absolute = Path.GetFullPath(root);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(absolute).StartsWith("PlaylistFlac-DuplicateTests-", StringComparison.Ordinal) ||
                    (File.GetAttributes(absolute) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Unsafe duplicate test cleanup path.");
                Directory.Delete(absolute, true);
            }
        }

        private static void CheckAlbumCopies(string folder)
        {
            var original = TrackFor("Original");
            var deluxe = TrackFor("Deluxe");
            var hits = TrackFor("Greatest Hits");
            var otherArtist = TrackFor("Other Album"); otherArtist.Artist = "Other Artist"; otherArtist.Artists = new[] { "Other Artist" };
            var playlist = List(original, otherArtist, deluxe, hits, original);
            string old = Write(folder, "album/original.flac", new byte[] { 11, 12, 13 });
            string keep = Write(folder, "playlist/Song.flac", new byte[] { 21, 22, 23 });
            string other = Write(folder, "playlist/Song (Other Artist).flac", new byte[] { 31, 32, 33 });
            var index = Rows(Row(original, old), Row(deluxe, keep), Row(hits, "", 2), Row(otherArtist, other));
            IndexStore.Save(folder, index, playlist);
            byte[] oldIndex = File.ReadAllBytes(LibraryLayout.PathFor(folder,"_index.csv"));
            byte[] oldPlaylist = File.ReadAllBytes(Path.Combine(folder, "playlist.m3u8"));
            var preview=new List<string>();
            var planned=DuplicateLibrary.Consolidate(folder,playlist,RecordingGroups.Build(playlist.Tracks),index,CancellationToken.None,null,preview);
            Assert(planned.ArchivedFiles==1 && preview.Count==1 && File.Exists(old) && oldIndex.SequenceEqual(File.ReadAllBytes(LibraryLayout.PathFor(folder,"_index.csv"))) && index[IndexStore.Key(original)].Path==old,"Duplicate preview must not mutate files or saved state.");
            var result = Clean(folder, playlist, index);
            Assert(result.LinkedEntries == 2 && result.ArchivedFiles == 1, "Album copies and a pending alias must share the completed finalized file.");
            foreach (Track track in new[] { original, deluxe, hits })
            {
                SavedTrack row = index[IndexStore.Key(track)];
                Assert(row.Path == keep && IndexStore.Done(row) && row.Reason == 0, "Every original album key must point to the retained recording.");
                Assert(Object.ReferenceEquals(row.Track, track), "Linking must retain each source album's metadata.");
            }
            Assert(index.Count == 4 && index[IndexStore.Key(otherArtist)].Path == other, "Same title and ISRC from another artist must remain separate.");
            Assert(!File.Exists(old) && File.ReadAllBytes(keep).SequenceEqual(new byte[] { 21, 22, 23 }) &&
                File.ReadAllBytes(other).SequenceEqual(new byte[] { 31, 32, 33 }), "Only the extra album file is moved; retained audio is unchanged.");
            string[] archived = Directory.GetFiles(result.BackupDirectory, "*.flac");
            Assert(archived.Length == 1 && File.ReadAllBytes(archived[0]).SequenceEqual(new byte[] { 11, 12, 13 }), "Extra bytes must survive in the recovery folder.");
            Assert(File.ReadAllBytes(Path.Combine(result.BackupDirectory, "_index.csv")).SequenceEqual(oldIndex) &&
                File.ReadAllBytes(Path.Combine(result.BackupDirectory, "playlist.m3u8")).SequenceEqual(oldPlaylist), "Both original metadata files must be recoverable.");
            string map = File.ReadAllText(Path.Combine(result.BackupDirectory, "duplicate-map.csv"));
            Assert(map.Contains(IndexStore.Quote(old)) && map.Contains(IndexStore.Quote(keep)) && map.Contains(IndexStore.Quote(archived[0])), "Recovery map must identify original, archived and retained files.");
            Assert(File.Exists(Path.Combine(result.BackupDirectory, "READ ME.txt")), "Recovery instructions must be present.");
            Assert(Playback(folder).SequenceEqual(new[] { "playlist/Song.flac", "playlist/Song (Other Artist).flac" }), "Playback must keep first occurrence order and list each physical file once.");
            var reread = IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv"));
            Assert(reread.Count == 4 && new[] { original, deluxe, hits }.All(x => reread[IndexStore.Key(x)].Path == keep), "All album aliases must survive a saved-index round trip.");
            AssertRepeatUnchanged(folder, playlist, index);
        }

        private static void CheckMissingRepresentative(string folder)
        {
            var original = TrackFor("Original"); var deluxe = TrackFor("Deluxe");
            var playlist = List(original, deluxe);
            string keep = Write(folder, "album/available.flac", new byte[] { 42 });
            string missing = Path.Combine(folder, "album", "missing.flac");
            var index = Rows(Row(original, missing), Row(deluxe, keep, 3));
            IndexStore.Save(folder, index, playlist);
            var result = Clean(folder, playlist, index);
            Assert(result.LinkedEntries == 1 && result.ArchivedFiles == 0 && !File.Exists(missing), "A missing representative must reuse another album's existing completion.");
            Assert(index[IndexStore.Key(original)].Path == keep && index[IndexStore.Key(deluxe)].State == 3, "Existing already-downloaded state must be retained for the keeper.");
            Assert(Playback(folder).SequenceEqual(new[] { "album/available.flac" }), "Missing representative recovery must not duplicate playback.");
            AssertRepeatUnchanged(folder, playlist, index);
        }

        private static void CheckCancellation(string folder)
        {
            Playlist playlist; Dictionary<string, SavedTrack> index;
            PreparePair(folder, out playlist, out index);
            var originalRows = new Dictionary<string, SavedTrack>(index);
            var before = Snapshot(folder);
            bool logged = false, cancelled = false;
            using (var cancel = new CancellationTokenSource())
            {
                try
                {
                    DuplicateLibrary.Consolidate(folder, playlist, RecordingGroups.Build(playlist.Tracks), index, cancel.Token,
                        delegate(string message) { logged = true; cancel.Cancel(); });
                }
                catch (OperationCanceledException) { cancelled = true; }
            }
            Assert(logged && cancelled, "Cancellation at the pre-move callback must propagate.");
            AssertSnapshot(folder, before, "Cancellation must preserve every original file and metadata byte.");
            AssertRowsUnchanged(index, originalRows);
            Assert(Directory.GetFiles(folder, "*.flac", SearchOption.AllDirectories).Length == 2, "Cancellation before moves must leave both music files in place.");
        }

        private static void CheckCommitRollback(string folder)
        {
            Playlist playlist; Dictionary<string, SavedTrack> index;
            PreparePair(folder, out playlist, out index);
            var originalRows = new Dictionary<string, SavedTrack>(index);
            var before = Snapshot(folder);
            FileStream held = null;
            IOException failure = null;
            try
            {
                try
                {
                    DuplicateLibrary.Consolidate(folder, playlist, RecordingGroups.Build(playlist.Tracks), index, CancellationToken.None,
                        delegate(string message) { held = new FileStream(Path.Combine(folder, "playlist.m3u8"), FileMode.Open, FileAccess.Read, FileShare.None); });
                }
                catch (IOException ex) { failure = ex; }
            }
            finally { if (held != null) held.Dispose(); }
            Assert(held != null && failure != null, "An exclusive playlist lock after planning must force commit failure.");
            AssertSnapshot(folder, before, "Commit failure must restore moved files and the original index and playlist bytes.");
            AssertRowsUnchanged(index, originalRows);
            Assert(failure.Message.IndexOf("could not fully roll back", StringComparison.OrdinalIgnoreCase) < 0,
                "An unchanged locked playback file must not produce a false incomplete-rollback report.");
            Assert(Directory.GetFiles(folder, "*.flac", SearchOption.AllDirectories).Length == 2, "A failed commit must return both archived files to their original paths.");
            Assert(Directory.GetFiles(folder, "*.restore-*", SearchOption.TopDirectoryOnly).Length == 0 &&
                Directory.GetFiles(folder, "*.tmp", SearchOption.TopDirectoryOnly).Length == 0, "Rollback must not leave temporary metadata files behind.");
        }

        private static void CheckOutsideIndex(string parent)
        {
            string folder = Path.Combine(parent, "library"); Directory.CreateDirectory(folder);
            var first = TrackFor("Original"); var second = TrackFor("Deluxe"); var playlist = List(first, second);
            string external = Write(parent, "elsewhere.flac", new byte[] { 55, 56 });
            var index = Rows(Row(first, external), Row(second, Path.Combine(folder, "missing.flac")));
            IndexStore.Save(folder, index, playlist);
            var before = Snapshot(folder); var rows = new Dictionary<string, SavedTrack>(index);
            var result = Clean(folder, playlist, index);
            Assert(result.LinkedEntries == 0 && result.ArchivedFiles == 0 && result.BackupDirectory == null, "An outside indexed file cannot be selected as a reusable keeper.");
            Assert(!DuplicateLibrary.SafeFile(folder, external), "Outside paths must fail the containment check.");
            Assert(File.ReadAllBytes(external).SequenceEqual(new byte[] { 55, 56 }), "An outside file must remain untouched.");
            AssertRowsUnchanged(index, rows); AssertSnapshot(folder, before, "Ignoring an outside index must not rewrite metadata.");
        }

        private static void CheckHistoricalReference(string folder)
        {
            Playlist playlist; Dictionary<string, SavedTrack> index;
            PreparePair(folder, out playlist, out index);
            string extra = index[IndexStore.Key(playlist.Tracks[0])].Path;
            string keep = index[IndexStore.Key(playlist.Tracks[1])].Path;
            var historical = new Track { Title = "Previously imported song", Artist = "Historical Artist", Album = "History", DurationSeconds = 100 };
            index.Add(IndexStore.Key(historical), Row(historical, extra, 3));
            IndexStore.Save(folder, index, playlist);
            var result = Clean(folder, playlist, index);
            Assert(result.LinkedEntries == 1 && result.ArchivedFiles == 0, "Completed historical index references must prevent archival.");
            Assert(File.Exists(extra) && File.Exists(keep) && index[IndexStore.Key(historical)].Path == extra,
                "The historical row and its audio must remain intact after active aliases are linked.");
            Assert(index[IndexStore.Key(playlist.Tracks[0])].Path == keep && Playback(folder).Length == 1, "Current playback can still share the keeper without orphaning history.");
            AssertRepeatUnchanged(folder, playlist, index);
        }

        private static void CheckEncodedAudio(string folder)
        {
            var track = TrackFor("Original"); var playlist = List(track);
            byte[] frames = { 255, 248, 105, 24, 0, 0, 1, 2, 3, 4, 5, 6 };
            byte[] changed = (byte[])frames.Clone(); changed[8] = 99;
            string keep = Write(folder, "playlist/Song.flac", Flac(new byte[] { 1, 2 }, frames));
            byte[] duplicateBytes = Flac(new byte[] { 9, 8, 7, 6 }, frames);
            string duplicate = Write(folder, "playlist/Song (2).flac", duplicateBytes);
            byte[] differentBytes = Flac(new byte[] { 1, 2 }, changed);
            string different = Write(folder, "playlist/Song (3).flac", differentBytes);
            string noAudio = Write(folder, "playlist/Song (4).flac", Flac(new byte[] { 1 }, new byte[0]));
            string elsewhere = Write(folder, "other/identical.flac", duplicateBytes);
            string nested = Write(folder, "playlist/nested/identical.flac", duplicateBytes);
            var index = Rows(Row(track, keep)); IndexStore.Save(folder, index, playlist);
            Assert(FlacIdentity.QuickSignature(keep) == FlacIdentity.QuickSignature(different), "Differing-audio fixture must share the declared format and MD5 candidate signature.");
            Assert(FlacIdentity.AudioHash(keep, CancellationToken.None) == FlacIdentity.AudioHash(duplicate, CancellationToken.None) &&
                FlacIdentity.AudioHash(keep, CancellationToken.None) != FlacIdentity.AudioHash(different, CancellationToken.None), "Fixtures must distinguish encoded audio from metadata and declared MD5.");
            var result = Clean(folder, playlist, index);
            Assert(result.LinkedEntries == 0 && result.ArchivedFiles == 1 && !File.Exists(duplicate), "Only unindexed exact encoded-audio matches may be archived.");
            Assert(File.ReadAllBytes(Directory.GetFiles(result.BackupDirectory, "*.flac").Single()).SequenceEqual(duplicateBytes), "Archived unindexed audio must preserve all original metadata and bytes.");
            Assert(File.ReadAllBytes(different).SequenceEqual(differentBytes) && File.Exists(noAudio), "Different frames and metadata-only files must survive candidate matching.");
            Assert(File.Exists(elsewhere) && File.Exists(nested), "Unindexed scans must stay in the finalized playlist directory and not recurse.");
            Assert(File.ReadAllBytes(keep).SequenceEqual(Flac(new byte[] { 1, 2 }, frames)), "Encoded comparisons and cleanup cannot modify retained audio.");
            AssertRepeatUnchanged(folder, playlist, index);
        }

        private static void CheckPlaybackOrder(string folder)
        {
            var first = TrackFor("A"); var alias = TrackFor("B");
            var second = new Track { Title = "Second", Artist = "Artist", Album = "A", DurationSeconds = 100 };
            string one = Write(folder, "playlist/one.flac", new byte[] { 1 }); string two = Write(folder, "playlist/two.flac", new byte[] { 2 });
            var playlist = List(second, alias, first, second, alias);
            var index = Rows(Row(first, one), Row(alias, one), Row(second, two));
            IndexStore.Save(folder, index, playlist);
            Assert(Playback(folder).SequenceEqual(new[] { "playlist/two.flac", "playlist/one.flac" }), "Saving alone must emit each physical file at its first playlist occurrence, independent of index order.");
        }

        private static void CheckDriveRootRejected(string tempFolder)
        {
            // Empty inputs ensure that even a regressed guard has no file to move or index to save.
            string driveRoot = Path.GetPathRoot(Path.GetFullPath(tempFolder));
            bool rejected = false;
            try { Clean(driveRoot, new Playlist(), new Dictionary<string, SavedTrack>()); }
            catch (IOException) { rejected = true; }
            catch (ArgumentException) { rejected = true; }
            Assert(rejected, "Drive-root cleanup must be rejected before scanning or creating recovery metadata.");
        }

        private static void PreparePair(string folder, out Playlist playlist, out Dictionary<string, SavedTrack> index)
        {
            var first = TrackFor("Original"); var second = TrackFor("Deluxe"); playlist = List(first, second);
            string extra = Write(folder, "album/original.flac", new byte[] { 61, 62 });
            string keep = Write(folder, "playlist/Song.flac", new byte[] { 71, 72 });
            index = Rows(Row(first, extra), Row(second, keep)); IndexStore.Save(folder, index, playlist);
        }

        private static void AssertRepeatUnchanged(string folder, Playlist playlist, Dictionary<string, SavedTrack> index)
        {
            var before = Snapshot(folder); int backups = Directory.GetDirectories(Path.Combine(folder,RecoveryFolders.Name), ".duplicates-backup-*").Length;
            var result = Clean(folder, playlist, index);
            Assert(result.LinkedEntries == 0 && result.ArchivedFiles == 0 && result.BackupDirectory == null, "Repeated cleanup must be idempotent.");
            Assert(Directory.GetDirectories(Path.Combine(folder,RecoveryFolders.Name), ".duplicates-backup-*").Length == backups, "Repeated cleanup must not create another recovery folder.");
            AssertSnapshot(folder, before, "Repeated cleanup must not rewrite existing files.");
        }

        private static DuplicateCleanupResult Clean(string folder, Playlist playlist, Dictionary<string, SavedTrack> index)
        { return DuplicateLibrary.Consolidate(folder, playlist, RecordingGroups.Build(playlist.Tracks), index, CancellationToken.None, null); }
        private static Track TrackFor(string album)
        { return new Track { Title = "Song", Artist = "Artist", Artists = new[] { "Artist" }, Album = album, DurationSeconds = 100, Isrc = "USAAA2000001" }; }
        private static Playlist List(params Track[] tracks) { var playlist = new Playlist(); playlist.Tracks.AddRange(tracks); return playlist; }
        private static SavedTrack Row(Track track, string path, int state = 1) { return new SavedTrack { Track = track, Path = path, State = state, Reason = state == 2 ? 5 : 0 }; }
        private static Dictionary<string, SavedTrack> Rows(params SavedTrack[] rows) { return rows.ToDictionary(x => IndexStore.Key(x.Track)); }
        private static string[] Playback(string folder) { return File.ReadAllLines(Path.Combine(folder, "playlist.m3u8")).Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal)).ToArray(); }
        private static string Write(string folder, string relative, byte[] bytes)
        { string path = Path.GetFullPath(Path.Combine(folder, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); return path; }
        private static Dictionary<string, byte[]> Snapshot(string folder)
        { return Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(x => x, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase); }
        private static void AssertSnapshot(string folder, Dictionary<string, byte[]> before, string message)
        { foreach (var item in before) Assert(File.Exists(item.Key) && File.ReadAllBytes(item.Key).SequenceEqual(item.Value), message + " " + Path.GetFileName(item.Key)); }
        private static void AssertRowsUnchanged(Dictionary<string, SavedTrack> actual, Dictionary<string, SavedTrack> expected)
        { Assert(actual.Count == expected.Count && expected.All(x => actual.ContainsKey(x.Key) && Object.ReferenceEquals(actual[x.Key], x.Value)), "A failed operation must leave the caller's index rows unchanged."); }

        private static byte[] Flac(byte[] metadata, byte[] frames)
        {
            // Structural fixture for encoded-byte comparison, not a decoder-valid music sample.
            var bytes = new byte[46 + metadata.Length + frames.Length];
            bytes[0] = 102; bytes[1] = 76; bytes[2] = 97; bytes[3] = 67; bytes[7] = 34; bytes[8] = 16; bytes[10] = 16;
            ulong packed = ((ulong)44100 << 44) | ((ulong)1 << 41) | ((ulong)15 << 36) | (ulong)441000;
            for (int i = 25; i >= 18; i--) { bytes[i] = (byte)packed; packed >>= 8; }
            bytes[42] = 0x86; bytes[43] = (byte)(metadata.Length >> 16); bytes[44] = (byte)(metadata.Length >> 8); bytes[45] = (byte)metadata.Length;
            Buffer.BlockCopy(metadata, 0, bytes, 46, metadata.Length); Buffer.BlockCopy(frames, 0, bytes, 46 + metadata.Length, frames.Length);
            return bytes;
        }
        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Duplicate cleanup test failed: " + message); }
    }
}
