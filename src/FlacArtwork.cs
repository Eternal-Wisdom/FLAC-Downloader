using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    internal sealed class ArtworkWriteResult
    {
        internal bool Changed;
        internal string BackupMetadataPath, ManifestPath;
    }

    // RFC 9639 sections 8 and 8.8. Audio frames are copied verbatim; this
    // intentionally does not decode, re-encode or validate their individual CRCs.
    internal static class FlacArtwork
    {
        internal const int MaximumImageBytes = 10 * 1024 * 1024;
        private const int MaximumMetadataBytes = 32 * 1024 * 1024;
        private const long MaximumPixels = 16 * 1024 * 1024;

        private sealed class Metadata
        {
            internal byte[] Bytes;
            internal int LastHeader;
            internal bool HasCover;
        }

        private sealed class PictureInfo
        {
            internal string Mime;
            internal int Width, Height, Depth, Colors;
        }

        internal sealed class RecoveryManifest
        {
            public int Version { get; set; }
            public string TargetPath { get; set; }
            public string MetadataFileName { get; set; }
            public string OriginalMetadataHash { get; set; }
            public string AddedMetadataHash { get; set; }
            public string AudioHash { get; set; }
            public long AudioLength { get; set; }
        }

        internal static bool HasFrontCover(string path)
        {
            try
            {
                return ReadFrontCover(path,CancellationToken.None);
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (SecurityException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        internal static bool ReadFrontCover(string path,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string full=SafePath(path);
            using(var input=Open(full))return ReadMetadata(input,ct).HasCover;
        }

        internal static bool IsValidCoverImage(byte[] image)
        {
            try { InspectImage(image); return true; }
            catch (InvalidDataException) { return false; }
        }

        internal static ArtworkWriteResult AddFrontCover(string path, byte[] image, string recoveryDirectory, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string target = SafePath(path);
            string staged = Path.Combine(Path.GetDirectoryName(target), ".artwork-" + Guid.NewGuid().ToString("N") + ".tmp");
            string metadataPath = null, manifestPath = null;
            bool committed = false;
            try
            {
                using (var input = Open(target))
                {
                    Metadata original = ReadMetadata(input, ct);
                    if (original.HasCover) return new ArtworkWriteResult();
                    PictureInfo picture = InspectImage(image);
                    byte[] added = AppendPicture(original, image, picture);
                    string recovery = SafePath(recoveryDirectory);
                    if (String.Equals(Path.GetPathRoot(recovery).TrimEnd('\\', '/'), recovery.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Choose a recovery folder rather than a drive root.");
                    Directory.CreateDirectory(recovery);
                    SafePath(recovery);
                    string id = Guid.NewGuid().ToString("N");
                    metadataPath = SafePath(Path.Combine(recovery, id + ".flac-metadata"));
                    manifestPath = SafePath(Path.Combine(recovery, id + ".json"));
                    DateTime modified = File.GetLastWriteTimeUtc(target);
                    long fileLength = input.Length, audioLength = fileLength - input.Position;
                    string audioHash;
                    using (var output = Create(staged))
                    {
                        output.Write(added, 0, added.Length);
                        audioHash = CopyAudio(input, output, ct);
                        output.Flush(true);
                    }
                    ct.ThrowIfCancellationRequested();
                    WriteNew(metadataPath, original.Bytes);
                    var manifest = new RecoveryManifest {
                        Version = 1, TargetPath = target, MetadataFileName = Path.GetFileName(metadataPath),
                        OriginalMetadataHash = Hash(original.Bytes), AddedMetadataHash = Hash(added),
                        AudioHash = audioHash, AudioLength = audioLength
                    };
                    WriteNew(manifestPath, Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(manifest)));
                    VerifyUnchanged(target, original.Bytes, fileLength, modified, ct);
                    SafePath(staged);
                    ct.ThrowIfCancellationRequested();
                    File.Replace(staged, target, null);
                    committed = true;
                    return new ArtworkWriteResult { Changed = true, BackupMetadataPath = metadataPath, ManifestPath = manifestPath };
                }
            }
            finally
            {
                DeleteOwnFile(staged);
                if (!committed) { DeleteOwnFile(metadataPath); DeleteOwnFile(manifestPath); }
            }
        }

        // Restores only this operation's metadata and refuses files subsequently
        // retagged or changed. The manifest and metadata backup remain available.
        internal static bool RestoreMetadata(string manifestPath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string manifestFile = SafePath(manifestPath);
            var info = new FileInfo(manifestFile);
            if (info.Length > 65536) throw new InvalidDataException("Artwork recovery record is too large.");
            RecoveryManifest manifest;
            try { manifest = new JavaScriptSerializer().Deserialize<RecoveryManifest>(File.ReadAllText(manifestFile, Encoding.UTF8)); }
            catch (ArgumentException e) { throw new InvalidDataException("Invalid artwork recovery record.", e); }
            if (manifest == null || manifest.Version != 1 || String.IsNullOrEmpty(manifest.MetadataFileName) ||
                manifest.MetadataFileName != Path.GetFileName(manifest.MetadataFileName) || manifest.AudioLength < 8 ||
                String.IsNullOrEmpty(manifest.TargetPath)) throw new InvalidDataException("Invalid artwork recovery record.");
            string backup = SafePath(Path.Combine(Path.GetDirectoryName(manifestFile), manifest.MetadataFileName));
            if (new FileInfo(backup).Length > MaximumMetadataBytes) throw new InvalidDataException("Artwork metadata backup is too large.");
            byte[] oldMetadata = File.ReadAllBytes(backup);
            if (Hash(oldMetadata) != manifest.OriginalMetadataHash) throw new InvalidDataException("Artwork metadata backup has changed.");
            ValidateMetadataOnly(oldMetadata, ct);
            string target = SafePath(manifest.TargetPath);
            string staged = Path.Combine(Path.GetDirectoryName(target), ".artwork-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var input = Open(target))
                {
                    Metadata current = ReadMetadata(input, ct);
                    string currentHash = Hash(current.Bytes);
                    if (currentHash != manifest.AddedMetadataHash && currentHash != manifest.OriginalMetadataHash)
                        throw new InvalidDataException("The song's metadata changed after its cover was added; recovery was stopped.");
                    if (input.Length - input.Position != manifest.AudioLength)
                        throw new InvalidDataException("The song's audio changed after its cover was added; recovery was stopped.");
                    DateTime modified = File.GetLastWriteTimeUtc(target);
                    long fileLength = input.Length;
                    string audioHash;
                    using (var output = Create(staged))
                    {
                        output.Write(oldMetadata, 0, oldMetadata.Length);
                        audioHash = CopyAudio(input, output, ct);
                        output.Flush(true);
                    }
                    if (audioHash != manifest.AudioHash) throw new InvalidDataException("The song's audio changed after its cover was added; recovery was stopped.");
                    if (currentHash == manifest.OriginalMetadataHash) return false;
                    VerifyUnchanged(target, current.Bytes, fileLength, modified, ct);
                    SafePath(staged);
                    ct.ThrowIfCancellationRequested();
                    File.Replace(staged, target, null);
                    return true;
                }
            }
            finally { DeleteOwnFile(staged); }
        }

        private static Metadata ReadMetadata(Stream input, CancellationToken ct)
        {
            var result = ReadBlocks(input, ct);
            if (input.Length - input.Position < 8) throw new InvalidDataException("FLAC audio is missing or truncated.");
            long audioStart = input.Position;
            int first = input.ReadByte(), second = input.ReadByte();
            input.Position = audioStart;
            if (first != 255 || (second & 254) != 248) throw new InvalidDataException("The FLAC audio frame header is missing.");
            return result;
        }

        private static void ValidateMetadataOnly(byte[] bytes, CancellationToken ct)
        {
            using (var input = new MemoryStream(bytes, false))
            {
                ReadBlocks(input, ct);
                if (input.Position != input.Length) throw new InvalidDataException("Unexpected data in the metadata backup.");
            }
        }

        private static Metadata ReadBlocks(Stream input, CancellationToken ct)
        {
            byte[] magic = ReadExactly(input, 4, ct);
            if (magic[0] != 'f' || magic[1] != 'L' || magic[2] != 'a' || magic[3] != 'C')
                throw new InvalidDataException("This file is not a native FLAC file.");
            var result = new Metadata();
            using (var metadata = new MemoryStream())
            {
                metadata.Write(magic, 0, magic.Length);
                for (int blocks = 0; ; blocks++)
                {
                    if (blocks >= 4096) throw new InvalidDataException("Too many FLAC metadata blocks.");
                    byte[] header = ReadExactly(input, 4, ct);
                    int type = header[0] & 127;
                    int length = (header[1] << 16) | (header[2] << 8) | header[3];
                    if ((blocks == 0 && (type != 0 || length != 34)) || (blocks != 0 && type == 0) || type == 127)
                        throw new InvalidDataException("Invalid FLAC metadata block.");
                    if (metadata.Length + 4 + length > MaximumMetadataBytes || length > input.Length - input.Position)
                        throw new InvalidDataException("FLAC metadata is too large or truncated.");
                    byte[] body = ReadExactly(input, length, ct);
                    if (blocks == 0) ValidateStreamInfo(body);
                    if (type == 6 && IsUsableFrontCover(body)) result.HasCover = true;
                    result.LastHeader = checked((int)metadata.Position);
                    metadata.Write(header, 0, 4);
                    metadata.Write(body, 0, body.Length);
                    if ((header[0] & 128) != 0) break;
                }
                result.Bytes = metadata.ToArray();
            }
            return result;
        }

        private static void ValidateStreamInfo(byte[] info)
        {
            int minimum = (info[0] << 8) | info[1], maximum = (info[2] << 8) | info[3];
            ulong packed = 0;
            for (int i = 10; i < 18; i++) packed = (packed << 8) | info[i];
            int minFrame = (info[4] << 16) | (info[5] << 8) | info[6];
            int maxFrame = (info[7] << 16) | (info[8] << 8) | info[9];
            if (minimum < 16 || maximum < minimum || (packed >> 44) == 0 || ((packed >> 36) & 31) + 1 < 4 ||
                (minFrame != 0 && maxFrame != 0 && minFrame > maxFrame)) throw new InvalidDataException("Invalid FLAC stream information.");
        }

        private static bool IsUsableFrontCover(byte[] body)
        {
            try
            {
                int offset = 0;
                if (Read32(body, ref offset) != 3) return false;
                uint mimeLength = Read32(body, ref offset);
                if (mimeLength > 64 || mimeLength > body.Length - offset) return false;
                string mime = Encoding.ASCII.GetString(body, offset, (int)mimeLength);
                offset += (int)mimeLength;
                if (mime != "image/jpeg" && mime != "image/png") return false;
                uint descriptionLength = Read32(body, ref offset);
                if (descriptionLength > body.Length - offset) return false;
                offset += (int)descriptionLength;
                for (int i = 0; i < 4; i++) Read32(body, ref offset);
                uint imageLength = Read32(body, ref offset);
                if (imageLength == 0 || imageLength > MaximumImageBytes || imageLength != body.Length - offset) return false;
                byte[] image = new byte[(int)imageLength];
                Buffer.BlockCopy(body, offset, image, 0, image.Length);
                return InspectImage(image).Mime == mime;
            }
            catch (InvalidDataException) { return false; }
        }

        private static PictureInfo InspectImage(byte[] image)
        {
            if (image == null || image.Length < 24 || image.Length > MaximumImageBytes)
                throw new InvalidDataException("Cover images must be JPEG or PNG files no larger than 10 MiB.");
            int width = 0, height = 0;
            string mime;
            if (image[0] == 137 && image[1] == 80 && image[2] == 78 && image[3] == 71 && image[4] == 13 && image[5] == 10 && image[6] == 26 && image[7] == 10)
            {
                int offset = 8;
                if (Read32(image, ref offset) != 13 || image[12] != 'I' || image[13] != 'H' || image[14] != 'D' || image[15] != 'R')
                    throw new InvalidDataException("Invalid PNG image header.");
                offset = 16;
                uint w = Read32(image, ref offset), h = Read32(image, ref offset);
                if (w > 8192 || h > 8192) throw new InvalidDataException("Cover image dimensions are too large.");
                width = (int)w; height = (int)h; mime = "image/png";
            }
            else if (image[0] == 255 && image[1] == 216)
            {
                mime = "image/jpeg";
                int offset = 2;
                while (offset < image.Length)
                {
                    if (image[offset++] != 255) throw new InvalidDataException("Invalid JPEG image header.");
                    while (offset < image.Length && image[offset] == 255) offset++;
                    if (offset >= image.Length) break;
                    int marker = image[offset++];
                    if (marker == 217 || marker == 218) break;
                    if (marker == 1 || (marker >= 208 && marker <= 215)) continue;
                    if (offset + 2 > image.Length) break;
                    int length = (image[offset] << 8) | image[offset + 1];
                    if (length < 2 || length > image.Length - offset) break;
                    bool frame = marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204;
                    if (frame)
                    {
                        if (length < 8) break;
                        height = (image[offset + 3] << 8) | image[offset + 4];
                        width = (image[offset + 5] << 8) | image[offset + 6];
                        break;
                    }
                    offset += length;
                }
            }
            else throw new InvalidDataException("Cover images must be JPEG or PNG files.");
            if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > MaximumPixels)
                throw new InvalidDataException("Cover image dimensions are invalid or too large.");
            try
            {
                using (var memory = new MemoryStream(image, false))
                using (var decoded = Image.FromStream(memory, false, true))
                {
                    if (decoded.Width != width || decoded.Height != height ||
                        (mime == "image/png" && decoded.RawFormat.Guid != ImageFormat.Png.Guid) ||
                        (mime == "image/jpeg" && decoded.RawFormat.Guid != ImageFormat.Jpeg.Guid)) throw new InvalidDataException("Invalid cover image.");
                    // Force bounded decoding before putting downloaded bytes into a song.
                    using (var validated = new Bitmap(decoded)) { validated.GetPixel(width - 1, height - 1); }
                    return new PictureInfo { Mime = mime, Width = width, Height = height,
                        Depth = Image.GetPixelFormatSize(decoded.PixelFormat),
                        Colors = (decoded.PixelFormat & PixelFormat.Indexed) != 0 ? decoded.Palette.Entries.Length : 0 };
                }
            }
            catch (ArgumentException e) { throw new InvalidDataException("The cover image could not be decoded.", e); }
            catch (OutOfMemoryException e) { throw new InvalidDataException("The cover image could not be decoded.", e); }
            catch (System.Runtime.InteropServices.ExternalException e) { throw new InvalidDataException("The cover image could not be decoded.", e); }
        }

        private static byte[] AppendPicture(Metadata original, byte[] image, PictureInfo picture)
        {
            byte[] mime = Encoding.ASCII.GetBytes(picture.Mime);
            int length = checked(32 + mime.Length + image.Length);
            if ((long)original.Bytes.Length + length + 4 > MaximumMetadataBytes)
                throw new InvalidDataException("The combined FLAC metadata would be too large.");
            using (var output = new MemoryStream())
            {
                output.Write(original.Bytes, 0, original.Bytes.Length);
                output.Position = original.LastHeader;
                output.WriteByte((byte)(original.Bytes[original.LastHeader] & 127));
                output.Position = output.Length;
                output.WriteByte(0x86);
                output.WriteByte((byte)(length >> 16)); output.WriteByte((byte)(length >> 8)); output.WriteByte((byte)length);
                Write32(output, 3); Write32(output, (uint)mime.Length); output.Write(mime, 0, mime.Length);
                Write32(output, 0); // Empty UTF-8 description.
                Write32(output, (uint)picture.Width); Write32(output, (uint)picture.Height);
                Write32(output, (uint)picture.Depth); Write32(output, (uint)picture.Colors);
                Write32(output, (uint)image.Length); output.Write(image, 0, image.Length);
                return output.ToArray();
            }
        }

        private static FileStream Open(string path)
        {
            // Delete sharing lets File.Replace commit while this handle still
            // prevents in-place writers from touching the original audio.
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.SequentialScan);
        }

        private static FileStream Create(string path)
        {
            SafePath(path);
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan);
        }

        private static void WriteNew(string path, byte[] bytes)
        {
            using (var output = Create(path)) { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
        }

        private static void VerifyUnchanged(string path, byte[] metadata, long length, DateTime modified, CancellationToken ct)
        {
            SafePath(path);
            if (File.GetLastWriteTimeUtc(path) != modified) throw new IOException("The song changed while its artwork was being prepared.");
            using (var check = Open(path))
            {
                if (check.Length != length || Hash(ReadMetadata(check, ct).Bytes) != Hash(metadata))
                    throw new IOException("The song changed while its artwork was being prepared.");
            }
        }

        private static string CopyAudio(Stream input, Stream output, CancellationToken ct)
        {
            using (var hash = SHA256.Create())
            {
                byte[] buffer = new byte[65536];
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int count = input.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    output.Write(buffer, 0, count);
                    hash.TransformBlock(buffer, 0, count, buffer, 0);
                }
                ct.ThrowIfCancellationRequested();
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(hash.Hash).Replace("-", "");
            }
        }

        private static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }

        private static byte[] ReadExactly(Stream input, int count, CancellationToken ct)
        {
            byte[] bytes = new byte[count];
            for (int read = 0; read < count; )
            {
                ct.ThrowIfCancellationRequested();
                int got = input.Read(bytes, read, count - read);
                if (got == 0) throw new InvalidDataException("Truncated FLAC metadata.");
                read += got;
            }
            return bytes;
        }

        private static uint Read32(byte[] bytes, ref int offset)
        {
            if (offset < 0 || bytes.Length - offset < 4) throw new InvalidDataException("Truncated picture metadata.");
            uint value = ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
            offset += 4;
            return value;
        }

        private static void Write32(Stream output, uint value)
        {
            output.WriteByte((byte)(value >> 24)); output.WriteByte((byte)(value >> 16));
            output.WriteByte((byte)(value >> 8)); output.WriteByte((byte)value);
        }

        private static string SafePath(string path)
        {
            string full = Path.GetFullPath(path);
            for (string current = full; !String.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Artwork changes do not follow file or folder links.");
                if (current == Path.GetPathRoot(current)) break;
            }
            return full;
        }

        private static void DeleteOwnFile(string path)
        {
            if (String.IsNullOrEmpty(path)) return;
            try { if (File.Exists(SafePath(path))) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }
    }
}
