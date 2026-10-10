using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PlaylistFlac
{
    internal static class DeepFlacCheck
    {
        internal static bool Check(string decoder,string path,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            decoder=Path.GetFullPath(decoder);path=Path.GetFullPath(path);
            if(!File.Exists(decoder) || (File.GetAttributes(decoder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a real flac.exe from the official FLAC tools.");
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked audio files cannot be checked.");
            using(var process=new Process {StartInfo=new ProcessStartInfo(decoder,"--test --silent --warnings-as-errors -- \""+path+"\"") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true}})
            {
                process.OutputDataReceived+=delegate {};process.ErrorDataReceived+=delegate {};
                process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
                var timer=Stopwatch.StartNew();
                try {
                    while(!process.WaitForExit(100)) {
                        ct.ThrowIfCancellationRequested();
                        if(timer.Elapsed>TimeSpan.FromMinutes(10))throw new IOException("A full audio check exceeded ten minutes; the check was stopped.");
                    }
                    process.WaitForExit();ct.ThrowIfCancellationRequested();return process.ExitCode==0;
                } finally {if(!process.HasExited){process.Kill();process.WaitForExit();}}
            }
        }
    }
}
