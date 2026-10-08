using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    internal sealed class RetryEntry
    {
        public string Key { get; set; }
        public Track Track { get; set; }
        public int Attempts { get; set; }
        public DateTime NextUtc { get; set; }
        public bool Paused { get; set; }
        public string LastReason { get; set; }
    }

    internal sealed class RetryList
    {
        private readonly string path;
        private readonly Dictionary<string,RetryEntry> entries = new Dictionary<string,RetryEntry>();
        internal int Count { get { return entries.Count; } }
        internal RetryList(string stateDirectory, string folder, Playlist playlist)
        {
            string identity = Path.GetFullPath(folder).TrimEnd('\\').ToUpperInvariant()+"\n"+playlist.Source;
            string id;
            using(var sha=SHA256.Create()) id=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").ToLowerInvariant();
            path=Path.Combine(stateDirectory,"retry-lists",id+".json");
            if(!File.Exists(path)) return;
            // A corrupt queue must stop automatic work rather than silently erase history.
            var saved=ReadValidated(path);
            var valid=new HashSet<string>(playlist.Tracks.Select(IndexStore.Key));
            foreach(var entry in saved)
                if(valid.Contains(entry.Key))
                { entry.Attempts=Math.Max(0,Math.Min(20,entry.Attempts)); entries[entry.Key]=entry; }
        }
        internal static List<RetryEntry> ReadValidated(string path)
        {
            var saved=new JavaScriptSerializer { MaxJsonLength=32*1024*1024 }.Deserialize<List<RetryEntry>>(File.ReadAllText(path));
            if(saved==null)throw new FormatException("The saved retry list is invalid. It was preserved; automatic retries are paused.");
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in saved)
            {
                if(entry==null || entry.Track==null || entry.Key!=IndexStore.Key(entry.Track) ||
                   String.IsNullOrWhiteSpace(entry.Track.Title) || String.IsNullOrWhiteSpace(entry.Track.Artist) ||
                   entry.NextUtc==DateTime.MinValue || !keys.Add(entry.Key))
                    throw new FormatException("The saved retry list contains incomplete or conflicting entries. It was preserved; automatic retries are paused.");
            }
            return saved;
        }
        internal HashSet<string> Due(DateTime utc, int limit)
        {
            if(limit<1 || limit>20)throw new ArgumentOutOfRangeException("limit");
            return new HashSet<string>(entries.Values.Where(e=>!e.Paused && e.NextUtc<=utc).OrderBy(e=>e.NextUtc).ThenBy(e=>e.Key,StringComparer.Ordinal).Take(limit).Select(e=>e.Key));
        }
        internal void Update(Playlist playlist, IDictionary<string,string> statuses, ISet<string> attempted, DateTime utc, IDictionary<string,string> reasons=null)
        {
            foreach(var track in playlist.Tracks.GroupBy(IndexStore.Key).Select(g=>g.First()))
            {
                string key=IndexStore.Key(track), state;
                if(!statuses.TryGetValue(key,out state))continue;
                if(state=="Downloaded" || state=="Review versions") {entries.Remove(key); continue;}
                if(state!="Failed")continue;
                RetryEntry entry;
                if(!entries.TryGetValue(key,out entry))
                { entry=new RetryEntry {Key=key,Track=track,NextUtc=utc.AddMinutes(15)};entries.Add(key,entry); }
                string reason;if(reasons!=null && reasons.TryGetValue(key,out reason))entry.LastReason=reason;
                if(attempted!=null && attempted.Contains(key))
                { entry.Attempts=Math.Min(20,entry.Attempts+1);entry.NextUtc=utc.AddMinutes(Math.Min(360,15*Math.Pow(2,Math.Min(5,entry.Attempts-1)))); }
            }
            Save();
        }
        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temp,new JavaScriptSerializer {MaxJsonLength=32*1024*1024}.Serialize(entries.Values.OrderBy(e=>e.Key,StringComparer.Ordinal).ToList()),new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
