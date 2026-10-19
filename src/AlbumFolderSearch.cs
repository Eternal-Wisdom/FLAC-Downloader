using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    // Discovery results stay in memory; direct links exist only in the private active input file.
    // Never match by track count alone.
    internal sealed class AlbumFileChoice
    {
        internal Track Track;
        internal string RemotePath, Link;
    }
    internal sealed class AlbumFolderChoice
    {
        internal List<AlbumFileChoice> Files=new List<AlbumFileChoice>();
        internal int Coverage;
        internal double Speed, Quality;
        internal bool FreeSlot;
    }
    internal static class AlbumFolderSearch
    {
        internal static void MapProgress(AlbumFolderChoice choice,EngineProgress update)
        {
            var found=choice.Files.Where(f=>String.Equals(f.RemotePath,update.SourceFilename,StringComparison.OrdinalIgnoreCase)).ToList();
            if(found.Count==0 && !String.IsNullOrEmpty(update.Title))
                found=choice.Files.Where(f=>String.Equals(FileTitle(f.RemotePath),update.Title,StringComparison.OrdinalIgnoreCase)).ToList();
            if(found.Count==1) {update.Title=found[0].Track.Title;update.Artist=found[0].Track.Artist;update.TrackKey=IndexStore.Key(found[0].Track);}
            else {update.Title="";update.Artist="";update.TrackKey="";}
        }
        private static string FileTitle(string path)
        {string name=(path ?? "").Replace('/','\\').Split('\\').Last();return name.EndsWith(".flac",StringComparison.OrdinalIgnoreCase)?name.Substring(0,name.Length-5):name;}
        internal static bool Applies(Playlist source)
        {
            if(source==null || source.Tracks.Count<2 || !SpotifyImporter.IsAlbumLink(source.Source))return false;
            try {SpotifyImporter.ParseAlbumId(source.Source);}catch(FormatException){return false;}
            return source.Tracks.All(t=>!String.IsNullOrWhiteSpace(t.Album)) && source.Tracks.Select(t=>t.Album).Distinct(StringComparer.OrdinalIgnoreCase).Count()==1;
        }
        internal static string KeyText(string value)
        {try{return Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();}catch(ArgumentException){return "";}}
        private static bool Has(string container,string value)
        {string token=KeyText(value);return token.Length>0 && (" "+KeyText(container)+" ").Contains(" "+token+" ");}
        private static string Text(Dictionary<string,object> obj,string key)
        {object v;return obj!=null && obj.TryGetValue(key,out v)?Convert.ToString(v,CultureInfo.InvariantCulture):"";}
        private static double Number(Dictionary<string,object> obj,string key)
        {double n;return Double.TryParse(Text(obj,key),NumberStyles.Float,CultureInfo.InvariantCulture,out n) && !Double.IsNaN(n) && !Double.IsInfinity(n) && n>0?n:0;}
        internal static AlbumFolderChoice Select(string json,List<Track> all,List<Track> pending,DownloadTuning tuning)
        {
            if(String.IsNullOrEmpty(json) || json.Length>8*1024*1024)return null;
            object[] folders;
            try {folders=new JavaScriptSerializer{MaxJsonLength=8*1024*1024,RecursionLimit=30}.DeserializeObject(json) as object[];}
            catch(ArgumentException){return null;}
            if(folders==null || folders.Length>1000)return null;
            var choices=new List<AlbumFolderChoice>();
            var pendingKeys=new HashSet<string>(pending.Select(IndexStore.Key));
            foreach(var item in folders) {
                var entry=item as Dictionary<string,object>;object u,f;
                if(entry==null || !entry.TryGetValue("User",out u) || !entry.TryGetValue("Files",out f))continue;
                var user=u as Dictionary<string,object>;var files=f as object[];string peer=Text(user,"Username");
                if(files==null || files.Length>5000 || String.IsNullOrWhiteSpace(peer) || peer.Any(Char.IsSurrogate) || peer.Any(Char.IsControl) || peer.IndexOfAny(new[]{'/', '\\'})>=0)continue;
                var matches=new Dictionary<string,List<AlbumFileChoice>>();double quality=Double.MaxValue;
                foreach(var file in files) {
                    var row=file as Dictionary<string,object>;string path=Text(row,"Filename");
                    string[] parts=path.Replace('/','\\').Split('\\');
                    if(parts.Length<2 || parts.Any(p=>p==".." || p=="." || p.IndexOfAny(new[]{'\r','\n','\0'})>=0) || !path.EndsWith(".flac",StringComparison.OrdinalIgnoreCase) || Number(row,"Size")==0)continue;
                    string directory=String.Join("\\",parts.Take(parts.Length-1));
                    string originalStem=FileTitle(path);
                    string stem=Regex.Replace(originalStem,@"^\s*(?:\d{1,2}[-.]\s*)?\d{1,3}\s*[-._ ]+","");
                    double length=Number(row,"Length");
                    var eligible=all.Where(t=>t.DurationSeconds>0 && length>0 && Math.Abs(length-t.DurationSeconds)<=5 && Has(directory,t.Album) && Has(directory,t.PrimaryArtist) &&
                        (new[]{stem,originalStem}.Any(s=>KeyText(s)==KeyText(t.Title) || KeyText(s)==KeyText(t.PrimaryArtist+" "+t.Title)))).ToList();
                    if(eligible.Count!=1)continue;
                    var track=eligible[0];string key=IndexStore.Key(track);List<AlbumFileChoice> list;
                    if(!matches.TryGetValue(key,out list))matches[key]=list=new List<AlbumFileChoice>();
                    list.Add(new AlbumFileChoice{Track=track,RemotePath=path,Link="slsk://"+Uri.EscapeDataString(peer)+"/"+String.Join("/",parts.Select(Uri.EscapeDataString))});
                    quality=Math.Min(quality,Number(row,"BitDepth")*1000000+Number(row,"SampleRate"));
                }
                var unique=matches.Where(p=>p.Value.Count==1).ToList();
                var choice=new AlbumFolderChoice{Coverage=unique.Count,Quality=quality==Double.MaxValue?0:quality,Speed=Number(user,"UploadSpeed"),FreeSlot=Text(user,"HasFreeUploadSlot")=="True"};
                choice.Files=unique.Where(p=>pendingKeys.Contains(p.Key)).Select(p=>p.Value[0]).ToList();
                if(choice.Files.Count>0)choices.Add(choice);
            }
            // Coverage first, then the chosen quality/speed policy. No mixing folders in this pass.
            return choices.OrderByDescending(c=>c.Coverage).ThenByDescending(c=>c.Files.Count)
                .ThenByDescending(c=>tuning.QualityProfile=="Prefer high resolution"?c.Quality:0)
                .ThenByDescending(c=>c.FreeSlot).ThenByDescending(c=>c.Speed).FirstOrDefault();
        }
    }
}
