# Using the main window

Current guide for **1.19.1**. Made using AI (OpenAI Codex).

Click a song column heading to sort the displayed queue; click again to reverse. **#** restores source order. Sorting does not change download priority or playlist order.

In **Review saved retries**, search songs, artists or recorded reasons and choose All retries, Scheduled, Paused or Unreadable. Ctrl+F focuses search; Escape clears it. Filtering does not remove entries or start downloads. Actions apply only to selected visible editable entries.

Made using AI (OpenAI Codex).

1. Paste a public Spotify album URL and choose **Load link** without developer setup. Or use **Import > Import CSV file** or **Import > Paste songs or CSV**. Spotify playlist links use the optional **Spotify link setup** connection.
2. Enter your Soulseek username and password. Choose a quality preference and concurrent jobs; jobs include searches and queued songs, so the number is not a promise of active transfers.
3. Choose the music folder and select **Download FLAC**. Existing verified downloads are reused.

Use **Ctrl+F** to find songs by title, artist, or album. **Escape** clears the search. Filters change the visible rows, not the download scope. Double-click a song for details; right-click for file actions.

**Library tools** contains header checks, optional full audio checks, repair previews, saved retry review, unavailable-song export, safe diagnostics and troubleshooting. **Activity** shows the running log. Optional Spotify setup is not needed for public album, CSV or pasted-text imports.

Public album metadata includes song titles, artist credits, durations and available artwork. Review the count: public pages can be unavailable or change format. The app refuses explicitly incomplete, prerelease, empty or very large public lists. CSV is the fallback. It does not use preview audio or anonymous page tokens. Albums first try a matching shared folder, then search for missing songs individually; a single edition or master is not guaranteed. Imported public album and pasted tracklists reopen from private cached CSVs while preserving collection identity.

Pasted text uses one **Artist - Title** per line, with spaced em/en dashes also accepted. Invalid lines reject the whole text import; nothing downloads automatically. For punctuation that makes artist/title boundaries ambiguous, paste a CSV with headers instead. See [tracklist formats](CSV-FORMAT.md).

Before the engine starts, the app checks destination readiness, minimum free space, writable access and path/link safety. Warnings identify cloud-synced, network and long destinations; the reserve is not a prediction of total download size. See [troubleshooting](TROUBLESHOOTING.md).

For a bug report, open **Library tools > Preview safe diagnostics**, review the version/policy/count fields, then choose **Copy diagnostics**. Raw logs, account details, song names, paths, peers and IP addresses are excluded. Nothing is sent automatically. Track details and saved retries explain the engine's reported failure code; they do not invent individual filter rejections.

The optional Spotify help dialog includes **Copy URI** for the exact redirect address. It is only relevant when configuring a Web API application.

Select songs with Ctrl+A to select all visible rows. Filtering first limits the selection. Ctrl+C copies selected titles and artists. The right-click menu also offers Select all visible songs and Clear selection.


To save a subset, filter or select songs, then right-click and choose Export selected songs as CSV. The CSV contains music metadata and can be reimported; it excludes account settings and local music paths.


In Activity, use Find to show messages containing a word or phrase. Ctrl+F focuses Find; Escape clears it. Follow new messages keeps results current. Turn Follow off to search the frozen reading snapshot, then turn it on to catch up. The existing recent-message limit still applies.


## Album-folder matching

Spotify album imports search shared album folders before individual songs. Activity explains folder coverage and the number of missing tracks requested. Completed songs are kept. Matches require FLAC, matching title/version text, artist and album folder labels, and a duration within five seconds. Only unambiguous matches are accepted; remaining songs use the existing individual searches. Folder selection prefers track coverage, then the selected quality/speed policy. A folder match cannot prove the release edition or source provenance.

