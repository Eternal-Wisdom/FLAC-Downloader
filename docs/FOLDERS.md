# Folders and recovery

## Music folders

The selected destination contains named collection folders with finalized FLAC files directly inside them. Playlists keep title filenames without numbering. Spotify albums use `01 - Title.flac`, `02 - Title.flac`, and so on in the imported album sequence; numbering continues across discs. Missing tracks retain their positions instead of shifting the remaining tracks. Windows Explorer should be sorted by Name to display that sequence. This is album track order, not release-date sorting.

Artists are added when titles clash. A different source with an already reserved collection name gets a readable suffix such as `Album (2)`. Existing unrelated folders are never merged or overwritten.

## App state

The portable app creates `state/` for private settings, imported tracklists, artwork caches, retries, and the recording catalog. `state/collections/<id>/` stores each collection's index, original tracklist, reports, private playback list, search/recovery maps, staging audio, and lock. `music-root.txt` identifies its music folder. The opaque ID is internal and does not appear in the music destination.

Playback lists stored here use absolute music paths so they remain playable outside the collection. Recovery copies live in `state/collections/<id>/recovery/`. They protect in-progress work and failed operations. After a download job or library repair completes successfully, staging, search maps, raw session indexes and all repair backups are cleared automatically. This also applies to settled jobs with unavailable or review tracks; saved retry history remains. Duplicate copies removed by a completed repair cannot be restored from app backups. Keep the entire `state/` directory when updating or moving the portable app. Sharing it can expose credentials, song lists, local paths, and peers from an interrupted session; never put it in Git or release archives.

Temporary engine sign-in configurations remain under the Windows user's local application-data folder. Normal completion removes them. Startup and the next engine run remove abandoned configurations only while holding the session lease. Job containment is best effort and cannot guarantee an old uncontained process is gone.

## Older collections

Download and Fix library discover the old `Collection - hash` path and migrate it to a readable collection name under a lock. Canonical music paths become relative before the directory is renamed. A saved relocation journal and stable internal identity preserve restart and retry behavior. The redundant `playlist/` directory is removed only once empty; indexed completed songs are renamed into the collection root. Unindexed FLAC files directly inside the old `playlist/` directory are moved into the collection root with their filenames preserved and safe collision suffixes. Other unrelated files are preserved for review.

Known metadata, staging, and recovery folders move into app state. Linked paths and conflicting metadata stop migration rather than overwrite data. Interrupted moves retain their remaining source files and can be resumed. Old duplicate maps retain their original copy when archive paths are relocated. Audio is moved without transcoding. Album order comes from the imported tracklist, not download completion order.

Recovery files are available only while a stopped or failed operation still needs them. When available, artwork metadata recovery uses `FLAC-Downloader.exe --restore-artwork "path-to-manifest.json"`. Original index/playback copies and filename maps accompany recovery archives. Later filename or folder changes may require following those maps when restoring historical data. Recovery is not a one-click undo of a complete maintenance operation.

Do not manually delete bookkeeping to tidy music, or copy the music into a differently named destination while expecting automatic resume. Use the app's own collection organization and preserve its private state.
