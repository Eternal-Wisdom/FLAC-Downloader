# Folders and recovery

## App folder

`state/` contains private settings, cached artwork, cached imported tracklists in `imports/`, retry lists, and the recording catalog. It is created on first use and never belongs in Git or a release ZIP. Cached public album and pasted imports preserve their source identity when the app reopens. Keep developer-created app-update rollback copies outside the installation; do not mix them with music or include them in distribution archives. Developer source and historical test reports belong in the repository, not beside the runnable app.

Temporary engine configurations live under the current Windows user's local application-data folder, separate from music. Normal completion removes them. Startup and the next engine run remove abandoned configs only while holding the shared session lease; linked entries are skipped. Windows job containment normally ends the engine when the app is force-closed, but assignment can fail and is reported. This is not secure erasure or a guarantee that an old uncontained process is gone.

## Collection folder

`playlist/` contains finalized songs and `playlist.m3u8` references them. The hidden `.playlist-flac/` folder stores `_index.csv`, `playlist.csv`, `Covers.csv`, `FLAC-check.csv`, and optional `FLAC-audio-check.csv`. New active-session indexes also live there. The index is essential: do not delete it to tidy the folder.

Legacy metadata is moved only during an explicit download, library repair, or check operation. Read-only library status can still read old layouts. Migration preserves bytes and interprets canonical index paths relative to the collection root. If both old and new metadata exist, the app stops and preserves both instead of choosing one silently.

`.search/` maps and `.incoming/` temporary files can refer to absolute locations, so older directories stay in place. Recovery files live in `.playlist-flac-recovery/`. Older `.artwork-backup-*` and `.duplicates-backup-*` directories may contain absolute paths; keep them where they were created. Naming recovery can be safely grouped by the app.

Windows may show these folders when Show hidden items is enabled. They are operational data, not duplicate music to delete. Do not move recovery archives without also updating and testing their manifests. Artwork recovery can be invoked with `FLAC-Downloader.exe --restore-artwork "path-to-recovery-directory"`.
