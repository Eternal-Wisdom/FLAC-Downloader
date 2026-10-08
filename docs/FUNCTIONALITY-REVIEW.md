# Functionality and dependency review

Reviewed 2026-10-07. These changes remain local; no GitHub upload or release is authorized. This is a scoped source review and synthetic regression exercise, not a guarantee that every possible bug has been found.

## Workflow decisions

| Area | Assessment and decision |
| --- | --- |
| Spotify playlist/album import | Keep PKCE, loopback callback, in-memory tokens, bounded pagination, destination validation and paced rate-limit handling. Album import is individual-track import, not a guarantee of one coherent album edition. Do not replace this with scraped browser credentials. |
| CSV import | Keep the current parser and validation tests. A parser replacement needs a compatibility corpus of supported exports first; adding a dependency by itself does not improve existing imports. |
| Unicode search | Keep conservative normalization and source-provided bilingual alternatives. Do not guess kanji readings or strip version qualifiers indiscriminately. |
| Candidate quality and transfer speed | Keep strict FLAC/title/artist/duration constraints and bounded profiles. Source details explain available evidence. The event stream does not support trustworthy per-track adaptive scheduling everywhere; retain fixed bounds until controlled peer benchmarks and identity mapping exist. |
| Retry lists | Retain explicit arming, 15-minute initial delay, increasing backoff and per-entry pause. The preceding local change rejects structurally damaged queues rather than dropping history. |
| Recording duplicates and names | Retain recording-level identity, duration/version separation, artist suffixes only for title collisions, reversible changes and download locks. A shared preview/apply plan is a better future improvement than rewriting matching rules twice. |
| Cross-collection reuse | Changed repeated full catalog scans into an ISRC lookup with cached recording descriptors. Artist/title/version/duration checks, source timestamp/size/signature checks and byte copying remain in place. Registration refreshes the in-memory lookup. Unknown ISRCs still require search. |
| Artwork | Keep four bounded lookups, shared cache, provider backoff, and serialized reversible metadata writes. A generic tag library would not automatically provide the current recovery guarantees. |
| File checks | Changed scanning to exclude incoming/search staging and legacy naming/artwork recovery folders as well as existing exclusions. Skip linked FLAC files and reject linked parents. Acquire the collection lock before scanning or changing reports, preserving existing reports if another process owns the folder. |
| File actions | Retain saved-identity resolution, selection-specific Explorer reveal, confirmation for recycling, retry pause and status refresh. Tests substitute a reversible temporary move; Windows Recycle Bin interaction was not exercised on personal music. |
| UI and activity | Existing bounded log queue and indexed row lookup are useful. Extracting a session controller is preferable to a framework rewrite, but should be a separately tested refactor. No unsupported claim of a faster UI is made here. |
| Storage and recovery | Keep atomic metadata replacement, preserved recovery records and private-state separation. A header audit is not a disk-health assessment. Development tests do not access a real music library. |
| Packaging and privacy | Keep pinned engine hashes, explicit source-package allowlist, synthetic tests and prominent Made using AI disclosure. No updater or automatic publication added. |

## Dependencies considered

These decisions concern this app's current needs; they are not rankings of the upstream projects.

| Candidate | Evidence and decision |
| --- | --- |
| [Sockseek daemon/API](https://github.com/fiso64/sockseek/releases/) | Upstream provides persistent daemon/API capabilities. This is the best backend experiment to evaluate before adding a second Soulseek engine. Defer integration until authenticated loopback lifecycle, cancellation, restart recovery and stable track-to-job mapping are covered. Keep the pinned CLI in this revision. |
| [slskd](https://github.com/slskd/slskd) | A client/server Soulseek alternative. Switching introduces another service and configuration lifecycle; there is no measured throughput advantage for this application yet. Not bundled. |
| [Xiph FLAC test mode](https://www.xiph.org/flac/documentation_tools_flac.html) | Strong candidate for optional full-stream verification: test mode decodes without writing decoded audio and checks stream integrity and stored MD5 when available. Prefer evaluating this focused tool before a full multimedia suite. Not integrated in this revision: select a pinned distributable, preserve license/source obligations, add corrupt-frame/timeout/cancel tests, and make the additional disk workload opt-in first. It cannot prove lossless provenance. |
| [FFmpeg](https://www.ffmpeg.org/legal.html) | Broad media functionality comes with build-dependent licensing and distribution requirements. It does not address Soulseek peer availability. No conversion or playback requirement justifies adding it here now. |
| [TagLib#](https://github.com/mono/taglib-sharp) | General media metadata library. Worth evaluating if supported formats expand; current FLAC-only changes require preservation and undo behavior that a library substitution would still need to implement. No replacement without equivalent tests. |
| [CsvHelper](https://joshclose.github.io/CsvHelper/getting-started/) | Mature CSV mapping API, but a replacement is not justified without demonstrated unsupported exports or parser maintenance problems. Keep current tested behavior. |
| [TPL Dataflow](https://learn.microsoft.com/dotnet/standard/parallel-programming/dataflow-task-parallel-library) | Offers pipeline components; existing artwork concurrency is already bounded and tested. Introduce only if a larger session/pipeline refactor demonstrably simplifies cancellation and backpressure. No package added just to recreate existing behavior. |

## Validation and measured limits

The full offline suite passed after these changes, including exact reuse bytes, conflicting artist/code/duration rejection, changed/missing sources, refreshed lookup after registration, cancellation, audit exclusions and active-folder lock protection.

One local synthetic comparison used 2,000 catalog entries and 80 matching queries. Reconstructing descriptors for each full scan took 399.30 ms; indexed queries took 0.69 ms. Loading the JSON and constructing the index took 68.18 ms. Both strategies returned the same matches. This is a single-run local microbenchmark, not an internet download benchmark or an end-to-end speed claim. Timings are informational, not flaky test thresholds. The index adds memory proportional to the catalog size.

No new dependency was added. The strongest immediate wins came from removing repeated work and correcting scan scope while preserving existing behavior. Full audio decoding, coherent album-folder downloading, adaptive peer scheduling and library-change previews remain explicitly unimplemented.
