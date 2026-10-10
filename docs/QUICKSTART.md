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

### Live Activity log

Open Activity to follow new log entries as they arrive (refreshed approximately every 200 ms). The window stays live during downloads and library operations, and the main app controls remain usable. Closing and reopening Activity shows the latest retained entries; the log keeps the most recent 160 lines.


## Finding songs and reviewing albums

Use **Find** to search titles, artists, and source album names. **Ctrl+F** focuses it; **Escape** clears it. The status menu offers All songs, Downloading, Waiting, Missing, Completed, and Review. The count shows visible songs / all recording groups. Waiting includes ready songs and reported peer waits; it does not invent a peer queue position. Filters only change the view: Download FLAC still handles the collection, while Download selected songs handles your selection.

Right-click the list and choose **Album availability** to see imported songs grouped by artist and album. Disc and track positions are used when supplied; CSV accepts `disc_number` and `track_number`. This is not a promise that a partial playlist contains an entire published album or that files came from one release/master.

## Previewing repairs

**Fix library** first displays a read-only plan of duplicate copies to archive and proposed filenames. Close the preview to cancel, or choose Apply repairs. It lists names before duplicate consolidation, so consolidation can reduce that list. The app rechecks files on application. Artwork availability is checked during repair; the preview does not promise a cover for every file. Existing recovery maps and copies remain available; there is no new one-click whole-repair undo.

## Full audio integrity check

**Check files** offers Quick header check or Full audio check. Full audio check asks for the official FLAC project's `flac.exe`, available from [Xiph's FLAC releases](https://github.com/xiph/flac/releases). Extract the Windows tools and select the Win64 executable. No decoder is bundled or automatically fetched.

The full check runs one decoder process at a time in test mode, treats decoder warnings as review items, supports Stop, and limits each file to ten minutes. It never writes decoded audio. Results go to `.playlist-flac/FLAC-audio-check.csv`; the quick report remains `FLAC-check.csv`. It can detect corrupt/truncated frames that a header scan misses, but cannot prove lossless source provenance. Full checks read the entire audio file, so they can take much longer than header checks.

Uncheck **Follow new messages** in Activity to hold the displayed text while reading. Logging continues; checking it again catches up to the latest 160 retained messages.
