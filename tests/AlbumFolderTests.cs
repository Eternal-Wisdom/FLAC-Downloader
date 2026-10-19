using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using PlaylistFlac;

internal static class AlbumFolderTests
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    private static object FileRow(string title,int length=180,string album="Album",int depth=16)
    {return new {Filename="Music\\Artist\\"+album+"\\01 - "+title+".flac",Length=length,Size=4096,BitDepth=depth,SampleRate=44100};}
    private static object Folder(object[] files,int speed=1,bool free=true)
    {return new {User=new {Username="Synthetic peer + %",UploadSpeed=speed,HasFreeUploadSlot=free},Files=files};}
    private static string Json(params object[] folders){return new JavaScriptSerializer().Serialize(folders);}
    internal static void Run()
    {
        var a=new Track{Title="First",Artist="Artist",Album="Album",DurationSeconds=180};
        var b=new Track{Title="日本語",Artist="Artist",Album="Album",DurationSeconds=180};
        var all=new List<Track>{a,b};var pending=new List<Track>{b};
        var p=new Playlist{Source="https://open.spotify.com/album/0000000000000000000000",Tracks=all};
        Check(AlbumFolderSearch.Applies(p),"album mode not selected");p.Source="csv";Check(!AlbumFolderSearch.Applies(p),"playlist accidentally treated as album");
        var selected=AlbumFolderSearch.Select(Json(Folder(new[]{FileRow("First"),FileRow("日本語")},1),Folder(new[]{FileRow("日本語")},999)),all,pending,DownloadTuning.Default);
        Check(selected.Coverage==2 && selected.Files.Count==1 && selected.Files[0].Track==b,"coverage or existing-file exclusion failed");
        Check(selected.Files[0].Link.Contains("Synthetic%20peer%20%2B%20%25") && !selected.Files[0].Link.Contains("\n"),"remote URI escaping");
        var update=new EngineProgress{SourceFilename=selected.Files[0].RemotePath,Title="remote file"};AlbumFolderSearch.MapProgress(selected,update);
        Check(update.TrackKey==IndexStore.Key(b) && update.Title==b.Title,"album progress lost original recording identity");
        update=new EngineProgress{Title="unknown"};AlbumFolderSearch.MapProgress(selected,update);Check(update.TrackKey=="" && update.Title=="","unknown album progress claimed another track");
        Check(AlbumFolderSearch.Select(Json(Folder(new[]{FileRow("日本語 (Live)"),FileRow("First",250)})),all,pending,DownloadTuning.Default)==null,"version/duration mismatch accepted");
        Check(AlbumFolderSearch.Select(Json(Folder(new[]{FileRow("日本語",180,"Other")})),all,pending,DownloadTuning.Default)==null,"wrong album accepted");
        Check(AlbumFolderSearch.Select(Json(Folder(new[]{FileRow("日本語"),FileRow("日本語")})),all,pending,DownloadTuning.Default)==null,"ambiguous duplicate file accepted");
        Check(AlbumFolderSearch.Select("not json",all,pending,DownloadTuning.Default)==null,"malformed peer result accepted");
        NativeEngine();
        Recovery();
    }
    private static void Recovery()
    {
        string root=Path.Combine(Path.GetTempPath(),"FlacAlbumFolderTest-"+Guid.NewGuid().ToString("N"));
        string incoming=Path.Combine(root,".incoming",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(incoming);Directory.CreateDirectory(Path.Combine(root,".search"));
        try {
            var tracks=Enumerable.Range(1,4).Select(i=>new Track{Title="Song "+i,Artist="Artist",Album="Album",DurationSeconds=180}).ToList();
            var make=typeof(AuditTests).GetMethod("MakeHeader",BindingFlags.Static|BindingFlags.NonPublic);
            byte[] valid=(byte[])make.Invoke(null,new object[]{44100,16,2,(ulong)(44100*180)});
            File.WriteAllBytes(Path.Combine(incoming,"1.flac"),valid);
            File.WriteAllBytes(Path.Combine(incoming,"2.flac"),(byte[])make.Invoke(null,new object[]{44100,16,2,(ulong)(44100*240)}));
            File.WriteAllBytes(Path.Combine(incoming,"3.flac"),new byte[]{1,2,3});
            File.WriteAllBytes(Path.Combine(incoming,"4.flac"),valid);
            string existing=Path.Combine(root,"existing.flac");File.WriteAllBytes(existing,valid);
            var index=new Dictionary<string,SavedTrack>{{IndexStore.Key(tracks[3]),new SavedTrack{Track=tracks[3],Path=existing,State=1}}};
            File.WriteAllText(Path.Combine(root,".search","album-map.json"),new JavaScriptSerializer().Serialize(new AlbumRecovery{Incoming=incoming,Tracks=tracks}));
            string links=Path.Combine(root,".search","album-files.txt");File.WriteAllText(links,"synthetic private peer link");
            using(var downloader=new SmartDownloader()) {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(SmartDownloader).GetField("folder",flags).SetValue(downloader,root);
                typeof(SmartDownloader).GetField("index",flags).SetValue(downloader,index);
                typeof(SmartDownloader).GetField("recordings",flags).SetValue(downloader,RecordingGroups.Build(tracks));
                var recover=typeof(SmartDownloader).GetMethod("RecoverAlbumFiles",flags);recover.Invoke(downloader,null);
                Check(index.Count==2 && index[IndexStore.Key(tracks[0])].Path==Path.Combine(incoming,"1.flac"),"completed album recovery failed or wrong-duration/invalid audio accepted");
                Check(index[IndexStore.Key(tracks[3])].Path==existing && !File.Exists(links),"existing song replaced or private active links retained");
                recover.Invoke(downloader,null);Check(index.Count==2,"restart recovery duplicated recordings");
            }
        }
        finally {Directory.Delete(root,true);}
    }
    private static void NativeEngine()
    {
        string root=Path.Combine(Path.GetTempPath(),"FlacAlbumFolderTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string library=Path.Combine(root,"mock","Artist","Album");Directory.CreateDirectory(library);
            File.WriteAllBytes(Path.Combine(library,"01 - First.flac"),new byte[4096]);
            File.WriteAllBytes(Path.Combine(library,"02 - Second.flac"),new byte[4097]);
            string config=Path.Combine(root,"session.conf"),csv=Path.Combine(root,"album.csv"),output=Path.Combine(root,"output");Directory.CreateDirectory(output);
            File.WriteAllText(config,"username = offline-test\r\npassword = offline-test\r\n");
            File.WriteAllText(csv,"title,artist,album,length\r\n,Artist,Album,0\r\n");
            string args=DownloadEngine.BuildArguments(csv,output,config,false,Path.Combine(root,"index.csv"),Path.Combine(root,"list.m3u8"),DownloadTuning.Default,true,false);
            Check(!args.Contains("\"--song\"") && args.Contains("\"json-all\""),"album discovery still forces song mode");
            string common=" --mock-files-dir "+DownloadEngine.QuoteArgument(Path.Combine(root,"mock"))+" --mock-files-no-read-tags --searches-per-time 64 --searches-renew-time 1";
            string text=Capture(args+common,root);
            string json=text.Split('\n').Select(x=>x.Trim()).FirstOrDefault(x=>x.StartsWith("[{\"User\""));
            Check(json!=null,"real engine did not return album-folder JSON: "+text);
            var data=new JavaScriptSerializer().DeserializeObject(json) as object[];
            var entry=(Dictionary<string,object>)data[0];var files=(object[])entry["Files"];
            var user=(Dictionary<string,object>)entry["User"];
            var chosen=(Dictionary<string,object>)files[0];
            string remote=(string)chosen["Filename"],peer=(string)user["Username"];
            string list=Path.Combine(root,"direct.txt");
            File.WriteAllText(list,"slsk://"+Uri.EscapeDataString(peer)+"/"+String.Join("/",remote.Replace('/','\\').Split('\\').Select(Uri.EscapeDataString))+"\r\n",new UTF8Encoding(false));
            args=DownloadEngine.BuildArguments(list,output,config,false,Path.Combine(root,"direct-index.csv"),Path.Combine(root,"direct.m3u8"),DownloadTuning.Default,false,true);
            text=Capture(args+common,root);
            Check(File.Exists(Path.Combine(output,"1.flac")),"direct album file did not retain its stable input row: "+text);
            Check(Directory.GetFiles(output,"*.flac",SearchOption.AllDirectories).Length==1,"direct album pass downloaded unrequested files");
        }
        finally {if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("FlacAlbumFolderTest-"))Directory.Delete(root,true);}
    }
    private static string Capture(string args,string root)
    {
        string engine=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"engine","sockseek.exe");
        using(var p=Process.Start(new ProcessStartInfo(engine,args){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true})) {
            p.StandardInput.Close();var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
            if(!p.WaitForExit(30000)){p.Kill();throw new Exception("Album engine fixture timed out");}
            p.WaitForExit();string text=stdout.Result+"\n"+stderr.Result;
            Check(p.ExitCode==0,"Album engine fixture failed: "+text);return text;
        }
    }
}
