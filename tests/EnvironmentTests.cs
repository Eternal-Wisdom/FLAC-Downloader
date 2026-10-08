using System;
using System.IO;
using PlaylistFlac;

internal static class EnvironmentTests
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"FLAC-EnvironmentTests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string state=Path.Combine(root,"state");EnvironmentGuard.EnsureWritableState(state);
            if(Directory.GetFiles(state).Length!=0)throw new Exception("State probe left debris");
            string blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"unchanged");
            bool rejected=false;try{EnvironmentGuard.EnsureWritableState(blocked);}catch(IOException){rejected=true;}
            if(!rejected || File.ReadAllText(blocked)!="unchanged")throw new Exception("Unwritable state guard failed");
        }
        finally { Directory.Delete(root,true); }
    }
}
