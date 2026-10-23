# Changelog

## 1.19.4 — 2026-10-21

- Reduce repeated folder metadata reads while restoring downloaded-song status. Each scan reuses parent-folder checks; file existence is always checked and no cache survives into the next scan.
- Preserve containment and symbolic-link rejection; add regression coverage for deletion and changed parent links.


## 1.19.3 (local, 2026-10-21)

- Bound CSV imports to 32 MiB on disk, 16 Mi characters, 50,000 data rows, 256 columns and 65,536 characters per field. Read files in bounded chunks and reject oversize input before building a partial queue. Existing download-index parsing keeps its independent behavior.
- Declare the .NET Framework 4.8 target explicitly and let Windows select HTTPS protocols instead of forcing TLS 1.2 in the app's network helpers. Certificate validation remains enabled.
- Save the allowlisted diagnostics preview to a local text file with atomic replacement. No logs, music names, accounts or credentials are added to the report, and saving does not upload anything.
- Record the supplied research proposal review and validation limits. No new dependencies or engine changes.

## 1.19.2 (local, 2026-10-21)

- Check free space, path safety and path length for temporary download storage as well as the music folder before starting a batch. Identify which location needs attention, and check that private collection storage is writable before creating the music folder or migrating files.
- Use the flat song layout when checking destination path length, avoiding warnings based on the removed nested playlist directory.

## 1.19.1 (2026-10-19)

- Stage filename metadata replacements beside their destination so atomic commits and rollback work when music and private app state are on different drives.
- Automatically clear staging, search maps, raw session indexes and repair backups after a settled download or successful library repair. Keep canonical history, music, reports and saved retries. Preserve recovery data when work stops or fails.
- Flatten older unindexed FLAC files into the collection root without overwriting or deleting them. Update repair wording to explain permanent duplicate cleanup.

- Save songs directly in readable collection folders, removing the redundant playlist directory. Keep playlist filenames unnumbered and prefix album songs by imported track order.
- Move collection indexes, reports, playback lists, transfer staging, locks and recovery files into private app state. Preserve music bytes, safe collision handling, saved retries and existing download recognition.
- Journal legacy folder moves, protect operations with temporary recovery maps, and reject conflicting or linked paths rather than overwrite data.

## 1.18.1

- Search shared album folders first for imported Spotify albums instead of forcing every request into song mode. Rank matching recording coverage before the selected quality/speed policy, then request only missing FLAC files from one folder.
- Validate titles, version labels and durations; reject ambiguous matches. Preserve completed tracks, recover finished album files after interruption, and fall back to individual searches for gaps. Keep album searches bounded and retain existing search pacing.
- Map direct-file progress back to imported songs and verify local FLAC headers/durations before adding files to the library.

## 1.18.0

- Load public Spotify album links without developer credentials or sign-in. Validate page/album identity, metadata and response limits; reject incomplete or unverifiable lists. Preserve canonical album source identity across restart. Public page availability and schema can change; optional connected API imports remain supported.
- Import pasted Artist - Title text or CSV from the Import menu, with atomic validation and locally cached tracklists that reopen after restart.
- Check destination readiness, free-space reserve, path/link safety and write access before launching the engine. Warn about long, cloud-synced or network destinations.
- Preview and copy allowlisted diagnostics without exporting logs, accounts, song names, local paths, peers or IP addresses.
- Explain reported engine failure codes in track details and saved retries; add a Copy URI action to Spotify setup and sharing etiquette to Soulseek onboarding.
- Add troubleshooting, tracklist-format and proposal-review guides. New form tools are split into a separate source file; retain the current engine and runtime.

## 1.17.6

- Remove leftover temporary sign-in files at startup or before the next download, only while holding the shared session lease. Skip linked folders/files, preserve unrelated files and saved cooldowns, and retry locked files later.
- Add Windows kill-on-close job containment for the download engine. Log a warning if Windows declines containment. Assignment happens just after launch, so this is best effort, not an absolute guarantee against orphan processes.
- Test cleanup with synthetic credentials, active leases, locked files, unrelated files, a mock engine, and a real Windows child process. Fresh live CSV/Soulseek verification passed; Spotify API import remains untested without a Web API Client ID.

## 1.17.5

