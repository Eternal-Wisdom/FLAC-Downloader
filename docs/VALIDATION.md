# Release validation

## Release 1.19.1 completion cleanup (2026-10-19)

Completion cleanup is checked with synthetic saved music, partial transfers, repair backups, raw indexes, settled review jobs, repeated cleanup and an indexed-file protection guard. Older unindexed music is preserved while the redundant playlist directory is removed. All 40 regression suites passed with zero failures or skips on the final 1.19.1.0 build. A fresh pinned-engine offline workflow downloaded only the missing album track, removed staging/search/repair directories after completion, preserved the existing audio hash, and launched no engine after restart. Both final FLAC files passed full decoding. No fresh peer-transfer, authenticated Spotify API, or native click-through test was performed for this layout-only update.

## Local 1.19.0 layout verification (2026-10-19)

All 39 regression suites passed with zero failures or skips on the final local build. New coverage checks flat playlist output, album order across discs, friendly-folder migration, stable retry identity, same-name source separation, restart recognition, an interrupted folder journal, adoption after a crash, staging migration, file actions, and reports staying outside music. Existing safety, rollback, locking, artwork, duplicate, and engine suites also passed. Tests use synthetic files; private music is not used as test data.

The pinned engine was exercised end to end in its offline client with synthetic FLAC files: one preexisting album track stayed byte-identical, only the missing track was fetched through an album-folder pass, exactly two engine launches occurred, and restarting launched no engine. Final music contained only `01 - First.flac` and `02 - Second.flac`, and both passed full FLAC decoding. This is an offline engine workflow, not a fresh Soulseek peer-transfer or authenticated Spotify API test. These checks cover the offline engine workflow.


## Release 1.18.1 verification (2026-10-19)

All 38 regression suites passed again with zero failures or skips on the final 1.18.1.0 executable on 2026-10-19. Fresh live public-album imports returned 18, 9 and 36 tracks with required metadata and CSV round-trip counts intact. Native UI checks loaded THE BOOK without credentials, filtered its Japanese title, and restored its nine-track queue after restarting the isolated app. The test UI deliberately omitted the engine folder; actual engine behavior was checked separately below. Live Soulseek peer transfers and authenticated Spotify Web API imports remain untested for this release. An additional end-to-end run through SmartDownloader and the actual pinned engine's offline client used synthetic FLAC audio: one existing recording stayed byte-identical, only the missing track downloaded via an album folder, both tracks were indexed, temporary peer links were removed, and restart launched no engine. The pipeline used exactly two engine runs (one album discovery and one direct-file pass), with no individual song search.

The album-first pipeline uses the pinned engine's album search rather than creating one album job per song. Tests exercise actual engine album-folder JSON output and a direct single-file download through its offline mock client; the stable row filename is checked and unrequested files must not download. Regression coverage checks folder/artist/title/duration matching, Unicode, version rejection, ambiguous duplicates, coverage ranking, skipping completed tracks, progress identity, restart recovery, invalid-header and wrong-duration rejection, and removal of temporary direct links.

This is offline engine validation, not a fresh live Soulseek transfer or a speed benchmark. Folder names and peer metadata cannot prove edition or source provenance. Compilation folders without per-track artists, translated titles, and missing durations may fall back to individual searches. Discovery is bounded to 75 seconds including pacing; cancellation remains available.

## Local 1.18.0 improvement review (2026-10-17)

All 37 suites passed with zero failures or skips before final packaging. New coverage checks credential-free public album import through a mock HTTP handler, cancellation, rate limiting without retry spam, album identity, malformed/empty/prerelease/very-large/partial metadata rejection, artist credits, durations, Unicode, ignored anonymous page tokens and unknown disc/ISRC fields. Existing authenticated/paginated API album tests still pass.

Fresh live imports using the built importer, with no Spotify credentials, returned 18 tracks for Global Warming, 9 for THE BOOK, and 36 for Ghosts I-IV. All imported tracks had positive durations and artist data; each exported CSV reimported with the same count. These use Spotify's public album embed, whose internal page schema may change and whose completeness cannot always be proved from the page. The app rejects explicit truncation, lists of 100 or more tracks, missing fields and identity conflicts; it asks users to review the count and offers CSV fallback. Anonymous page tokens and audio previews are not used or cached. Signed-in Web API import remains a separate, previously untested live route without a developer Client ID.

