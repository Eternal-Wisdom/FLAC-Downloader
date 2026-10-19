# Releasing

1. Run `scripts/Build.ps1`, `scripts/Test.ps1`, and `scripts/Package.ps1` on Windows.
2. Inspect the synthetic screenshot, check `build/test-results.xml`, `build/test-results.txt` and `build/tested-build.json`, and extract the portable ZIP into a new directory. Confirm it contains no state, credentials, music, local paths, update backups, or personal test reports.
3. Review and merge the tested pull request. Create a `vVERSION` tag only when the candidate is approved for release, replacing VERSION with the version in `scripts/Package.ps1` and the assembly metadata.
4. Create a GitHub Release from the tag. Upload `dist/flac-downloader-VERSION-windows-x64.zip`, `dist/SHA256SUMS.txt`, and `dist/flac-downloader-VERSION-source.zip` for that exact version. GitHub also provides a source snapshot for the tag. Put the Made using AI disclosure prominently in the release notes and document current validation limits.
5. Download the uploaded artifacts and check their SHA256 hashes before announcing the release.

The CI workflow builds and tests pull requests and pushes. Publication is manual; a tag alone does not publish binaries. The portable release includes the upstream engine's corresponding source archive and license. Do not distribute a replacement engine without updating those files, checksums, notices, and tests.

## Release notes for 1.18.0

**Made using AI (OpenAI Codex).** Version 1.18.0 adds public Spotify album import without a developer Client ID or sign-in, pasted songs/CSV, restart-safe cached imports, destination preflight checks, safe diagnostic preview/copy, plain-language failure explanations and a copyable Spotify redirect URI. Documentation covers the current interface, formats, troubleshooting, privacy and recovery. No new runtime dependency or replacement transport is introduced.

All **37 regression suites** passed with zero failures or skips. Fresh unauthenticated public imports returned **18, 9 and 36 tracks**, with Unicode, artist data, durations and CSV round trips checked. Native checks loaded a nine-track Japanese album without setup, reviewed safe diagnostics, rejected malformed pasted text, imported valid Unicode songs and restored their source identity after restart. The 1.17.6 Soulseek/decoder results remain historical evidence; no new Soulseek transfer test was performed for this revision. Authenticated Spotify Web API import remains a live-test gap without a Client ID. See [validation](VALIDATION.md).

Public embed page schemas and regional availability can change. The app rejects explicit partial data, identity conflicts, empty/prerelease lists and lists of 100 or more tracks, and asks users to review the count. Metadata completeness cannot always be proved from the public page; CSV is the fallback. Anonymous tokens and preview audio are not used. Destination checks do not diagnose hardware faults or estimate total batch size. Single-peer album/master consistency and network-speed improvements are not claimed.

Windows x64; unsigned executable. No live-peer speed improvement is claimed. Publication is a separate manual step; creating a local candidate does not authorize an automatic release.

Packaging checks the successful test receipt against the application and engine hashes. Rebuilding either binary requires another successful test run. Review the archive privacy audit and unresolved live-test limits before approving publication.

## Local 1.18.1 preparation

Album imports now perform a bounded album-folder discovery and request only missing, unambiguously matched FLAC tracks from one folder before individual fallback. All 38 suites and an additional offline end-to-end run passed, including existing-file preservation and no-download restart. This is not a new live Soulseek transfer benchmark.
