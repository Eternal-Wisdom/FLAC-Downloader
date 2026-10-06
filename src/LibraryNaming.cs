using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PlaylistFlac
{
    public sealed class RenameResult
    {
        public int Renamed, Unchanged, Skipped;
        public string BackupDirectory;
    }

    public static class LibraryNaming
    {
        private sealed class Change
        {
            public string From, To;
        }

        private sealed class CompletedFile
        {
            public string From, GroupKey;
        }

        public static RenameResult RenameCompleted(string folder, Playlist original, CancellationToken ct, Action<string> log, string destinationDirectory = null)
        {
            if (original == null) throw new ArgumentNullException("original");
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root.Length < 3) throw new IOException("Choose a playlist folder rather than a drive root.");
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Choose an existing folder that is not a directory link.");
            string targetDirectory = null;
            if (destinationDirectory != null)
            {
                targetDirectory = Path.GetFullPath(Path.IsPathRooted(destinationDirectory) ? destinationDirectory : Path.Combine(root, destinationDirectory)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!String.Equals(targetDirectory, root, StringComparison.OrdinalIgnoreCase)) targetDirectory = SafePath(root, targetDirectory);
                if (File.Exists(targetDirectory)) throw new IOException("The destination must be a directory.");
            }
            string indexPath = SafePath(root, LibraryLayout.PathFor(root,"_index.csv")), playlistPath = SafePath(root, "playlist.m3u8");
            var result = new RenameResult();
            if (!File.Exists(indexPath)) { result.Skipped = original.Tracks.Count; return result; }
            byte[] indexBytes = File.ReadAllBytes(indexPath);
            byte[] playlistBytes = File.Exists(playlistPath) ? File.ReadAllBytes(playlistPath) : null;
            var rows = ReadCsv(Encoding.UTF8.GetString(indexBytes).TrimStart('\uFEFF'));
            if (rows.Count == 0) throw new InvalidDataException("The download index is empty.");
            string[] required = { "filepath", "artist", "album", "title", "length", "tracktype", "state", "failurereason" };
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows[0].Count; i++) columns.Add(rows[0][i].Trim(), i);
            foreach (string name in required) if (!columns.ContainsKey(name)) throw new InvalidDataException("Unknown download index format: missing " + name + ".");
            var recordings = RecordingGroups.Build(original.Tracks);
            var names = RecordingNames(recordings);
            var originals = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
            foreach (Track track in original.Tracks)
            {
                string key = IndexStore.Key(track);
                if (!originals.ContainsKey(key)) originals.Add(key, track);
            }
            var changes = new List<Change>();
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<CompletedFile>();
            var byPath = new Dictionary<string, CompletedFile>(StringComparer.OrdinalIgnoreCase);
            for (int n = 1; n < rows.Count; n++)
            {
                ct.ThrowIfCancellationRequested();
                var row = rows[n];
                if (row.Count == 1 && row[0].Length == 0) continue;
                if (row.Count != rows[0].Count) throw new InvalidDataException("Malformed download index row " + (n + 1) + ".");
                int state, length; Track track;
                if (!Int32.TryParse(row[columns["state"]], out state) || (state != 1 && state != 3) ||
                    row[columns["tracktype"]] != "0" || row[columns["failurereason"]] != "0" ||
                    !Int32.TryParse(row[columns["length"]], NumberStyles.Integer, CultureInfo.InvariantCulture, out length) ||
                    !originals.TryGetValue(IndexStore.Key(new Track { Title = row[columns["title"]], Artist = row[columns["artist"]], Album = row[columns["album"]], DurationSeconds = length }), out track))
                { result.Skipped++; continue; }
                string from;
                try { from = SafePath(root, row[columns["filepath"]]); }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is ArgumentException) && !(ex is UnauthorizedAccessException)) throw;
                    result.Skipped++; continue;
                }
                if (!File.Exists(from) || !String.Equals(Path.GetExtension(from), ".flac", StringComparison.OrdinalIgnoreCase))
                { result.Skipped++; continue; }
                var recording = recordings.GroupForKey(IndexStore.Key(track));
                if (recording == null) { result.Skipped++; continue; }
                string groupKey = recording.Key;
                CompletedFile file;
                if (!byPath.TryGetValue(from, out file))
                {
                    file = new CompletedFile { From = from, GroupKey = groupKey };
                    byPath.Add(from, file); files.Add(file);
                }
                else if (!String.Equals(file.GroupKey, groupKey, StringComparison.Ordinal))
                    throw new InvalidDataException("One completed file is indexed as different recordings; no files were renamed.");
            }
            foreach (CompletedFile file in files)
            {
                ct.ThrowIfCancellationRequested();
                string title = names[file.GroupKey];
                string directory = targetDirectory ?? Path.GetDirectoryName(file.From);
                string to = Path.Combine(directory, title + ".flac");
                int suffix = 2;
                while (reserved.Contains(to) || Directory.Exists(to) ||
                    (File.Exists(to) && !String.Equals(to, file.From, StringComparison.OrdinalIgnoreCase)))
                    to = Path.Combine(directory, title + " (" + (suffix++).ToString(CultureInfo.InvariantCulture) + ").flac");
                SafePath(root, to); reserved.Add(to);
                if (String.Equals(file.From, to, StringComparison.OrdinalIgnoreCase)) result.Unchanged++;
                else { mapping.Add(file.From, to); changes.Add(new Change { From = file.From, To = to }); }
            }
            if (changes.Count == 0) return result;
            // A historical source row can share the same physical file without
            // belonging to this import. Every safe reference must follow its move.
            foreach (var row in rows.Skip(1))
            {
                ct.ThrowIfCancellationRequested();
                if (row.Count != rows[0].Count) continue;
                try
                {
                    string destination;
                    if (mapping.TryGetValue(SafePath(root, row[columns["filepath"]]), out destination))
                        row[columns["filepath"]] = Relative(root, destination);
                }
                catch (IOException) { }
                catch (ArgumentException) { }
                catch (UnauthorizedAccessException) { }
            }
            string newPlaylist = playlistBytes == null ? null : RewritePlaylist(root, Encoding.UTF8.GetString(playlistBytes), mapping);
            ct.ThrowIfCancellationRequested();
            RecoveryFolders.Prepare(root);
            string backup = RecoveryFolders.NewPath(root,"naming");
            Directory.CreateDirectory(backup); result.BackupDirectory = backup;
            File.WriteAllBytes(Path.Combine(backup, "_index.csv"), indexBytes);
            if (playlistBytes != null) File.WriteAllBytes(Path.Combine(backup, "playlist.m3u8"), playlistBytes);
            File.WriteAllText(Path.Combine(backup, "rename-map.csv"), WriteCsv(new List<List<string>> { new List<string> { "original", "renamed" } }.Concat(changes.Select(c => new List<string> { Relative(root, c.From), Relative(root, c.To) })).ToList()), new UTF8Encoding(false));
            string stagedIndex = Path.Combine(backup, "index.new"), stagedPlaylist = Path.Combine(backup, "playlist.new");
            File.WriteAllText(stagedIndex, WriteCsv(rows), new UTF8Encoding(false));
            if (newPlaylist != null) File.WriteAllText(stagedPlaylist, newPlaylist, new UTF8Encoding(false));
            var applied = new List<Change>(); bool indexUpdated = false, playlistUpdated = false;
            try
            {
                if (targetDirectory != null && !String.Equals(targetDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    SafePath(root, targetDirectory); Directory.CreateDirectory(targetDirectory); SafePath(root, targetDirectory);
                }
                if (log != null) log("Applying title filenames to " + changes.Count + " completed downloads.");
                foreach (Change change in changes)
                {
                    ct.ThrowIfCancellationRequested(); SafePath(root, change.From); SafePath(root, change.To);
                    File.Move(change.From, change.To); applied.Add(change);
                }
                ct.ThrowIfCancellationRequested();
                SafePath(root, indexPath); SafePath(root, playlistPath);
                File.Replace(stagedIndex, indexPath, null); indexUpdated = true;
                if (newPlaylist != null) { File.Replace(stagedPlaylist, playlistPath, null); playlistUpdated = true; }
                result.Renamed = changes.Count;
            }
            catch (Exception failure)
            {
                var errors = new List<Exception>();
                for (int i = applied.Count - 1; i >= 0; i--)
                {
                    try { SafePath(root, applied[i].To); SafePath(root, applied[i].From); File.Move(applied[i].To, applied[i].From); }
                    catch (Exception ex) { errors.Add(ex); }
                }
                if (indexUpdated) TryRestore(root, indexPath, indexBytes, backup, errors);
                if (playlistUpdated) TryRestore(root, playlistPath, playlistBytes, backup, errors);
                if (errors.Count > 0) throw new IOException("Filename changes could not be fully rolled back. Recovery copies and rename-map.csv are in " + backup + ". " + errors[0].Message, failure);
                throw;
            }
            if (log != null) log("Renamed " + result.Renamed + " files; preserved index and playlist backups in " + backup + ".");
            return result;
        }

        private static void TryRestore(string root, string path, byte[] bytes, string backup, List<Exception> errors)
        {
            try
            {
                SafePath(root, path);
                string temp = Path.Combine(backup, Guid.NewGuid().ToString("N") + ".restore");
                File.WriteAllBytes(temp, bytes); File.Replace(temp, path, null);
            }
            catch (Exception ex) { errors.Add(ex); }
        }

        private static Dictionary<string, string> RecordingNames(RecordingGroups recordings)
        {
            var titles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var group in recordings.Groups)
                foreach (Track track in group.Tracks)
                {
                    string title = NormalizeName(SafeTitle(track.Title));
                    HashSet<string> members;
                    if (!titles.TryGetValue(title, out members)) { members = new HashSet<string>(StringComparer.Ordinal); titles.Add(title, members); }
                    members.Add(group.Key);
                }
            var collisions = new HashSet<string>(titles.Values.Where(x => x.Count > 1).SelectMany(x => x), StringComparer.Ordinal);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in recordings.Groups)
                names.Add(group.Key, ComposeName(group.Representative.Title, collisions.Contains(group.Key) ? ArtistLabel(group.Representative) : null));
            var albumNeeded = new HashSet<string>(names.GroupBy(x => NormalizeName(x.Value)).Where(x => x.Count() > 1).SelectMany(x => x.Select(y => y.Key)), StringComparer.Ordinal);
            foreach (var group in recordings.Groups)
                if (albumNeeded.Contains(group.Key) && !String.IsNullOrWhiteSpace(group.Representative.Album))
                    names[group.Key] = ComposeName(group.Representative.Title, ArtistLabel(group.Representative) + " - " + group.Representative.Album);
            return names;
        }

        private static string ArtistLabel(Track track)
        { return String.IsNullOrWhiteSpace(track.PrimaryArtist) ? (String.IsNullOrWhiteSpace(track.Artist) ? "Unknown artist" : track.Artist) : track.PrimaryArtist; }

        private static string NormalizeName(string text)
        { return Regex.Replace((text ?? "").Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ").ToLowerInvariant(); }

        private static string ComposeName(string title, string qualifier)
        {
            string name = SafeTitle(title);
            if (String.IsNullOrEmpty(qualifier)) return name;
            string label = SafeTitle(qualifier);
            if (label.Length > 100) label = Truncate(label, 100);
            string ending = " (" + label + ")";
            return Truncate(name, 150 - ending.Length) + ending;
        }

        private static string Truncate(string value, int length)
        { return value.Length <= length ? value : value.Substring(0, Char.IsHighSurrogate(value[length - 1]) ? length - 1 : length).TrimEnd(' ', '.'); }

        private static string Relative(string root, string path)
        { return "./" + path.Substring(root.Length + 1).Replace('\\', '/'); }

        private static string SafePath(string root, string value)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new IOException("An indexed path is empty.");
            string path = Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root, value));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("An indexed path leaves the playlist folder.");
            string cursor = path;
            while (cursor.Length >= root.Length)
            {
                if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked files and folders are not renamed.");
                if (String.Equals(cursor, root, StringComparison.OrdinalIgnoreCase)) break;
                cursor = Path.GetDirectoryName(cursor);
                if (cursor == null) break;
            }
            return path;
        }

        private static string SafeTitle(string title)
        {
            var result = new StringBuilder();
            foreach (char c in (title ?? "")) result.Append(c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0 ? '_' : c);
            string name = result.ToString().Trim(' ', '.');
            if (name.Length > 150) name = name.Substring(0, Char.IsHighSurrogate(name[149]) ? 149 : 150).TrimEnd(' ', '.');
            if (name.Length == 0) name = "Untitled";
            if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)", RegexOptions.IgnoreCase)) name = "_" + name;
            return name;
        }

        private static string RewritePlaylist(string root, string text, Dictionary<string, string> mapping)
        {
            return Regex.Replace(text, @"[^\r\n]+", delegate(Match match)
            {
                string line = match.Value.TrimStart('\uFEFF'), destination;
                if (line.StartsWith("#", StringComparison.Ordinal)) return match.Value;
                try
                {
                    if (mapping.TryGetValue(SafePath(root, line), out destination))
                        return (match.Value.StartsWith("\uFEFF", StringComparison.Ordinal) ? "\uFEFF" : "") + Relative(root, destination);
                }
                catch (IOException) { }
                catch (ArgumentException) { }
                catch (UnauthorizedAccessException) { }
                return match.Value;
            });
        }

        private static List<List<string>> ReadCsv(string text)
        {
            var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder();
            bool quoted = false, closed = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else { quoted = false; closed = true; } }
                    else field.Append(c);
                }
                else if (c == ',' || c == '\r' || c == '\n')
                {
                    row.Add(field.ToString()); field.Length = 0; closed = false;
                    if (c != ',') { rows.Add(row); row = new List<string>(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
                }
                else if (c == '"' && field.Length == 0 && !closed) quoted = true;
                else { if (closed || c == '"') throw new InvalidDataException("Malformed quoting in download index."); field.Append(c); }
            }
            if (quoted) throw new InvalidDataException("Unclosed quote in download index.");
            if (row.Count > 0 || field.Length > 0 || closed) { row.Add(field.ToString()); rows.Add(row); }
            return rows;
        }

        private static string WriteCsv(List<List<string>> rows)
        { return String.Join("\r\n", rows.Select(row => String.Join(",", row.Select(cell => "\"" + cell.Replace("\"", "\"\"") + "\"")))) + "\r\n"; }
    }
}