Native UI verification loaded THE BOOK directly into a nine-song queue without showing Spotify setup and with blank account settings. The diagnostics dialog displayed only approved version/policy/count fields. Pasted text rejected a malformed second line without replacing the queue, then imported two valid Unicode tracks. Reopening restored both pasted tracks and the same source identity, preserving the download-folder identity. Regression checks also cover free-space/path/cloud preflight, an unwritable file-parent conflict, pasted CSV, atomic text validation and adversarial diagnostic input.

No new Soulseek audio-transfer test was performed for 1.18.0. The earlier 1.17.6 online and decoder results below remain historical evidence. Destination checks do not diagnose hardware health or estimate total batch size. New dependencies, signing claims, partial-transfer resume promises and single-master album guarantees are not introduced. See the [proposal decisions](REVIEW-IMPROVEMENT-IDEAS.md).

## Release 1.17.6 live verification (completed 2026-10-17)

The installed 1.17.6 executable was copied into isolated test folders and hash-matched before testing. All 21 online backend assertions passed, and the full regression run passed 35 suites with zero failures or skips. Test account settings were cleared afterward. No private music or personal diagnostic logs are included in the release.

Three distinct English/Japanese recordings downloaded from a four-entry CSV, with the repeated entry skipped. Restart performed zero searches. An unavailable synthetic recording was exported and reimported, retry scheduling remained inactive at 14 minutes and due at 16 minutes, and cancellation after a real search preserved completed audio. Direct artwork, MusicBrainz/Cover Art Archive fallback, downloaded-copy embedding, and a four-file artwork pipeline passed. Full FLAC decoding passed for four backend files and three GUI files without changing their hashes; decoding does not prove lossless source provenance.

The native app started real transfers from the Download button and displayed live status and speed. Force-killing the isolated app stopped its real engine; reopening removed its abandoned temporary sign-in file. Resume finished all three recordings and preserved the hash of the recording completed before the crash. The reopened app showed 3 of 3 downloaded, zero unfinished and zero unavailable. Another Download click skipped completed audio and left all audio hashes unchanged. The Activity window updated while open, including completion messages; Ctrl+F, substring filtering and Escape reset passed. A real Windows Recycle Bin test on isolated copies reset the selected recording while preserving the other two.

Observed peak rates were 6,479.2 KiB/s in the first backend pass and 9,327.6 KiB/s during its retry. These are peer-dependent peaks, not sustained benchmarks or evidence of a throughput improvement; only one moving transfer was observed at a time. GUI cover repair reported two covers present and one unavailable. Spotify API import remains excluded because no Web API Client ID is configured. Every concurrency/profile combination and every possible network condition were not tested.

## Local 1.17.6 interrupted-session protection (2026-10-16)

The added Windows tests use synthetic sign-in files and disposable local processes. They exercise abandoned-file cleanup, active lease exclusion, locked-file retry, unrelated-file preservation, directory junction exclusion, cleanup before a mock engine run, kill-on-close job disposal, and force-killing a helper parent while its assigned child is running. They do not access the user's music or saved credentials.

Job containment is best effort: the engine starts before assignment, and Windows may reject assignment. The app logs that failure. Cleanup does not terminate uncontained orphan processes or promise secure erasure of previously deleted files. REA's reviewed native boundary now includes the four Windows job/handle calls in addition to the existing UI message call.

These initial offline checks were followed by the fresh 1.17.6 live verification documented above. Spotify live import remains untested without a Web API Client ID. Earlier network results below remain historical evidence.

## Local 1.17.5 optimization review

All 34 suites passed with zero failures or skips with normal local Windows access. The initial sandboxed run denied temporary-file moves in both existing and changed code; rerunning the same build outside that restriction passed. No new live peer-transfer test was performed for this batch.