- Prepare queue sort values once per refresh instead of repeatedly accessing controls and parsing positions during comparisons. Preserve stable ties and keep unknown/non-finite durations last in either direction.
- Write CSV exports and engine tracklists to a temporary sibling before replacing the destination. Failed writes preserve the existing tracklist and clean up temporary files. Reuse one artist-list serializer per export.

## 1.17.4

- Avoid repainting unchanged activity search results, preserving selected text as unrelated messages arrive. Reuse parsed snapshot lines while changing the search.
- Remove temporary packaging directories after success or failure, preventing full app copies from accumulating between builds.

## 1.17.3

- Search the activity log with case-insensitive Unicode matching. Ctrl+F focuses search and Escape clears it. Matching messages continue updating while Follow is enabled; searching while paused preserves the reading snapshot.

## 1.17.2

- Export selected visible songs as a reusable CSV from the queue menu. Preserve display order, metadata and distinct same-title artists without exporting local library paths or account settings.

## 1.17.1

- Make song column headers clickable, fixing the sorting control introduced in 1.17.0.
- Add Select all visible songs (Ctrl+A), Clear selection, and Copy selected songs and artists (Ctrl+C). Filtered-out songs are excluded; handle a busy clipboard without crashing.

## 1.17.0

- Sort displayed songs by source position, title, artist, duration or status. Reverse direction with another click; retain stable ties and put unknown durations last. Playlist/download order is unchanged.
- Search saved retries and switch between All retries, Scheduled, Paused and Unreadable views. Hidden entries remain; actions apply to selected visible editable entries.
- Keep retry filter controls visible at smaller sizes and derive the window caption from the assembly version.

## 1.16.2

- Prepare each imported queue row's recording key once and reuse it for transfer status/source updates, reducing temporary allocations during repeated progress callbacks. Reimport creates fresh row identities.

## 1.16.1

- Avoid repainting song status, rebuilding summaries, or rescanning status filters when repeated transfer events do not change song status. Keep source telemetry observation active.
- Batch nonterminal summary refreshes through the existing 200 ms UI timer, while completion, failure, and stopping flush immediately.
- Add an optional REA compiled-release inspection for developers; REA is not bundled or required by users.

## 1.16.0

- Reorganize the main window into Add music, Soulseek account, and Save music steps, with a useful empty-queue import prompt.
- Give the song queue more room, clearer status colors, concise counts, search hints, and row tooltips.
- Group checks, repairs, saved retries, and unavailable-song export in Library tools. Keep optional Spotify setup collapsed until needed.
- Use consistent dark menus and single-line icon buttons, improve keyboard navigation, and keep settings usable at smaller window sizes.
- Preserve the download engine, existing-file reuse, concurrency, and retry policy; no new dependencies.

## 1.15.3

- Process completed cover lookups as they arrive, keeping available slots working even when an earlier lookup is slow. Keep the four-request limit and sequential file writes.
- Detect asynchronous cover lookup failures promptly and cancel/drain outstanding requests before releasing the library lock.

## 1.15.2 (included in the 1.15.3 public update)

- Avoid scanning the entire queue on transfer updates when All songs is selected, including during text searches. Status filters still refresh as downloads change.
- Prepare search text once per refresh and search individual fields without allocating a combined title/artist/album string for every song. Preserve Unicode and multi-field search behavior.

## 1.15.1 (included in the 1.15.3 public update)

- Clear stale transfer timing and show Transfers stopped when an operation finishes, so later library repair and file checks cannot leave Waiting for transfer progress on an idle app.

## 1.15.0

- Search the queue by title, artist, or album; filter by download status without losing hidden tracks or changing download scope. Ctrl+F focuses Find and Escape clears it.
- Refresh track details during a run with observed speed, elapsed time, latest engine state, and time since the last byte update. Unknown per-track telemetry remains unknown.
- Pause Activity's displayed text while reading older messages, then catch up to the latest retained messages.
- Preview library renames and duplicate archival before manual repairs. The preview does not write files; final repairs recheck the library and retain recovery data.
- Show album availability for imported songs, separated by artist and source album. Preserve optional disc/track positions through CSV imports/exports and Spotify metadata parsing without reordering the original playlist.
- Offer an optional full audio integrity check using an externally supplied official FLAC decoder. Keep normal header checks lightweight and store the two reports separately. No new decoder is bundled.
- Keep the pinned download engine, concurrency policy, existing-file reuse, and retry pacing unchanged.

## 1.14.1

