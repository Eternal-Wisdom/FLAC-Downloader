using System;

namespace PlaylistFlac
{
    internal static class QueueFilter
    {
        internal static bool Matches(Track track,string status,string query,string filter)
        {
            query=(query ?? "").Trim();status=status ?? "Ready";
            return MatchesPrepared(track,status,query,filter);
        }
        // Prepare the query once per queue refresh, rather than once per song.
        internal static bool MatchesPrepared(Track track,string status,string query,string filter)
        {
            status=status ?? "Ready";
            if(query.Length>0)
            {
                bool found=(track.Title ?? "").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0 ||
                    (track.Artist ?? "").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0 ||
                    (track.Album ?? "").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;
                // Preserve searches crossing the original field separators.
                if(!found && (query.IndexOf('\n')<0 || String.Join("\n",track.Title,track.Artist,track.Album).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0))return false;
            }
            switch(filter)
            {
                case "Completed":return status=="Downloaded";
                case "Missing":return status=="Failed";
                case "Review":return status.StartsWith("Review",StringComparison.OrdinalIgnoreCase);
                case "Waiting":return status=="Ready" || status.IndexOf("wait",StringComparison.OrdinalIgnoreCase)>=0 || status.IndexOf("queue",StringComparison.OrdinalIgnoreCase)>=0;
                case "Downloading":return status.IndexOf("download",StringComparison.OrdinalIgnoreCase)>=0 && status!="Downloaded";
                default:return true;
            }
        }
    }
}
