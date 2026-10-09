# Proposed functions review

Reviewed the five-page proposed-changes report against the local source on 2026-10-09. The report explicitly reviewed documentation rather than source. Its proposals are useful leads, not verified defects. Personal source material is not included in the release.

## Accepted in local 1.14

- Item 15: a queue context-menu action exports confirmed missing/failed recordings using the existing CSV writer. Reimports preserve Unicode, quoted titles, artist credits and duration. Repeated album editions are exported once; an existing downloaded counterpart suppresses export. Ready and Review versions rows are excluded. No accounts, peer identifiers, local paths or activity logs are exported. The file still contains the user's music list and should be treated as private.
- Item 11, scoped to details: when idle, opening details for a completed song reads its native FLAC header in the background. It reports bit depth, sample rate and channels, explicitly distinguishing header facts from decoded integrity and provenance. It uses the same contained, unambiguous file resolver as Show in folder. Invalid or missing files do not get a quality label. Reading every queue row on import would add disk work and has not been added.
- Item 1, documentation portion: the release checklist below makes remaining manual and authenticated test gaps explicit. Adding an automation library alone would not close those gaps.

Acceptance tests cover failed-only selection, duplicate album editions, same-title songs from different artists, downloaded counterpart suppression, Japanese and CSV quoting round trips, valid 24/96 headers and invalid/missing files. Existing offline suites continue to cover file resolution and link rejection.

## Decisions on remaining items

| Items | Decision and evidence needed |
| --- | --- |
| 2: full decode | Still useful but deferred. Requires a pinned decoder with license notices, cancellation, timeout, corrupt-frame fixtures and deployment checks. Current header checks must retain their explicit limitation. |
| 3: session controller | Defer to an isolated refactor with progress/cancellation/restart acceptance tests. Export and selected-file inspection do not require changing the download lifecycle. |
| 4: safe diagnostics | Prefer a strict allowlist of versions, counts and fixed error categories over trying to redact arbitrary logs. Implement only with adversarial privacy fixtures and a preview; no log-export shortcut added. |
| 5: signing | Requires an external signing identity and eligibility review. No certificate or signing service is available in this change. |
| 6: stale credentials | The current engine removes its private config in a finally block; a hard process kill bypasses it. A startup sweep needs a cross-process ownership lease so it cannot remove another live session's config, plus a synthetic hard-kill test. This remains a security follow-up, not claimed fixed. |
| 7: accessibility | Status already has text and details support Enter. Broader keyboard/high-contrast work needs native accessibility inspection and visual tests rather than assuming colors are the only status signal. |
| 8: rejection explanations | The current adapter does not expose complete rejected-candidate evidence. Do not invent a wrong-duration/version explanation or silently relax matching; first prove engine evidence and per-track override persistence. |
| 9: manual candidate choice | Deferred until candidate IDs, metadata and exact job mapping can be validated. |
| 10: queue states | Existing text statuses and observed-byte telemetry are retained. Peer queue positions must stay unknown without engine evidence. Per-track selection already permits explicit downloading. |
| 12: refresh preview | Useful future scope; requires a shared read-only identity plan and tests for ambiguous editions. Removed entries must never delete music. |
| 13: coherent albums | Deferred pending disc/track/edition mapping and explicit fallback behavior. |
| 14: more import sources | CSV already works without a Spotify account. M3U can lack artist/title metadata; new sources need their own concrete fixtures and a demonstrated need. |
| 16: update check | Deferred until a version comparison, supported-release policy and privacy-conscious request flow are defined. Current local installation permission is separate from an app auto-updater. |
| 17: daemon | Benchmark authentication/startup cost and verify cancellation/restart/job mapping first. Keep the pinned CLI. |
| 18: notifications/rate control/adaptive parallelism | Defer until lifecycle and engine capabilities are established. No automatic downloads merely on opening the app; no unmeasured concurrency increase. |

The report's exclusions are retained: no lossless-authenticity claims from FFTs, lossy conversion presented as an upgrade, mixed-peer chunk merging, automatic music/recovery deletion, or speculative rewrites and dependencies.

## Manual release checklist and test limits

- Import a synthetic Unicode CSV, export its failed subset, reimport it and verify selection and artist credits.
- With synthetic downloaded files, open details with Enter and the context menu; close the dialog before its background read completes. Confirm invalid and unavailable files remain labelled honestly.
- Exercise Show in folder and confirm/cancel Recycle Bin using disposable files, including an ambiguous mapping. Never use private library files as fixtures.
- Test authenticated Spotify playlist and album import with a configured Web API Client ID; a Soloist key is not a substitute.
- Verify MusicBrainz/Cover Art Archive fallback with provider-compliant requests and synthetic tracks, including Retry-After behavior.
- Review package contents for private settings, credentials, paths and personal source materials; preserve the Made using AI disclosure.

Offline tests do not replace live workflow validation. The 1.14 follow-up exercised native CSV export/reimport and header details, live CSV/Soulseek downloads, and verified MusicBrainz/Cover Art Archive fallback. Authenticated Spotify import and native Explorer/Recycle Bin actions remain untested in this follow-up. See VALIDATION.md for results and limits. No private library was used.
