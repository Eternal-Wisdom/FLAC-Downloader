using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PlaylistFlac
{
    internal sealed class DuplicateCleanupResult
    {
        internal int LinkedEntries, ArchivedFiles;
        internal string BackupDirectory;
    }

    internal static class DuplicateLibrary
    {
        // Every source row keeps its canonical key; matching recordings point to
        // one physical file. This preserves old indexes and source metadata.
        internal static int ReuseCompleted(string folder, RecordingGroups groups, Dictionary<string, SavedTrack> index)
        {
            int linked = 0;
            foreach (var group in groups.Groups)
            {
                if (group.Tracks.Count < 2 || group.Tracks.Any(t => groups.GroupForKey(IndexStore.Key(t)) == null)) continue;
                SavedTrack keeper = FindKeeper(folder, group, index);
                if (keeper == null) continue;
                foreach (var track in group.Tracks)
                {
                    string key = IndexStore.Key(track); SavedTrack old;
                    if (index.TryGetValue(key, out old) && IndexStore.Done(old) && String.Equals(old.Path, keeper.Path, StringComparison.OrdinalIgnoreCase)) continue;
                    index[key] = new SavedTrack { Track = track, Path = keeper.Path, State = 1, Reason = 0 }; linked++;
                }
            }
            return linked;
        }

        private static SavedTrack FindKeeper(string folder, RecordingGroup group, Dictionary<string, SavedTrack> index)
        {
            SavedTrack fallback = null;
            string finalized = Path.GetFullPath(Path.Combine(folder, "playlist")).TrimEnd('\\') + "\\";
            foreach (var track in group.Tracks)
            {
                SavedTrack row;
                if (!index.TryGetValue(IndexStore.Key(track), out row) || !IndexStore.Done(row) || !SafeFile(folder, row.Path)) continue;
                if (row.Path.StartsWith(finalized, StringComparison.OrdinalIgnoreCase)) return row;
                if (fallback == null) fallback = row;
            }
            return fallback;
        }

        internal static DuplicateCleanupResult Consolidate(string folder, Playlist playlist, RecordingGroups groups, Dictionary<string, SavedTrack> index, CancellationToken ct, Action<string> log, List<string> preview=null)
        {
            string root = Path.GetFullPath(folder).TrimEnd('\\', '/');
            if (root.Length < 3 || String.Equals(root, Path.GetPathRoot(Path.GetFullPath(folder)).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose a playlist folder rather than a drive root.");
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Choose an existing playlist folder that is not a directory link.");
            SafePath(root, LibraryLayout.PathFor(root,"_index.csv"));
            var result = new DuplicateCleanupResult();
            var next = new Dictionary<string, SavedTrack>(index);
            result.LinkedEntries = ReuseCompleted(root, groups, next);
            var referenced = new HashSet<string>(next.Values.Where(x => IndexStore.Done(x) && SafeFile(root, x.Path)).Select(x => Path.GetFullPath(x.Path)), StringComparer.OrdinalIgnoreCase);
            var moves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var old in index)
            {
                SavedTrack replacement;
                if (!IndexStore.Done(old.Value) || !SafeFile(root, old.Value.Path) || !next.TryGetValue(old.Key, out replacement)) continue;
                if (!String.Equals(old.Value.Path, replacement.Path, StringComparison.OrdinalIgnoreCase) && !referenced.Contains(old.Value.Path))
                    moves[old.Value.Path] = replacement.Path;
            }

            // Old app versions could leave an unindexed numbered copy. Only
            // archive those after comparing the actual encoded audio bytes;
            // matching titles or declared header MD5 alone never suffice.
            string music = Path.Combine(root, "playlist");
            if (Directory.Exists(music))
            {
                SafePath(root, music);
                var bySignature = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (string path in referenced)
                {
                    ct.ThrowIfCancellationRequested();
                    string signature = FlacIdentity.QuickSignature(path);
                    if (signature == null) continue;
                    List<string> matches;
                    if (!bySignature.TryGetValue(signature, out matches)) { matches = new List<string>(); bySignature.Add(signature, matches); }
                    matches.Add(path);
                }
                var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string path in Directory.EnumerateFiles(music, "*.flac", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    if (referenced.Contains(path) || moves.ContainsKey(path) || !SafeFile(root, path)) continue;
                    string signature = FlacIdentity.QuickSignature(path); List<string> matches;
                    if (signature == null || !bySignature.TryGetValue(signature, out matches)) continue;
                    string hash = FlacIdentity.AudioHash(path, ct);
                    if (hash == null) continue;
                    foreach (string kept in matches)
                    {
                        string keptHash;
                        if (!hashes.TryGetValue(kept, out keptHash)) { keptHash = FlacIdentity.AudioHash(kept, ct); hashes.Add(kept, keptHash); }
                        if (keptHash == hash) { moves.Add(path, kept); break; }
                    }
                }
            }
            ct.ThrowIfCancellationRequested();
            if(preview!=null) {foreach(var move in moves)preview.Add(Path.GetFileName(move.Key)+" → keep "+Path.GetFileName(move.Value));result.ArchivedFiles=moves.Count;return result;}
            if (result.LinkedEntries == 0 && moves.Count == 0) return result;
            string indexPath = SafePath(root, LibraryLayout.PathFor(root,"_index.csv")), playlistPath = SafePath(root, Path.Combine(root, "playlist.m3u8"));
            byte[] oldIndex = File.Exists(indexPath) ? File.ReadAllBytes(indexPath) : null;
            byte[] oldPlaylist = File.Exists(playlistPath) ? File.ReadAllBytes(playlistPath) : null;
            RecoveryFolders.Prepare(root);
            string backup = SafePath(root,RecoveryFolders.NewPath(root,"duplicates"));
            Directory.CreateDirectory(backup); File.SetAttributes(backup, File.GetAttributes(backup) | FileAttributes.Hidden);
            result.BackupDirectory = backup;
            if (oldIndex != null) File.WriteAllBytes(Path.Combine(backup, "_index.csv"), oldIndex);
            if (oldPlaylist != null) File.WriteAllBytes(Path.Combine(backup, "playlist.m3u8"), oldPlaylist);
            var archived = new List<KeyValuePair<string, string>>();
            var plan = new List<KeyValuePair<string, string>>();
            var manifest = new StringBuilder("original,archived,kept\r\n");
            int n = 0;
            foreach (var move in moves)
            {
                string to = SafePath(root, Path.Combine(backup, (++n).ToString("0000") + ".flac"));
                plan.Add(new KeyValuePair<string, string>(move.Key, to));
                manifest.AppendLine(String.Join(",", new[] { move.Key, to, move.Value }.Select(IndexStore.Quote)));
            }
            File.WriteAllText(Path.Combine(backup, "duplicate-map.csv"), manifest.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(backup, "READ ME.txt"), "Duplicate files were moved here, not deleted. duplicate-map.csv records each original path and the retained copy. The original index and playback playlist are backed up here. Stop FLAC-Downloader before restoring files or metadata. Kept paths in the map describe this cleanup; later filename changes have their own .naming-backup recovery maps.\r\n");
            bool indexSaved = false, playlistSaved = false;
            try
            {
                if (log != null) log("Consolidating " + result.LinkedEntries + " duplicate playlist entries; keeping " + moves.Count + " extra files in a recovery folder.");
                foreach (var move in plan)
                {
                    ct.ThrowIfCancellationRequested(); SafePath(root, move.Key); SafePath(root, move.Value);
                    File.Move(move.Key, move.Value); archived.Add(move);
                }
                ct.ThrowIfCancellationRequested();
                IndexStore.Save(root, next, playlist, name=>{if(name=="index")indexSaved=true;else if(name=="playlist")playlistSaved=true;});
                index.Clear(); foreach (var row in next) index.Add(row.Key, row.Value);
                result.ArchivedFiles = archived.Count;
            }
            catch (Exception failure)
            {
                var errors = new List<Exception>();
                for (int i = archived.Count - 1; i >= 0; i--)
                {
                    try { SafePath(root, archived[i].Value); SafePath(root, archived[i].Key); File.Move(archived[i].Value, archived[i].Key); }
                    catch (Exception ex) { errors.Add(ex); }
                }
                if(indexSaved) Restore(root, indexPath, oldIndex, errors);
                if(playlistSaved) Restore(root, playlistPath, oldPlaylist, errors);
                if (errors.Count > 0) throw new IOException("Duplicate cleanup could not fully roll back. Recovery files are in " + backup + ". " + errors[0].Message, failure);
                throw;
            }
            return result;
        }

        private static void Restore(string root, string path, byte[] bytes, List<Exception> errors)
        {
            try
            {
                SafePath(root, path);
                if (bytes == null) { if (File.Exists(path)) File.Delete(path); return; }
                string staged = path + ".restore-" + Guid.NewGuid().ToString("N");
                File.WriteAllBytes(staged, bytes);
                if (File.Exists(path)) File.Replace(staged, path, null); else File.Move(staged, path);
            }
            catch (Exception ex) { errors.Add(ex); }
        }
        internal static bool SafeFile(string root, string path)
        {
            try { return File.Exists(SafePath(Path.GetFullPath(root).TrimEnd('\\', '/'), path)) && String.Equals(Path.GetExtension(path), ".flac", StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) { if (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException) return false; throw; }
        }
        private static string SafePath(string root, string value)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new IOException("An indexed file path is empty.");
            string path = Path.GetFullPath(value);
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("A duplicate file path leaves the playlist folder.");
            string cursor = path;
            while (cursor != null && cursor.Length >= root.Length)
            {
                if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files and folders are not consolidated.");
                if (String.Equals(cursor, root, StringComparison.OrdinalIgnoreCase)) break;
                cursor = Path.GetDirectoryName(cursor);
            }
            return path;
        }
    }
}
