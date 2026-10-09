# Quick start

Need a Soulseek login? Start with the [account setup guide](SOULSEEK-ACCOUNT.md).

Extract the complete portable ZIP into a writable folder. Open FLAC-Downloader.exe, enter your own Soulseek credentials, import a tracklist, select where music goes, and click Download FLAC. The app does not download just because it is opened.

CSV requires artist and title columns; album, duration, ISRC, and cover URL metadata improve matching, deduplication, and artwork. See `examples/playlist.csv` in the source repository. Spotify links require your own developer Client ID; Setup Help explains configuration.

Use the 32-character **Client ID of a Spotify Web API app**, with redirect URI `http://127.0.0.1:48723/callback`. A Spotify Soloist API key (beginning with `spak_`) is for the separate Soloist playback client and cannot replace that client ID. Do not paste a client secret or Soloist key. Invalid client-ID values are not saved in settings. See [Spotify Web API app setup](https://developer.spotify.com/documentation/web-api/concepts/apps) and [Soloist credentials](https://developer.spotify.com/documentation/soloist/reference/command-line).

Use Balanced for general selection or Fast FLAC to accept an earlier matching source. Raising simultaneous jobs permits more queued work but cannot force slow peers to upload faster. Stop cancels active work and pauses automatic retry processing. Saved retries can be reviewed, paused, resumed, or removed.

Open opens finalized music. Fix library adds missing covers, consolidates confirmed duplicates, and resolves title collisions. Recovery data is preserved. Check files inspects FLAC headers and writes a report under the collection's `.playlist-flac/` directory.

## Track and source details

Double-click a track, select it and press Enter, or choose **Track and source details** from its right-click menu. The view shows the latest source information reported for that track and explains the matching policy. Source selection means a peer/file was chosen; it does not mean bytes are already arriving. Unknown quality, progress, and queue information are not guessed.

## Find or remove a downloaded song

Right-click one song and choose **Show in folder** to open Explorer with its saved FLAC selected. Choose **Move to Recycle Bin...** to remove that file after confirmation; Windows may also show its own confirmation or cancellation dialog. The song stays in the tracklist so you can download it again. Automatic retries pause after you confirm removal, and the collection's saved status and playable playlist are refreshed. Album reissues can share one file, so removing it affects every entry using that file. These actions are unavailable while the app is busy or multiple rows are selected. If the song has no saved file, or multiple possible files, the app explains the problem rather than guessing a filename.

Right-click the song list and choose **Export missing songs as CSV** to save confirmed failed songs for another search or later import. Ready songs and Review versions entries are excluded. The CSV contains music metadata, so treat it as private.

For a completed song, open **Track and source details** (right-click or Enter). When the app is idle, it reads that file's header in the background and shows its bit depth, sample rate, and channels. These facts do not verify the audio frames or the recording's origin.
