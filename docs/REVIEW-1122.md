# External review decisions (2026-10-09)

The supplied review identifies useful validation and robustness work. Its example replacement is a console scaffold, not a replacement downloader: it omits playlist import, transfer orchestration, retries, library history and the desktop interface. Version 1.13 implements the applicable improvements below.

## Implemented

- **Separate production and tests.** The production build compiles only `src`. A separate test executable references the actual built app through a named friend assembly; tests no longer ship in the portable executable. No SDK migration or test-framework dependency is needed. The offline suite verifies that production contains no test types.
- **Checkable results.** `test-results.xml` is JUnit XML with one testcase per test suite, suite counts (not inflated assertion counts), failures, skipped count, UTC timestamp, per-suite duration, app/engine/runner SHA256, revision and working-tree status. A modified tree is explicitly marked: the revision alone does not identify uncommitted changes. Console output is retained separately. Packaging rejects changed reports and binaries. CI retains results even when tests fail.
- **Input properties.** Reproducible randomized tests exercise 500 filename inputs and 300 quoted CSV round trips, plus malformed CSV. Reserved superscript COM/LPT device names and control characters are sanitized. Existing path containment, collision handling, Unicode and surrogate-aware truncation remain in place.
- **Portable state errors.** Startup checks the ability to create/write/delete a unique state probe and provides extraction/writable-folder guidance before opening the normal UI. Preview mode does not touch state. Unreadable DPAPI passwords produce re-entry guidance and retain the old encrypted value until a replacement is entered or remembering is disabled.
- **Provider cooldowns.** Artwork requests respect Retry-After delta/date headers and exponentially extend repeated throttling cooldowns. Waiting workers recheck the service gate before issuing a request. Cooldowns do not sleep the UI or turn temporary failures into permanent cached misses. Backoff is bounded (six hours; valid server delays up to 24 hours). MusicBrainz's existing process-wide pacing remains at least 1.1 seconds between requests. The User-Agent now identifies this project and its public URL.
- **Realistic catalog scale.** A 50,000-entry synthetic benchmark measures JSON loading/index construction and 1,000 indexed lookups. It uses no audio, accounts or network. Measurements are observations, not test speed thresholds.

## Already covered; retain

- Credentials are not passed in the engine command line. The backend requires a temporary plaintext configuration, protected with a restricted ACL and removed after the session. Moving secrets into environment variables would not make them inaccessible to same-user processes; an encrypted file the engine cannot read is not a usable replacement.
- Artwork has a 10 MiB byte limit, dimension/header checks before image decoding, a 16 Mi-pixel cap, bounded FLAC metadata, format validation and malformed/oversized fixtures. Network and cache limits apply too. The suggested ImageSharp sample checks dimensions after loading; replacing the current checks would add a dependency without improving this protection.
- Filenames already reject Windows punctuation, trim trailing dots/spaces, shorten names without splitting surrogate pairs and avoid overwrites. Long total paths are checked and fail safely; trimming a basename to 200 characters alone would not guarantee a valid total path.
- Release checksums, prominent AI disclosure, wrapper AGPL-3.0-only license, engine source/license and third-party notices are already supplied. No private state or music belongs in packages or public test evidence.

## Deferred or rejected

- **FFT 'true lossless' label: rejected.** Band-limited music, quiet passages, mastering choices and stereo cancellation can produce the proposed spectral ratios. Absence of a cutoff also cannot establish source provenance. Its final version labels many unproven recordings authentic. It must not auto-delete music or advertise verified losslessness.
- **Full decoding / `flac -t`: deferred.** A properly bounded official decoder integration could detect corrupt frames and embedded MD5 mismatches, but cannot establish source quality. The supplied process wrapper redirects output without draining it and has no timeout/cancellation: copying it could hang. Current Check files still checks headers, explicitly not every frame. A decoder would require pinned distribution, notices, timeout/cancellation and valid/corrupt real-audio fixtures before inclusion.
- **AcoustID/Chromaprint: deferred.** Optional identity assistance needs supported credentials, privacy consent and evaluation on edits/live/remastered recordings. Fingerprints are not blanket proof of edition identity. Existing metadata/ISRC/duration conflict safeguards remain conservative.
- **SQLite or lazy catalog rebuild: deferred.** At 50,000 synthetic rows this machine loaded/indexed the catalog in about 2.18 seconds and performed 1,000 indexed lookups in 4.42 ms. This happens when the download workflow uses the catalog, not at ordinary app startup. No evidence yet justifies migration/recovery complexity; very large real libraries may warrant a separate benchmark.
- **Dry-run search / additional filters: deferred.** Existing selected-song download, source details and quality profiles cover the immediate workflow. A preview that still searches Soulseek would consume search allowance and needs separate state handling; do not present it as a free operation.
- **Code signing: deferred.** A trusted signing identity/certificate and protected signing workflow are not configured. Keep the unsigned status explicit; never claim checksums remove SmartScreen warnings.
- **Configurable artwork concurrency: deferred.** Existing four-worker limit and provider-specific pacing/cooldowns are tested. More controls need measured benefit; MusicBrainz must remain paced even with multiple workers.

## References and measurement limits

- [MusicBrainz API rate limiting](https://musicbrainz.org/doc/MusicBrainz_API/Rate_Limiting): concurrency does not override the one-request-per-second policy.
- [Windows filename rules](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file): device names include superscript COM/LPT variants.
- [Official FLAC tool](https://www.xiph.org/flac/documentation_tools_flac.html): decoding/test detects stream errors and checks embedded audio MD5 where present, not original recording provenance.

The earlier artwork timing uses eight independent 200 ms mock responses. It demonstrates bounded scheduling and equal output, not a MusicBrainz speed multiplier. Direct image hosts and cached results have different limits. No new online speed claim is made.
