# Importing tracklists

Use **Import > Import CSV file** or **Import > Paste songs or CSV**. Imports only build the queue; downloads start explicitly with **Download FLAC**.

## CSV

Use UTF-8 text with a header row and at least `title` and `artist` columns. Exportify's `Track Name` and `Artist Name(s)` headers also work. Comma, semicolon and tab separators are detected. Quote fields containing separators, quotes or line breaks; inside a quoted field, write a quote twice.

```csv
title,artist,album,length,isrc,spotify_id,cover_url,disc_number,track_number
"A Song, Part II",Example Artist,Example Album,180.5,,,,1,2
日本語の曲,Example Artist,,175,,,,,
```

Optional durations use seconds in `length`/`duration`, milliseconds in `duration_ms`, or `m:ss` text. Blank duration means unknown; it is not zero-length audio. Preserve ISRC and Spotify IDs when available: they help conservative duplicate/version decisions. Supply a public HTTPS `cover_url` when available. `disc_number` and `track_number` are positive integers. Do not invent metadata to fill blank fields.

The app's CSV exports also include `artist_names_json` to preserve individual artist names, including names containing commas. Downloads use track metadata rather than row position as recording identity. Reimporting an updated CSV at the same location lets existing completed recordings be reused; it does not delete songs removed from the newer list.

## Pasted text

Use one **Artist - Title** per line. Spaced em/en dashes also work. The first spaced separator separates artist and title; additional dashes stay in the title. Blank lines are ignored. A malformed line rejects the whole pasted import so a partial queue is not silently accepted.

```text
Example Artist - A Song
別のアーティスト — 日本語の曲
Example Artist - A Song - Live
```

For artist names that contain a spaced dash, use CSV instead. Pasted lists preserve repeated entries for the normal duplicate matcher. A local cached CSV lets the tracklist reopen after restarting; equivalent pasted content keeps the same source identity. These private cached imports live in `state/imports/` and must not be included in bug reports.
