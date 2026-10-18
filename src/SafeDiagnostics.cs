using System;
using System.Globalization;
using System.Text;

namespace PlaylistFlac
{
    internal static class SafeDiagnostics
    {
        // Accept only counts, flags and a normalized policy. Never consume logs, paths or account settings.
        internal static string Create(int total,int downloaded,int unavailable,int review,bool enginePresent,int parallel,string profile,bool strict,bool busy)
        {
            var text=new StringBuilder("FLAC-Downloader diagnostics\r\nMade using AI (OpenAI Codex)\r\n");
            text.AppendLine("App version: "+typeof(SafeDiagnostics).Assembly.GetName().Version);
            text.AppendLine("Windows version: "+Environment.OSVersion.Version);
            text.AppendLine("64-bit process: "+Environment.Is64BitProcess);
            text.AppendLine("CLR version: "+Environment.Version);
            text.AppendLine("Engine file present: "+enginePresent);
            text.AppendLine("Concurrent jobs: "+(parallel==8 || parallel==20 || parallel==32 ? parallel.ToString(CultureInfo.InvariantCulture) : "Unknown"));
            text.AppendLine("Quality policy: "+DownloadTuning.NormalizeQuality(profile));
            text.AppendLine("Strict matching: "+strict);text.AppendLine("Operation running: "+busy);
            text.AppendLine("Recording counts: total="+Math.Max(0,total)+", downloaded="+Math.Max(0,downloaded)+", unavailable="+Math.Max(0,unavailable)+", review="+Math.Max(0,review));
            text.AppendLine("Excluded: account details, Spotify credentials, song names, local paths, peer names, IP addresses and activity logs.");
            return text.ToString();
        }
    }
}
