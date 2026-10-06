using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PlaylistFlac
{
    // A recording may occur on several albums. This is separate from the source
    // index identity, which must keep each original playlist row addressable.
    internal sealed class RecordingGroup
    {
        internal Track Representative { get; private set; }
        internal List<Track> Tracks { get; private set; }
        internal string Key { get; private set; }
        private string isrc = "";
        private double minimum = Double.PositiveInfinity, maximum = Double.NegativeInfinity;
        private readonly List<RecordingDescriptor> identities = new List<RecordingDescriptor>();
        private readonly List<RecordingDescriptor> unknownLengths = new List<RecordingDescriptor>();
        private readonly HashSet<string> identityKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> unknownKeys = new HashSet<string>(StringComparer.Ordinal);

        internal RecordingGroup(Track first, RecordingDescriptor descriptor, string key)
        {
            Representative = first; Tracks = new List<Track>(); Key = key;
            Add(first, descriptor);
        }

        internal bool Accepts(RecordingDescriptor candidate)
        {
            // Reissues often have different Spotify track IDs, but a conflicting
            // recording code is evidence of another recording and stays separate.
            if (isrc.Length > 0 && candidate.Isrc.Length > 0 && isrc != candidate.Isrc) return false;
            if (candidate.KnownLength)
            {
                if (candidate.Length < maximum - 2 || candidate.Length > minimum + 2) return false;
                foreach (var previous in unknownLengths)
                    if (!RecordingDescriptor.SameIdentifier(previous, candidate)) return false;
            }
            else
            {
                // An unknown duration cannot bridge recordings just because some
                // other group member has a matching identifier.
                foreach (var previous in identities)
                    if (!RecordingDescriptor.SameIdentifier(previous, candidate)) return false;
            }
            return true;
        }

        internal void Add(Track track, RecordingDescriptor descriptor)
        {
            Tracks.Add(track);
            if (descriptor.Isrc.Length > 0) isrc = descriptor.Isrc;
            if (descriptor.KnownLength)
            {
                minimum = Math.Min(minimum, descriptor.Length);
                maximum = Math.Max(maximum, descriptor.Length);
            }
            if (identityKeys.Add(descriptor.IdentifierKey)) identities.Add(descriptor);
            if (!descriptor.KnownLength && unknownKeys.Add(descriptor.IdentifierKey)) unknownLengths.Add(descriptor);
        }
    }

    internal sealed class RecordingGroups
    {
        private readonly List<RecordingGroup> groups = new List<RecordingGroup>();
        private readonly Dictionary<Track, RecordingGroup> byTrack = new Dictionary<Track, RecordingGroup>();
        private readonly Dictionary<string, RecordingGroup> bySourceKey = new Dictionary<string, RecordingGroup>(StringComparer.Ordinal);
        internal IList<RecordingGroup> Groups { get { return groups.AsReadOnly(); } }

        internal static RecordingGroups Build(IEnumerable<Track> tracks)
        {
            if (tracks == null) throw new ArgumentNullException("tracks");
            var result = new RecordingGroups();
            var buckets = new Dictionary<string, List<RecordingGroup>>(StringComparer.Ordinal);
            var groupKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var track in tracks)
            {
                if (track == null) continue;
                var descriptor = new RecordingDescriptor(track);
                string sourceKey = IndexStore.Key(track);
                RecordingGroup repeated;
                if (result.byTrack.TryGetValue(track, out repeated))
                {
                    repeated.Add(track, descriptor);
                    continue;
                }
                List<RecordingGroup> candidates;
                if (!buckets.TryGetValue(descriptor.MetadataKey, out candidates))
                    buckets[descriptor.MetadataKey] = candidates = new List<RecordingGroup>();
                RecordingGroup group = null;
                // Empty artist/title metadata never establishes recording identity.
                if (descriptor.HasMetadata)
                    foreach (var candidate in candidates)
                        if (candidate.Accepts(descriptor)) { group = candidate; break; }
                if (group == null)
                {
                    string groupKey = sourceKey;
                    int suffix = 1;
                    while (!groupKeys.Add(groupKey)) groupKey = sourceKey + "\nrecording:" + (++suffix).ToString(CultureInfo.InvariantCulture);
                    group = new RecordingGroup(track, descriptor, groupKey);
                    candidates.Add(group); result.groups.Add(group);
                }
                else group.Add(track, descriptor);
                // The canonical index predates recording IDs. An ambiguous legacy
                // key cannot safely select a recording; exact source objects can.
                result.byTrack.Add(track, group);
                RecordingGroup existing;
                if (!result.bySourceKey.TryGetValue(sourceKey, out existing)) result.bySourceKey.Add(sourceKey, group);
                else if (existing != group) result.bySourceKey[sourceKey] = null;
            }
            return result;
        }

        internal RecordingGroup GroupFor(Track track)
        {
            if (track == null) return null;
            RecordingGroup group;
            return byTrack.TryGetValue(track, out group) ? group : GroupForKey(IndexStore.Key(track));
        }

        internal RecordingGroup GroupForKey(string canonicalKey)
        {
            if (canonicalKey == null) return null;
            RecordingGroup group;
            return bySourceKey.TryGetValue(canonicalKey, out group) ? group : null;
        }
    }

    internal sealed class RecordingDescriptor
    {
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex IsrcPattern = new Regex(@"^[A-Z]{2}[A-Z0-9]{3}[0-9]{7}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SpotifyPattern = new Regex(@"^[A-Za-z0-9]{22}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        internal readonly string MetadataKey, Isrc, SpotifyId, IdentifierKey;
        internal readonly double Length;
        internal readonly bool KnownLength, HasMetadata;

        internal RecordingDescriptor(Track track)
        {
            string title = Normalize(track.Title);
            string[] credits = track.Artists;
            bool validCredits = credits != null && credits.Length > 0;
            if (validCredits) foreach (string credit in credits) if (Normalize(credit).Length == 0) { validCredits = false; break; }
            if (!validCredits) credits = new[] { track.Artist ?? "" };
            var artist = new StringBuilder();
            bool hasArtist = false;
            foreach (string credit in credits)
            {
                string normalized = Normalize(credit);
                hasArtist |= normalized.Length > 0;
                artist.Append(Part(normalized));
            }
            MetadataKey = Part(title) + Part(artist.ToString());
            HasMetadata = title.Length > 0 && hasArtist;
            string code = Spaces.Replace((track.Isrc ?? "").Normalize(NormalizationForm.FormKC), "").Replace("-", "").ToUpperInvariant();
            Isrc = IsrcPattern.IsMatch(code) ? code : "";
            string spotify = (track.SpotifyId ?? "").Trim();
            if (spotify.StartsWith("spotify:track:", StringComparison.Ordinal)) spotify = spotify.Substring(14);
            SpotifyId = SpotifyPattern.IsMatch(spotify) ? spotify : "";
            IdentifierKey = Part(Isrc) + Part(SpotifyId);
            Length = track.DurationSeconds;
            KnownLength = Length > 0 && !Double.IsNaN(Length) && !Double.IsInfinity(Length);
        }

        internal static bool SameIdentifier(RecordingDescriptor a, RecordingDescriptor b)
        {
            return (a.Isrc.Length > 0 && a.Isrc == b.Isrc) || (a.SpotifyId.Length > 0 && a.SpotifyId == b.SpotifyId);
        }
        private static string Part(string value) { return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value; }
        private static string Normalize(string value) { return Spaces.Replace((value ?? "").Normalize(NormalizationForm.FormKC).Trim(), " ").ToUpperInvariant(); }
    }
}