The queue sort comparison used 10,000 synthetic rows, ten status sorts per sample, one warm-up and five samples per version. Median elapsed time was 1,050 ms on 1.17.4 and 117 ms on 1.17.5. These are UI computation measurements, not network throughput or whole-app speed measurements.

Regression coverage includes non-finite durations remaining last in both sort directions, stable source order, live status regrouping, and a deliberately failing CSV export preserving the original file without temporary-file residue. Successful replacement is reimported with Unicode intact. CSV writing also reuses its serializer across tracks. No new dependencies or transport/concurrency settings were introduced. Download orchestration, cancellation, artwork processing, retry persistence and file-action code were reviewed; changes in this pass are limited to CSV safety and sorting. Spotify live import remains untested without a Web API Client ID.

## Local 1.17.4 cleanup verification

All 34 suites passed with zero failures or skips on the final executable. Activity regression checks verify that unrelated incoming messages cause no filtered text replacement and preserve the selected text, alongside existing live-update, Unicode and pause/resume tests. This is a display optimization; no network throughput improvement is claimed. The preceding 1.17.3 online results remain historical evidence, not a new online test of 1.17.4.

## 1.17.3 live verification, 2026-10-14

The tested application matched the installed executable hash. All 21 isolated online checks passed: English and Japanese Soulseek downloads, recording deduplication, byte-progress telemetry, hidden legacy playlist preservation, completed-file resolution, restart with zero searches, unavailable-song export, 15-minute retry scheduling, cancellation and artwork HTTP/fallback/embedding workflows. The initial Japanese attempt was unavailable; the next pass succeeded without repeating the completed English songs. Two simultaneous moving transfers were observed in the first run. Reported peak rates are peer-dependent snapshots, not benchmark guarantees.

The actual Windows GUI independently downloaded three distinct recordings from four CSV entries, showed completion and a live-updating open activity log, and left all completed audio unchanged when Download FLAC was pressed again. Native checks exercised clickable sorting, filtered Ctrl+A selection, the selected CSV save dialog, Ctrl+F activity search and Escape clearing. The exported file contained exactly two visible selected songs. A real Windows Recycle Bin test on disposable copies removed only the selected file and reset its indexed status while preserving the other two.

Full FLAC decoding passed for four backend-test files (including an artwork-modified copy) and all three GUI-downloaded files. All 34 offline suites also passed again. No application correction was required. Automatic cover lookup reported three unavailable covers for the GUI fixture; direct artwork, MusicBrainz/Cover Art Archive fallback and embedding tests passed separately. Cover availability and peer availability cannot be guaranteed. Spotify authentication/import remains excluded because no Web API Client ID is configured. These checks do not exhaust every possible peer, playlist, interrupted transfer or Windows environment. Test music and private diagnostics were isolated and are excluded from release packages. Nothing was uploaded.

## Local 1.17.3 activity search verification

All 34 suites passed with zero failures or skips. Activity tests verify case-insensitive filtering, live matching Unicode updates, no-match results, paused snapshot preservation, clearing search and catch-up when following resumes. A synthetic activity-window render was visually reviewed. No new online transfer or authenticated Spotify test was performed for this local log feature.

## Local 1.17.2 selected export verification

All 34 suites passed with zero failures or skips. Synthetic queue tests export two selected filtered songs, exclude hidden rows and source paths, and reimport the CSV with both distinct artists in display order. Existing CSV tests cover Unicode, quotes, commas and durations. No new online transfer or authenticated Spotify test was run for this local export feature.

## Local 1.17.1 selection verification

All 34 suites passed with zero failures or skips on the final executable. Added regression checks require clickable sorting headers and verify that selecting a filtered queue selects only its two visible songs, copying excludes hidden rows, and clearing selection produces empty copied text. Existing sorting, retry, file actions and layout checks pass. No new online transfer or Spotify authentication test was performed for this UI change.

## Local 1.17.0 organization verification

All 34 suites passed with zero failures or skips on the final executable. Sorting tests cover both directions, stable ties, unknown durations, selection preservation, unchanged imported order, source-order restoration and live completion regrouping. Retry tests cover Unicode search, empty matches, filter restoration, Scheduled/Paused/Unreadable views, pause/resume updates, corrupt-file preservation and minimum-width layout. The window caption is checked against assembly version. Synthetic WinForms renders were visually reviewed. REA scope and limitations are in REA-FEATURE-REVIEW.md.

