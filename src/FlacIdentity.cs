using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Threading;

namespace PlaylistFlac
{
    // A conservative encoded-audio comparison. This does not decode frames,
    // check their CRCs, or establish the original recording's provenance.
    internal static class FlacIdentity
    {
        internal static string QuickSignature(string path)
        {
            try
            {
                using (var input = Open(path))
                {
                    byte[] info;
                    bool last;
                    if (!ReadInfo(input, CancellationToken.None, out info, out last) || input.Length <= input.Position)
                        return null;
                    var identity = new byte[24];
                    Buffer.BlockCopy(info, 10, identity, 0, identity.Length);
                    return Hex(identity);
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (SecurityException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        internal static string AudioHash(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (var input = Open(path))
                {
                    byte[] info;
                    bool last;
                    if (!ReadInfo(input, ct, out info, out last)) return null;
                    var header = new byte[4];
                    int blocks = 1;
                    while (!last)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (++blocks > 65536 || !ReadExactly(input, header, ct)) return null;
                        int type = header[0] & 127;
                        if (type == 0 || type == 127) return null;
                        last = (header[0] & 128) != 0;
                        int length = (header[1] << 16) | (header[2] << 8) | header[3];
                        if (length > input.Length - input.Position) return null;
                        input.Seek(length, SeekOrigin.Current);
                    }
                    // A FLAC frame needs a header, subframe and trailing CRC.
                    // Sync screening avoids treating metadata-only files as audio;
                    // it is not a substitute for decoding every frame.
                    if (input.Length - input.Position < 8) return null;
                    long audioStart = input.Position;
                    int first = input.ReadByte(), second = input.ReadByte();
                    if (first != 255 || (second & 254) != 248) return null;
                    input.Position = audioStart;
                    using (var hash = SHA256.Create())
                    {
                        // Include only rate/channel/depth/sample count from STREAMINFO;
                        // encoder settings, MD5 presence, tags and artwork may differ.
                        var format = new byte[8];
                        Buffer.BlockCopy(info, 10, format, 0, format.Length);
                        hash.TransformBlock(format, 0, format.Length, format, 0);
                        var buffer = new byte[65536];
                        int count;
                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            count = input.Read(buffer, 0, buffer.Length);
                            if (count == 0) break;
                            hash.TransformBlock(buffer, 0, count, buffer, 0);
                        }
                        ct.ThrowIfCancellationRequested();
                        hash.TransformFinalBlock(new byte[0], 0, 0);
                        return Hex(hash.Hash);
                    }
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (SecurityException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static FileStream Open(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        }

        private static bool ReadInfo(Stream input, CancellationToken ct, out byte[] info, out bool last)
        {
            info = null;
            last = false;
            var prefix = new byte[8];
            if (!ReadExactly(input, prefix, ct) || prefix[0] != 'f' || prefix[1] != 'L' ||
                prefix[2] != 'a' || prefix[3] != 'C' || (prefix[4] & 127) != 0 ||
                prefix[5] != 0 || prefix[6] != 0 || prefix[7] != 34) return false;
            last = (prefix[4] & 128) != 0;
            info = new byte[34];
            if (!ReadExactly(input, info, ct)) return false;
            int minimumBlock = (info[0] << 8) | info[1];
            int maximumBlock = (info[2] << 8) | info[3];
            int minimumFrame = (info[4] << 16) | (info[5] << 8) | info[6];
            int maximumFrame = (info[7] << 16) | (info[8] << 8) | info[9];
            if (minimumBlock < 16 || maximumBlock < minimumBlock ||
                (minimumFrame != 0 && maximumFrame != 0 && minimumFrame > maximumFrame)) return false;
            ulong packed = 0;
            for (int i = 10; i < 18; i++) packed = (packed << 8) | info[i];
            int rate = (int)(packed >> 44);
            int bits = (int)((packed >> 36) & 31) + 1;
            return rate > 0 && bits >= 4;
        }

        private static bool ReadExactly(Stream input, byte[] bytes, CancellationToken ct)
        {
            int position = 0;
            while (position < bytes.Length)
            {
                ct.ThrowIfCancellationRequested();
                int read = input.Read(bytes, position, bytes.Length - position);
                if (read == 0) return false;
                position += read;
            }
            return true;
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
