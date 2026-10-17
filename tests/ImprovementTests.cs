using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using PlaylistFlac;

internal static class ImprovementTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(Action action,string message){try{action();}catch(FormatException){return;}catch(IOException){return;}throw new Exception(message);}
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"PF-"+Guid.NewGuid().ToString("N").Substring(0,10));Directory.CreateDirectory(root);
        try
        {
            Check(DestinationPreflight.Check(Path.Combine(root,"new","music"),2L*1024*1024*1024).Count==0,"normal destination preflight");
            Check(!Directory.Exists(Path.Combine(root,"new")),"preflight inspection is read-only");
            Reject(()=>DestinationPreflight.Check(root,63L*1024*1024),"low free space was not blocked");
            Check(DestinationPreflight.Check(root,512L*1024*1024).Any(w=>w.Contains("1 GiB")),"low free space warning");
            Check(DestinationPreflight.Check(Path.Combine(root,"OneDrive","songs"),2L*1024*1024*1024).Any(w=>w.Contains("cloud")),"synced destination warning");
            string file=Path.Combine(root,"file");File.WriteAllText(file,"preserved");
            Reject(()=>DestinationPreflight.Check(Path.Combine(file,"music"),Int64.MaxValue),"file parent was accepted");
            Reject(()=>DestinationPreflight.Check(Path.Combine(root,new string('x',240)),Int64.MaxValue),"long destination was accepted");
            string invalidProfile="private-user token=secret C:\\private 192.0.2.44";
            string report=SafeDiagnostics.Create(10,5,2,1,true,999,invalidProfile,true,false);
            foreach(string secret in new[]{"private-user","token=secret","C:\\private","192.0.2.44"})Check(!report.Contains(secret),"diagnostics leaked arbitrary text");
            Check(report.Contains("Concurrent jobs: Unknown") && report.Contains("total=10"),"diagnostics counts/policy allowlist");
            Check(FailureExplanation.Describe("Failed: NoSearchResults").Contains("no search results"),"no results explanation");
            Check(FailureExplanation.Describe("Failed: 10").Contains("matching rules"),"numeric engine reason");
            Check(FailureExplanation.Describe("2").Contains("did not report"),"bare failure state was misrepresented as a reason");
            Check(!FailureExplanation.Describe("Failed: peer=private-peer secret").Contains("private-peer"),"unknown reasons do not echo arbitrary data");
            Check(FailureExplanation.Describe("Failed: NoMatchingResults").Contains("does not identify"),"rejection reasons not invented");
            var pasted=PastedTracklist.Parse("Reol — 感情御中\r\nAC-DC - A Song - Live\n\nReol — 感情御中");
            Check(pasted.Tracks.Count==3 && pasted.Tracks[1].Artist=="AC-DC" && pasted.Tracks[1].Title=="A Song - Live","pasted Unicode/separators and repeated entries");
            Check(pasted.Source==PastedTracklist.Parse("Reol — 感情御中\nAC-DC - A Song - Live\nReol — 感情御中").Source,"line endings changed pasted identity");
            Check(PastedTracklist.Parse("title,artist,length\nSong,Artist,180").Tracks.Single().DurationSeconds==180,"pasted CSV import");
            Reject(()=>PastedTracklist.Parse("Artist - Song\nMalformed"),"partially invalid list was accepted");
            Reject(()=>PastedTracklist.Parse("Artist - "),"missing title was accepted");
            string cache=Path.Combine(root,"cache"),saved=ImportedTracklistCache.Save(cache,pasted);
            var restored=ImportedTracklistCache.Read(cache,saved,pasted.Source,pasted.Name);
            Check(AppState.PlaylistFolder(root,pasted.Name,pasted.Source)==AppState.PlaylistFolder(root,restored.Name,restored.Source),"restart changed imported download location");
            Check(restored.Tracks[0].Title==pasted.Tracks[0].Title,"cached Unicode changed");
            using(var form=new MainForm(true))
            {
                form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();
                var button=(Button)typeof(MainForm).GetField("import",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                Check(button.AccessibleName.Contains("pasted"),"pasted import is not accessible");
                var tools=(Button)typeof(MainForm).GetField("libraryTools",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                Check(tools.ContextMenuStrip.Items.Cast<ToolStripItem>().Any(i=>i.Text=="Preview safe diagnostics..."),"safe diagnostics menu missing");
            }
        }
        finally {Directory.Delete(root,true);}
    }
}
