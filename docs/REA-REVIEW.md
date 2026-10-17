# REA evaluation

> Historical review: this records the version and date stated below. For current 1.18.0 behavior and validation, see the [documentation index](README.md), [interface guide](UI-GUIDE.md) and [validation record](VALIDATION.md). Earlier deferred-feature and publication notes are historical, not current instructions.

Made using AI (OpenAI Codex).

REA 6.4.0 was used locally to inspect the compiled 1.16.0 executable without launching it. Complete metadata inspection confirmed an x64 CIL assembly and nine framework assembly references. Native declaration inspection found one Unicode WinAPI import: user32.dll SendMessage, used for text-field hints. These results did not identify a production bug or demonstrate a download speed improvement.

An optional developer check is available in scripts/Inspect-WithRea.ps1. Provide the path to an independently installed REA scripts/rea.mjs CLI. It checks assembly identity, architecture, the framework dependency allowlist, and the known native declaration; unexpected changes require review. REA and its Node dependencies are not bundled with the downloader and are not required for ordinary builds or use.

Whole-assembly method inspection returned unavailable coverage because REA exceeded its 64 MiB representation budget. The installed npm 6.4.0 command did not accept the --type option documented on upstream main at evaluation time. Neither result establishes an absence of defects. Native declarations do not establish export availability or actual runtime behavior. Continue normal automated and live workflow testing.

Raw REA evidence includes absolute input paths and stays in private local work storage. The optional script writes a small summary without those paths into ignored build storage. This check is not a privacy certification or an automatic security audit.

Upstream: https://github.com/morluto/rea and docs/managed-code-analysis.md.

Current 1.18.0 inspection also allows the four reviewed kernel32 job/handle declarations used by engine crash containment. The original one-import result above applies only to 1.16.0.
