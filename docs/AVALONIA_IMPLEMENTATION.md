# DinoSaurList Core + CLI + Avalonia conversion

Requested by Jay Baek on 2026-11-18. Target repository is **DinoSourList-AppStore-Official-Release**; **NPISpectreConverter** is the architectural/update reference, not the application being refactored. Source baseline `fd4eca5`; reference `9d24a95`. Server planning document: `NPIServer/docs/DINOSAURLIST_AVALONIA_MIGRATION_PLAN.md` (`06a08a5`).

This branch is a **parallel preview**, not an assertion of complete behavioral parity. Existing Python entry points, release assets and user installations remain usable while the native implementation is validated. Do not replace the production release channel based only on these tests.

## Structure

- `dotnet/core`: catalogue, Flow/version parsing, Box Manager transport, checksum verification, extraction/install lifecycle, platform launcher, Windows MSI/delete/shortcut integration, installed versions/default selection, legacy import, settings/favourites/news, profiles/photos, local activity, TF-IDF retrieval and Velopack update orchestration.
- `dotnet/cli`: JSON stdout / diagnostic stderr host for Core. `help` lists catalogue/readme/guide/recommend/installed/install/launch/pin/remove/favourites/import-preview/import/settings/news/profile/photo/activity/update. Mutating remove/import/update-apply require `--yes`; profile writes and installs are explicit commands.
- `dotnet/app`: Avalonia UI, MVVM presentation state, native file picker/dialogs and image rendering. Storage and installed-version operations use the same Core services as the CLI.
- `dotnet/tests`, `dotnet/ui-tests`: behavioral and headless rendered UI tests. Legacy contract fixture captures actual Python version/name results and Python configparser expectations over 28 real Flow documents.

Target .NET 10 LTS: .NET 9 reached end of support 2026-11-10 (https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). Avalonia 12.1.2, Toolkit 8.4.2, Velopack 1.2.158. The reference update lifecycle is retained with `SimpleWebSource`; GithubSource/PAT injection is absent.

## Build and run

```sh
cd dotnet
dotnet build DinoSaurList.sln -c Release
dotnet test --solution DinoSaurList.sln -c Release --no-progress
dotnet run --project cli -- help
dotnet run --project cli -- catalogue --refresh --data-dir /tmp/dinosaurlist-preview
dotnet run --project app
```

The repository root has the pinned `vpk` local tool manifest. `native-dotnet.yml` uses GitHub-hosted Linux/Windows/macOS runners: this public repository must not execute public PR code on the private NPI server. CI tests on each target OS, then publishes self-contained Win-x64 and macOS-arm64 app/CLI bundles and creates Velopack preview artifacts. Unsigned preview artifacts are not production installers. No production feed publication is automated.

Data defaults to per-user LocalApplicationData/DinoSaurList, outside the installation. Override with CLI `--data-dir` or UI `DINOSAURLIST_DATA_DIR`. Box defaults to the existing central Box Manager; its credential-free listing/download/upload API is shared by both hosts. Default feed URL is reserved `https://npi-service.kor.broadcom.net/dinosaurlist/`; it must contain validated preview releases before updater testing. It is not deployed by this branch.

## Compatibility and validation gates

| Behavior | Current evidence | Remaining gate before replacing legacy |
|---|---|---|
| Live catalogue, version/Flow metadata | 28 live tools, 108 packages with SHA1; 49 Python version comparisons, 4 installed folder contracts, 28 Flow contracts | Refresh-select/new-only modes and nested metadata traversal |
| Streaming downloads and ZIP extraction | Bad checksum/truncation, traversal and archive symlink rejection tests | Large real Windows package/network interruption workflow |
| Install/update/downgrade | Stable final working directory, old records retained on failed installer; MSI previous uninstall and Flow Delete implemented | Real MSI cancellation/reboot/rollback cases, protected Python env reuse; external installer side effects cannot be rolled back by JSON transaction |
| Windows shortcuts / execution | Platform selection and literal path arguments; COM shortcut integration | Real Windows .cmd/.exe/.msi/.lnk interaction |
| macOS | Native host builds and runs only tools that declare macOS support | All 28 current tools omit Platform and default to Windows; Mac-supported tool inventory and Intel build requirement need confirmation |
| Favourites, default version, removal, settings, news | Shared CLI/UI Core state tests; legacy installation/favourites import preserves original files | Import legacy rich HTML news/settings/local profile data; full rich Markdown/HTML rendering parity |
| Profiles/photos | Legacy-compatible Box JSON names and payload tests; shared upload transport | Real write test on dedicated profile; shared Box contribution calendar and Useful Links view |
| Assistant | Native TF-IDF retrieval smoke test, no fabricated tool responses | Old NLTK POS/WordNet/Porter preprocessing and conversational templates are not yet equivalent; compare a real query corpus before acceptance |
| Logs/activity | Bounded in-memory operation log, persistent local action history | Existing persistent log viewer/telemetry and shared contribution date parity |
| Updates / previous install transition | Velopack server-source lifecycle, parallel read-only legacy linking | Signed/native installer + feed check/download/restart on Windows and Mac; rollback release selection |

Legacy telemetry remains disabled: copying its hardcoded password or unrestricted SQL contract would not preserve a safe native architecture. Define an authenticated, limited replacement contract before enabling reporting. Existing legacy source credentials require separate owner rotation; this branch neither copies nor rotates them.

A package build, a unit test or a Linux rendered screenshot is evidence for that specific check, not evidence of a successful Windows/macOS installer/update workflow. Keep this matrix open until those workflows and remaining feature contracts are exercised.
