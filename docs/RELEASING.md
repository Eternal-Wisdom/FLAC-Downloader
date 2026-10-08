# Releasing

1. Run `scripts/Build.ps1`, `scripts/Test.ps1`, and `scripts/Package.ps1` on Windows.
2. Inspect the synthetic screenshot, check `build/test-results.txt`, and extract the portable ZIP into a new directory. Confirm it contains no state, credentials, music, local paths, update backups, or personal test reports.
3. Review and merge the tested pull request. Create an annotated `v1.12.0` tag only when this candidate is approved for release.
4. Create a GitHub Release from the tag. Upload `dist/flac-downloader-1.12.0-windows-x64.zip`, `dist/SHA256SUMS.txt`, and `dist/flac-downloader-1.12.0-source.zip`. Include the notes below. GitHub also provides a source snapshot for the tag.
5. Download the uploaded artifacts and check their SHA256 hashes before announcing the release.

The CI workflow builds and tests pull requests and pushes. Publication is manual; a tag alone does not publish binaries. The portable release includes the upstream engine's corresponding source archive and license. Do not distribute a replacement engine without updating those files, checksums, notices, and tests.

## Release notes

Made using AI (OpenAI Codex). Version 1.12 fixes access-denied errors on hidden active playlists and stops fatal engine failures from triggering more searches. Retries search before optional name lookups, which now have a 30-second budget. The update adds track/source details and right-click file actions, improves saved-retry validation and local recording lookup, and keeps audits away from staging and recovery files. Soloist API keys receive a specific explanation and are not saved as Web API client IDs.

The offline suite passed, and bounded live tests downloaded four recordings including Japanese, observed concurrent transfers, verified restart skipping and retry scheduling, and checked full audio decoding and embedded checksums. Direct artwork retrieval and embedding also passed. These observations are not a guarantee of peer availability or sustained download speed. Authenticated Spotify Web API import remains unverified without a configured Web API app; Soloist keys cannot substitute for its client ID. Native click-through testing was limited by window activation failures. See docs/VALIDATION.md for details.

Windows x64; unsigned executable. No live-peer speed improvement is claimed. Publication is a separate manual step; creating a local candidate does not authorize an automatic release.

Packaging checks the successful test receipt against the application and engine hashes. Rebuilding either binary requires another successful test run. Review the archive privacy audit and unresolved live-test limits before approving publication.
