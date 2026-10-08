using System;
using PlaylistFlac;

internal static class TransferTelemetryTests
{
    internal static void Run()
    {
        var clock=new DateTime(2026,10,4,0,0,0,DateTimeKind.Utc);var meter=new TransferTelemetry();
        Check(meter.Observe("a",1000,10000,clock)==0,"resume offset isn't speed");
        Check(meter.Observe("a",3000,10000,clock.AddSeconds(2))==1000,"rate from byte difference");
        meter.Observe("b",0,20000,clock);meter.Observe("b",4000,20000,clock.AddSeconds(2));
        Check(meter.Speed(clock.AddSeconds(2))==3000 && meter.Moving(clock.AddSeconds(2))==2,"independent streams aggregate");
        Check(meter.Speed(clock.AddSeconds(7))==0 && meter.Moving(clock.AddSeconds(7))==0,"silent jobs expire");
        Check(meter.Observe("a",0,10000,clock.AddSeconds(3))==0,"retry resets baseline");
        Check(meter.Observe("a",1000,10000,clock.AddSeconds(4))==1000,"rate recovers after retry");
        Check(meter.Observe("",5000,10000,clock)==0,"unidentified progress excluded");
        meter.Finish("a");Check(meter.Moving(clock.AddSeconds(4))==1,"terminal jobs removed");
        meter.Clear();Check(meter.Speed(clock.AddSeconds(4))==0,"pass reset");
        using(var engine=new DownloadEngine())
        {
            EngineProgress latest=null;engine.Progress+=p=>latest=p;
            Check(engine.TryProgress("{\"type\":\"download_progress\",\"data\":{\"jobId\":\"job-1\",\"bytesTransferred\":4294967296,\"totalBytes\":8589934592,\"percent\":50}}"),"job-only event parses");
            Check(latest.JobId=="job-1" && latest.Title=="" && latest.BytesTransferred==4294967296L && latest.TotalBytes==8589934592L,"64-bit counters retained without inventing song identity");
            Check(engine.TryProgress("{\"type\":\"download_start\",\"data\":{\"artist\":\"Artist\",\"title\":\"Song\",\"username\":\"SyntheticPeer\",\"filename\":\"Album/Song.flac\",\"extension\":\"flac\",\"size\":1048576}}"),"source event parses");
            Check(latest.TotalBytes==1048576 && latest.SourceFilename=="Album/Song.flac" && latest.SourceFormat=="flac" && latest.Status.Contains("waiting for bytes"),"source metadata retained without claiming active transfer");
            var details=new TrackDetails();details.Observe(latest);
            var track=new Track {Title="Song",Artist="Artist",Album="Album"};
            string description=details.Describe(track,latest.Status,"Balanced",true);
            Check(description.Contains("SyntheticPeer") && description.Contains("1.00 MiB") && description.Contains("Not available"),"source details keep unknown progress explicit");
            details.Observe(new EngineProgress {Type="download_start",Peer="SecondPeer"});
            description=details.Describe(track,"Waiting","Balanced",true);
            Check(!description.Contains("SyntheticPeer") && !description.Contains("Album/Song.flac"),"a new source cannot inherit stale metadata");
        }
        using(var engine=new DownloadEngine())
        {
            engine.Progress+=p=>{throw new FormatException("synthetic subscriber error");};
            bool propagated=false;try {engine.TryProgress("{\"type\":\"search_start\",\"data\":{\"title\":\"Song\"}}");}catch(FormatException){propagated=true;}
            Check(propagated,"subscriber errors are not swallowed as malformed JSON");
        }
        foreach(string profile in new[]{"Fast FLAC","Balanced","Prefer high resolution"})
        {
            string args=DownloadEngine.BuildArguments("in","out","config",true,"index","playlist",new DownloadTuning(20,profile));
            Check(args.Contains("\"--format\" \"flac\"") && args.Contains("\"--strict-title\""),"profiles preserve identity and format");
            Check(args.Contains("\"--fast-search\" \""+(profile=="Fast FLAC"?"true":"false")+"\""),"profile search policy");
        }
        Check(DownloadTuning.NormalizeQuality("bad")=="Prefer high resolution","legacy settings default");
    }
    private static void Check(bool value,string reason) {if(!value)throw new Exception("Transfer telemetry: "+reason);}
}
