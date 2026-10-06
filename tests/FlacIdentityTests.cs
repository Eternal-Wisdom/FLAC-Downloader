using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace PlaylistFlac
{
    public static class FlacIdentityTests
    {
        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "PlaylistFlac-IdentityTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                byte[] frames = {255, 248, 105, 24, 0, 0, 1, 2, 3, 4, 5, 6};
                string original = Write(root, "original", Make(44100, 16, 2, 441000, new byte[] {1,2,3}, frames));
                string tags = Write(root, "tags", Make(44100, 16, 2, 441000, new byte[] {9,8,7,6,5,4,3}, frames));
                string hash = FlacIdentity.AudioHash(original, CancellationToken.None);
                Assert(hash != null && hash.Length == 64, "Valid structural audio produces SHA256.");
                Assert(hash == FlacIdentity.AudioHash(tags, CancellationToken.None), "Changed tags/artwork preserve encoded audio identity.");
                Assert(FlacIdentity.QuickSignature(original) == FlacIdentity.QuickSignature(tags), "Quick signatures ignore metadata size/content, including a zero MD5.");
                byte[] changedFrames = (byte[])frames.Clone(); changedFrames[8] = 99;
                string different = Write(root, "different", Make(44100, 16, 2, 441000, new byte[] {1}, changedFrames));
                Assert(hash != FlacIdentity.AudioHash(different, CancellationToken.None), "Different encoded frames stay separate.");
                string rate = Write(root, "rate", Make(48000, 16, 2, 441000, new byte[0], frames));
                string depth = Write(root, "depth", Make(44100, 24, 2, 441000, new byte[0], frames));
                string channel = Write(root, "channel", Make(44100, 16, 1, 441000, new byte[0], frames));
                string samples = Write(root, "samples", Make(44100, 16, 2, 441001, new byte[0], frames));
                foreach (string path in new[] {rate,depth,channel,samples})
                {
                    Assert(hash != FlacIdentity.AudioHash(path, CancellationToken.None), "Format and sample count are part of audio identity.");
                    Assert(FlacIdentity.QuickSignature(original) != FlacIdentity.QuickSignature(path), "Quick signature includes format and sample count.");
                }
                byte[] md5 = Make(44100, 16, 2, 441000, new byte[0], frames); md5[26] = 1;
                string signed = Write(root, "md5", md5);
                Assert(FlacIdentity.QuickSignature(original) != FlacIdentity.QuickSignature(signed), "Declared MD5 is part of candidate screening.");
                Assert(hash == FlacIdentity.AudioHash(signed, CancellationToken.None), "Final encoded comparison does not depend on declared MD5 presence.");
                byte[] headerOnly = Make(44100, 16, 2, 441000, new byte[0], new byte[0]);
                Assert(FlacIdentity.AudioHash(Write(root,"empty",headerOnly),CancellationToken.None) == null, "No audio cannot be a duplicate.");
                byte[] truncated = Make(44100, 16, 2, 441000, new byte[] {1,2,3}, frames);
                Assert(FlacIdentity.AudioHash(Write(root,"truncated",truncated.Take(47).ToArray()),CancellationToken.None) == null, "Truncated metadata is rejected.");
                Assert(FlacIdentity.QuickSignature(Write(root,"short",truncated.Take(41).ToArray())) == null, "Truncated STREAMINFO is rejected.");
                byte[] wrongType = Make(44100,16,2,441000,new byte[0],frames); wrongType[42] = 128;
                Assert(FlacIdentity.AudioHash(Write(root,"twice",wrongType),CancellationToken.None) == null, "Second STREAMINFO is invalid.");
                wrongType[42] = 255;
                Assert(FlacIdentity.AudioHash(Write(root,"reserved",wrongType),CancellationToken.None) == null, "Invalid metadata type 127 is rejected.");
                byte[] noSync = Make(44100,16,2,441000,new byte[0],new byte[12]);
                Assert(FlacIdentity.AudioHash(Write(root,"nosync",noSync),CancellationToken.None) == null, "A missing first-frame sync is rejected.");
                foreach (byte[] malformed in new[] {
                    Make(0,16,2,441000,new byte[0],frames),
                    Make(44100,3,2,441000,new byte[0],frames)})
                {
                    string path = Write(root,Guid.NewGuid().ToString("N"),malformed);
                    Assert(FlacIdentity.QuickSignature(path) == null && FlacIdentity.AudioHash(path,CancellationToken.None) == null, "Malformed sample format fails both checks.");
                }
                string missing = Path.Combine(root,"missing.flac");
                Assert(FlacIdentity.QuickSignature(missing) == null && FlacIdentity.AudioHash(missing,CancellationToken.None) == null, "Missing files are skipped safely.");
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel(); bool canceled = false;
                    try { FlacIdentity.AudioHash(original,cancel.Token); } catch (OperationCanceledException) { canceled=true; }
                    Assert(canceled, "Caller cancellation is preserved.");
                }
                Assert(File.ReadAllBytes(original).SequenceEqual(Make(44100,16,2,441000,new byte[] {1,2,3},frames)), "Comparisons never change music.");
            }
            finally
            {
                string absolute = Path.GetFullPath(root);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("PlaylistFlac-IdentityTests-",StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected identity test directory.");
                Directory.Delete(absolute,true);
            }
        }

        private static string Write(string root,string name,byte[] data)
        {
            string path=Path.Combine(root,name+".flac"); File.WriteAllBytes(path,data); return path;
        }

        private static byte[] Make(int rate,int bits,int channels,ulong samples,byte[] metadata,byte[] frames)
        {
            var bytes=new byte[46+metadata.Length+frames.Length];
            bytes[0]=102;bytes[1]=76;bytes[2]=97;bytes[3]=67;bytes[7]=34;bytes[8]=16;bytes[10]=16;
            ulong packed=((ulong)rate<<44)|((ulong)(channels-1)<<41)|((ulong)(bits-1)<<36)|samples;
            for(int i=25;i>=18;i--){bytes[i]=(byte)packed;packed>>=8;}
            bytes[42]=0x86;
            bytes[43]=(byte)(metadata.Length>>16);bytes[44]=(byte)(metadata.Length>>8);bytes[45]=(byte)metadata.Length;
            Buffer.BlockCopy(metadata,0,bytes,46,metadata.Length);
            Buffer.BlockCopy(frames,0,bytes,46+metadata.Length,frames.Length);
            return bytes;
        }

        private static void Assert(bool condition,string message)
        {
            if(!condition)throw new InvalidOperationException("FLAC identity test failed: "+message);
        }
    }
}
