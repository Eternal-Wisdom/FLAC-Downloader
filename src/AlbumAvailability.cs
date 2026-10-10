using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PlaylistFlac
{
    internal static class AlbumAvailability
    {
        internal static string Describe(Playlist playlist,IDictionary<string,string> states)
        {
            var text=new StringBuilder("Counts cover imported songs only, not the full published album. Source album metadata does not prove that downloaded files share one master or edition.\r\n\r\n");
            foreach(var album in playlist.Tracks.GroupBy(t=>new {Artist=t.PrimaryArtist ?? "",Album=t.Album ?? ""}).OrderBy(g=>g.Key.Artist).ThenBy(g=>g.Key.Album))
            {
                var entries=album.GroupBy(IndexStore.Key).Select(g=>g.First()).ToList();
                int completed=0,missing=0,review=0;
                foreach(var track in entries) {string state;states.TryGetValue(IndexStore.Key(track),out state);if(state=="Downloaded")completed++;else if(state=="Failed")missing++;else if((state ?? "").StartsWith("Review",StringComparison.OrdinalIgnoreCase))review++;}
                text.AppendLine(album.Key.Artist+" — "+(album.Key.Album.Length==0 ? "Unknown album" : album.Key.Album));
                text.AppendLine(completed+" / "+entries.Count+" downloaded; "+missing+" missing; "+review+" need review");
                foreach(var track in entries.OrderBy(t=>t.DiscNumber>0?t.DiscNumber:Int32.MaxValue).ThenBy(t=>t.TrackNumber>0?t.TrackNumber:Int32.MaxValue)) {
                    string state;states.TryGetValue(IndexStore.Key(track),out state);
                    text.AppendLine("  "+(track.TrackNumber>0 ? (track.DiscNumber>0?track.DiscNumber.ToString():"?")+"."+track.TrackNumber+"  " : "")+track.Title+" — "+(state ?? "Ready"));
                }
                text.AppendLine();
            }
            return text.ToString();
        }
    }
}
