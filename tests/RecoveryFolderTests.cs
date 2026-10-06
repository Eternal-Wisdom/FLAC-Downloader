using System;
using System.IO;
using System.Threading;
using PlaylistFlac;

internal static class RecoveryFolderTests
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"PlaylistFlacRecoveryTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string name=".naming-backup-20260924-174043-aabbccdd",old=Path.Combine(root,name);Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old,"rename-map.csv"),"original,renamed\r\n./old.flac,./song.flac\r\n");
            byte[] original=File.ReadAllBytes(Path.Combine(old,"rename-map.csv"));
            string music=Path.Combine(root,"playlist");Directory.CreateDirectory(music);File.WriteAllText(Path.Combine(music,"song.flac"),"unchanged music");
            string duplicate=Path.Combine(root,".duplicates-backup-20260924-174043-aabbccdd");Directory.CreateDirectory(duplicate);
            using(var cancel=new CancellationTokenSource())
            {cancel.Cancel();bool stopped=false;try{RecoveryFolders.OrganizeLegacy(root,cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped && Directory.Exists(old),"cancelled operation leaves backups in place");}
            Check(RecoveryFolders.OrganizeLegacy(root,CancellationToken.None)==1,"one legacy naming backup moved");
            string recovery=Path.Combine(root,RecoveryFolders.Name);
            Check((File.GetAttributes(recovery)&FileAttributes.Hidden)!=0,"parent hidden on Windows");
            Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(recovery,name,"rename-map.csv")))==Convert.ToBase64String(original),"recovery map preserved byte for byte");
            Check(Directory.Exists(duplicate) && File.ReadAllText(Path.Combine(music,"song.flac"))=="unchanged music","audio and legacy absolute-path archives untouched");
            Check(RecoveryFolders.OrganizeLegacy(root,CancellationToken.None)==0,"second organization makes no changes");
            Directory.CreateDirectory(old);File.WriteAllText(Path.Combine(old,"extra.txt"),"preserve");
            Check(RecoveryFolders.OrganizeLegacy(root,CancellationToken.None)==0 && File.Exists(Path.Combine(old,"extra.txt")),"existing destination never overwritten");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void Check(bool value,string message){if(!value)throw new Exception("Recovery folders: "+message);}
}
