# Releasing

1. Run `scripts/Build.ps1`, `scripts/Test.ps1`, and `scripts/Package.ps1` on Windows.
2. Inspect the synthetic screenshot, check `build/test-results.txt`, and extract the portable ZIP into a new directory. Confirm it contains no state, credentials, music, local paths, update backups, or personal test reports.
3. Review and merge the tested pull request. Create an annotated `v1.12.0` tag only when this candidate is approved for release.
4. Create a GitHub Release from the tag. Upload `dist/flac-downloader-1.12.0-windows-x64.zip`, `dist/SHA256SUMS.txt`, and `dist/flac-downloader-1.12.0-source.zip`. Include the notes below. GitHub also provides a source snapshot for the tag.
5. Download the uploaded artifacts and check their SHA256 hashes before announcing the release.

The CI workflow builds and tests pull requests and pushes. Publication is manual; a tag alone does not publish binaries. The portable release includes the upstream engine's corresponding source archive and license. Do not distribute a replacement engine without updating those files, checksums, notices, and tests.

## Release notes

Made using AI (OpenAI Codex). Version 1.12 adds track/source details, stricter index validation, prompt engine shutdown after processing failures, and private-state isolation for synthetic previews. See CHANGELOG.md and docs/CODE-REVIEW.md for scope and validation.

Windows x64; unsigned executable. No live-peer speed improvement is claimed. Publication is a separate manual step; creating a local candidate does not authorize an automatic release.
