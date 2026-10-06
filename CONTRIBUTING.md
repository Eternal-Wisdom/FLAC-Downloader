# Contributing

Build and run the offline tests before submitting a pull request. Keep C# compatible with the .NET Framework compiler (language version 5). Use synthetic tracks and temporary folders in tests. Never commit account details, real settings, music, library indexes, personal logs, or private filesystem paths.

Folder changes must preserve recoverability, protect against directory links and overwrite conflicts, and keep playable playlist paths working. Test cancellation and interrupted jobs. Performance claims need reproducible measurements; report queued jobs separately from active transfers.

Describe the problem, changed behavior, and validation in your pull request. Contributions are licensed under the project's AGPL-3.0-only license.
