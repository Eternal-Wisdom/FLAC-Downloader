using System;

namespace PlaylistFlac
{
    public sealed class DownloadTuning
    {
        public int ParallelTracks { get; private set; }
        public string QualityProfile { get; private set; }
        public static DownloadTuning Default { get { return new DownloadTuning(20); } }
        public DownloadTuning(int parallelTracks) : this(parallelTracks, "Prefer high resolution") { }
        public DownloadTuning(int parallelTracks,string qualityProfile) { ParallelTracks = NormalizeParallelTracks(parallelTracks); QualityProfile=NormalizeQuality(qualityProfile); }
        public static string NormalizeQuality(string value) { return value=="Fast FLAC" || value=="Balanced" ? value : "Prefer high resolution"; }
        public static int NormalizeParallelTracks(int value) { return value == 8 || value == 20 || value == 32 ? value : 20; }
        internal const int SearchWindowMilliseconds = 28000;
    }
}
