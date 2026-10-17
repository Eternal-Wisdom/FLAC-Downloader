# Security

Do not put passwords, tokens, private music metadata, or unredacted logs in public issues. For a sensitive report, use this repository's private vulnerability reporting feature if the maintainer has enabled it. Otherwise contact the maintainer privately before sharing details.

For ordinary bug reports, **Library tools > Preview safe diagnostics** builds an allowlisted report of versions, normalized settings and counts. Review it before copying. It does not export raw logs, account information, song names, local paths, peer names or IP addresses, and nothing is uploaded automatically.

The app is currently unsigned and has no automatic updater. Download releases only from the maintainer's repository and compare SHA256SUMS.txt. Keep the portable `state/` directory private; remembered credentials are tied to the Windows account.

Public album import reads metadata from Spotify's public embed page without a developer account. It does not use the page's anonymous tokens or audio-preview URLs. Responses are bounded, album identities are checked, and incomplete or unverifiable lists are rejected. The public schema may change; CSV remains the fallback. Authenticated playlist/album API imports are optional and keep tokens in memory.

Temporary Soulseek sign-in files have restricted access and are removed after use. Abandoned-run cleanup uses the session lease and skips links. Best-effort Windows job containment reduces orphaned engines after a crash, but a launch/assignment gap and Windows assignment failures remain possible. These controls do not securely erase previously deleted files or diagnose a failing drive.