- Keep the Activity window synchronized with new log entries through the existing 200 ms UI refresh.
- Keep app controls usable while Activity is open, reuse the window, and disconnect its log subscription when closed.

## 1.14.0

- Export confirmed missing songs as a reusable Unicode CSV from the queue menu; repeated recordings are exported once. Ready and conflicting-version rows are excluded.
- Track details can inspect a completed file's FLAC header asynchronously and show bit depth, sample rate, and channel count without claiming lossless provenance.
- Resolve missing release data from MusicBrainz ISRC responses through at most three paced, identity-checked recording lookups. Refresh older partial metadata caches.
- Reviewed the proposed-functions report; documented acceptance criteria and remaining live test gaps.

- Pin GitHub build actions to verified release commits, disable persisted checkout credentials, and prepare monthly grouped Dependabot updates for review.
- Cancel superseded CI runs for the same event/ref and bound build jobs to 15 minutes.
- Document the second external review and why its speculative rewrite and source integrations are deferred.

## 1.13.0

- Keep test code out of the production app; run a separate test executable against the built application.
- Emit JUnit test evidence with suite counts, timings, revision/tree state and binary hashes; retain CI reports and reject changed reports when packaging.
- Retain the test process handle so Windows PowerShell 5.1 reports its exit code reliably.
- Exercise 500 reproducible filename inputs, 300 quoted CSV round trips, malformed CSV, DPAPI failures and writable-state probes.
- Handle superscript Windows device names and control characters in filenames.
- Explain unwritable portable locations and passwords saved under another Windows account; preserve unreadable encrypted preferences until replacement.
- Respect artwork Retry-After headers, extend repeated provider cooldowns exponentially, and identify the project in the User-Agent.
- Measure synthetic catalog loading and lookup at 50,000 entries; document review recommendations and limits in docs/REVIEW-1122.md.

## 1.12.0

- Restore track-list, import, and footer layout when panel sizes change after the container layout event. Add resize and minimize/restore regression coverage.
- Explain why Soloist API keys cannot be used for Web API playlist imports; save only valid client IDs in the client-ID preference.
- Require successful offline tests of the exact application and engine binaries before packaging.
- Avoid Windows access-denied errors on hidden legacy active playlists by writing the engine playlist to a fresh per-pass staging path. Fatal CLI errors now stop the operation instead of being treated as missing tracks and starting another search pass.
- Start retries with a real search before optional alternate-name lookups. Limit remote name lookup to 30 seconds per run, then continue with local aliases.
- Index cross-collection recording lookup by ISRC instead of repeatedly scanning and normalizing the entire catalog; retain existing matching and file-integrity checks.
- Exclude staging and legacy recovery folders from FLAC checks, skip file links, and respect the collection lock before scanning or updating reports.
- Add right-click actions to show a saved song in Explorer or move it to the Recycle Bin with confirmation. Removal pauses automatic retries, updates saved status and the playlist, and respects the download lock. Targets are resolved by recording identity; outside/linked files and ambiguous targets are rejected.
- Preserve structurally damaged saved retry lists instead of silently dropping entries. Both automatic retries and the retry editor reject incomplete or conflicting queues before changing them.
- Double-click a track, press Enter, or use its context menu to inspect the selected source and matching policy. Unknown engine information remains explicitly unknown.
- Distinguish source selection from actual byte progress.
- Stop the child engine if a progress consumer fails; preserve the original error and remove temporary credentials.
- Reject incomplete or malformed download indexes instead of silently dropping history.
- Preserve active recovery data and avoid further finalization after an unexpected processing failure.
- Keep synthetic interface previews independent of private settings and prevent preview settings writes.
- Prevent stale test reports from passing validation; terminate timed-out test processes.
- Record the code review and decisions on the downloader research recommendations.

## 1.11.0

- Prepare a separate public source repository and minimal portable release.
- Consolidate collection indexes, tracklists, and reports in `.playlist-flac/`.
- Preserve legacy data, relative music paths, and recovery archives during migration.
- Open the collection's actual music folder from the Open button.
- Include pinned engine setup, offline test scripts, packaging, CI, documentation, and third-party source/license notices.

## 1.10.0

- Resizable queue, grouped controls, consistent icons, status colors, and separate Activity window.

## 1.9.0

- Quality/speed profiles, saved retry review, cross-collection recording reuse, selected-song downloads, and CSV drag and drop.

