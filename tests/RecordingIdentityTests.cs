using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PlaylistFlac
{
    internal static class RecordingIdentityTests
    {
        private static int checks;
        internal static void Run()
        {
            checks = 0;
            var kanye = Make("KING", "Kanye West, Ye", "BULLY", 188.4, "QZQAY2608133", "4LP6bmyAxdMNrqUrMpaVQ8");
            kanye.Artists = new[] { "Kanye West", "Ye" };
            var deluxe = Make("KING", "Kanye West, Ye", "BULLY - DELUXE", 188.4, "QZQAY2608133", "0bzDXvdOnnU4AuHJb3QfUP");
            deluxe.Artists = new[] { "Kanye West", "Ye" };
            var kanaria = Make("KING", "Kanaria", "KING", 134.0, "JPX562000062", "5vCNAauCaecW0tT2mZDLG9");
            var groups = RecordingGroups.Build(new[] { kanye, deluxe, kanaria, kanye });
            Check(groups.Groups.Count == 2, "standard and deluxe reuse one recording; Japanese KING stays separate");
            Check(groups.GroupFor(kanye) == groups.GroupFor(deluxe), "release ID can differ for the same recording");
            Check(groups.GroupFor(kanaria) != groups.GroupFor(kanye), "same title from another artist stays separate");
            Check(groups.Groups[0].Tracks.Count == 3, "repeated playlist rows retained");
            Check(groups.Groups[0].Representative == kanye && groups.Groups[0].Key == IndexStore.Key(kanye), "stable first representative");
            Check(groups.GroupForKey(IndexStore.Key(deluxe)) == groups.GroupFor(kanye), "every source key addresses its recording");
            Check(groups.GroupForKey("absent") == null && groups.GroupForKey(null) == null && groups.GroupFor(null) == null, "missing key is harmless");
            Check(kanye.Album == "BULLY" && deluxe.Album == "BULLY - DELUXE", "source metadata is untouched");

            Pair(Make("Song", "Artist", "Original", 200), Make("Song", "Artist", "Greatest hits", 201.9), true, "known metadata fallback across albums");
            Pair(Make("Song", "Artist", "A", 200), Make("Song", "Artist", "B", 202), true, "inclusive two-second tolerance");
            Pair(Make("Song", "Artist", "A", 200), Make("Song", "Artist", "B", 202.001), false, "longer edit stays separate");
            Pair(Make("Song", "Artist", "A", 200, "USAAA2000001"), Make("Song", "Artist", "B", 201, "USAAA2000002"), false, "conflicting ISRC stays separate");
            Pair(Make("Song", "Artist", "A", 200, "USAAA2000001"), Make("Song", "Artist", "B", 230, "USAAA2000001"), false, "same ISRC does not override duration difference");
            Pair(Make("Song", "Artist", "A", 200, "USAAA2000001"), Make("Song", "Artist", "B", 200, "us-aaa-20-00001"), true, "ISRC hyphens and case normalize");

            foreach (string version in new[] { "Song - Live", "Song (Remix)", "Song - Acoustic", "Song - 2020 Remaster", "Song (Instrumental)", "Song - Radio Edit", "Song - ライブ" })
                Pair(Make("Song", "Artist", "A", 200, "USAAA2000001"), Make(version, "Artist", "B", 200, "USAAA2000001"), false, "version preserved: " + version);
            Pair(Make("Song", "Artist", "A", 200), Make("歌", "Artist", "B", 200), false, "language is not guessed");
            Pair(Make("ＳＯＮＧ　名", "Ａｒｔｉｓｔ", "A", 200), Make("song  名", "artist", "B", 200), true, "Unicode width case and whitespace normalize");
            Pair(Make("café", "Artist", "A", 200), Make("cafe\u0301", "Artist", "B", 200), true, "composed accents normalize");
            Pair(Make("Song!", "Artist", "A", 200), Make("Song", "Artist", "B", 200), false, "punctuation is preserved");

            var duet = Make("Song", "Main, Guest", "A", 200); duet.Artists = new[] { "Main", "Guest" };
            var solo = Make("Song", "Main", "B", 200); solo.Artists = new[] { "Main" };
            Pair(duet, solo, false, "all artist credits matter");
            var differentGuest = Make("Song", "Main, Other", "B", 200); differentGuest.Artists = new[] { "Main", "Other" };
            Pair(duet, differentGuest, false, "different collaborator stays separate");
            var commaArtist = Make("Song", "Main, Guest", "B", 200); commaArtist.Artists = new[] { "Main, Guest" };
            Pair(duet, commaArtist, false, "artist boundaries cannot collide");

            Pair(Make("Song", "Artist", "A", 0), Make("Song", "Artist", "B", 0), false, "unknown length without IDs stays separate");
            Pair(Make("Song", "Artist", "A", 200), Make("Song", "Artist", "B", 0), false, "one unknown length needs an identifier");
            Pair(Make("Song", "Artist", "A", 0, "USAAA2000001"), Make("Song", "Artist", "B", 0, "USAAA2000001"), true, "same valid ISRC supports unknown length");
            Pair(Make("Song", "Artist", "A", Double.NaN, "USAAA2000001"), Make("Song", "Artist", "B", Double.PositiveInfinity, "USAAA2000001"), true, "nonfinite lengths treated as unknown");
            Pair(Make("Song", "Artist", "A", 0, "not-an-isrc"), Make("Song", "Artist", "B", 0, "not-an-isrc"), false, "invalid recording codes do not prove identity");
            Pair(Make("Song", "Artist", "A", 0, null, "4LP6bmyAxdMNrqUrMpaVQ8"), Make("Song", "Artist", "B", 200, null, "spotify:track:4LP6bmyAxdMNrqUrMpaVQ8"), true, "same valid Spotify identifier supports unknown length");
            Pair(Make("Song", "Artist", "A", 0, null, "bad-id"), Make("Song", "Artist", "B", 0, null, "bad-id"), false, "invalid Spotify ID does not prove identity");
            Pair(Make("Song", "Artist", "A", 200, "USAAA2000001", "4LP6bmyAxdMNrqUrMpaVQ8"), Make("Song", "Artist", "B", 200, "USAAA2000002", "4LP6bmyAxdMNrqUrMpaVQ8"), false, "conflicting recording codes override shared Spotify ID");

            var chain = new[] { Make("Song", "Artist", "A", 200), Make("Song", "Artist", "B", 202), Make("Song", "Artist", "C", 204) };
            groups = RecordingGroups.Build(chain);
            Check(groups.Groups.Count == 2 && groups.Groups[0].Tracks.Count == 2, "duration tolerance cannot chain through the middle track");
            Array.Reverse(chain);
            groups = RecordingGroups.Build(chain);
            Check(groups.Groups.Count == 2 && groups.Groups[0].Tracks.Count == 2, "duration bounds work in either order");
            groups = RecordingGroups.Build(new[] { Make("Song", "Artist", "A", 200, "USAAA2000001"), Make("Song", "Artist", "B", 200), Make("Song", "Artist", "C", 0, "USAAA2000001") });
            Check(groups.Groups.Count == 2, "unknown duration does not inherit unrelated group member identity");
            groups = RecordingGroups.Build(new[] { Make("Song", "Artist", "A", 0, "USAAA2000001"), Make("Song", "Artist", "B", 200, "USAAA2000001"), Make("Song", "Artist", "C", 200) });
            Check(groups.Groups.Count == 2, "known duration does not bridge an unidentified track into unknown-duration group");
            Pair(Make("", "Artist", "A", 200), Make("", "Artist", "B", 200), false, "empty title never establishes identity");
            Pair(Make("Song", "", "A", 200), Make("Song", "", "B", 200), false, "empty artist never establishes identity");

            var conflictA = Make("Song", "Artist", "Same album", 200, "USAAA2000001");
            var conflictB = Make("Song", "Artist", "Same album", 200, "USAAA2000002");
            groups = RecordingGroups.Build(new[] { conflictA, conflictB });
            Check(groups.Groups.Count == 2 && groups.Groups[0].Key != groups.Groups[1].Key, "groups remain distinct when legacy source keys collide");
            Check(groups.GroupFor(conflictA) != groups.GroupFor(conflictB), "exact source lookup retains conflicting recording IDs");
            Check(groups.GroupForKey(IndexStore.Key(conflictA)) == null, "ambiguous source keys never select an arbitrary recording");

            var large = new List<Track>();
            for (int i = 0; i < 10000; i++) large.Add(Make("Track " + i, "Artist", "A", 200));
            for (int i = 0; i < 10000; i++) large.Add(Make("Track " + i, "Artist", "Deluxe", 201));
            var watch = Stopwatch.StartNew();
            groups = RecordingGroups.Build(large); watch.Stop();
            Check(groups.Groups.Count == 10000 && groups.Groups.All(x => x.Tracks.Count == 2), "large playlist grouping uses indexed metadata buckets");
            Check(groups.GroupForKey(IndexStore.Key(large[19999])) == groups.GroupFor(large[9999]), "large playlist key lookup");
            Check(RecordingGroups.Build(new Track[] { null }).Groups.Count == 0, "null rows ignored");
            Console.WriteLine("Recording identity: " + checks + " checks passed; 20,000 rows grouped in " + watch.ElapsedMilliseconds + " ms.");
        }

        private static Track Make(string title, string artist, string album, double seconds, string isrc = null, string spotify = null)
        { return new Track { Title = title, Artist = artist, Album = album, DurationSeconds = seconds, Isrc = isrc, SpotifyId = spotify }; }
        private static void Pair(Track a, Track b, bool same, string label)
        { Check((RecordingGroups.Build(new[] { a, b }).Groups.Count == 1) == same, label); }
        private static void Check(bool value, string label)
        { checks++; if (!value) throw new Exception("Recording identity: " + label); }

#if RECORDING_IDENTITY_TEST_RUNNER
        public static void Main() { Run(); }
#endif
    }
}
