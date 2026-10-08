using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PlaylistFlac
{
    internal sealed class SavedTrack
    {
        public Track Track { get; set; }
        public string Path { get; set; }
        public int State { get; set; }
        public int Reason { get; set; }
    }
    internal static class IndexStore
    {
        internal static string Key(Track t)
        { return String.Join("\n", new[] { t.Artist ?? "", t.Album ?? "", t.Title ?? "", Seconds(t).ToString(CultureInfo.InvariantCulture) }).ToLowerInvariant(); }
        internal static int Seconds(Track t) { return t.DurationSeconds > 0 ? (int)t.DurationSeconds : -1; }
        internal static bool Done(SavedTrack t) { return t != null && (t.State == 1 || t.State == 3) && File.Exists(t.Path); }
        internal static Dictionary<string, SavedTrack> Read(string path)
        {
            var result = new Dictionary<string, SavedTrack>();
            if (!File.Exists(path)) return result;
            var rows = CsvPlaylist.ParseRows(File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF'), ',');
            if (rows.Count == 0) throw new InvalidDataException("The download index is empty. It was preserved for recovery.");
            var headers = rows[0];
            string[] required={"filepath","artist","album","title","length","tracktype","state","failurereason"};
            if(headers.Distinct(StringComparer.Ordinal).Count()!=headers.Count || required.Any(h=>!headers.Contains(h)))
                throw new InvalidDataException("The download index has missing or duplicate columns. It was preserved for recovery.");
            foreach (var row in rows.Skip(1))
            {
                if(row.Count==1 && row[0].Length==0)continue;
                if (row.Count != headers.Count) throw new InvalidDataException("The download index has an incomplete row. It was preserved for recovery.");
                Func<string,string> field = name => { int i = headers.IndexOf(name); return i >= 0 && i < row.Count ? row[i] : ""; };
                int seconds, state, reason;
                if (!Int32.TryParse(field("length"), out seconds) || !Int32.TryParse(field("state"), out state) || !Int32.TryParse(field("failurereason"), out reason))
                    throw new InvalidDataException("The download index contains invalid numeric values. It was preserved for recovery.");
                string file = field("filepath");
                if (file.Length > 0) file = Path.GetFullPath(Path.Combine(IndexDirectory(path), file.Replace('/', Path.DirectorySeparatorChar)));
                var value = new SavedTrack { Track = new Track { Title=field("title"), Artist=field("artist"), Album=field("album"), DurationSeconds=seconds }, Path=file, State=state, Reason=reason };
                SavedTrack previous;
                if (!result.TryGetValue(Key(value.Track), out previous) || !Done(previous) || Done(value)) result[Key(value.Track)] = value;
            }
            return result;
        }
        internal static void Save(string folder, Dictionary<string,SavedTrack> index, Playlist playlist, Action<string> committed = null)
        {
            LibraryLayout.Prepare(folder, System.Threading.CancellationToken.None);
            string root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            var text = new StringBuilder("filepath,artist,album,title,length,tracktype,state,failurereason\r\n");
            foreach (var row in index.Values)
            {
                string file = row.Path ?? "";
                if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase)) file = "./" + file.Substring(root.Length).Replace('\\','/');
                text.AppendLine(String.Join(",", new[] { file, row.Track.Artist, row.Track.Album, row.Track.Title, Seconds(row.Track).ToString(CultureInfo.InvariantCulture), "0", row.State.ToString(), row.Reason.ToString() }.Select(Quote)));
            }
            AtomicWrite(LibraryLayout.PathFor(folder,"_index.csv"),text.ToString());
            if(committed!=null)committed("index");
            var m3u = new StringBuilder("#EXTM3U\r\n");
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var track in playlist.Tracks)
            {
                SavedTrack row;
                if (index.TryGetValue(Key(track),out row) && Done(row) && row.Path.StartsWith(root,StringComparison.OrdinalIgnoreCase) && listed.Add(Path.GetFullPath(row.Path)))
                    m3u.AppendLine(row.Path.Substring(root.Length).Replace('\\','/'));
            }
            AtomicWrite(Path.Combine(folder,"playlist.m3u8"),m3u.ToString());
            if(committed!=null)committed("playlist");
        }
        private static string IndexDirectory(string path) {string directory=Path.GetDirectoryName(Path.GetFullPath(path));return Path.GetFileName(directory)==LibraryLayout.Name && Path.GetFileName(path)=="_index.csv" ? Path.GetDirectoryName(directory) : directory;}
        internal static string Quote(string value) { return "\"" + (value ?? "").Replace("\"","\"\"") + "\""; }
        internal static void AtomicWrite(string path,string text)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp,text,new UTF8Encoding(false)); if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
    }
}
