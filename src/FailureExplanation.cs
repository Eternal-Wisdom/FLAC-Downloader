using System;

namespace PlaylistFlac
{
    // These are the reasons exposed by the pinned engine. Do not infer individual rejected filters.
    internal static class FailureExplanation
    {
        internal static string Describe(string state)
        {
            string reason=(state ?? "").Trim();int colon=reason.IndexOf(':');
            if(colon>=0)reason=reason.Substring(colon+1).Trim();
            // A bare number can be a terminal state, not a failure-reason enum.
            int numeric;if(colon<0 && Int32.TryParse(reason,out numeric))reason="";
            switch(reason)
            {
                case "InvalidSearchString": case "1": return "The engine could not form a valid search. Check the song title and artist.";
                case "OutOfDownloadRetries": case "2": return "The engine exhausted its transfer retries. Try again when more peers are available.";
                case "AllDownloadsFailed": case "4": return "All attempted transfers failed. Peers may be offline, busy, or unreachable.";
                case "ExtractionFailed": case "6": return "The engine could not read the supplied tracklist.";
                case "Cancelled": case "7": return "The job was cancelled. Completed files are kept.";
                case "ChildJobsFailed": case "8": return "The grouped job's transfers did not complete successfully.";
                case "NoSearchResults": case "9": return "Soulseek returned no search results for this query.";
                case "NoMatchingResults": case "10": return "Results were found, but none met the active matching rules. The engine does not identify each rejected rule here.";
                default: return "The track is unavailable; the engine did not report a more specific reason.";
            }
        }
    }
}
