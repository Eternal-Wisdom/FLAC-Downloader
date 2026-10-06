using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaylistFlac
{
    public static class SearchNameTests
    {
        private static int checks;
        public static void Run()
        {
            checks = 0;
            var original = new Track { Title = "けっかおーらい - Kekka Orai", Artist = "Kocchi no Kento", Artists = new [] { "Kocchi no Kento" }, Album = "Album", DurationSeconds = 172.55, Isrc = "JPU902500397", SpotifyId = "7Ea65yIjXDZDfg39hsvcE6" };
            var variants = SearchNames.GetVariants(original);
            Check(variants.Count == 2, "two bilingual alternatives");
            Check(variants.Any(x => x.Title == "けっかおーらい"), "native label retained");
            Check(variants.Any(x => x.Title == "Kekka Orai"), "explicit romanized label");
            Check(variants.All(x => x.Album == original.Album && x.DurationSeconds == original.DurationSeconds && x.Isrc == original.Isrc && x.SpotifyId == original.SpotifyId), "identity metadata retained");
            Check(original.Title == "けっかおーらい - Kekka Orai", "original untouched");

            var featured = new Track { Title = "ファタール - Fatal", Artist = "GEMN, Kento Nakajima, Tatsuya Kitani", Artists = new [] { "GEMN", "Kento Nakajima", "Tatsuya Kitani" } };
            variants = SearchNames.GetVariants(featured);
            Check(variants.Count == 3 && variants.All(x => x.Artist == "GEMN"), "primary artist avoids requiring every credit");
            Check(variants[0].Title == "ファタール" && variants[1].Title == featured.Title && variants[2].Title == "Fatal", "native, primary, romanized order");

            var escaped = new Track { Title = "EARFQUAKE", Artist = @"Tyler\, The Creator", Artists = new [] { "Tyler, The Creator" } };
            variants = SearchNames.GetVariants(escaped);
            Check(variants.Count == 1 && variants[0].Artist == "Tyler, The Creator", "escaped display artist becomes correct search name");
            Check(escaped.Artist == @"Tyler\, The Creator", "display identity remains exact");

            var commaArtist = new Track { Title = "September", Artist = "Earth, Wind & Fire" };
            Check(SearchNames.GetVariants(commaArtist).Count == 0, "plain CSV comma does not invent primary artist");
            var nativeOnly = new Track { Title = "靴の花火", Artist = "Yorushika" };
            Check(SearchNames.GetVariants(nativeOnly).Count == 0, "no fabricated romanization");
            variants = SearchNames.GetVariants(new Track { Title = "Good Flirts (feat. Kendrick Lamar & Momo Boyd)", Artist = "Baby Keem, Kendrick Lamar, Momo Boyd", Artists = new [] { "Baby Keem", "Kendrick Lamar", "Momo Boyd" } });
            Check(variants.Any(x => x.Title == "Good Flirts" && x.Artist == "Baby Keem"), "explicit featured credits omitted in search variant");
            variants = SearchNames.GetVariants(new Track { Title = "Song - Remix (ft. Guest)", Artist = "Artist" });
            Check(variants.Count == 1 && variants[0].Title == "Song - Remix", "featured credit removal preserves recording qualifier");

            variants = SearchNames.GetVariants(new Track { Title = "弥渡山歌（Midu Echoing）", Artist = "YANGYINYUE" });
            Check(variants.Any(x => x.Title == "弥渡山歌(Midu Echoing)"), "fullwidth punctuation normalized");
            Check(variants.Any(x => x.Title == "弥渡山歌") && variants.Any(x => x.Title == "Midu Echoing"), "parenthetical explicit aliases");
            variants = SearchNames.GetVariants(new Track { Title = "Kekka Orai - けっかおーらい", Artist = "Artist" });
            Check(variants.Count == 2, "reverse bilingual order");

            foreach (string title in new [] { "夜のピエロ - TeddyLoid Remix", "ドーナツホール - COVER", "202 feat. 泉まくら - New Mix", "ないものねだり - Revenge THE FIRST TAKE", "夜 - Live", "夜 (Instrumental)", "Native - 日本語 - Remix", "夜 - DJ版", "Title - Acoustic", "夜 - ライブ" })
            {
                variants = SearchNames.GetVariants(new Track { Title = title, Artist = "Main, Guest", Artists = new [] { "Main", "Guest" } });
                Check(variants.All(x => x.Title == title), "recording qualifier preserved: " + title);
            }
            variants = SearchNames.GetVariants(new Track { Title = "弥渡山歌（Midu Echoing）", Artist = "Ｍａｉｎ, Guest", Artists = new [] { "Ｍａｉｎ", "Guest" } });
            Check(variants.Count <= 4, "variant limit");
            Check(variants.Select(x => x.Title + "\u001f" + x.Artist).Distinct(StringComparer.OrdinalIgnoreCase).Count() == variants.Count, "variants distinct");
            Console.WriteLine("Search names: " + checks + " checks passed.");
        }

#if SEARCH_NAME_TEST_RUNNER
        public static void Main() { ImportTests.Run(); Run(); }
#endif

        private static void Check(bool passed, string label)
        {
            checks++;
            if (!passed) throw new Exception("Search-name test failed: " + label);
        }
    }
}
