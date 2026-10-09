# Shared review follow-up (2026-10-09)

The [shared report](https://share.gemini.google/099QUnqK2m4f) explicitly says it could not inspect this repository. It is a general proposal, not evidence of defects in this app. Decisions below come from comparison with the current source. The Made using AI disclosure remains unchanged.

## Prepared locally

The GitHub workflow now pins checkout v7.0.1 and upload-artifact v7.0.2 to their verified upstream release commits. Both use Node 24. Checkout no longer persists a token in the working copy; this read-only build has no push step. The workflow keeps test evidence, limits jobs to 15 minutes, and cancels obsolete runs for the same event/ref while retaining separate push and pull-request checks.

Monthly Dependabot version checks group build-action updates and allow at most two open update pull requests. Updates require review and CI; no automatic merging or release publishing is configured. This becomes active only after these local files are explicitly published. It does not monitor the embedded engine, whose archive/executable hashes and source remain pinned separately. SHA pinning is not a promise that every dependency is vulnerability-free.

## Existing behavior retained

The app already separates import, engine orchestration, matching, retry history, artwork, and library maintenance into modules. It bounds concurrent jobs, saves retry schedules and completed-track history, uses Spotify PKCE with a loopback callback, protects remembered passwords using Windows DPAPI, embeds native FLAC picture blocks, and distributes a portable Windows app with checksums and engine source. Tests are separate from the production app. Private settings and music are excluded from release packages.

## Deferred or rejected

- HTTP range splitting and DASH processing do not accelerate the Soulseek transfer protocol. A new transport needs protocol support and identity-safe resume tests before inclusion.
- A database migration or GUI rewrite lacks measured justification. The existing 50,000-entry catalog benchmark and native Windows interface are the current baseline.
- Frequency cutoffs are not proof of lossy origin. Automatic deletion on spectral heuristics would risk valid recordings. ISRC matching also does not guarantee the same mastering or edition.
- Additional streaming sources, browser cookie harvesting, proxy rotation, lyrics, semantic AI search, headless servers, and mobile ports expand scope and dependencies without demonstrated benefit for this workflow. They are not added by this review.
- A mandatory 1,000-pixel cover threshold would reject useful artwork. Artificial enlargement does not add detail. Broader tag enrichment needs reliable album/disc metadata and reversible conflict handling before changing existing peer tags.
- A forced educational-use dialog is not a substitute for appropriate use or permission. Existing usage guidance remains.
- Mandatory telemetry is not added. Existing requests to metadata and peer services remain documented; zero telemetry does not mean zero network disclosure.

## Validation limits

Local YAML/schema checks and upstream action manifests validate configuration structure, revisions, inputs, and runtime requirements. New hosted CI and Dependabot behavior cannot be exercised without uploading these files; neither is claimed tested online. There is no application-binary change or new download-speed claim in this follow-up.

References: [checkout](https://github.com/actions/checkout), [upload-artifact](https://github.com/actions/upload-artifact), [Dependabot for Actions](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/auto-update-actions).
