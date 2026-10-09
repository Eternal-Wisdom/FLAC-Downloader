using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlaylistFlac;

internal static class MissingTrackExportTests
{
    internal static void Run()
    {
        var song=new Track {Title="KING",Artist="Artist A",Album="Original",DurationSeconds=180};
        var reissue=new Track {Title="KING",Artist="Artist A",Album="Deluxe",DurationSeconds=180};
        var other=new Track {Title="KING",Artist="Artist B",DurationSeconds=180};
        var japanese=new Track {Title="感情御中, \"song\"",Artist="日本語",DurationSeconds=123.456};
        var ready=new Track {Title="Untested",Artist="A"};
        var review=new Track {Title="Conflict",Artist="A"};
        var list=new Playlist {Tracks=new List<Track>{song,reissue,other,japanese,ready,review}};
        var states=list.Tracks.ToDictionary(IndexStore.Key,t=>"Failed");
        states[IndexStore.Key(ready)]="Ready";states[IndexStore.Key(review)]="Review versions";
        var result=MissingTrackExport.Select(list,states);
        Check(result.Tracks.Count==3,"reissues deduplicated; other artists retained; ready and review excluded");
        states[IndexStore.Key(reissue)]="Downloaded";
        result=MissingTrackExport.Select(list,states);
        Check(result.Tracks.Count==2 && !result.Tracks.Contains(song),"a downloaded reissue suppresses its failed counterpart");
        string root=Path.Combine(Path.GetTempPath(),"MissingExport-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string path=Path.Combine(root,"missing.csv");CsvPlaylist.Write(path,result);
            var read=CsvPlaylist.Read(path);
            Check(read.Tracks.Count==2 && read.Tracks.Any(t=>t.Title==japanese.Title && t.DurationSeconds==123.456),"Unicode, quotes, commas, and duration reimport unchanged");
            byte[] header=new byte[42];header[0]=102;header[1]=76;header[2]=97;header[3]=67;header[4]=128;header[7]=34;header[8]=16;header[10]=16;
            ulong packed=((ulong)96000<<44)|((ulong)1<<41)|((ulong)23<<36)|96000;
            for(int i=25;i>=18;i--){header[i]=(byte)packed;packed>>=8;}
            string file=Path.Combine(root,"sample.flac");File.WriteAllBytes(file,header);
            string description=TrackDetails.DescribeLocalHeader(file);
            Check(description.Contains("24-bit / 96 kHz / 2 channel") && description.Contains("have not been decoded"),"header facts are labelled without authenticity claims");
            File.WriteAllText(file,"not flac");
            Check(TrackDetails.DescribeLocalHeader(file).Contains("invalid"),"invalid audio never receives a quality label");
            Check(TrackDetails.DescribeLocalHeader(Path.Combine(root,"missing.flac")).Contains("invalid"),"missing file is handled");
        } finally {Directory.Delete(root,true);}
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception("Export/details: "+message);}
}
