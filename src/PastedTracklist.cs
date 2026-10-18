using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PlaylistFlac
{
    internal static class PastedTracklist
    {
        internal static Playlist Parse(string text)
        {
            if(String.IsNullOrWhiteSpace(text))throw new FormatException("Paste one Artist - Title per line, or a CSV with title and artist columns.");
            if(text.Length>2*1024*1024)throw new FormatException("The pasted list is too large. Import a CSV file instead.");
            text=text.TrimStart('\uFEFF');
            string first=text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).First().Trim();
            Playlist result;
            if(first.IndexOf(',')>=0 || first.IndexOf('\t')>=0 || first.IndexOf(';')>=0)
            {
                // Delimited artist/title text is ambiguous: require a proper CSV header instead of guessing.
                try {result=CsvPlaylist.Parse(text,"Pasted songs",null);}
                catch(FormatException) {result=ParseLines(text);}
            }
            else result=ParseLines(text);
            if(result.Tracks.Count==0)throw new FormatException("The list contains no complete songs.");
            // Stable content identity lets the same pasted list find its earlier download folder.
            using(var sha=SHA256.Create())
                result.Source="pasted:"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(String.Join("\n",result.Tracks.Select(t=>IndexStore.Key(t)))))).Replace("-","").ToLowerInvariant();
            return result;
        }
        private static Playlist ParseLines(string text)
        {
            var result=new Playlist {Name="Pasted songs"};int lineNumber=0;
            using(var reader=new StringReader(text))
            {
                string line;while((line=reader.ReadLine())!=null)
                {
                    lineNumber++;if(String.IsNullOrWhiteSpace(line))continue;
                    int position=-1;string separator=null;
                    foreach(string candidate in new[]{" - "," — "," – "})
                    {int index=line.IndexOf(candidate,StringComparison.Ordinal);if(index>=0 && (position<0 || index<position)){position=index;separator=candidate;}}
                    if(position<0)throw new FormatException("Line "+lineNumber+" needs Artist - Title. Nothing was imported.");
                    string artist=line.Substring(0,position).Trim(),title=line.Substring(position+separator.Length).Trim();
                    if(artist.Length==0 || title.Length==0 || artist.Any(Char.IsControl) || title.Any(Char.IsControl))
                        throw new FormatException("Line "+lineNumber+" needs a complete artist and title. Nothing was imported.");
                    result.Tracks.Add(new Track {Artist=artist,Artists=new[]{artist},Title=title});
                    if(result.Tracks.Count>10000)throw new FormatException("The pasted list exceeds 10,000 songs. Import a CSV file instead.");
                }
            }
            return result;
        }
    }
}
