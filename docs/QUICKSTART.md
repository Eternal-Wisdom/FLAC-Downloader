# Quick start

Need a Soulseek login? Start with the [account setup guide](SOULSEEK-ACCOUNT.md).

Extract the complete portable ZIP into a writable folder. Open FLAC-Downloader.exe, enter your own Soulseek credentials, import a tracklist, select where music goes, and click Download FLAC. The app does not download just because it is opened.

CSV requires artist and title columns; album, duration, ISRC, and cover URL metadata improve matching, deduplication, and artwork. See `examples/playlist.csv` in the source repository. Spotify links require your own developer Client ID; Setup Help explains configuration.

Use Balanced for general selection or Fast FLAC to accept an earlier matching source. Raising simultaneous jobs permits more queued work but cannot force slow peers to upload faster. Stop cancels active work and pauses automatic retry processing. Saved retries can be reviewed, paused, resumed, or removed.

Open opens finalized music. Fix library adds missing covers, consolidates confirmed duplicates, and resolves title collisions. Recovery data is preserved. Check files inspects FLAC headers and writes a report under the collection's `.playlist-flac/` directory.

## Track and source details

Double-click a track, select it and press Enter, or choose **Track and source details** from its right-click menu. The view shows the latest source information reported for that track and explains the matching policy. Source selection means a peer/file was chosen; it does not mean bytes are already arriving. Unknown quality, progress, and queue information are not guessed.
