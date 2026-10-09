using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaylistFlac
{
    // Export only confirmed failures, once per recording. Ready/review rows are not failures.
    internal static class MissingTrackExport
    {
        internal static Playlist Select(Playlist playlist, IDictionary<string,string> statuses)
        {
            if(playlist==null || statuses==null)throw new ArgumentNullException();
            var selected=new Playlist {Name="Missing songs",Source=""};
            foreach(var group in RecordingGroups.Build(playlist.Tracks).Groups)
            {
                var labels=group.Tracks.Select(t=> {string value;return statuses.TryGetValue(IndexStore.Key(t),out value)?value:"Ready";}).ToList();
                if(labels.Contains("Downloaded") || labels.Contains("Review versions") || !labels.Contains("Failed"))continue;
                selected.Tracks.Add(group.Representative);
            }
            return selected;
        }
    }
}
