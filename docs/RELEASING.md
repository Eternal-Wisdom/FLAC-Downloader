# Releasing

1. Run `scripts/Build.ps1`, `scripts/Test.ps1`, and `scripts/Package.ps1` on Windows.
2. Inspect the synthetic screenshot, check `build/test-results.txt`, and extract the portable ZIP into a new directory. Confirm it contains no state, credentials, music, local paths, update backups, or personal test reports.
3. Create a GitHub repository from this project. Enable private vulnerability reporting if desired. Push the source and create an annotated `v1.11.0` tag after reviewing the changes.
4. Create a GitHub Release from the tag. Upload `dist/playlist-flac-1.11.0-windows-x64.zip`, `dist/SHA256SUMS.txt`, and `dist/playlist-flac-1.11.0-source.zip`. Include the notes below. GitHub also provides a source snapshot for the tag.
5. Download the uploaded artifacts and check their SHA256 hashes before announcing the release.

The CI workflow builds and tests pull requests and pushes. Publication is manual; a tag alone does not publish binaries. The portable release includes the upstream engine's corresponding source archive and license. Do not distribute a replacement engine without updating those files, checksums, notices, and tests.

## Release notes

First public release of Playlist FLAC: a portable Windows app for Spotify/CSV metadata import, FLAC search via Soulseek, conservative recording deduplication, song-title filenames, cover lookup, and scheduled retries while the app is open.

Version 1.11 adds clean release packaging and grouped playlist metadata while preserving old library layouts and recovery data. Includes the redesigned interface from 1.10. Windows x64; unsigned executable. Peer availability determines download success and speed. No account credentials are included. Full offline regression checks pass; real peer transfer speeds are variable and are not a release guarantee.
