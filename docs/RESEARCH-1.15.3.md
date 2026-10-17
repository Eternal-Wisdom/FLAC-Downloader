# Downloader research and local decisions

> Historical review: this records the version and date stated below. For current 1.18.0 behavior and validation, see the [documentation index](README.md), [interface guide](UI-GUIDE.md) and [validation record](VALIDATION.md). Earlier deferred-feature and publication notes are historical, not current instructions.

Made using AI (OpenAI Codex). Reviewed October 11, 2026. Implemented in version 1.15.3.

This is a focused review of relevant established projects and official documentation, not an exhaustive survey of the internet.

| Source | Relevant idea and decision |
| --- | --- |
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | Download archives and paced retries are useful reliability patterns. Retain the existing completed-recording index and retry backoff. Fragment parallelism applies to segmented streams; it cannot be transferred directly to Soulseek files. |
| [aria2](https://github.com/aria2/aria2) | Segmented, multi-source transfers depend on its supported protocols and compatible resources. Soulseek is not supported. Adding aria2 would not accelerate the current music transport. |
| [JDownloader concurrency guidance](https://support.jdownloader.org/sv/knowledgebase/article/simultaneous-downloads-connections-chunks) | Higher connection counts do not necessarily increase speed. Keep existing track limits until peer-controlled measurements justify a change. Improve utilization of existing work slots instead. |
| [Sockseek](https://github.com/fiso64/sockseek/blob/master/README.md) | Candidate ranking and persistent-client capabilities are relevant. The app already uses its ranking through quality profiles. A backend replacement remains a separate project requiring authentication, cancellation, index ownership, and controlled transfer validation. |
| [slskd configuration](https://github.com/slskd/slskd/blob/master/docs/config.md) | Configurable transfers and separate incomplete/download directories are relevant. Preserve existing staging and completion handling. Introducing a separately administered service and web credentials would add setup and privacy costs. |
| [Nicotine+](https://github.com/nicotine-plus/nicotine-plus/blob/master/README.md) | A full Soulseek client is a useful reference, but replacing the compact playlist workflow with a complete client is not justified by this scheduling issue. |
| [Motrix](https://github.com/agalwood/Motrix) | A broader manager and extension ecosystem can suit general downloads. Another framework/plugin layer is not needed for the measured bottleneck in this portable app. |
| [Microsoft completion-order asynchronous processing](https://learn.microsoft.com/en-gb/dotnet/csharp/asynchronous-programming/start-multiple-async-tasks-and-process-them-as-they-complete) | Use the existing framework's Task.WhenAny to process ready cover results rather than awaiting the oldest request. Implemented with bounded concurrency, sequential writes, and cancellation/draining. No added dependency. |

The implemented change fixes head-of-line waiting in cover repair. Before, a slow oldest request kept finished later results waiting and delayed scheduling their replacements. Now completed results are processed and their slots replenished immediately. File ownership checks and recovery handling remain in place. CSV cover report rows follow completion order; the imported playlist and download order are unchanged.

See [validation](VALIDATION.md) for the controlled comparison and regression evidence. The result is faster cover processing under uneven lookup latency; it is not a promise of higher Soulseek peer throughput.
