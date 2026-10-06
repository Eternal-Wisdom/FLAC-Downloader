using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    internal sealed class LibrarySnapshot
    {
        internal ReadOnlyDictionary<string, string> Statuses { get; private set; }
        internal int Completed { get; private set; }
        internal int Failed { get; private set; }
        internal int Ready { get; private set; }
        internal int Total { get { return Completed + Failed + Ready; } }

        internal LibrarySnapshot(Dictionary<string, string> statuses, Playlist playlist)
        {
            Statuses = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(statuses));
            foreach (Track track in playlist.Tracks)
            {
                string status = StatusFor(track);
                if (status == "Downloaded") Completed++;
                else if (status == "Failed") Failed++;
                else Ready++;
            }
        }

        internal string StatusFor(Track track)
        {
            string status;
            return Statuses.TryGetValue(IndexStore.Key(track), out status) ? status : "Ready";
        }
    }

    internal static class LibraryStatus
    {
        // A snapshot never changes files or retains references to mutable SavedTrack records.
        // Counts include duplicate playlist rows, matching the number displayed in the UI.
        internal static LibrarySnapshot Read(string folder, Playlist playlist, bool includeActive = true)
        {
            if (playlist == null) throw new ArgumentNullException("playlist");
            var statuses = new Dictionary<string, string>();
            foreach (Track track in playlist.Tracks) statuses[IndexStore.Key(track)] = "Ready";
            string root;
            try { root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception ex) { if (!Recoverable(ex)) throw; return new LibrarySnapshot(statuses, playlist); }

            Dictionary<string, SavedTrack> canonical = ReadIndex(LibraryLayout.PathFor(root,"_index.csv"));
            foreach (Track track in playlist.Tracks)
            {
                string key = IndexStore.Key(track); SavedTrack saved;
                if (canonical.TryGetValue(key, out saved)) statuses[key] = Label(root, saved);
            }
            if (includeActive) MergeActive(root, statuses);
            var recordings=RecordingGroups.Build(playlist.Tracks);
            foreach(var group in recordings.Groups)
            {
                bool ambiguous=false;
                foreach(var track in group.Tracks) if(recordings.GroupForKey(IndexStore.Key(track))==null) {ambiguous=true;break;}
                if(ambiguous) { foreach(var track in group.Tracks) statuses[IndexStore.Key(track)]="Review versions"; continue; }
                bool downloaded=false;
                foreach(var track in group.Tracks) if(statuses[IndexStore.Key(track)]=="Downloaded") { downloaded=true;break; }
                if(downloaded) foreach(var track in group.Tracks) statuses[IndexStore.Key(track)]="Downloaded";
            }
            return new LibrarySnapshot(statuses, playlist);
        }

        private static void MergeActive(string root, Dictionary<string, string> statuses)
        {
            string mapPath = Path.Combine(root, ".search", "active-map.json");
            if (!File.Exists(mapPath)) return;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
                var jobs = serializer.Deserialize<List<StatusMapping>>(File.ReadAllText(mapPath));
                if (jobs == null) return;
                var ambiguousQueries = new HashSet<string>(jobs.Where(j => j != null && j.Original != null && j.Query != null)
                    .GroupBy(j => IndexStore.Key(j.Query)).Where(g => g.Select(j => IndexStore.Key(j.Original)).Distinct().Skip(1).Any()).Select(g => g.Key));
                var active = ReadIndex(LibraryLayout.PathFor(root,".active-search-index.csv"));
                foreach (StatusMapping job in jobs)
                {
                    if (job == null || job.Original == null || job.Query == null) continue;
                    if (ambiguousQueries.Contains(IndexStore.Key(job.Query))) continue;
                    string key = IndexStore.Key(job.Original), previous;
                    if (!statuses.TryGetValue(key, out previous) || previous == "Downloaded") continue;
                    if (!String.IsNullOrEmpty(job.OriginalKey) && !String.Equals(job.OriginalKey, key, StringComparison.Ordinal)) continue;
                    if (!String.IsNullOrEmpty(job.Owner) && !String.Equals(Path.GetFullPath(job.Owner).TrimEnd('\\', '/'), root, StringComparison.OrdinalIgnoreCase)) continue;
                    SavedTrack saved;
                    if (!active.TryGetValue(IndexStore.Key(job.Query), out saved)) continue;
                    // A raw success is useful only while its completed file is present.
                    // A malformed or interrupted raw write must not clear canonical failure history.
                    string label = Label(root, saved);
                    if (label == "Downloaded" || label == "Failed") statuses[key] = label;
                }
            }
            catch (Exception ex) { if (!Recoverable(ex)) throw; }
        }

        private static Dictionary<string, SavedTrack> ReadIndex(string path)
        {
            try { return IndexStore.Read(path); }
            catch (Exception ex) { if (!Recoverable(ex)) throw; return new Dictionary<string, SavedTrack>(); }
        }

        private static string Label(string root, SavedTrack saved)
        {
            if (saved == null) return "Ready";
            if (saved.State == 1 || saved.State == 3)
                return ExistingContainedFile(root, saved.Path) ? "Downloaded" : "Ready";
            return saved.State == 2 || saved.State == 4 ? "Failed" : "Ready";
        }

        private static bool ExistingContainedFile(string root, string file)
        {
            if (String.IsNullOrEmpty(file)) return false;
            try
            {
                string path = Path.GetFullPath(file);
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
                string cursor = path;
                while (cursor != null && cursor.Length >= root.Length)
                {
                    if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return false;
                    if (String.Equals(cursor, root, StringComparison.OrdinalIgnoreCase)) break;
                    cursor = Path.GetDirectoryName(cursor);
                }
                return true;
            }
            catch (Exception ex) { if (!Recoverable(ex)) throw; return false; }
        }

        private static bool Recoverable(Exception ex)
        { return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is SecurityException; }

        private sealed class StatusMapping
        {
            public Track Original { get; set; }
            public Track Query { get; set; }
            public string OriginalKey { get; set; }
            public string Owner { get; set; }
            public StatusMapping() { }
        }
    }
}
