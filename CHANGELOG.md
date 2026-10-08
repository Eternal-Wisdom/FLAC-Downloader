# Changelog

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
