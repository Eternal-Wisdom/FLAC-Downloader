using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace PlaylistFlac
{
    public static class AuditTests
    {
        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "PlaylistFlac-AuditTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string album = Path.Combine(root, "Album, one");
                Directory.CreateDirectory(album);
                string validPath = Path.Combine(album, "Unicode-é.FLAC");
                byte[] original = MakeHeader(44100, 16, 2, 441000);
                File.WriteAllBytes(validPath, original);
                FlacHeaderResult valid = FlacAudit.CheckFile(validPath, CancellationToken.None);
                Assert(valid.IsValid, "A well-formed header should pass.");
                Assert(valid.SampleRateHz == 44100 && valid.BitsPerSample == 16 && valid.Channels == 2,
                    "Sample metadata must be unpacked correctly.");
                Assert(valid.TotalSamples == 441000 && valid.DurationSeconds == 10.0,
                    "Sample count and duration must be correct.");

                string highPath = Path.Combine(root, "high-range.flac");
                File.WriteAllBytes(highPath, MakeHeader(1048575, 32, 8, 0xfffffffffUL));
                FlacHeaderResult high = FlacAudit.CheckFile(highPath, CancellationToken.None);
                Assert(high.IsValid && high.SampleRateHz == 1048575 && high.BitsPerSample == 32 &&
                    high.Channels == 8 && high.TotalSamples == 0xfffffffffUL,
                    "All 36 sample-count bits and supported metadata maxima must survive parsing.");

                string unknownPath = Path.Combine(root, "unknown-length.flac");
                File.WriteAllBytes(unknownPath, MakeHeader(48000, 24, 1, 0));
                FlacHeaderResult unknown = FlacAudit.CheckFile(unknownPath, CancellationToken.None);
                Assert(unknown.IsValid && !unknown.DurationSeconds.HasValue,
                    "Unknown total samples must produce an unknown duration, not zero seconds.");

                WriteInvalid(root, "renamed-mp3.flac", new byte[] { 73, 68, 51, 4, 0, 0, 0, 0 });
                WriteInvalid(root, "short-marker.flac", new byte[] { 102, 76, 97 });
                byte[] truncated = new byte[41];
                Buffer.BlockCopy(original, 0, truncated, 0, truncated.Length);
                WriteInvalid(root, "truncated-streaminfo.flac", truncated);
                byte[] shortLength = (byte[])original.Clone();
                shortLength[7] = 33;
                WriteInvalid(root, "wrong-length-33.flac", shortLength);
                byte[] longLength = (byte[])original.Clone();
                longLength[7] = 35;
                WriteInvalid(root, "wrong-length-35.flac", longLength);
                byte[] wrongType = (byte[])original.Clone();
                wrongType[4] = 0x84;
                WriteInvalid(root, "wrong-block-type.flac", wrongType);
                byte[] smallBlock = (byte[])original.Clone();
                smallBlock[8] = 0;
                smallBlock[9] = 15;
                WriteInvalid(root, "minimum-block-15.flac", smallBlock);
                byte[] reversedBlocks = (byte[])original.Clone();
                reversedBlocks[10] = 0;
                reversedBlocks[11] = 16;
                WriteInvalid(root, "reversed-blocks.flac", reversedBlocks);
                byte[] reversedFrames = (byte[])original.Clone();
                reversedFrames[14] = 20;
                reversedFrames[17] = 10;
                WriteInvalid(root, "reversed-frames.flac", reversedFrames);
                WriteInvalid(root, "zero-audio-rate.flac", MakeHeader(0, 16, 2, 100));
                WriteInvalid(root, "depth-three.flac", MakeHeader(44100, 3, 2, 100));
                Assert(!FlacAudit.CheckFile(Path.Combine(root, "missing.flac"), CancellationToken.None).IsValid,
                    "An unreadable file must produce an invalid result rather than stop the audit.");

                File.WriteAllText(Path.Combine(root, "ignored.mp3"), "not part of this audit");
                string recovery=Path.Combine(root,".duplicates-backup-test");Directory.CreateDirectory(recovery);
                File.WriteAllBytes(Path.Combine(recovery,"recoverable-copy.flac"),original);
                string groupedRecovery=RecoveryFolders.Prepare(root);
                File.WriteAllBytes(Path.Combine(groupedRecovery,"archived-copy.flac"),original);
                FlacAuditResult result = FlacAudit.CheckFolder(root, CancellationToken.None);
                Assert(result.TotalFiles == 14 && result.ValidHeaders == 3 && result.InvalidHeaders == 11,
                    "Recursive scanning must count valid, invalid and uppercase FLAC extensions correctly.");
                string csv = File.ReadAllText(result.ReportPath);
                Assert(csv.StartsWith("path,validation,sample_rate_hz,bits,channels,duration_seconds", StringComparison.Ordinal),
                    "The report must expose the documented columns.");
                Assert(csv.Contains("\"" + validPath + "\"") && csv.Contains(",44100,16,2,10"),
                    "CSV must quote paths containing commas, preserve Unicode and include metadata.");
                Assert(csv.Contains("audio not decoded"), "Passing rows must state the limited scope of the check.");
                Assert(File.ReadAllBytes(validPath).Length == original.Length &&
                    Convert.ToBase64String(File.ReadAllBytes(validPath)) == Convert.ToBase64String(original),
                    "The audit must never modify audio files.");

                string previousReport = csv;
                using (CancellationTokenSource cancel = new CancellationTokenSource())
                {
                    cancel.Cancel();
                    bool canceled = false;
                    try { FlacAudit.CheckFolder(root, cancel.Token); }
                    catch (OperationCanceledException) { canceled = true; }
                    Assert(canceled && File.ReadAllText(result.ReportPath) == previousReport,
                        "A canceled audit must preserve the existing report.");
                }
                CultureInfo savedCulture = Thread.CurrentThread.CurrentCulture;
                try
                {
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                    File.WriteAllBytes(unknownPath, MakeHeader(48000, 24, 1, 24000));
                    FlacAudit.CheckFolder(root, CancellationToken.None);
                    Assert(File.ReadAllText(result.ReportPath).Contains(",48000,24,1,0.5"),
                        "Report numbers must use invariant decimal separators.");
                }
                finally { Thread.CurrentThread.CurrentCulture = savedCulture; }
            }
            finally
            {
                string absoluteRoot = Path.GetFullPath(root);
                string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absoluteRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(absoluteRoot).StartsWith("PlaylistFlac-AuditTests-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to remove an unexpected test directory.");
                Directory.Delete(absoluteRoot, true);
            }
        }

        private static void WriteInvalid(string root, string name, byte[] contents)
        {
            string path = Path.Combine(root, name);
            File.WriteAllBytes(path, contents);
            Assert(!FlacAudit.CheckFile(path, CancellationToken.None).IsValid,
                "Malformed input should fail: " + name);
        }

        private static byte[] MakeHeader(int rate, int bits, int channels, ulong samples)
        {
            byte[] bytes = new byte[42];
            bytes[0] = 102; bytes[1] = 76; bytes[2] = 97; bytes[3] = 67;
            bytes[4] = 0x80;
            bytes[7] = 34;
            bytes[8] = 0x10;
            bytes[10] = 0x10;
            ulong packed = ((ulong)rate << 44) | ((ulong)(channels - 1) << 41) |
                ((ulong)(bits - 1) << 36) | samples;
            for (int i = 25; i >= 18; i--)
            {
                bytes[i] = (byte)(packed & 255);
                packed >>= 8;
            }
            return bytes;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Audit test failed: " + message);
        }
    }
}
