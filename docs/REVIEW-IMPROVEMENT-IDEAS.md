# Improvement-ideas review for 1.18.0

Made using AI (OpenAI Codex). The supplied proposal described 1.13.0 without reading its source. This review compares its ideas with the actual 1.17.6 implementation. No new runtime dependency or transport replacement is needed for this update.

| Idea | Decision and implementation |
| --- | --- |
| Full FLAC decoding | Already present: external official decoder, drained output, cancellation, timeout and corruption tests. Keep the distinction from header inspection and source-provenance claims. |
| Public Spotify albums without developer setup | Added public embed-metadata import for disconnected users, with host/identity validation, bounded response size/time, Unicode, durations and artwork. No preview audio or anonymous token is consumed. Preserve the original source identity across restart. Public page schema is not a stable API; reject incomplete or unverifiable lists and offer CSV. |
| Album-aware single-peer sourcing | Deferred: existing orchestration maps each track and maintains per-recording recovery. A genuine album-folder job needs explicit edition/track identity and transactional integration; adding a flag would not establish matching masters. |
| Failure reasons | Existing engine reason codes now receive plain-language explanations in song details and saved retries. Distinguish no results, no matching results, exhausted retries and failed transfers. Do not fabricate individual filter decisions or blame a particular peer. |
| Spotify setup wizard | Existing setup and validation retained; add a Copy URI button. Public albums now bypass setup. Playlist API import remains optional and requires a valid developer app. |
| One shared library plan for preview/application | Existing read-only preview remains, with revalidation on application. A transaction-plan rewrite is deferred: candidate changes and disk state require conflict checks and undo boundaries. Do not claim the current preview is an immutable commit plan. |
| Peer memory | Deferred until failed-file evidence can be linked reliably to a source; byte telemetry alone cannot prove a wrong edition or bad peer. Avoid persistent peer identifiers without a clear need. |
| Per-track matching/relaxed retries/manual candidates | Deferred pending stable candidate IDs, explanations and per-track engine control. Never silently loosen matching. |
| Cached search-only preview | Deferred: the current CLI adapter cannot inject cached candidates into the actual transfer, so a separate search would still consume the search budget. |
| Cheap truncation heuristic/spectrogram | Retain actual decoding. FLAC compression ratios and silence make size-only judgments unreliable; a viewer is a separate exploratory feature with no correctness verdict. |
| Atomic indexes | Already present. Do not add rotating full-library copies without measured need or automatically remove recovery data. |
| Tag completeness/AcoustID | Existing artwork/header/identity tools retained. Broad tag reports and optional audio-fingerprint submission need separate metadata coverage, consent and API design. |
| Playlist sync | Reimport already reuses completed recordings. Added restart-safe cached imports; no automatic removal of songs. A remote sync workflow needs stable playlist identity and change-preview semantics. |
| More inputs | Added pasted Artist - Title and pasted CSV. Defer additional platform integrations, discographies, folder scans and release-ID import until each source has validated identity and pagination. |
| Queue controls/filter/sort/export | Sorting, filters, selection and missing-track CSV export already exist. Per-transfer pause/priority needs engine support; Stop remains global cancellation with completed-file preservation. |
| Notifications/tray/PerMonitorV2 | Deferred. Background/tray lifecycle should not obscure active transfers; multi-monitor DPI changes require actual mixed-DPI verification before enabling a manifest switch. |
| Bandwidth cap | Deferred until the pinned adapter exposes and verifies the transport control. More concurrent jobs do not imply a speed limit. |
| Accessibility/localization | New controls have explicit accessible names and keyboard-friendly dialogs. Full string-resource localization is separate work. |
| Headless CLI | Deferred pending protected credential input, explicit-start semantics, stable exit codes and controller separation. Do not add passwords to command-line arguments. |
| Soulseek etiquette | Added sharing guidance to onboarding and troubleshooting, without configuring private shared folders automatically. |
| Benchmarks/adaptive concurrency | Existing measured UI benchmark retained. No adaptive network tuning without repeatable peer-dependent throughput evidence across profiles and job counts. |
| Partial transfer resume | Completed-file recovery already works. Durable partial resume needs stable staging/peer identities; old partial files are preserved rather than misidentified or discarded. |
| Destination preflight | Added free-space reserve/readiness checks, path/link checks, cloud/network warnings and a disposable write probe before the engine starts. No speculative whole-album size estimate. |
| Logged-in-elsewhere | Existing single-account guidance retained; never turn an authentication failure into aggressive retries. Automatic reconnection policy needs an explicit authenticated engine event. |
| Incremental index storage/SQLite | Deferred until profiling proves that current atomic index writes are the bottleneck. Avoid a database dependency without a measured benefit. |
| Scoop/winget/provenance/signing | Deferred to an explicitly authorized distribution/security-permission change. A local implementation request does not authorize package-registry submissions or expanding workflow access. |
| Notify-only updates/antivirus submissions | Deferred: opt-in network/lifecycle design and externally submitted binaries are distinct tasks. No unsolicited samples or telemetry are sent. |
| Controller/main-form split | New import/diagnostic tools live in a separate partial form file. A full controller extraction requires lifecycle acceptance tests rather than a speculative rewrite. |
| Sanitized diagnostics | Implemented as an allowlist, not raw-log redaction. Preview and explicit Copy; no automatic transmission. Version, engine presence, normalized policy and recording counts only. |
| CI/style hygiene | Added a conservative .editorconfig and diagnostic guidance in bug templates. No new analyzer/dependency or mass formatting that obscures functional changes. |
| .NET migration | Keep the existing Windows Framework build and portable deployment. |
| Documentation | Added troubleshooting and CSV/pasted-import guides. A GIF is deferred until a stable, synthetic-only demonstration is useful. |
