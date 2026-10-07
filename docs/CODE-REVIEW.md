# Architecture review and recommendation decisions

Reviewed 2026-10-07 against the v1.11.0 source and bundled Sockseek 3.0.5 source. Changes are a local v1.12.0 candidate, not a published release.

## Rewrite decision

Retain the existing application. Its small WinForms shell, separate import/recording/transfer modules, pinned engine, synthetic tests, and recovery mechanisms are useful foundations. A full rewrite would incur substantial compatibility and migration work without evidence of better matching or network throughput.

The code does need incremental cleanup. Several files contain dense one-line methods, the main form owns too many responsibilities, and progress callbacks currently perform synchronous bookkeeping. Future work should extract a download-session controller and keep transport events separate from library transactions. Avoid changing these boundaries together with album matching or transfer scheduling.

## Findings fixed

| Finding | Change | Verification |
| --- | --- | --- |
| A reader/callback failure could leave the child running while the parent waited for exit. Some callback exceptions were mistaken for malformed JSON. | Stop the child on a reader failure, propagate the original exception, and dispatch progress outside JSON exception handling. | Synthetic child sleeps for 30 seconds; injected storage and callback failures must end the run within five seconds and remove its temporary credential file. |
| Index parsing silently ignored short rows or invalid numbers. A later save could remove that history. | Require the expected columns and complete, parseable rows; preserve malformed input and fail visibly. | Empty, wrong-header, partial-row, and invalid-number fixtures; existing canonical and real-engine index tests. |
| Failed periodic finalization was logged while downloading continued. Exception cleanup attempted additional index/rename writes. | Stop on unexpected finalization failures and leave active recovery data available for a later explicit run. | Exception propagation tests plus existing recovery, cancellation, index, and naming regressions. No physical disk-failure experiment was performed. |
| The source-start event looked like active downloading. Users could not inspect the chosen remote file. | Show that a source is selected and waiting for bytes; add a session-only details view with peer, file, size, format, matching policy, and explicit unknowns. | Synthetic source events, metadata reset on source changes, 64-bit progress tests, and interface preview. |
| Preview startup loaded private settings, and settings callbacks could write during preview. | Enter preview mode before form initialization, skip loading private settings, and block settings writes. | Synthetic preview rendering; no real account or music library required. |
| Test execution could read an old success report. | Remove the previous report before starting; kill timed-out test processes. | Test script review and fresh full-suite execution. |

## Recommendation decisions

| Research recommendation | Decision and reason |
| --- | --- |
| Explain source selection | Implemented the available source facts and policy explanation. Full candidate ranking/manual override is deferred: the current event feed does not provide complete ranked candidates. Do not invent a per-file ranking explanation. |
| Clear queue and transfer visibility | Improved the source-start label and details. Exact peer queue positions remain unavailable through this CLI event feed. Existing retry review already shows scheduling information. |
| Adaptive concurrency and peer learning | Deferred pending controlled benchmarks and engine-level telemetry. The CLI globally throttles progress reports, and some events contain a job ID without song identity. Increasing jobs is not evidence of more active transfers or greater throughput. |
| Coherent album-folder downloads | Deferred. Current wrapper bookkeeping assumes one CSV row per track and deduplicated recording. Folder-mode output must first be mapped by disc, track, edition, and identity, including interrupted and mixed-source cases. Existing album-link import remains available. |
| Library-change preview | Useful future work. Build a shared read-only change plan consumed by both preview and commit, rather than duplicating matching/rename logic. Current recovery protections remain in place. |
| Full FLAC decode verification | Deferred as an optional feature. Header checks already exist; adding a decoder introduces package size, licensing, CPU, and disk-load considerations. A successful decode still cannot establish lossless provenance. |
| Responsive large lists and storage migration | Profile first. Keep existing indexed row lookup and recording catalog; no demonstrated reason to introduce SQLite or rewrite the UI framework in this revision. |
| Schedules, bandwidth limits, notifications | Lower priority than correctness. Retain explicit retry arming and current backoff. Validate engine support before presenting a rate-limit control. |
| Public release trust | Keep pinned engine hashes, AI disclosure, source packaging, and private-state exclusion. Signing and an updater need a separate release design; this candidate is not silently published. |

## Known boundaries

The bundled reporter's search/start/terminal events carry artist/title, while one progress path carries only job ID. It does not expose the mapping needed to assign those byte events safely to a song. Such events remain aggregate-only. Source peer/file details are held in memory and do not enter public diagnostics automatically.

There was no new live Soulseek throughput benchmark. Offline tests check concurrency limits, bytes copied, filtering, cancellation, and error behavior; they cannot establish internet download-speed gains. No real music library was modified for this review.

See [downloader research](DOWNLOADER-RESEARCH.md) for upstream sources and proposed benchmark criteria.