No new online peer transfer or Spotify authentication test was performed for these organization features; no download-speed claim is made. Private music and settings were not used for tests.

## Local 1.16.2 prepared row identity verification

All 33 suites passed with zero failures or skips. Regression checks verify that prepared row identities equal the imported recording identities and that reimport recreates those identities; existing duplicate, Unicode, hidden-row and terminal-progress tests remain passing. Three isolated 10,000-event UI comparisons took 35/36/36 ms on 1.16.1 and 31/31/31 ms on 1.16.2, with Gen0 collections reduced from two to one. This is a modest reduction in UI processing and temporary allocations, not network throughput. No new online peer download or Spotify authentication test was run.

REA 6.4.0 inspected exact copies of Motrix task-manager.ts and task-update-publisher.ts from commit 99a40ab3dd91133056ac2cc08e5bbd90ce3afb9e. Both files parsed without failures and the focused structural graph reported complete coverage. This applies to the supplied two-file slice only; it does not prove complete application or runtime behavior. Direct source review corroborated reuse of stable task identities. No Motrix code or dependency is bundled.

## Local 1.16.1 progress display verification

All 33 suites passed. A 10,000-event synthetic burst verified that unchanged progress status does not rebuild the summary or rescan a status filter, and that completion flushes immediately. Source-details observation still runs for each event. A comparison with 1.16.0 measured 679 ms versus 43 ms for that event path. This is a UI microbenchmark, not a Soulseek speed measurement. No new online download or authenticated Spotify test was run for this display-only change. See REA-DOWNLOADER-REVIEW.md for upstream evidence and limitations.

## Local 1.16.0 UI verification (2026-10-13)

All 33 automated suites passed with zero failures or skips against the final executable. Layout checks cover normal and minimum sizes, resize/minimize recovery, action captions, optional setup, empty-state import, and busy-state menu availability. Synthetic renders were inspected at 1240×900 and 850×650.

The running isolated app imported a two-song synthetic CSV through the native picker. Ctrl+F narrowed the queue to one song and Escape restored both. The Library tools menu opened saved retry review successfully. Selecting Balanced changed the quality caption; optional Spotify setup expanded with vertical scrolling and collapsed again. No private settings, credentials, or music were used.

This revision changes presentation and menu wiring. No new live peer download was run for 1.16.0; the preceding 1.15.3 live results remain below. Authenticated Spotify import remains untested without a Web API Client ID.

## 1.15.3 live release verification

The exact 1.15.3 executable was tested in isolated storage through a fresh CSV/Soulseek workflow. All 21 live harness assertions passed. Two English songs completed first; the Japanese song was initially unavailable and completed on the next attempt, while completed songs were preserved. Four CSV rows produced exactly three distinct recordings. A completed restart issued zero searches. Live byte progress was observed, including two simultaneous moving transfers in the first run. Peak sampled aggregate speed was about 9.6 MiB/s across the runs; this is peer-dependent, not sustained throughput or a guarantee of twenty simultaneous transfers.

An unavailable synthetic song was recorded as failed, exported/reimported through CSV, and scheduled for retry after the initial fifteen-minute delay. Cancellation after a real search ended cleanly and preserved all completed recordings. Direct cover retrieval and MusicBrainz/Cover Art Archive fallback passed. The new cover pipeline fetched and embedded artwork into four synthetic files and wrote all four report entries. Three downloaded recordings plus an artwork-modified copy passed the official FLAC decoder's full audio check without changing their bytes. Temporary engine sign-in files were absent after the harness finished.

Native app checks verified Japanese Find/Ctrl+F/Escape, album counts, Show in folder selecting the correct file in Explorer, quick header checks, repair preview/application, and Activity updates while its window stayed open. Pausing Activity retained the viewed text during another check; resuming caught up to both messages. The production Recycle Bin component removed a disposable copied file, reset its index, and preserved both unselected files; this bypassed confirmation dialogs, so the native deletion-confirmation path was not exercised. Full audio verification was also run from the app using the official decoder picker. The full offline suite passed with 33 suites, zero failures, and zero skips.

