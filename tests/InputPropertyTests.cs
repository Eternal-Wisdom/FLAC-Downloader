using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PlaylistFlac;

internal static class InputPropertyTests
{
    internal static void Run()
    {
        var method=typeof(LibraryNaming).GetMethod("SafeTitle",BindingFlags.Static|BindingFlags.NonPublic);
        var random=new Random(1122);
        string[] devices={"CON","CON.txt","con .txt","PRN","AUX","NUL","COM1","LPT9","COM¹","LPT²"};
        foreach(var device in devices)
            if(!((string)method.Invoke(null,new object[]{device})).StartsWith("_"))throw new Exception("Reserved device name not escaped");
        string reservedLong=(string)method.Invoke(null,new object[]{"CON."+new string('x',300)});
        if(reservedLong.Length>150 || reservedLong!=(string)method.Invoke(null,new object[]{reservedLong}))throw new Exception("Long reserved filename is not stable");
        string alphabet="ABcd123 夜歌é<>:\"/\\|?*. \t\r\n\u007f";
        for(int i=0;i<500;i++)
        {
            string input=new string(Enumerable.Range(0,random.Next(1,400)).Select(_=>alphabet[random.Next(alphabet.Length)]).ToArray());
            string safe=(string)method.Invoke(null,new object[]{input});
            if(safe.Length==0 || safe.Length>151 || safe!=safe.Trim(' ','.') || safe.Any(c=>Char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c)))throw new Exception("Filename property failed");
            if(safe!=(string)method.Invoke(null,new object[]{safe}))throw new Exception("Filename sanitization is not idempotent");
        }
        string root=Path.Combine(Path.GetTempPath(),"FLAC-InputProperties-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var playlist=new Playlist();
            string csvAlphabet="Abc夜歌é,;\"\t\n";
            for(int i=0;i<300;i++)
                playlist.Tracks.Add(new Track{Title="A"+new string(Enumerable.Range(0,random.Next(1,80)).Select(_=>csvAlphabet[random.Next(csvAlphabet.Length)]).ToArray())+"Z",Artist="Artist, Guest",Album="Album; \"Edition\"",DurationSeconds=123});
            string path=Path.Combine(root,"synthetic.csv"); CsvPlaylist.Write(path,playlist);var restored=CsvPlaylist.Read(path);
            if(restored.Tracks.Count!=playlist.Tracks.Count || !restored.Tracks.Select(t=>t.Title).SequenceEqual(playlist.Tracks.Select(t=>t.Title)))throw new Exception("CSV quoted-field property failed");
            foreach(var malformed in new[]{"title,artist\n\"unfinished,Artist","title,artist\n\"bad\"extra,Artist"})
            {
                bool rejected=false;try{CsvPlaylist.Parse(malformed,"test","test");}catch(FormatException){rejected=true;}
                if(!rejected)throw new Exception("Malformed CSV accepted");
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
