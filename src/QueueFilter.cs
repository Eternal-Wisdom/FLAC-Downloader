using System;

namespace PlaylistFlac
{
    internal static class QueueFilter
    {
        internal static bool Matches(Track track,string status,string query,string filter)
        {
            query=(query ?? "").Trim();status=status ?? "Ready";
            if(query.Length>0 && String.Join("\n",track.Title,track.Artist,track.Album).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)return false;
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