Spotify API import was excluded at the user's request. Saved retry timing was tested against real failed-track state with a simulated future clock; this was not a fifteen-minute unattended wait. The live run used the Balanced profile with eight job slots, not an exhaustive live comparison of all concurrency/profile combinations. Raw logs, test accounts, downloaded test music, private state, and personal paths are excluded from release files.

## Local 1.15.3 cover scheduling verification

All 33 offline regression suites passed with zero failures or skips. A controlled comparison used the previous FIFO implementation and the new completion-order implementation on 24 synthetic files with a 1,200 ms first lookup and 100 ms later lookups. FIFO took 2,088 ms; completion order took 1,248 ms (about 40% less elapsed time). Both reached at most four concurrent lookups and produced byte-identical files. This measures scheduling under simulated network delays, not a live Soulseek speed increase.

New deterministic tests hold the first lookup until all eight fixture lookups have started, proving that later completed work replenishes the bounded window. They compare resulting files with sequential output. Another test faults a later lookup asynchronously while earlier ones are blocked and verifies prompt failure propagation and complete cancellation/draining. Existing cancellation, malformed-file, ownership, recovery, report, and transfer tests remain passing. Cover report rows now follow completion order; playlist order is unchanged. No private library was used for these tests. Authenticated Spotify import remains untested without a Web API Client ID.

## Local 1.15.2 queue performance verification

All 33 offline regression suites passed with zero failures or skips. The queue test exercised 5,000 synthetic songs through twenty search changes in 3,493 ms, retained hidden rows across status restoration, and verified that hidden completions appear in the Completed filter. New checks preserve Unicode, null album fields, whitespace normalization, and searches crossing field separators, and verify that All songs progress does not schedule a membership scan.

An in-process comparison over 500,000 matching searches measured the previous combined-field implementation at 113 ms with twelve generation-zero collections and the new implementation at 57 ms with zero collections. These are local synthetic measurements of matching work, not end-to-end UI or network throughput guarantees. Transfer engine, concurrency, file handling, and retry policy are unchanged; no new live peer download was required for this queue-only change. Authenticated Spotify import remains untested without a Web API Client ID.

## 2026-10-11 live verification and local 1.15.1 follow-up

The exact installed 1.15.0 executable was tested in isolated C: storage. Four distinct recordings completed, including Japanese, with one repeated playlist entry suppressed. The Japanese track was unavailable on the first attempt and completed on the next: the initial harness had six failed assertions derived from that temporary absence, so it was not an all-pass first run. Follow-up confirmed all three initial recordings, exactly three files, and a completed restart with zero searches in 0.2 seconds. A fourth recording completed through the native app interface, which displayed four downloaded recordings and 100% completion.

Two simultaneous transfers and a peak sampled aggregate rate of about 11.3 MiB/s were observed across the runs. These are peer-dependent observations, not sustained-throughput or twenty-transfer guarantees. All four recordings and the artwork-modified copy passed the official FLAC decoder's full audio/MD5 check. Direct artwork retrieval, embedding into an isolated copy, and MusicBrainz/Cover Art Archive fallback passed. One album cover remained unavailable in the native app repair; cover availability is not guaranteed.

Live unavailable-song export/reimport, the initial 15-minute retry schedule, cancellation after a real search, and completed-file preservation passed. Native UI checks verified Japanese queue search, completed-status filtering, album availability/positions, local source/header details, Explorer selection for Show in folder, repair preview/application, and Activity updates without reopening. Pausing Activity kept its text unchanged during another file check; resuming displayed the collected message. The production file-action component used the real Windows Recycle Bin on a disposable copied collection, reset the selected recording's index, and preserved the three unselected files; this integration bypassed confirmation dialogs, so the native deletion-confirmation click path was not exercised.

