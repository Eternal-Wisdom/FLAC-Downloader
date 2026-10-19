# Open-source feature investigation

> Historical review: this records the version and date stated below. For current behavior and validation, see the [documentation index](README.md), [interface guide](UI-GUIDE.md) and [validation record](VALIDATION.md). Earlier deferred-feature and publication notes are historical, not current instructions.

Made using AI (OpenAI Codex). Reviewed 2026-10-13; local version 1.17.0. This is a focused review, not an exhaustive analysis of all open-source software. Features are independently implemented; upstream code and REA are not bundled.

REA 6.4.0 inspected exact copies of Motrix sort.ts and task-column-values.ts from commit 99a40ab3dd91133056ac2cc08e5bbd90ce3afb9e. Two TypeScript files parsed without failures; the focused structural graph reported complete coverage. Direct source review corroborated sortable columns, numeric values, stable ordering and unknown values. We implemented queue sorting by source position, title, artist, numeric duration and lifecycle status. Sorting changes displayed rows only, preserving the imported playlist and download sequence.

REA also inspected slskd TransfersHeader.jsx and TransferList.jsx from commit 71dcc7a60650b5a61d1d2a35609b65f3467d1396. Both JSX files parsed without failures; the focused structural graph reported complete coverage. Source review corroborated state-specific transfer organization and actions on eligible entries. We adapted the workflow to saved retries: text search, Scheduled/Paused/Unreadable filters, preserved hidden entries and actions restricted to selected visible editable rows.

Complete coverage applies only to these supplied source slices, not imported modules, running applications, network behavior or whole-program semantics. Raw evidence includes local paths and remains private.

| Pattern | Decision |
| --- | --- |
| Motrix task sorting | Implemented display sorting; selected-song actions already exist. |
| slskd state-specific controls | Adapted to retry review. Live per-file pause/resume needs engine support and transfer validation. |
| yt-dlp archives and paced retries | Existing recording index and retry backoff cover these needs. |
| aria2 segmented downloads | Not a compatible accelerator for Soulseek transport. |
| Broader metadata editing and folder templates | Existing artwork, naming, duplicate repair and recovery remain; expanded editing needs identity and recovery validation. |
| Plugins and remote control | Additional setup and credential surfaces are not justified for this portable workflow. |

Sources: https://github.com/agalwood/Motrix, https://github.com/slskd/slskd, https://github.com/yt-dlp/yt-dlp, https://github.com/aria2/aria2.

## 1.17.1 visible selection

REA rechecked the same two slskd transfer components. Their select-all workflow informed independently written visible-row selection, clearing and copying shortcuts. No upstream source or dependency is bundled. Regression tests also caught and now prevent non-clickable sorting headers. Structural analysis applies only to the supplied source slice, not runtime behavior.


## 1.17.2 reusable selection

Added CSV export for selected visible queue rows, extending the selected-entry workflow reviewed in slskd to portable tracklists. Existing CSV serialization is reused; no new runtime dependency or upstream code is included. Export is a local convenience feature, not a network performance improvement.

