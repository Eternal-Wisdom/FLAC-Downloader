# Releasing

1. Run `scripts/Build.ps1`, `scripts/Test.ps1`, and `scripts/Package.ps1` on Windows.
2. Inspect the synthetic screenshot, check `build/test-results.xml`, `build/test-results.txt` and `build/tested-build.json`, and extract the portable ZIP into a new directory. Confirm it contains no state, credentials, music, local paths, update backups, or personal test reports.
3. Review and merge the tested pull request. Create a `v1.13.0` tag only when this candidate is approved for release.
4. Create a GitHub Release from the tag. Upload `dist/flac-downloader-1.13.0-windows-x64.zip`, `dist/SHA256SUMS.txt`, and `dist/flac-downloader-1.13.0-source.zip`. Include the notes below. GitHub also provides a source snapshot for the tag.
5. Download the uploaded artifacts and check their SHA256 hashes before announcing the release.

The CI workflow builds and tests pull requests and pushes. Publication is manual; a tag alone does not publish binaries. The portable release includes the upstream engine's corresponding source archive and license. Do not distribute a replacement engine without updating those files, checksums, notices, and tests.

## Release notes

Made using AI (OpenAI Codex). Version 1.13 separates tests from the portable app and records checkable JUnit evidence with hashes, suite counts and timings. It improves artwork provider cooldowns, Windows reserved-name handling, writable-state diagnostics and recovery guidance for passwords encrypted under another Windows account. See docs/REVIEW-1122.md for the accepted and deferred external-review recommendations.

The full offline suite passed with 29 suites, 0 failures and 0 skipped. The production app has no test classes; new fixtures cover 500 filename inputs, 300 CSV round trips and temporary provider failures. A synthetic 50,000-entry catalog benchmark measured loading and indexed queries. These measurements are not download-speed claims. No new live download testing was performed for this follow-up; earlier 1.12 live-test observations and unresolved Spotify/UI test limits remain documented in docs/VALIDATION.md. Hosted CI results are available on the release pull request.

Windows x64; unsigned executable. No live-peer speed improvement is claimed. Publication is a separate manual step; creating a local candidate does not authorize an automatic release.

Packaging checks the successful test receipt against the application and engine hashes. Rebuilding either binary requires another successful test run. Review the archive privacy audit and unresolved live-test limits before approving publication.