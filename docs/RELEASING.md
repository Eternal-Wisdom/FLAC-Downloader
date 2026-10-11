# Releasing

1. Run `scripts/Build.ps1`, `scripts/Test.ps1`, and `scripts/Package.ps1` on Windows.
2. Inspect the synthetic screenshot, check `build/test-results.xml`, `build/test-results.txt` and `build/tested-build.json`, and extract the portable ZIP into a new directory. Confirm it contains no state, credentials, music, local paths, update backups, or personal test reports.
3. Review and merge the tested pull request. Create a `vVERSION` tag only when the candidate is approved for release, replacing VERSION with the version in `scripts/Package.ps1` and the assembly metadata.
4. Create a GitHub Release from the tag. Upload `dist/flac-downloader-VERSION-windows-x64.zip`, `dist/SHA256SUMS.txt`, and `dist/flac-downloader-VERSION-source.zip` for that exact version. GitHub also provides a source snapshot for the tag. Put the Made using AI disclosure prominently in the release notes and document current validation limits.
5. Download the uploaded artifacts and check their SHA256 hashes before announcing the release.

The CI workflow builds and tests pull requests and pushes. Publication is manual; a tag alone does not publish binaries. The portable release includes the upstream engine's corresponding source archive and license. Do not distribute a replacement engine without updating those files, checksums, notices, and tests.

## Release notes

Made using AI (OpenAI Codex). Version 1.15.3 improves cover scheduling under uneven request latency, reduces queue allocations and unnecessary progress scans, and clears stale transfer status. It includes the previously local 1.15.1 and 1.15.2 fixes. No additional runtime dependency or replacement transport is introduced.

The offline suite passed with 33 suites, zero failures, and zero skips. All 21 fresh live CSV/Soulseek assertions passed, including English/Japanese downloads, duplicate suppression, completed restart, unavailable-track export and paced retries, cancellation, and direct/fallback artwork retrieval. Native checks covered queue search, album availability, Explorer selection, repair preview/application, quick/full checks, and live Activity follow/pause. Downloaded audio passed the official decoder. Spotify API import was excluded by request; the native deletion-confirmation path and a live comparison of every concurrency/profile combination were not exercised. See `docs/VALIDATION.md` for evidence and limits.

Windows x64; unsigned executable. No live-peer speed improvement is claimed. Publication is a separate manual step; creating a local candidate does not authorize an automatic release.

Packaging checks the successful test receipt against the application and engine hashes. Rebuilding either binary requires another successful test run. Review the archive privacy audit and unresolved live-test limits before approving publication.
