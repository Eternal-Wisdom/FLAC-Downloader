using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PlaylistFlac;

internal static class ProposalReviewTests
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    private static void Reject(Action work){try{work();}catch(FormatException){return;}throw new Exception("Oversized CSV accepted");}
    internal static void Run()
    {
        var target=(TargetFrameworkAttribute)Attribute.GetCustomAttribute(typeof(Track).Assembly,typeof(TargetFrameworkAttribute));
        Check(target!=null && target.FrameworkName==".NETFramework,Version=v4.8","App framework target is not declared");
        var originalProtocol=ServicePointManager.SecurityProtocol;
        try {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.SystemDefault;
            using(var importer=new SpotifyImporter()){}
            Check(ServicePointManager.SecurityProtocol==SecurityProtocolType.SystemDefault,"Import changed OS TLS selection");
        } finally {ServicePointManager.SecurityProtocol=originalProtocol;}
        Reject(()=>CsvPlaylist.Parse(new string('x',CsvPlaylist.MaxCharacters+1),"test","test"));
        Reject(()=>CsvPlaylist.Parse("title,artist\n\""+new string('x',65537)+"\",artist","test","test"));
        Check(CsvPlaylist.Parse("title,artist\n\""+new string('x',65536)+"\",artist","test","test").Tracks.Count==1,"Field boundary rejected");
        Reject(()=>CsvPlaylist.Parse(String.Join(",",Enumerable.Repeat("column",257))+"\nx,y","test","test"));
        var batch=new StringBuilder("title,artist\n");for(int i=0;i<CsvPlaylist.MaxTracks;i++)batch.Append("Song,Artist\n");
        Check(CsvPlaylist.Parse(batch.ToString(),"test","test").Tracks.Count==CsvPlaylist.MaxTracks,"50,000-track boundary rejected");
        batch.Append("Extra,Artist\n");Reject(()=>CsvPlaylist.Parse(batch.ToString(),"test","test"));
        string root=Path.Combine(Path.GetTempPath(),"PF-Proposal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string file=Path.Combine(root,"large.csv");using(var stream=File.Create(file))stream.SetLength(CsvPlaylist.MaxFileBytes+1);
            Reject(()=>CsvPlaylist.Read(file));Check(new FileInfo(file).Length==CsvPlaylist.MaxFileBytes+1,"Rejected import changed input");
            string report=SafeDiagnostics.Create(2,1,1,0,true,20,"Balanced",true,false);
            string output=Path.Combine(root,"diagnostics.txt");File.WriteAllText(output,"old");IndexStore.AtomicWrite(output,report);
            Check(File.ReadAllText(output)==report && Directory.GetFiles(root,"*.tmp").Length==0,"Diagnostics export differed from preview or left debris");
        } finally {Directory.Delete(root,true);}
        var context=SynchronizationContext.Current;
        try {using(var main=new MainForm(true))using(var timer=new System.Windows.Forms.Timer {Interval=20}) {
            main.ShowInTaskbar=false;main.Opacity=0;main.Show();bool inspected=false;string error=null;
            timer.Tick+=delegate {
                var window=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text=="Safe diagnostics preview");if(window==null)return;
                try {var buttons=window.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().ToList();inspected=buttons.Any(b=>b.Text=="Save diagnostics..." && b.AccessibleName.Contains("privacy-safe"));}
                catch(Exception ex){error=ex.Message;}finally{timer.Stop();window.Close();}
            };
            timer.Start();typeof(MainForm).GetMethod("ShowDiagnostics",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(main,null);
            Check(inspected && error==null,"Diagnostics save action missing: "+error);main.Close();
        }}finally{SynchronizationContext.SetSynchronizationContext(context);}
    }
}
