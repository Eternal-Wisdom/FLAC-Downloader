using System;
using System.Collections.Generic;
using System.IO;
using PlaylistFlac;

internal static class AlbumAvailabilityTests
{
    internal static void Run()
    {
        var playlist=CsvPlaylist.Parse("title,artist,album,disc_number,track_number\nSecond,Artist,Album,2,3\nFirst,Artist,Album,1,1\nOther,Other Artist,Album,,\n","Synthetic","test");
        if(playlist.Tracks[0].DiscNumber!=2 || playlist.Tracks[0].TrackNumber!=3 || playlist.Tracks[2].TrackNumber!=0)throw new Exception("Disc and track columns were not parsed.");
        var states=new Dictionary<string,string>{{IndexStore.Key(playlist.Tracks[0]),"Failed"},{IndexStore.Key(playlist.Tracks[1]),"Downloaded"}};
        string summary=AlbumAvailability.Describe(playlist,states);
        if(!summary.Contains("1 / 2 downloaded; 1 missing") || summary.IndexOf("1.1  First")>summary.IndexOf("2.3  Second") || !summary.Contains("Other Artist — Album") || !summary.Contains("imported songs only"))throw new Exception("Album summary mixed artists, lost order, or overstated completeness.");
        string path=Path.Combine(Path.GetTempPath(),"FlacPositions-"+Guid.NewGuid().ToString("N")+".csv");
        try {CsvPlaylist.Write(path,playlist);var restored=CsvPlaylist.Read(path);if(restored.Tracks[0].DiscNumber!=2 || restored.Tracks[0].TrackNumber!=3 || restored.Tracks[0].Title!="Second")throw new Exception("CSV round trip lost positions or changed playlist order.");}finally{File.Delete(path);}
    }
}
