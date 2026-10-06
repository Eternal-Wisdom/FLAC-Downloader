using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PlaylistFlac
{
    public static class SearchNames
    {
        private static readonly Regex NativeScript = new Regex(@"[\u3040-\u30ff\u3400-\u9fff\uac00-\ud7af]", RegexOptions.Compiled);
        private static readonly Regex LatinScript = new Regex(@"[A-Za-z\u00c0-\u024f]", RegexOptions.Compiled);
        private static readonly Regex VersionWord = new Regex(@"\b(remix|mix|live|instrumental|karaoke|cover|version|ver|edit|acoustic|remaster(?:ed)?|slowed|sped|speed|demo|radio|reprise|rework|feat|ft|featuring|with|from|theme|opening|ending|soundtrack|explicit|clean|mono|stereo|bonus|session|take|tv|dj)\b|カバー|ライブ|インスト|混音|伴奏|现场|現場|翻唱", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // These are alternative search labels, not replacement library metadata.
        // Only labels already present in the source are used; kanji readings are never guessed.
        public static List<Track> GetVariants(Track original)
        {
            if (original == null) throw new ArgumentNullException("original");
            var result = new List<Track>();
            string title = original.Title ?? "", artist = original.Artist ?? "";
            string primary = original.PrimaryArtist ?? artist;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            seen.Add(Key(title, artist));

            string normalizedTitle = Normalize(title), normalizedArtist = Normalize(primary);
            string withoutCredits = Regex.Replace(normalizedTitle, @"\s*\(\s*(?:feat\.?|ft\.?|featuring)\s+[^()]+\)\s*$", "", RegexOptions.IgnoreCase).Trim();
            var bilingual = BilingualTitles(withoutCredits).ToList();
            foreach (string alternate in bilingual.Where(x => NativeScript.IsMatch(x)))
                Add(result, seen, original, alternate, normalizedArtist);
            Add(result, seen, original, title, primary);
            foreach (string alternate in bilingual.Where(x => !NativeScript.IsMatch(x)))
                Add(result, seen, original, alternate, normalizedArtist);
            if (withoutCredits != normalizedTitle) Add(result, seen, original, withoutCredits, normalizedArtist);
            Add(result, seen, original, normalizedTitle, normalizedArtist);
            return result;
        }

        private static void Add(List<Track> result, HashSet<string> seen, Track original, string title, string artist)
        {
            if (result.Count >= 4 || String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(artist) || !seen.Add(Key(title, artist))) return;
            result.Add(new Track {
                Title = title, Artist = artist, Artists = new [] { artist }, Album = original.Album,
                DurationSeconds = original.DurationSeconds, SpotifyId = original.SpotifyId, Isrc = original.Isrc
            });
        }

        private static string Normalize(string text)
        {
            return Regex.Replace((text ?? "").Normalize(NormalizationForm.FormKC), @"\s+", " ").Trim();
        }

        private static string Key(string title, string artist) { return title + "\u001f" + artist; }

        private static IEnumerable<string> BilingualTitles(string title)
        {
            // Removing qualifiers can select a different recording. Keep such titles intact.
            if (VersionWord.IsMatch(title)) return new string[0];
            string[] pieces = Regex.Split(title, @"\s+[-\u2013\u2014]\s+");
            if (pieces.Length == 2 && AreAlternateScripts(pieces[0], pieces[1])) return pieces.Select(x => x.Trim());

            Match brackets = Regex.Match(title, @"^(.*?)\s*\(([^()]+)\)$");
            if (!brackets.Success) brackets = Regex.Match(title, @"^(.*?)\s*\[([^\[\]]+)\]$");
            if (brackets.Success && AreAlternateScripts(brackets.Groups[1].Value, brackets.Groups[2].Value))
                return new [] { brackets.Groups[1].Value.Trim(), brackets.Groups[2].Value.Trim() };
            return new string[0];
        }

        private static bool AreAlternateScripts(string left, string right)
        {
            bool leftNative = NativeScript.IsMatch(left), rightNative = NativeScript.IsMatch(right);
            return left.Trim().Length > 0 && right.Trim().Length > 0 &&
                (leftNative && !rightNative && LatinScript.IsMatch(right) || rightNative && !leftNative && LatinScript.IsMatch(left));
        }
    }
}
