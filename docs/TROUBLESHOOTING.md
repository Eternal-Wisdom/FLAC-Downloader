# Troubleshooting

Made using AI (OpenAI Codex).

## Does an album link need Spotify setup?

Public Spotify album links can be imported without a developer Client ID or Spotify sign-in. Paste the normal `https://open.spotify.com/album/...` link and choose **Load link**. The app reads public embed metadata and never uses its audio previews or anonymous tokens. Review the imported track count. Public page formats and regional availability can change; incomplete, empty, prerelease or very large public lists are rejected rather than silently truncated. Use CSV when a public page cannot be imported. Connected users retain the paginated Web API album route.

Spotify playlist links still use the optional Web API connection. The public album route is not a promise that every playlist can be read without authentication. CSV and **Import > Paste songs or CSV** work without Spotify setup.

## Why is a song searching, queued or unavailable?

Searching and queued jobs are not moving transfers. Peers can be busy, disconnected or unreachable. The speed display changes only when bytes arrive. A larger job limit does not guarantee more active transfers or higher speed.

Open a song's details to see the engine's reported reason. **No search results** means the query returned nothing. **No matching results** means results failed the active filters; the adapter cannot identify each rejected filter. **All transfers failed** and **retry limit reached** describe attempted downloads. Do not assume an unavailable track is missing from Soulseek forever.

Saved retries begin after 15 minutes and back off to six hours. Stop pauses automatic work. Use **Library tools > Review saved retries** to inspect or pause individual saved entries.

## Why did a download stop before connecting?

The destination is checked before starting the engine. A drive with less than 64 MiB free is blocked; less than 1 GiB produces a warning. This is a minimum reserve, not an estimate of space needed for the whole tracklist. A temporary write probe checks the chosen folder and removes itself. Linked folders, device paths and unusably long destinations are refused. Cloud-synced and network folders produce warnings because locks, renames and disconnects can interrupt file updates. Prefer a short path on a healthy local drive.

An access-denied error can also mean another download owns the collection lock, files are read-only, or another application is holding them. The app preserves recovery files. Do not delete indexes, `.incoming` or recovery folders blindly: interrupted audio may still be recoverable. If Windows reports bad blocks or I/O failures, stop using that drive and protect the data before further writes.

## Is my FLAC complete and truly lossless?

**Check FLAC headers** validates metadata only. **Full audio check** uses a separately supplied official `flac.exe` to decode the entire file with timeout and cancellation. A successful decode does not prove the original source was lossless, identify a master, or certify a bit-depth improvement. Existing covers are retained; missing covers depend on metadata and artwork-service availability.

## Login keeps disconnecting

Disconnect SoulseekQt or Nicotine+ before using the same account here. Two clients can disconnect each other. Some peers require sharing; configure suitable shared music in your Soulseek client, using only files you may share. This application does not configure sharing. Do not repeatedly retry an incorrect password or post credentials in issues.

## Reporting a problem safely

Use **Library tools > Preview safe diagnostics**, review the text, then choose **Copy diagnostics** if you want to share it. The report is built from an allowlist of versions, flags, policy and counts. It excludes logs, accounts, song names, paths, peers and IP addresses. Nothing is uploaded automatically. Explain the problem separately and reproduce it with synthetic tracklists where possible. Do not attach `state/`, music, real playlists or raw Activity logs.
