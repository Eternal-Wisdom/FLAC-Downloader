# GitHub downloader research for FLAC-Downloader

Reviewed: **2026-10-06**. App baseline: **v1.11.0**.

## Scope and method

This is a broad review of prominent GitHub downloaders and directly relevant music clients, not an exhaustive inventory or a speed benchmark. Selection started with GitHub's [downloader topic sorted by stars](https://github.com/topics/downloader?o=desc&s=stars), then added established general download managers and Soulseek clients. Stars help discover projects; they do not prove speed, reliability, maintenance, or suitability.

The tables distinguish features described in upstream documentation from **our proposed adaptations**. Sources are primary project repositories or official documentation. Upstream default-branch documentation can describe beta features; it is not proof that a feature exists in every stable release. No candidate application was installed or benchmarked for this review.

## Most relevant: Soulseek and music

| Project and evidence | Upstream behavior | Useful adaptation and fit |
| --- | --- | --- |
| [Sockseek](https://github.com/fiso64/sockseek) | Required versus preferred file conditions, album-folder modes, track-count checks, result previews, failure downranking, HTTP/SignalR daemon. | **Highest fit.** Expose more of the existing engine before adding another backend. Candidate explanations and coherent album downloads are the strongest opportunities. |
| [slskd](https://github.com/slskd/slskd) | Sort/filter results; transfers grouped by user and folder; speed/status display and queue-position requests; batch actions. | **High fit.** Show why a track is waiting and which peer/folder supplies it. Treat slskd as a design reference or optional future adapter, not a second mandatory service. |
| [Nicotine+](https://github.com/nicotine-plus/nicotine-plus), [release notes](https://nicotine-plus.org/NEWS.html) | Wishlist searches, folder grouping, queue information, speed controls, keyboard and accessibility improvements. | **High fit.** Improve saved-retry visibility, result filters, keyboard navigation, and status clarity. Wishlist and bounded retries already exist here. |
| [spotDL](https://github.com/spotDL/spotify-downloader) | Spotify metadata, artwork, metadata-only save, metadata updates, and playlist synchronization. Audio comes from YouTube. | **High workflow fit, unsuitable FLAC fallback.** Offer a preview of playlist changes and metadata-only import. Do not copy automatic deletion of removed songs or convert a lossy source to claim lossless quality. |

The bundled **Sockseek 3.0.5 local help** was also inspected without logging in. It already exposes album quality/track-count controls, sorted result previews, mock transfers, and a loopback daemon. Those are implementation leads, not completed wrapper features. Public upstream documentation may change independently of the pinned engine.

## General download managers

| Project and evidence | Upstream behavior | Useful adaptation and limit |
| --- | --- | --- |
| [aria2](https://github.com/aria2/aria2), [manual](https://aria2.github.io/manual/en/html/aria2c.html) | Segmented transfers, session persistence, concurrency limits, checksums, RPC. Supports HTTP/FTP/SFTP/BitTorrent/Metalink. | Keep recoverable state and bounded work; consider separate transfer and search budgets. **No Soulseek support**, so adding aria2 would not accelerate the current audio transfers. |
| [AB Download Manager](https://github.com/amir1376/ab-download-manager) | Queues, scheduling, browser integration, multiple themes. | Named queues and an optional download window could help large libraries. Browser capture adds little to a Spotify-link/CSV workflow. |
| [Gopeed](https://github.com/GopeedLab/gopeed) | Batch task actions, filtering, restart recovery, responsive UI, multilingual support, API and extensions. | Adopt clear task filters and responsive detail views. Its supported download protocols do not include Soulseek; a UI framework rewrite is unnecessary. |
| [Motrix](https://github.com/agalwood/Motrix) | Current v2 beta documents an independent core, speed profiles, SQLite sessions, dashboard, and notifications. | Separate engine events from display logic, preserve sessions, and allow optional completion notices. Do not assume beta migration is production-ready or copy its plugin marketplace. |
| [Persepolis](https://github.com/persepolisdm/persepolis) | Queuing, scheduling, multi-segment downloads. | Queue priority and schedules suit this app. Its advertised 64 connections are not a useful Soulseek concurrency target. |
| [Xtreme Download Manager](https://github.com/subhra74/xdm) | Resume, schedules, browser integration, video conversion. | Interrupted-work recovery and explicit schedules fit. Published speed multipliers are upstream claims, not evidence of gains for this app. |
| [File Centipede](https://github.com/filecxx/FileCentipede) | Task details, catalogs, filters, speed limits, checksum tool, editable transfer settings. | Compact details and filters fit. Avoid bundling its many protocol/utilities features; a GitHub project page alone does not establish reusable source licensing. |
| [Ghost Downloader](https://github.com/XiaoYouChR/Ghost-Downloader-3) | Describes bandwidth detection, chunking, editable tasks, and RPC. | Measure bottlenecks before tuning concurrency. HTTP chunking, browser fingerprints, and media capture do not solve Soulseek peer limits. |
| [qBittorrent](https://github.com/qbittorrent/qBittorrent) | Unicode support and signed source/binary releases are documented in its repository. | Preserve Unicode throughout and improve release authenticity. Torrent swarm behavior should not be assumed available for Soulseek files. |
| [Transmission](https://github.com/transmission/transmission), [configuration](https://raw.githubusercontent.com/transmission/transmission/main/docs/Editing-Configuration-Files.md) | Persistent configuration, transfer/queue settings, and alternate speed controls. | Consider simple normal/limited bandwidth profiles and durable task settings. Do not introduce a torrent subsystem. |

## Media and collection downloaders

| Project and evidence | Upstream behavior | Useful adaptation and limit |
| --- | --- | --- |
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | Download archive, format selection, output templates, separate retry types and linear/exponential retry delays. | Stable recording history, understandable quality rules, and retries classified by failure. Existing duplicate handling and backoff should be extended, not replaced. |
| [youtube-dl](https://github.com/ytdl-org/youtube-dl) | Playlist processing, configurable network timeouts, and extensive CLI options. | Keep batch progress independent of one failed item; offer useful presets instead of exposing every engine flag. |
| [cobalt](https://github.com/imputnet/cobalt) | Simple paste-and-download workflow; separates API and web interface; emphasizes privacy. | Keep the primary workflow short and avoid tracking. Its web extraction/proxy architecture is not a Soulseek speed solution. |
| [lux](https://github.com/iawia002/lux) | Preview available formats, quality, size, and a selectable stream before downloading. | Add an optional source inspector with quality, size, duration, and availability before committing to a candidate. |
| [you-get](https://github.com/soimort/you-get) | Information mode lists available codecs, quality, sizes, and format choices. | Clearly show what will be downloaded and allow a manual override for ambiguous tracks. |
| [gallery-dl](https://github.com/mikf/gallery-dl) | Configurable naming and collection extraction. Its GitHub README now points active development to Codeberg. | Learn from explicit naming policies and documented configuration. Follow the maintained upstream if evaluating code; do not add an image-gallery engine for album covers. |
| [Instaloader](https://github.com/instaloader/instaloader) | Resumes interrupted collection iterations and supports incremental updates using saved timestamps. | Save collection progress and refresh only changed entries. Playlist reorderings mean a simple timestamp or first-existing-item shortcut is insufficient here. |
| [Tyrrrz/YoutubeDownloader](https://github.com/Tyrrrz/YoutubeDownloader) | URL/search input, playlists, selectable formats, automatic media tags. | Keep optional search and candidate selection close to the import workflow. Its YouTube backend is not a lossless music substitute. |
| [YTDLnis](https://github.com/deniscerri/ytdlnis) | Per-item playlist edits, shared batch settings, queues/schedules, reusable templates, failure retry. | Let users correct one imported track or apply quality preferences to a selection. Avoid a terminal or large custom-command surface in the normal UI. |
| [ytDownloader](https://github.com/aandrew-me/ytDownloader) | Playlist downloads, themes, translation support, and several distribution channels. | Improve readable themes, localization readiness, and clear official-download instructions. Video compression and subtitles are unrelated. |
| [Media Downloader](https://github.com/mhogomchungu/media-downloader) | GUI around CLI engines, presets, batch input, playlist monitoring, localization. | A clean engine adapter and curated presets fit well. Multiple bundled engines and unlimited concurrency would increase complexity without demonstrated benefit. |
| [N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) | Separate temporary/final paths, retry controls, stream selection, expected-versus-actual segment checks, Unicode-aware filename shortening. | Make verification a visible completion stage and handle long Unicode paths carefully. HLS/DASH segments and decryption features are unrelated to Soulseek transfers. |
| [N_m3u8DL-CLI](https://github.com/nilaoda/N_m3u8DL-CLI) | Resume, speed controls, and segmented downloading; README places it in maintenance and directs richer development to RE. | Maintenance status matters more than historical stars when considering dependencies. No reason to bundle either stream engine here. |
| [FileDownloader](https://github.com/lingochamp/FileDownloader) | Android task engine with customizable connections, output, database, and connection-count components; directs new enhancements to OkDownload. | Keep testable boundaries between transport, storage, and UI. Android libraries are not suitable Windows/Soulseek dependencies. |

### Additional popular entries screened

[BBDown](https://github.com/nilaoda/BBDown) currently states that it is archived and no longer maintained; its remaining README does not support a detailed feature assessment. The topic also includes platform-focused projects such as [TikTokDownloader](https://github.com/JoeanAmier/TikTokDownloader), [Douyin/TikTok API](https://github.com/Evil0ctal/Douyin_TikTok_Download_API), [XHS-Downloader](https://github.com/JoeanAmier/XHS-Downloader), [wechatDownload](https://github.com/qiye45/wechatDownload), [coursera-dl](https://github.com/coursera-dl/coursera-dl), and [VidBee](https://github.com/nexmoe/VidBee). These were screened for scope from the topic listing, not audited in depth. Platform extraction is outside this app's music/Soulseek needs; their presence in a popular list does not justify integration.

## What this app already has

The local README and download configuration already provide FLAC filtering, quality preferences, bounded 8/20/32 jobs, search pacing, a 20-second no-progress timeout, recording deduplication, collision-aware names, artwork repair, recovery data, CSV drag-and-drop, selected-track downloads, and saved retries starting at 15 minutes and increasing to six hours.

Jobs include searches and peer queues. They are not a count of simultaneous active transfers. Album links currently import individual tracks, and FLAC checks inspect headers rather than decoding every audio frame. These distinctions should remain visible in product claims.

## Recommended work, in order

These are **proposals**, not newly implemented features. Effort is relative: small means focused UI/configuration work, medium means multiple components, and large means engine/session changes.

| Priority | Proposal | Expected benefit | Effort and acceptance test |
| --- | --- | --- | --- |
| 1 | Explain source selection and offer an optional candidate inspector. | Fewer wrong versions; understandable quality/speed tradeoffs. | Medium. Synthetic candidates must reject wrong artist/version and non-FLAC files, label missing quality properties, and distinguish advertised from observed speed. A manual choice must still obey required format rules. |
| 2 | Benchmark peer selection and adaptive concurrency. | Potential improvement in completion time rather than just more queued jobs. | Large. Compare current profiles with cautious adjustments using identical authorized files, multiple trials, and controlled peers. Track completion time, goodput, failures, duplicate bytes, search count, CPU, memory, and disk use. Retain fixed limits unless gains are repeatable. |
| 3 | Add coherent album-folder mode. | Consistent edition, artwork, track order, and fewer mixed-source matches. | Medium/large. Reuse engine capabilities; test disc count, expected tracks, bonus editions, quality consistency, missing tracks, and peer failure. Offer an explicit individual-track fallback rather than silently mixing editions. |
| 4 | Make queue and retry states actionable. | Less uncertainty about idle-looking downloads. | Medium. Distinguish searching, waiting for peer, transferring, verifying, artwork, and retry due time. Show queue position when available and unknown when unavailable. Test restart recovery and cancellation without extra search spam. |
| 5 | Preview library and playlist changes. | Prevent unwanted duplicates, renames, or deletion during refresh/repair. | Medium. Show added, existing, uncertain matches, proposed names, and cover edits. Keep live/remix/acoustic recordings separate. Removed playlist entries must not automatically delete local songs. Verify idempotent repeat runs and undo behavior. |
| 6 | Add optional full FLAC decode verification and storage checks. | Detect incomplete/corrupt audio beyond headers. | Medium. Use a bounded verification queue, distinguish transfer from verification failure, check free space, and stop repeated writes on storage I/O failure. Test truncated frames, disk-full behavior, and index consistency. Decoding cannot prove lossless provenance. |
| 7 | Improve large-list responsiveness and diagnostics. | Easier troubleshooting and smoother large libraries. | Small/medium initially. Profile before changing CSV storage or introducing SQLite; coalesce display updates. Test thousands of synthetic tracks. Diagnostic export must omit credentials, tokens, account names, peer identifiers, absolute paths, and real tracklists by default. |
| 8 | Add optional schedules, bandwidth profiles, and completion notices. | Convenience while sharing a connection or downloading long collections. | Medium. No downloads merely because the app opens; retain explicit retry arming. Test time-zone changes, missed schedules, cancel/close, and unsupported engine rate controls. No always-running service is required. |
| 9 | Strengthen release trust and accessibility. | Easier setup and public support. | Medium. Evaluate signed releases, clear verified download sources, keyboard access, text status alongside color, readable light/dark themes, and translation resources. Keep AI disclosure prominent. Update mechanisms must preserve private state and avoid accumulating visible backup folders. |

### Speed experiment design

Start with instrumentation and controlled mocks, then authorized real peer transfers on healthy storage. Record search latency, peer wait time, active-transfer throughput, retries, and artwork/verification time separately. A fast advertised peer can have no free slot; a high-resolution file can take longer simply because it is larger.

Evaluate correctness first, then preferred quality, then availability/expected completion time. Any measured peer history should expire and remain local; a single slow session is not grounds for permanent exclusion. Inspect the existing engine's ranking and failure downranking before adding overlapping logic. Do not abort progressing downloads merely to chase a new speed estimate.

A persistent loopback engine session may reduce repeated startup/login overhead and improve task control, but it requires lifecycle, authentication, cancellation, and secret-handling tests. Evaluate Sockseek's existing daemon before adopting slskd. It is not yet a recommendation to replace the backend.

### Ideas to decline

- HTTP multi-range or torrent-style chunk merging across unrelated Soulseek copies: same title and size do not prove identical bytes.
- Raising search frequency or connections without bounds: more requests can increase queues, bans, and wasted retries.
- YouTube fallback converted to FLAC, higher bit depth, or upsampling presented as an audio-quality upgrade.
- Browser cookie capture, router reconnect/IP rotation, chat rooms, video tools, and a plugin marketplace in a focused music downloader.
- An Electron/Flutter rewrite, SQLite migration, FFmpeg bundle, or second backend without a measured need and maintenance plan.
- Silent playlist deletion, automatic removal of recovery data, or fake sharing counts.

## Decision rule

Accept an idea only when it fixes a reproducible problem, removes a frequent manual step, or produces a measured improvement while preserving matching accuracy, quality truthfulness, privacy, and recovery. Give each accepted change a small scope and a concrete acceptance test. Review upstream maintenance, license, release provenance, and pinned-version compatibility before reusing code or shipping a dependency.

The first implementation shortlist is **candidate explanations, transfer/queue visibility, a controlled speed benchmark, and coherent album mode**. These address existing user needs more directly than adding another general-purpose downloader.
