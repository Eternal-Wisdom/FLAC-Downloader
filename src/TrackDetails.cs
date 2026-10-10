using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace PlaylistFlac
{
    // Session-only information. Peer names and remote paths are not exported.
    internal sealed class TrackDetails
    {
        internal static string DescribeLocalHeader(string path)
        {
            var header=FlacAudit.CheckFile(path,CancellationToken.None);
            if(!header.IsValid)return "Local FLAC header: unreadable or invalid. Run Check files for details.\r\n";
            return "Local FLAC header: "+header.BitsPerSample+"-bit / "+(header.SampleRateHz/1000.0).ToString("0.###",CultureInfo.InvariantCulture)+" kHz / "+header.Channels+" channel(s).\r\nHeader facts only; audio frames have not been decoded and lossless origin is not proven.\r\n";
        }
        private string peer, filename, format, latestState;
        private long total, transferred;
        private bool hasProgress;
        private DateTime started,lastProgress;
        private double speed;
        internal void Observe(EngineProgress update)
        {
            if(!String.IsNullOrEmpty(update.Status))latestState=update.Status;
            if(update.Type=="search_start") {started=lastProgress=DateTime.MinValue;speed=0;peer=filename=format=null;total=transferred=0;hasProgress=false;}
            if(update.Type=="download_start") {started=DateTime.UtcNow;lastProgress=DateTime.MinValue;speed=0;peer=filename=format=null;total=transferred=0;hasProgress=false;}
            if(!String.IsNullOrEmpty(update.Peer))peer=update.Peer;
            if(!String.IsNullOrEmpty(update.SourceFilename))filename=update.SourceFilename;
            if(!String.IsNullOrEmpty(update.SourceFormat))format=update.SourceFormat;
            if(update.TotalBytes>0)total=update.TotalBytes;
            if(update.Type=="download_progress") {hasProgress=true;transferred=update.BytesTransferred;speed=update.BytesPerSecond;lastProgress=DateTime.UtcNow;}
        }
        internal string Describe(Track track,string status,string profile,bool strict)
        {
            var text=new StringBuilder();
            text.AppendLine(track.Title+" — "+track.Artist);
            text.AppendLine("Album: "+track.Album);
            if(track.TrackNumber>0)text.AppendLine("Disc / track: "+(track.DiscNumber>0 ? track.DiscNumber.ToString() : "?")+" / "+track.TrackNumber);
            text.AppendLine("Status: "+status);
            if(!String.IsNullOrEmpty(latestState))text.AppendLine("Latest engine state: "+latestState);
            text.AppendLine();
            text.AppendLine("Source peer: "+(peer ?? "Not reported for this track"));
            text.AppendLine("Source file: "+(filename ?? "Not reported for this track"));
            text.AppendLine("Reported format: "+(format ?? "Unknown"));
            text.AppendLine("Reported size: "+(total>0 ? (total/1048576.0).ToString("0.00",CultureInfo.InvariantCulture)+" MiB" : "Unknown"));
            text.AppendLine("Observed track progress: "+(hasProgress ? (transferred/1048576.0).ToString("0.00",CultureInfo.InvariantCulture)+" MiB" : "Not available"));
            text.AppendLine();
            text.AppendLine("Observed speed: "+(lastProgress!=DateTime.MinValue && (DateTime.UtcNow-lastProgress).TotalSeconds<=4 ? TransferTelemetry.FormatRate(speed) : "No recent byte progress"));
            if(started!=DateTime.MinValue)text.AppendLine("Elapsed since source selected: "+(DateTime.UtcNow-started).ToString(@"hh\:mm\:ss"));
            if(lastProgress!=DateTime.MinValue)text.AppendLine("Last byte update: "+Math.Max(0,(int)(DateTime.UtcNow-lastProgress).TotalSeconds)+" seconds ago");
            text.AppendLine("Selection policy: "+DownloadTuning.NormalizeQuality(profile));
            text.AppendLine(strict ? "Artist/title matching, FLAC format, and duration checks are required." : "FLAC format and duration checks are required. Strict artist/title matching is off.");
            text.AppendLine(profile=="Fast FLAC" ? "The engine can accept an early source that meets the preferences." : "The engine compares search results using its quality and peer preferences.");
            if(profile=="Prefer high resolution")text.AppendLine("Higher resolution is preferred; CD-quality FLAC remains a fallback.");
            text.AppendLine();
            text.AppendLine("The engine does not report a complete ranking explanation or queue position here. Some byte updates identify only a job, so they contribute to the overall speed without being assigned to a song. Unknown information is not estimated.");
            text.AppendLine("A FLAC extension or higher bit depth does not prove lossless origin or a better master.");
            return text.ToString();
        }
    }
}
