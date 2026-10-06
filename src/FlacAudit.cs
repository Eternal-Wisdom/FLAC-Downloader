using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;

namespace PlaylistFlac
{
    public sealed class FlacAuditResult
    {
        public int TotalFiles;
        public int ValidHeaders;
        public int InvalidHeaders;
        public string ReportPath;
    }

    public sealed class FlacHeaderResult
    {
        public bool IsValid;
        public string Validation;
        public int SampleRateHz;
        public int BitsPerSample;
        public int Channels;
        public ulong TotalSamples;
        public double? DurationSeconds;
    }

    public static class FlacAudit
    {
        // This checks the first metadata block only. It does not decode audio,
        // verify checksums, or establish the source's lossless provenance.
        public static FlacAuditResult CheckFolder(string directory, CancellationToken ct)
        {
            if (String.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Choose a folder to check.", "directory");
            string root = Path.GetFullPath(directory);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("The selected folder does not exist.");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Choose a real folder rather than a linked folder.");

            ct.ThrowIfCancellationRequested();
            List<string> paths = FindFlacFiles(root, ct);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            FlacAuditResult result = new FlacAuditResult();
            LibraryLayout.Prepare(root,ct);
            result.ReportPath = LibraryLayout.PathFor(root,"FLAC-check.csv");
            string temporaryPath = Path.Combine(root, ".FLAC-check-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (StreamWriter report = new StreamWriter(temporaryPath, false, new UTF8Encoding(true)))
                {
                    report.WriteLine("path,validation,sample_rate_hz,bits,channels,duration_seconds");
                    foreach (string path in paths)
                    {
                        ct.ThrowIfCancellationRequested();
                        FlacHeaderResult header = CheckFile(path, ct);
                        result.TotalFiles++;
                        if (header.IsValid) result.ValidHeaders++;
                        else result.InvalidHeaders++;
                        report.Write(CsvCell(path));
                        report.Write(",");
                        report.Write(CsvCell(header.Validation));
                        report.Write(",");
                        if (header.IsValid)
                        {
                            report.Write(header.SampleRateHz.ToString(CultureInfo.InvariantCulture));
                            report.Write(",");
                            report.Write(header.BitsPerSample.ToString(CultureInfo.InvariantCulture));
                            report.Write(",");
                            report.Write(header.Channels.ToString(CultureInfo.InvariantCulture));
                            report.Write(",");
                            if (header.DurationSeconds.HasValue)
                                report.Write(header.DurationSeconds.Value.ToString("0.######", CultureInfo.InvariantCulture));
                        }
                        else report.Write(",,,");
                        report.WriteLine();
                    }
                }
                ct.ThrowIfCancellationRequested();
                File.Copy(temporaryPath, result.ReportPath, true);
                return result;
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static FlacHeaderResult CheckFile(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] prefix = new byte[8];
                    if (!ReadExactly(input, prefix, ct)) return Invalid("Truncated FLAC file header");
                    if (prefix[0] != 'f' || prefix[1] != 'L' || prefix[2] != 'a' || prefix[3] != 'C')
                        return Invalid("Missing fLaC marker (not a native FLAC header)");
                    if ((prefix[4] & 0x7f) != 0)
                        return Invalid("First metadata block is not STREAMINFO");
                    int length = (prefix[5] << 16) | (prefix[6] << 8) | prefix[7];
                    if (length != 34) return Invalid("STREAMINFO length must be exactly 34 bytes");
                    byte[] info = new byte[34];
                    if (!ReadExactly(input, info, ct)) return Invalid("Truncated STREAMINFO block");
                    int minimumBlock = (info[0] << 8) | info[1];
                    int maximumBlock = (info[2] << 8) | info[3];
                    if (minimumBlock < 16 || maximumBlock < 16 || minimumBlock > maximumBlock)
                        return Invalid("Invalid STREAMINFO minimum/maximum block sizes");
                    int minimumFrame = (info[4] << 16) | (info[5] << 8) | info[6];
                    int maximumFrame = (info[7] << 16) | (info[8] << 8) | info[9];
                    if (minimumFrame != 0 && maximumFrame != 0 && minimumFrame > maximumFrame)
                        return Invalid("Invalid STREAMINFO minimum/maximum frame sizes");

                    // RFC 9639 section 8.2: 20-bit rate, 3-bit channel count - 1,
                    // 5-bit depth - 1, then a 36-bit interchannel sample count.
                    ulong packed = 0;
                    for (int i = 10; i < 18; i++) packed = (packed << 8) | info[i];
                    int sampleRate = (int)(packed >> 44);
                    int channels = (int)((packed >> 41) & 7) + 1;
                    int bits = (int)((packed >> 36) & 31) + 1;
                    ulong samples = packed & 0xfffffffffUL;
                    if (sampleRate == 0) return Invalid("Zero sample rate is not valid for audio");
                    if (bits < 4) return Invalid("FLAC bit depth must be between 4 and 32");
                    return new FlacHeaderResult
                    {
                        IsValid = true,
                        Validation = "Valid STREAMINFO header; audio not decoded",
                        SampleRateHz = sampleRate,
                        BitsPerSample = bits,
                        Channels = channels,
                        TotalSamples = samples,
                        DurationSeconds = samples == 0 ? (double?)null : (double)samples / sampleRate
                    };
                }
            }
            catch (IOException ex) { return Invalid("Cannot read file: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { return Invalid("Cannot read file: " + ex.Message); }
            catch (SecurityException ex) { return Invalid("Cannot read file: " + ex.Message); }
        }

        private static List<string> FindFlacFiles(string root, CancellationToken ct)
        {
            List<string> paths = new List<string>();
            Stack<string> directories = new Stack<string>();
            directories.Push(root);
            while (directories.Count != 0)
            {
                ct.ThrowIfCancellationRequested();
                string current = directories.Pop();
                // Recheck immediately before enumeration in case a directory changed.
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string path in Directory.GetFiles(current))
                {
                    ct.ThrowIfCancellationRequested();
                    if (String.Equals(Path.GetExtension(path), ".flac", StringComparison.OrdinalIgnoreCase))
                        paths.Add(path);
                }
                foreach (string child in Directory.GetDirectories(current))
                {
                    ct.ThrowIfCancellationRequested();
                    if (Path.GetFileName(child).StartsWith(".duplicates-backup-", StringComparison.OrdinalIgnoreCase) || (String.Equals(Path.GetFileName(child),RecoveryFolders.Name,StringComparison.OrdinalIgnoreCase) || String.Equals(Path.GetFileName(child),LibraryLayout.Name,StringComparison.OrdinalIgnoreCase))) continue;
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        directories.Push(child);
                }
            }
            return paths;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, CancellationToken ct)
        {
            int count = 0;
            while (count < buffer.Length)
            {
                ct.ThrowIfCancellationRequested();
                int read = stream.Read(buffer, count, buffer.Length - count);
                if (read == 0) return false;
                count += read;
            }
            return true;
        }

        private static FlacHeaderResult Invalid(string message)
        {
            return new FlacHeaderResult { IsValid = false, Validation = "Invalid header: " + message };
        }

        private static string CsvCell(string value)
        {
            value = value ?? String.Empty;
            string trimmed = value.TrimStart();
            if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0)
                value = "'" + value;
            if (value.Length > 0 && (value[0] == '\t' || value[0] == '\r' || value[0] == '\n'))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