Live repair exposed stale idle transfer text after an earlier download. Local 1.15.1 clears the last transfer timestamp and resets the speed line when work finishes, with a regression check covering the stale-state transition. Its final 33-suite run passed with zero failures or skips. The patched native interface selected the official decoder through its file picker, completed four full audio checks, saved a separate audio report, and displayed Transfers stopped afterwards. Authenticated Spotify playlist/album import remains untested: the user confirmed no Web API Client ID is available. No private music library or existing personal settings were accessed. New test-only account settings were cleared after testing. No new upload is authorized by this test request.

## 2026-10-10 local 1.15.0 candidate

The production build passed 33 offline suites with zero failures and skips. New coverage checks album counts and CSV position round trips, Unicode filtering, same-title artists, hidden-row status restoration, live hidden-song completion, paused-log catch-up, and read-only repair plans. A 5,000-song queue completed twenty search/reset operations in approximately 3.5 seconds on this machine (an early candidate measurement, not a transfer-speed benchmark). Synthetic default and narrow-window renders were inspected; a native combo-box contrast issue was corrected.

A separate integration run against the official FLAC 1.5.0 Windows x64 decoder passed generated audio and rejected corrupted and truncated copies whose STREAMINFO headers still passed. Cancellation before scanning and preservation of original bytes were verified. The archive was checked against Xiph's published SHA-256. The optional decoder is not bundled. Reproduce this integration with Windows PowerShell and `scripts/Test-AudioDecoder.ps1 -Decoder <path-to-flac.exe>` after building.

This revision has no new live Soulseek throughput claim. The download engine and concurrency policy are unchanged; earlier live results remain historical evidence only. Authenticated Spotify import still needs a Web API Client ID and remains a known live-test gap. No private music, credentials, or personal settings were used for these tests. See [implementation decisions](REVIEW-1.15.md).

## 2026-10-09 local 1.14.1 Activity fix

The built app passed 31 offline suites with zero failures and skips. A real WinForms message-loop regression confirms that worker-thread log entries reach the open Activity window through the existing 200 ms UI timer, the main controls stay enabled, repeated opening reuses one window, closing/reopening retains recent entries, and output remains capped at 160 lines. The test restores its UI synchronization context so subsequent asynchronous suites remain isolated. No new live downloads were needed for this display-only change; 1.14 live results below apply to the unchanged download workflow. Hosted validation is recorded on the release pull request.


## 2026-10-09 local 1.14 follow-up

The built production app passed 30 offline suites, 0 failures and 0 skipped suites under Windows PowerShell 5.1. Added tests verify missing-recording selection, downloaded reissue suppression, Japanese/quoted CSV reimports, honest FLAC header descriptions, and bounded, paced recording lookups when MusicBrainz ISRC responses omit releases. Wrong recording IDs/ISRCs and malformed responses cannot supply artwork; transient lookup failures are not cached as permanent misses.

Live CSV/Soulseek tests completed three distinct recordings, including Japanese, in isolated temporary storage. The Japanese recording was temporarily unavailable on the first search and completed on the next attempt. Two simultaneous transfers were observed, with a peak sampled aggregate rate of approximately 12.4 MiB/s; this is not a sustained throughput benchmark or a promise of peer availability. Restarting the completed collection performed zero searches and created no duplicates. Unavailable-song export/reimport, scheduled retry timing, cancellation and preservation of completed work passed. All three audio files and the artwork-modified copy fully decoded and matched their embedded audio MD5 values.

Direct artwork retrieval and insertion passed. The live MusicBrainz/Cover Art Archive test exposed omitted release data in an ISRC response; the bounded recording follow-up fix retrieved the verified album cover successfully, and cache reuse passed. Native interface tests confirmed local header details and Japanese/quoted missing-song CSV export and reimport using synthetic data and blank credentials.

Release scope is CSV/Soulseek. Authenticated Spotify playlist/album import remains untested because a Web API Client ID is not configured. Native Explorer/Recycle Bin actions have offline logic coverage but were not exercised in this follow-up. These results do not establish lossless source provenance, twenty simultaneous transfers, or correctness on every peer and Windows environment. No private music library was used. See [proposal decisions](REVIEW-PROPOSED-FUNCTIONS.md).

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



