# Changelog

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
