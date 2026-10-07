# Release validation

Local v1.12.0 candidate validated on Windows on 2026-10-07.

- App compiled from the public project using the documented build script.
- Pinned upstream engine archive and executable hashes verified.
- Full offline regression suite passed, including metadata migration, preserved relative music paths, conflict protection, repeat migration, corrupt-index rejection, source details, and prompt child termination after callback/storage failures.
- Synthetic interface preview inspected.
- Source and portable ZIP integrity and checksum checks passed.
- Packages exclude private settings, music, update archives, and personal test logs.
- Migration regression checks use synthetic local files; no real music library was modified for this revision.
- These results are from local validation before pull-request CI. This candidate is not a published release.

No new live-peer speed benchmark was run. Mock engine tests verify transfer bytes and configured concurrency, not internet throughput. See [code review](CODE-REVIEW.md) for the implementation decisions and remaining limits.
