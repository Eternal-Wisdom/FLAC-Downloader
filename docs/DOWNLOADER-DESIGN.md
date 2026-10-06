# Downloader design decisions

This project borrows useful ideas from established downloaders while keeping a focused Soulseek workflow. This is a design review, not a benchmark or a ranking of popularity.

| Reference | Useful idea | Application here |
| --- | --- | --- |
| [aria2 manual](https://github.com/aria2/aria2/blob/master/doc/manual-src/en/aria2c.rst) | Bounded concurrency, resumable transfers, integrity checks | Keep configurable job limits and preserve interrupted-job data. The engine handles Soulseek transfers; aria2 does not support Soulseek, so adding it would not accelerate these transfers. |
| [Nicotine+ release notes](https://nicotine-plus.org/NEWS.html) | Paced wishlist searches and usable transfer feedback | Keep unavailable tracks in saved retries with increasing intervals, and show observed transfer speed separately from searching and queued jobs. |
| [JDownloader shortcuts](https://support.jdownloader.org/en/knowledgebase/article/hotkeys-shortcuts) | Convenient access to frequent actions | Keep CSV drag-and-drop, Enter to load a link, selected-song downloads, and visible stop/retry controls. Add clear account setup help rather than more setup steps. |
| [JDownloader reconnect explanation](https://support.jdownloader.org/en/knowledgebase/article/why-should-i-set-up-my-reconnect) | Explain why a download is waiting | Report unavailable sources and saved retry times. Router reconnect/IP rotation is designed around hoster restrictions and is not a useful Soulseek feature here. |

## Criteria for future changes

Accept a change when it fixes a reproducible problem, removes a common manual step, or improves measured performance without worsening matching accuracy or reliability. Test with synthetic fixtures first; network benchmarks require consenting test sources and repeatable conditions.

Promising next experiments are learning from observed peer throughput and adapting concurrent transfers to congestion. Neither should be advertised as implemented until measurements show an improvement. Avoid unlimited connections, repeated searches every second, speculative speed multipliers, or conversion of lossy audio into files labeled lossless.

The public release includes account setup instructions, saved retries, bounded parallel work, source preference profiles, recoverable library changes, and a clean split between music and private app state. These existing controls cover the most relevant ideas without bundling an unrelated downloader.
