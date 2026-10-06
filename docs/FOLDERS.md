# Folders and recovery

## App folder

`state/` contains private settings, cached artwork, retry lists, and the recording catalog. It is created on first use and never belongs in Git or a release ZIP. Local app-update backups should be grouped under `state/update-backups/`. Developer source and historical test reports belong in the repository, not beside the runnable app.

## Collection folder

`playlist/` contains finalized songs and `playlist.m3u8` references them. The hidden `.playlist-flac/` folder stores `_index.csv`, `playlist.csv`, `Covers.csv`, and `FLAC-check.csv`. New active-session indexes also live there. The index is essential: do not delete it to tidy the folder.

Legacy metadata is moved only during an explicit download, library repair, or check operation. Read-only library status can still read old layouts. Migration preserves bytes and interprets canonical index paths relative to the collection root. If both old and new metadata exist, the app stops and preserves both instead of choosing one silently.

`.search/` maps and `.incoming/` temporary files can refer to absolute locations, so older directories stay in place. Recovery files live in `.playlist-flac-recovery/`. Older `.artwork-backup-*` and `.duplicates-backup-*` directories may contain absolute paths; keep them where they were created. Naming recovery can be safely grouped by the app.

Windows may show these folders when Show hidden items is enabled. They are operational data, not duplicate music to delete. Do not move recovery archives without also updating and testing their manifests. Artwork recovery can be invoked with `Playlist FLAC.exe --restore-artwork "path-to-recovery-directory"`.
