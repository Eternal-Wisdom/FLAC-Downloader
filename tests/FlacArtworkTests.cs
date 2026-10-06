using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PlaylistFlac
{
    // These are structural FLAC fixtures. The writer preserves encoded frames;
    // neither it nor these tests claim to decode or validate audio-frame CRCs.
    public static class FlacArtworkTests
    {
        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "PlaylistFlac-ArtworkTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                byte[] png = ImageBytes(ImageFormat.Png), jpeg = ImageBytes(ImageFormat.Jpeg);
                CheckRoundTrip(Path.Combine(root, "round-trip"), png);
                CheckPictures(Path.Combine(root, "pictures"), png, jpeg);
                CheckFailures(Path.Combine(root, "failures"), png);
                CheckRecoveryValidation(Path.Combine(root, "recovery"), png);
                CheckLockedReplacement(Path.Combine(root, "locked"), png);
            }
            finally
            {
                string absolute = Path.GetFullPath(root);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("PlaylistFlac-ArtworkTests-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected artwork test directory.");
                Directory.Delete(absolute, true);
            }
        }

        private static void CheckRoundTrip(string folder, byte[] png)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "夜の歌.flac"), recovery = Path.Combine(folder, "recover");
            byte[] original = Make(new Block(4, new byte[] { 9, 8, 7, 6, 5 }), new Block(2, new byte[] { 1, 3, 5, 7, 9 }), new Block(1, new byte[37]));
            File.WriteAllBytes(path, original);
            string audioHash = FlacIdentity.AudioHash(path, CancellationToken.None);
            Assert(!FlacArtwork.HasFrontCover(path), "A FLAC without pictures needs a cover.");
            var result = FlacArtwork.AddFrontCover(path, png, recovery, CancellationToken.None);
            Assert(result.Changed && File.Exists(result.BackupMetadataPath) && File.Exists(result.ManifestPath), "Successful artwork changes have recovery metadata and a manifest.");
            byte[] added = File.ReadAllBytes(path);
            Assert(FlacArtwork.HasFrontCover(path), "An embedded PNG front cover is detected.");
            Assert(audioHash != null && audioHash == FlacIdentity.AudioHash(path, CancellationToken.None), "Encoded audio identity is unchanged after adding artwork.");
            Assert(Audio(original).SequenceEqual(Audio(added)), "Every encoded audio byte is preserved.");
            List<Block> oldBlocks = Blocks(original), newBlocks = Blocks(added);
            Assert(newBlocks.Count == oldBlocks.Count + 1 && newBlocks.Last().Type == 6, "Exactly one picture block is appended.");
            for (int i = 0; i < oldBlocks.Count; i++)
                Assert(newBlocks[i].Type == oldBlocks[i].Type && newBlocks[i].Body.SequenceEqual(oldBlocks[i].Body), "STREAMINFO, tags, application data and padding are preserved.");
            Assert(newBlocks.Last().Body.Skip(newBlocks.Last().Body.Length - png.Length).SequenceEqual(png), "Downloaded image bytes are embedded without transcoding.");
            Assert(File.ReadAllBytes(result.BackupMetadataPath).SequenceEqual(Metadata(original)), "Recovery saves the exact original metadata, without copying audio.");
            Assert(new FileInfo(result.BackupMetadataPath).Length < original.Length, "Recovery metadata is smaller than the original song.");
            var manifest = ReadManifest(result.ManifestPath);
            Assert(manifest.TargetPath == path && manifest.MetadataFileName == Path.GetFileName(result.BackupMetadataPath) && manifest.AudioLength == Audio(original).Length, "Recovery identifies the exact song and audio length.");
            long stamp = File.GetLastWriteTimeUtc(path).Ticks;
            var skipped = FlacArtwork.AddFrontCover(path, null, null, CancellationToken.None);
            Assert(!skipped.Changed && skipped.BackupMetadataPath == null && skipped.ManifestPath == null, "Existing front covers skip image downloading and recovery creation.");
            Assert(File.ReadAllBytes(path).SequenceEqual(added) && File.GetLastWriteTimeUtc(path).Ticks == stamp, "Skipping a covered song does not rewrite it.");
            Assert(FlacArtwork.RestoreMetadata(result.ManifestPath, CancellationToken.None), "Recovery reports a restored song.");
            Assert(File.ReadAllBytes(path).SequenceEqual(original), "Recovery restores the exact original file.");
            Assert(File.Exists(result.ManifestPath) && File.Exists(result.BackupMetadataPath), "Recovery records remain available after restoration.");
            Assert(!FlacArtwork.RestoreMetadata(result.ManifestPath, CancellationToken.None), "Repeating a completed recovery is safe and reports no change.");
            AssertNoStages(folder);
        }

        private static void CheckPictures(string folder, byte[] png, byte[] jpeg)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "back-cover.flac"), recovery = Path.Combine(folder, "recover");
            byte[] back = Picture(4, "image/png", png);
            File.WriteAllBytes(path, Make(new Block(6, back)));
            Assert(!FlacArtwork.HasFrontCover(path), "A back cover is not mistaken for a front cover.");
            FlacArtwork.AddFrontCover(path, jpeg, recovery, CancellationToken.None);
            List<Block> blocks = Blocks(File.ReadAllBytes(path));
            Assert(blocks.Count(b => b.Type == 6) == 2 && blocks[1].Body.SequenceEqual(back), "Adding a front cover preserves a pre-existing back cover.");
            Assert(FlacArtwork.HasFrontCover(path), "JPEG front covers are supported.");
            string broken = Path.Combine(folder, "broken-front.flac");
            byte[] bad = Picture(3, "image/png", new byte[25]);
            File.WriteAllBytes(broken, Make(new Block(6, bad)));
            Assert(!FlacArtwork.HasFrontCover(broken), "An undecodable front-cover block does not count as a usable cover.");
            FlacArtwork.AddFrontCover(broken, png, recovery, CancellationToken.None);
            blocks = Blocks(File.ReadAllBytes(broken));
            Assert(FlacArtwork.HasFrontCover(broken) && blocks.Count(b => b.Type == 6) == 2 && blocks[1].Body.SequenceEqual(bad), "A missing usable image is fixed while preserving unknown existing metadata.");
            string oversized = Path.Combine(folder, "oversized-picture.flac");
            byte[] picture = Picture(3, "image/png", png);
            picture[4] = 127;
            File.WriteAllBytes(oversized, Make(new Block(6, picture)));
            Assert(!FlacArtwork.HasFrontCover(oversized), "Out-of-bounds picture fields are rejected safely.");
        }

        private static void CheckFailures(string folder, byte[] png)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "song.flac"), recovery = Path.Combine(folder, "recover");
            byte[] original = Make();
            File.WriteAllBytes(path, original);
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                Expect<OperationCanceledException>(() => FlacArtwork.AddFrontCover(path, png, recovery, canceled.Token), "Cancellation must propagate.");
            }
            Assert(File.ReadAllBytes(path).SequenceEqual(original) && !Directory.Exists(recovery), "Cancellation before starting creates no files.");
            foreach (byte[] image in new[] { new byte[0], new byte[40], new byte[FlacArtwork.MaximumImageBytes + 1], png.Take(25).ToArray() })
            {
                Expect<InvalidDataException>(() => FlacArtwork.AddFrontCover(path, image, recovery, CancellationToken.None), "Malformed, truncated or oversized images must fail.");
                Assert(File.ReadAllBytes(path).SequenceEqual(original), "Invalid cover bytes do not modify audio files.");
            }
            byte[] giantPng = (byte[])png.Clone(); giantPng[16] = 127;
            Expect<InvalidDataException>(() => FlacArtwork.AddFrontCover(path, giantPng, recovery, CancellationToken.None), "Unbounded image dimensions must fail before decoding.");
            var malformed = new List<byte[]> { new byte[0], Encoding.ASCII.GetBytes("not a flac file"), original.Take(30).ToArray(), Metadata(original), Metadata(original).Concat(new byte[8]).ToArray() };
            byte[] duplicateInfo = Make(new Block(0, new byte[34])); malformed.Add(duplicateInfo);
            byte[] reserved = Make(new Block(127, new byte[0])); malformed.Add(reserved);
            byte[] badLength = (byte[])original.Clone(); badLength[5] = 255; malformed.Add(badLength);
            byte[] zeroRate = (byte[])original.Clone(); zeroRate[18] = 0; zeroRate[19] = 0; zeroRate[20] &= 15; malformed.Add(zeroRate);
            foreach (byte[] bytes in malformed)
            {
                File.WriteAllBytes(path, bytes);
                Assert(!FlacArtwork.HasFrontCover(path), "Malformed FLAC files do not have a usable cover.");
                Expect<InvalidDataException>(() => FlacArtwork.AddFrontCover(path, png, recovery, CancellationToken.None), "Malformed or truncated FLAC input must fail.");
                Assert(File.ReadAllBytes(path).SequenceEqual(bytes), "Rejected input is left byte-for-byte unchanged.");
            }
            File.WriteAllBytes(path, original);
            Expect<IOException>(() => FlacArtwork.AddFrontCover(Path.Combine(folder, "missing.flac"), png, recovery, CancellationToken.None), "Missing input is a propagated I/O failure.");
            Expect<IOException>(() => FlacArtwork.AddFrontCover(path, png, Path.GetPathRoot(path), CancellationToken.None), "Drive-root recovery destinations are rejected.");
            AssertNoStages(folder);
        }

        private static void CheckRecoveryValidation(string folder, byte[] png)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "song.flac"), recovery = Path.Combine(folder, "recover");
            byte[] original = Make(new Block(4, new byte[] { 5, 6, 7 }));
            File.WriteAllBytes(path, original);
            var result = FlacArtwork.AddFrontCover(path, png, recovery, CancellationToken.None);
            byte[] added = File.ReadAllBytes(path), backup = File.ReadAllBytes(result.BackupMetadataPath);
            string manifestText = File.ReadAllText(result.ManifestPath);
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                Expect<OperationCanceledException>(() => FlacArtwork.RestoreMetadata(result.ManifestPath, canceled.Token), "Recovery cancellation must propagate.");
                Assert(File.ReadAllBytes(path).SequenceEqual(added), "Canceled recovery does not modify the song.");
            }
            byte[] changed = (byte[])added.Clone(); changed[46] ^= 1;
            File.WriteAllBytes(path, changed);
            Expect<InvalidDataException>(() => FlacArtwork.RestoreMetadata(result.ManifestPath, CancellationToken.None), "Recovery must refuse later metadata edits.");
            Assert(File.ReadAllBytes(path).SequenceEqual(changed), "A later tag edit remains untouched after failed recovery.");
            changed = (byte[])added.Clone(); changed[changed.Length - 1] ^= 1;
            File.WriteAllBytes(path, changed);
            Expect<InvalidDataException>(() => FlacArtwork.RestoreMetadata(result.ManifestPath, CancellationToken.None), "Recovery verifies audio content, not only length.");
            Assert(File.ReadAllBytes(path).SequenceEqual(changed), "Changed audio is preserved after failed recovery.");
            File.WriteAllBytes(path, added);
            byte[] changedBackup = (byte[])backup.Clone(); changedBackup[changedBackup.Length - 1] ^= 1;
            File.WriteAllBytes(result.BackupMetadataPath, changedBackup);
            Expect<InvalidDataException>(() => FlacArtwork.RestoreMetadata(result.ManifestPath, CancellationToken.None), "Recovery verifies its metadata backup hash.");
            File.WriteAllBytes(result.BackupMetadataPath, backup);
            var manifest = ReadManifest(result.ManifestPath); manifest.MetadataFileName = "../outside.flac-metadata";
            File.WriteAllText(result.ManifestPath, new JavaScriptSerializer().Serialize(manifest));
            Expect<InvalidDataException>(() => FlacArtwork.RestoreMetadata(result.ManifestPath, CancellationToken.None), "Recovery refuses metadata path traversal.");
            File.WriteAllText(result.ManifestPath, new string(' ', 65537));
            Expect<InvalidDataException>(() => FlacArtwork.RestoreMetadata(result.ManifestPath, CancellationToken.None), "Recovery manifest size is bounded.");
            File.WriteAllText(result.ManifestPath, manifestText);
            Assert(FlacArtwork.RestoreMetadata(result.ManifestPath, CancellationToken.None) && File.ReadAllBytes(path).SequenceEqual(original), "A valid recovery remains usable after failed attempts.");
            AssertNoStages(folder);
        }

        private static void CheckLockedReplacement(string folder, byte[] png)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "song.flac"), recovery = Path.Combine(folder, "recover");
            byte[] original = Make(); File.WriteAllBytes(path, original);
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Expect<IOException>(() => FlacArtwork.AddFrontCover(path, png, recovery, CancellationToken.None), "An atomic replacement blocked by another reader must report the I/O failure.");
            Assert(File.ReadAllBytes(path).SequenceEqual(original), "Failed replacement leaves the original song intact.");
            Assert(!Directory.Exists(recovery) || Directory.GetFiles(recovery).Length == 0, "Failed replacement cleans only its uncommitted recovery files.");
            AssertNoStages(folder);
            Directory.CreateDirectory(recovery);
            string unrelated = Path.Combine(recovery, "keep.txt"); File.WriteAllText(unrelated, "keep");
            using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                Expect<IOException>(() => FlacArtwork.AddFrontCover(path, png, recovery, CancellationToken.None), "A concurrent in-place writer must prevent artwork preparation.");
            Assert(File.ReadAllText(unrelated) == "keep" && File.ReadAllBytes(path).SequenceEqual(original), "Failed preparation preserves unrelated recovery files and the source.");
            var result = FlacArtwork.AddFrontCover(path, png, recovery, CancellationToken.None);
            byte[] added = File.ReadAllBytes(path);
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Expect<IOException>(() => FlacArtwork.RestoreMetadata(result.ManifestPath, CancellationToken.None), "A locked recovery replacement must report failure.");
            Assert(File.ReadAllBytes(path).SequenceEqual(added) && File.Exists(result.ManifestPath) && File.Exists(result.BackupMetadataPath), "Failed recovery preserves the covered song and recovery records.");
            AssertNoStages(folder);
        }

        private sealed class Block
        {
            internal readonly int Type; internal readonly byte[] Body;
            internal Block(int type, byte[] body) { Type = type; Body = body; }
        }

        private static byte[] Make(params Block[] extra)
        {
            byte[] info = new byte[34]; info[0] = 16; info[2] = 16;
            ulong packed = ((ulong)44100 << 44) | ((ulong)1 << 41) | ((ulong)15 << 36) | 441000;
            for (int i = 17; i >= 10; i--) { info[i] = (byte)packed; packed >>= 8; }
            var blocks = new List<Block> { new Block(0, info) }; blocks.AddRange(extra);
            using (var output = new MemoryStream())
            {
                output.Write(new byte[] { 102, 76, 97, 67 }, 0, 4);
                for (int i = 0; i < blocks.Count; i++)
                {
                    Block block = blocks[i]; output.WriteByte((byte)(block.Type | (i == blocks.Count - 1 ? 128 : 0)));
                    output.WriteByte((byte)(block.Body.Length >> 16)); output.WriteByte((byte)(block.Body.Length >> 8)); output.WriteByte((byte)block.Body.Length);
                    output.Write(block.Body, 0, block.Body.Length);
                }
                byte[] audio = new byte[131075]; audio[0] = 255; audio[1] = 248;
                for (int i = 2; i < audio.Length; i++) audio[i] = (byte)(i * 17);
                output.Write(audio, 0, audio.Length); return output.ToArray();
            }
        }

        private static byte[] ImageBytes(ImageFormat format)
        {
            using (var bitmap = new Bitmap(3, 2))
            using (var output = new MemoryStream())
            {
                bitmap.SetPixel(0, 0, Color.CornflowerBlue); bitmap.SetPixel(2, 1, Color.Crimson);
                bitmap.Save(output, format); return output.ToArray();
            }
        }

        private static byte[] Picture(int type, string mime, byte[] image)
        {
            using (var output = new MemoryStream())
            {
                byte[] mimeBytes = Encoding.ASCII.GetBytes(mime);
                Write32(output, type); Write32(output, mimeBytes.Length); output.Write(mimeBytes, 0, mimeBytes.Length);
                Write32(output, 0); Write32(output, 3); Write32(output, 2); Write32(output, 24); Write32(output, 0);
                Write32(output, image.Length); output.Write(image, 0, image.Length); return output.ToArray();
            }
        }

        private static void Write32(Stream output, int value)
        {
            output.WriteByte((byte)(value >> 24)); output.WriteByte((byte)(value >> 16)); output.WriteByte((byte)(value >> 8)); output.WriteByte((byte)value);
        }

        private static int AudioOffset(byte[] file)
        {
            int offset = 4;
            while (true)
            {
                bool last = (file[offset] & 128) != 0;
                int length = (file[offset + 1] << 16) | (file[offset + 2] << 8) | file[offset + 3];
                offset += 4 + length; if (last) return offset;
            }
        }

        private static byte[] Audio(byte[] file) { return file.Skip(AudioOffset(file)).ToArray(); }
        private static byte[] Metadata(byte[] file) { return file.Take(AudioOffset(file)).ToArray(); }

        private static List<Block> Blocks(byte[] file)
        {
            var result = new List<Block>(); int offset = 4, end = AudioOffset(file);
            while (offset < end)
            {
                int type = file[offset] & 127, length = (file[offset + 1] << 16) | (file[offset + 2] << 8) | file[offset + 3];
                result.Add(new Block(type, file.Skip(offset + 4).Take(length).ToArray())); offset += 4 + length;
            }
            return result;
        }

        private static FlacArtwork.RecoveryManifest ReadManifest(string path)
        {
            return new JavaScriptSerializer().Deserialize<FlacArtwork.RecoveryManifest>(File.ReadAllText(path));
        }

        private static void AssertNoStages(string folder)
        {
            Assert(Directory.GetFiles(folder, ".artwork-*.tmp", SearchOption.AllDirectories).Length == 0, "Temporary staged audio files are cleaned up.");
        }

        private static void Expect<T>(Action action, string message) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new InvalidOperationException("FLAC artwork test failed: " + message);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FLAC artwork test failed: " + message);
        }
    }
}
