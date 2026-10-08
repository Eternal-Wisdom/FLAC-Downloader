# Third-party notices

## Sockseek 3.0.5

- Upstream: https://github.com/fiso64/sockseek
- License: GNU Affero General Public License version 3; the unmodified upstream license is bundled with the engine.
- Role: independently launched command-line Soulseek search/download process.
- Source snapshot: `third-party/sockseek-3.0.5-source.zip`, upstream tag `v3.0.5` (archive prefix commit `8d1d815`). The same source archive accompanies portable distributions in `engine/`.
- Windows release: `sockseek_3.0.5_win-x64.zip`; pinned checksums are in `third-party/sockseek.json`.

Sockseek's own dependencies and notices are described in its supplied source projects and package references. The binary is not modified by this app.

FLAC-Downloader uses the Windows .NET Framework and system Segoe UI font. Icons are drawn by this project; the interface screenshot uses synthetic song names and contains no downloaded music or album artwork.

## External metadata and artwork services

These services are contacted at runtime, not bundled as software or artwork in releases. Their service/data terms are distinct from the application code license.

- MusicBrainz: [API identification and rate limiting](https://musicbrainz.org/doc/MusicBrainz_API), [data licensing](https://musicbrainz.org/doc/About/Data_License). Requests are serialized and paced at least 1.1 seconds apart; provider cooldowns apply independently of the four-worker artwork queue.
- Cover Art Archive: [API documentation](https://musicbrainz.org/doc/Cover_Art_Archive/API). Image hosts can impose their own throttling; Retry-After and temporary-failure cooldowns are respected. Album images are not relicensed by this application's license.
- Spotify: [developer terms](https://developer.spotify.com/terms) and [developer policy](https://developer.spotify.com/policy). Users configure their own Web API app for authenticated metadata import. No Spotify login, token or artwork is included in the distributed app.
