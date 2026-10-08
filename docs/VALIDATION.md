# Release validation

## 2026-10-09 local 1.13 follow-up

The separate test runner passed 29 suites with 0 failures and 0 skipped suites against the built production assembly. Counts represent suites, each containing multiple assertions. `build/test-results.xml` records per-suite durations, UTC timestamp, app/engine/runner hashes, local revision and whether the working tree is modified. The production-type check found no test classes in the app. No new live downloads or private-library tests were performed; earlier live results below apply to 1.12. Hosted CI results are recorded separately on the release pull request.

A synthetic 50,000-row catalog occupied 14,938,891 JSON bytes. On this machine serialization/write took 1,362.64 ms; deserialize/index construction took 2,177.70 ms; 1,000 indexed queries took 4.42 ms. These local measurements are not transfer-speed improvements or universal performance thresholds. The existing eight-image/200 ms artwork benchmark does not model MusicBrainz rate limits.

New deterministic fixtures cover exponential provider cooldowns, Retry-After date/delta values, state-directory failures, unreadable saved passwords, 500 filename inputs and 300 quoted CSV round trips. See [review decisions](REVIEW-1122.md).

## Earlier 1.12 validation

Layout follow-up: the new hidden-window regression test fails on the prior layout implementation with "Queue layout did not recover after resize." Section-size event handling repairs the regression. Coverage includes multiple window sizes, delayed section sizing, and minimize/restore with synthetic tracks and no private settings.

The latest 2026-10-08 candidate passed the full offline suite, including Soloist-key rejection without echoing its value and client-ID persistence filtering. Packaging rejection was verified both with a missing test receipt and mismatched binary hashes. Synthetic interface renders were inspected at default size and 800-by-600 client size; the narrow layout uses scrolling. The public screenshot contains synthetic tracks and blank credentials.

Broader live validation of the download workflow completed four distinct recordings, including Japanese. Two concurrent transfers were observed. All four completed files fully decoded and matched their embedded audio MD5 values. A completed two-track collection restarted with zero searches. Cancellation preserved completed work, and unavailable searches could retry after cancellation. Saved retry state was reloaded and checked using simulated future times for 15-minute initial and 30-minute second-attempt intervals. Direct Spotify image retrieval and artwork insertion on an isolated copy passed, followed by another successful audio checksum check. These tests do not establish lossless source provenance, 20 simultaneous transfers, or sustained throughput.

Live-test limits: Spotify Web API client credentials are not configured; authenticated playlist/album import remains unverified. A Soloist API key is not usable for this purpose. Native UI activation failed, so current click-through import/context-menu/Recycle Bin actions remain unverified; their underlying logic has offline coverage. MusicBrainz/Cover Art Archive fallback was not validated live. Do not describe this candidate as exhaustively tested on every workflow or environment.

2026-10-08 follow-up: reproduced the access-denied error when overwriting a hidden temporary playlist. The repaired workflow downloaded successfully with a hidden legacy active-playlist fixture in 36.2 seconds overall; its second run performed zero searches. The full offline suite passed, including a fake CLI that returns exit 1 with a fatal access error: the wrapper propagates a redacted exception and removes temporary credentials. The first suite attempt encountered a temporary executable cleanup access error; a complete rerun passed. Tests used C: temporary storage, not the real music library.

Local v1.12.0 candidate validated on Windows on 2026-10-07.

- App compiled from the public project using the documented build script.
- Pinned upstream engine archive and executable hashes verified.
- Full offline regression suite passed, including metadata migration, preserved relative music paths, conflict protection, repeat migration, corrupt-index rejection, source details, and prompt child termination after callback/storage failures.
- Synthetic interface preview inspected.
- Source and portable ZIP integrity and checksum checks passed.
- Packages exclude private settings, music, update archives, and personal test logs.
- Migration regression checks use synthetic local files; no real music library was modified for this revision.
- These historical results are from local validation before pull-request CI.

The subsequent local-only retry-list integrity fix also passed the full offline suite. Tests cover syntactically valid but damaged JSON, conflicting entries, missing schedules, intentionally empty lists, and isolation of unreadable queues in the retry editor. It is included in the 1.12 release.

On 2026-10-07, an authorized live Soulseek test downloaded a 12,915,143-byte FLAC in 30.9 seconds overall using the bundled engine. A separate full SmartDownloader workflow completed in 24.5 seconds. Repeating that workflow against its completed file returned success with zero network searches. Both used temporary storage outside the music library. These single-track observations include search and connection time; they are not sustained-speed or concurrent-throughput benchmarks and do not reproduce every peer or library condition.

The full offline suite passed after making retries search before optional metadata lookup. Its cancellation regression checks that a retry reaches the search stage before remote alias lookup while preserving the saved index and releasing its collection lock. Remote alternate-name lookup now has a shared 30-second budget; local aliases remain available afterwards. The user's reported 10â€“15-minute stall was not reproduced in the live single-track tests. Mock engine tests verify transfer bytes and configured concurrency, not internet throughput. See [code review](CODE-REVIEW.md) for the implementation decisions and remaining limits.
