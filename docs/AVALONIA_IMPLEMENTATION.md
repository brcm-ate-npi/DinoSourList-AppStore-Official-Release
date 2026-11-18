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

The repository root has the pinned `vpk` local tool manifest. `native-dotnet.yml` uses GitHub-hosted Linux/Windows/macOS runners: this public repository must not execute public PR code on the private NPI server. CI tests on each target OS, then publishes self-contained Win-x64 and macOS-arm64 app/CLI bundles and creates Velopack preview artifacts. UI and CLI executables are co-located in the installed app root, sharing runtime files and the updater locator. Unsigned preview artifacts are not production installers. No production feed publication is automated.

The native Velopack package ID is `DinoSaurListNative` (display title DinoSaurList), so its Windows install root cannot collide with legacy `%LOCALAPPDATA%/DinoSaurList/dev_photos` or native data. Data defaults to per-user LocalApplicationData/DinoSaurList, outside that installation. Override with CLI `--data-dir` or UI `DINOSAURLIST_DATA_DIR`. Box defaults to the existing central Box Manager; its credential-free listing/download/upload API is shared by both hosts. Default feed URL is reserved `https://npi-service.kor.broadcom.net/dinosaurlist/`; it must contain validated preview releases before updater testing. It is not deployed by this branch.

## Compatibility and validation gates

| Behavior | Current evidence | Remaining gate before replacing legacy |
|---|---|---|
| Live catalogue, version/Flow metadata | 28 live tools, 108 packages with SHA1; 49 Python version comparisons, 4 installed folder contracts, 28 Flow contracts | Native Windows/macOS workflow verification of refresh modes (all/select/metadata/new-only are implemented with cache-preservation tests and depth-3 metadata traversal) |
| Streaming downloads and ZIP extraction | Bad checksum/truncation, traversal and archive symlink rejection tests | Large real Windows package/network interruption workflow |
| Install/update/downgrade | Stable final working directory, old records retained on failed installer; MSI previous uninstall and Flow Delete implemented | Real MSI cancellation/reboot/rollback cases, protected Python env reuse; external installer side effects cannot be rolled back by JSON transaction |
| Windows shortcuts / execution | Platform selection and literal path arguments; COM shortcut integration | Real Windows .cmd/.exe/.msi/.lnk interaction |
| macOS | Native host builds and runs only tools that declare macOS support | All 28 current tools omit Platform and default to Windows; Mac-supported tool inventory and Intel build requirement need confirmation |
| Favourites, default version, removal, settings, news | Shared CLI/UI Core state tests; legacy installation/favourites import preserves original files | Legacy news imports as inert text with original HTML backed up; refresh settings, pending local messages and manual activity are imported. Rich Markdown/HTML layout and external per-user photo/link cache migration remain to compare |
| Profiles/photos | Legacy-compatible Box JSON names and payload tests; shared upload transport | Real write test on dedicated profile; Dedicated profile write test; manual activity import and calendar visual comparison (native calendar now uses actual Box created/modified dates; Useful Links is bundled) |
| Assistant | Native TF-IDF retrieval smoke test, no fabricated tool responses | Pure .NET POS/WordNet/Porter inference matches all 8,846 tokens in 25 real READMEs; ranked README identities match 39/39 actual queries. Golden tests cover 208 public normalization sentences and 9 conversation messages. Longest direct tool name wins over a shorter prefix. Continue user acceptance with additional task queries. |
| Logs/activity | Bounded in-memory operation log, persistent local action history | Existing persistent log viewer/telemetry and calendar visual comparison; Box dates and imported manual events are supported |
| Updates / previous install transition | Velopack server-source lifecycle, parallel read-only legacy linking | Native CI now installs both setup artifacts and captures real UI/CLI evidence; inspect that run, then validate signed production feed check/download/restart and rollback release selection |

Legacy telemetry remains disabled: copying its hardcoded password or unrestricted SQL contract would not preserve a safe native architecture. Define an authenticated, limited replacement contract before enabling reporting. Existing legacy source credentials require separate owner rotation; this branch neither copies nor rotates them.

A package build, a unit test or a Linux rendered screenshot is evidence for that specific check, not evidence of a successful Windows/macOS installer/update workflow. Keep this matrix open until those workflows and remaining feature contracts are exercised.

## Preview evidence

`7527f82` passed the GitHub-hosted Linux/Windows/macOS pipeline and produced Windows-x64/macOS-arm64 Velopack preview artifacts: https://github.com/brcm-ate-npi/DinoSourList-AppStore-Official-Release/actions/runs/44240017924. This validates compilation, 30 tests on Linux/macOS (Windows skips Unix symlink creation) and packaging. It does not validate interactive installer/update behavior. The next revision adds refresh-mode cache tests (32 total local cases).

`fe23598` also passed native setup installation, opening the installed Avalonia app, screenshot capture and execution of the installed self-contained CLI on Windows and macOS: https://github.com/brcm-ate-npi/DinoSourList-AppStore-Official-Release/actions/runs/44241285468 . Manual screenshot review then found startup category selection could hide the loaded catalogue; the next revision strengthens this gate to require visible tools and the actual version picker selection, and tests initialization after window creation. Later revisions also preserve pending cloud profile edits/photos and serialize CLI/UI state mutations.

The native assistant now runs entirely in C#: lazy-loaded public NLTK English model (1.9 MiB gzip), WordNet morphology and NLTK Porter extensions, linear TF/smoothed IDF/cosine/0.20 threshold, intent handling and deterministic README response templates. Python is only a model-export/baseline tool. Public model provenance/hash and redistribution notices are in `dotnet/core/Assistant/{Data,Licenses}`; no application README documents are embedded. Tokens longer than 1024 characters are excluded as malformed input. Actual comparison evidence is kept privately on the NPI server under `.scratch/dinosaurlist-ai-baseline`; the public fixtures contain synthetic text/public vocabulary only.
