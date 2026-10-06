# Release validation

Validated locally on Windows on 2026-10-06.

- App compiled from the public project using the documented build script.
- Pinned upstream engine archive and executable hashes verified.
- Full offline regression suite passed, including metadata migration, preserved relative music paths, conflict protection, and repeat migration.
- Synthetic interface preview inspected.
- Source and portable ZIP integrity and checksum checks passed.
- Packages exclude private settings, music, update archives, and personal test logs.
- Collection migration verified: metadata content hashes and playable playlist preserved, without scanning or rewriting audio.
- GitHub-hosted CI and public upload have not been executed yet.

No new live-peer speed benchmark was run for this folder/packaging release.
