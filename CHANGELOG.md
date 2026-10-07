# Changelog

## 1.12.0 (local candidate)

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
