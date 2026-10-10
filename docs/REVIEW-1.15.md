# Downloader research: implementation decisions

Made using AI (OpenAI Codex). Local candidate; publication requires a separate request.

## Implemented

| Recommendation | Result |
| --- | --- |
| Search and queue filters | Title/artist/album search, status filters, keyboard shortcuts, visible count. Hidden rows remain in the underlying progress model and exports. Status-driven filtering is coalesced by the existing UI timer. |
| Clearer transfers and recovery | Selected-source details refresh while a run is active, including observed speed, elapsed time, latest engine state and time since bytes were reported. Existing selected-song retry and source policy details remain. No queue position or missing candidate ranking is invented. |
| Repair preview | Manual Fix library previews proposed names and duplicate archival using the same planning logic as the mutating operations. It does not write files. Existing recovery copies and automatic failure rollback remain; full transaction-wide undo is not implemented. |
| Deep verification | Optional official FLAC test-mode adapter, sequential and cancellable with a per-file timeout. Separate quick/full reports. Tested against real generated and damaged audio. No extra runtime dependency for normal use. |
| Album visibility | Imported-song availability grouped by artist/source album, with disc/track positions retained. Original playlist order is unchanged. No claim of a complete published album or a uniform downloaded master. |
| Reading live logs | Follow/pause control that preserves the viewed text while the app keeps collecting bounded recent output. |

## Deliberately deferred

- **Persistent engine, manual candidate switching, and coherent album-folder downloads:** the pinned Sockseek 3.0.5 source's `docs/api.md` explicitly labels its daemon API experimental and insufficiently tested. Its local help and OpenAPI expose useful candidate/album controls, but adopting them would change cancellation, index ownership, authentication, and recovery semantics. They need an isolated adapter and controlled peer comparisons before replacing the current workflow. This update does not claim that prototype or a speed improvement.
- **Adaptive concurrency:** existing profiles and limits remain. More outstanding jobs are not evidence of more simultaneous bytes or shorter completion time. No controlled live comparison justifies a change here.
- **Incremental audit caching / database migration:** quick checks read a small STREAMINFO block; reusing a cache solely by length and timestamp can mask changes. Full checks intentionally read current bytes. No measured bottleneck justifies introducing another persistent index in this update.
- **Automatic edition decisions and audio fingerprints:** a source album label or matching title does not prove identical masters. Keep ambiguous versions separate instead of merging them automatically.
- **Whole-app theme rewrite, remote control, extra services and scheduling:** keyboard navigation improvements are included, but broad new features would need separate design and test work. Existing paced retries are retained.

The optional verifier follows [Xiph's FLAC test-mode documentation](https://xiph.org/flac/documentation_tools_flac.html). The integration was checked with [FLAC 1.5.0](https://github.com/xiph/flac/releases/tag/1.5.0); that does not imply redistributing the decoder or its source in this app.
