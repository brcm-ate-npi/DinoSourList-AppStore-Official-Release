# DinoSaurList Core + CLI + Avalonia conversion

Requested by Jay Baek on 2026-11-18. Target repository is **DinoSourList-AppStore-Official-Release**; **NPISpectreConverter** is the architectural/update reference, not the application being refactored. Source baseline `fd4eca5`; reference `9d24a95`. Server planning document: `NPIServer/docs/DINOSAURLIST_AVALONIA_MIGRATION_PLAN.md` (`06a08a5`).

This branch is a **parallel preview**, not an assertion of complete behavioral parity. Existing Python entry points, release assets and user installations remain usable while the native implementation is validated. Do not replace the production release channel based only on these tests.

## Structure

- `dotnet/core`: catalogue, Flow/version parsing, Box Manager transport, checksum verification, extraction/install lifecycle, platform launcher, Windows MSI/delete/shortcut integration, installed versions/default selection, legacy import, settings/favourites/news, profiles/photos, local activity, TF-IDF retrieval and Velopack update orchestration.
- `dotnet/cli`: JSON stdout / diagnostic stderr host for Core. `help` lists catalogue/readme/guide/recommend/installed/install/launch/pin/remove/favourites/import-preview/import/settings/news/profile/photo/activity/update. Mutating remove/import/update-apply require `--yes`; profile writes and installs are explicit commands.
- `dotnet/app`: Avalonia UI, MVVM presentation state, native file picker/dialogs and image rendering. Storage and installed-version operations use the same Core services as the CLI.
- `dotnet/tests`, `dotnet/ui-tests`: behavioral and headless rendered UI tests. Legacy contract fixture captures actual Python version/name results and Python configparser expectations over 28 real Flow documents.

Target .NET 10 LTS: .NET 9 reached end of support 2026-11-10 (https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). Avalonia 12.1.2, Toolkit 8.4.2, Velopack 1.2.158. The release update lifecycle uses `SimpleWebSource` with a public `GithubSource` fallback. No PAT is injected into binaries.

## Build and run

```sh
cd dotnet
dotnet build DinoSaurList.sln -c Release
dotnet test --solution DinoSaurList.sln -c Release --no-progress
dotnet run --project cli -- help
dotnet run --project cli -- catalogue --refresh --data-dir /tmp/dinosaurlist-preview
dotnet run --project app
```

The repository root has a pinned local `vpk` tool manifest. Per Jay's 2026-11-18 instruction, `native-dotnet.yml` uses the same self-hosted labels as Spectre: `self-hosted-rocky` (tests/publishing/NPI deployment), `self-host-win11` (Windows), and `self-host-osx` (Apple Silicon Mac). The workflow only accepts version-tag pushes or manual dispatch; public PR code never runs on these private machines. Runner access must be enabled for this repository. CI runs the entire solution on each target OS, packages self-contained app/CLI bundles, installs each setup and captures the populated UI and installed CLI assistant output. UI and CLI executables share the installed app root/runtime/updater locator. Installers are currently unsigned previews.

Push a version tag (`v0.1.0-preview.2` or a stable `v1.0.0`), or dispatch with `version` and `publish=true`, to publish both validated platform artifacts in one GitHub Release and then atomically update the NPI feed. Branch pushes do not duplicate platform builds; use manual dispatch for unpublished validation. The NPI deploy job can be retried independently from the immutable GitHub release. Feed JSON is copied after the package bytes; older version packages are retained. Stable channels are `win` / `osx`; prereleases use `win-preview` / `osx-preview`. Clients retain their installed channel and cannot silently downgrade.

The native Velopack package ID is `DinoSaurListNative` (display title DinoSaurList), so its Windows install root cannot collide with legacy `%LOCALAPPDATA%/DinoSaurList/dev_photos` or native data. Data defaults to per-user LocalApplicationData/DinoSaurList, outside that installation. Override with CLI `--data-dir` or UI `DINOSAURLIST_DATA_DIR`. Box defaults to the existing central Box Manager; its credential-free listing/download/upload API is shared by both hosts. Default update feed is `https://npi-service.kor.broadcom.net/dinosaurlist/`; the NPIServer repository owns its nginx route and download page. The app automatically checks and downloads releases in the background at startup. Velopack applies a prepared update on the next start; Settings also offers immediate restart. Failure to reach the default NPI feed falls back to public GitHub Releases without credentials. The GitHub repository URL can also be saved as the preferred feed. Custom HTTPS feeds do not silently fall back to a different publisher. No native code monitors main or runs the old Python updater. Automatic startup updates are skipped in installer smoke mode so verification is repeatable. See [Velopack's update lifecycle](https://docs.velopack.io/integrating/overview) and [public GitHub source](https://docs.velopack.io/integrating/update-sources).

## Compatibility and validation gates

| Behavior | Current evidence | Remaining gate before replacing legacy |
|---|---|---|
| Live catalogue, version/Flow metadata | 28 live tools, 108 packages with SHA1; 49 Python version comparisons, 4 installed folder contracts, 28 Flow contracts | Native Windows/macOS workflow verification of refresh modes (all/select/metadata/new-only are implemented with cache-preservation tests and depth-3 metadata traversal) |
| Streaming downloads and ZIP extraction | Bad checksum/truncation, traversal and archive symlink rejection tests | Large real Windows package/network interruption workflow |
| Install/update/downgrade | Stable final working directory, old records retained on failed installer; MSI previous uninstall and Flow Delete implemented | Real MSI cancellation/reboot/rollback cases, protected Python env reuse; external installer side effects cannot be rolled back by JSON transaction |
| Windows shortcuts / execution | Platform selection and literal path arguments; COM shortcut integration | Real Windows .cmd/.exe/.msi/.lnk interaction |
| macOS | Native host builds and runs only tools that declare macOS support | All 28 current tools omit Platform and default to Windows; Mac-supported tool inventory and Intel build requirement need confirmation |
| Favourites, default version, removal, settings, news | Shared CLI/UI Core state tests; legacy installation/favourites import preserves original files | Legacy news imports as inert text with original HTML backed up; refresh settings, pending local messages and manual activity are imported. Native Markdown/HTML/table viewer is implemented and rendered in UI tests; external per-user photo/link cache migration remains to compare |
| Profiles/photos | Legacy-compatible Box JSON names and payload tests; shared upload transport | Real write test on dedicated profile; Dedicated profile write test; manual activity import and calendar visual comparison (native calendar now uses actual Box created/modified dates; Useful Links is bundled) |
| Assistant | Native TF-IDF retrieval smoke test, no fabricated tool responses | Pure .NET POS/WordNet/Porter inference matches all 8,846 tokens in 25 real READMEs; ranked README identities match 39/39 actual queries. Golden tests cover 208 public normalization sentences and 9 conversation messages. Longest direct tool name wins over a shorter prefix. Continue user acceptance with additional task queries. |
| Logs/activity | Bounded in-memory operation log, persistent local action history | Existing persistent log viewer/telemetry and calendar visual comparison; Box dates and imported manual events are supported |
| Updates / previous install transition | Release-based startup download/next-start apply; NPI/GitHub sources; parallel read-only legacy linking | Native CI installs both setups and captures populated UI/CLI evidence (8116503, d1db5c3 passed); self-hosted build and actual two-version feed download/restart still need verification before production promotion |

Legacy telemetry remains disabled: copying its hardcoded password or unrestricted SQL contract would not preserve a safe native architecture. Define an authenticated, limited replacement contract before enabling reporting. Existing legacy source credentials require separate owner rotation; this branch neither copies nor rotates them.

A package build, a unit test or a Linux rendered screenshot is evidence for that specific check, not evidence of a successful Windows/macOS installer/update workflow. Keep this matrix open until those workflows and remaining feature contracts are exercised.

## Preview evidence

`7527f82` passed the GitHub-hosted Linux/Windows/macOS pipeline and produced Windows-x64/macOS-arm64 Velopack preview artifacts: https://github.com/brcm-ate-npi/DinoSourList-AppStore-Official-Release/actions/runs/44240017924. This validates compilation, 30 tests on Linux/macOS (Windows skips Unix symlink creation) and packaging. It does not validate interactive installer/update behavior. The next revision adds refresh-mode cache tests (32 total local cases).

`fe23598` also passed native setup installation, opening the installed Avalonia app, screenshot capture and execution of the installed self-contained CLI on Windows and macOS: https://github.com/brcm-ate-npi/DinoSourList-AppStore-Official-Release/actions/runs/44241285468 . Manual screenshot review then found startup category selection could hide the loaded catalogue; the next revision strengthens this gate to require visible tools and the actual version picker selection, and tests initialization after window creation. Later revisions also preserve pending cloud profile edits/photos and serialize CLI/UI state mutations.

The native assistant now runs entirely in C#: lazy-loaded public NLTK English model (1.9 MiB gzip), WordNet morphology and NLTK Porter extensions, linear TF/smoothed IDF/cosine/0.20 threshold, intent handling and deterministic README response templates. Python is only a model-export/baseline tool. Public model provenance/hash and redistribution notices are in `dotnet/core/Assistant/{Data,Licenses}`; no application README documents are embedded. Tokens longer than 1024 characters are excluded as malformed input. Actual comparison evidence is kept privately on the NPI server under `.scratch/dinosaurlist-ai-baseline`; the public fixtures contain synthetic text/public vocabulary only.

## Additional verified revisions

`8116503` passed the stricter populated UI/native setup checks on both operating systems: https://github.com/brcm-ate-npi/DinoSourList-AppStore-Official-Release/actions/runs/44242170074. `d1db5c3` passed the same setup checks plus installed assistant/model execution: https://github.com/brcm-ate-npi/DinoSourList-AppStore-Official-Release/actions/runs/44244735903. These runs used hosted runners before the explicit self-hosted requirement. They do not prove runner access or release-update application for the new workflow.

## Self-hosted workflow and redistribution review (2026-11-18)

Compared the actual release workflows in NPISpectreConverter, ATEDataAutoLinkFCM, CsvTool and Trace_Explorer. Reuse an installed compatible .NET 10 SDK first; install only when absent into a persistent user-owned directory. Confirm selection against global.json. Use current official Node 24 actions (checkout 7.0.1, setup-dotnet 6.1.0, upload-artifact 7.0.2, download-artifact 8.0.2). Velopack runtime/tool are pinned together to 1.2.161.

The replacement Mac runner has no PowerShell or passwordless sudo. Mac build steps use Bash; setup .pkg payload validation is followed by extracting the generated portable .app to an isolated user folder and running the real UI/CLI. This tests the portable/user-owned installation; it does not claim a system .pkg install on that runner. The previous hosted runner did test the actual system .pkg install. One tag checkout also failed at the network boundary (github.com:443 connection refused), separately from build or installation errors.

The NLTK stopwords corpus has unclear redistribution terms in the upstream dataset inventory. It has been removed from the model/export path and replaced with Snowball's BSD-3-Clause English list and explicit application compatibility rules. The effective filtered word set remains identical, including contraction fragments and retained modal words. Public tagger data retains its MIT notice; WordNet and Apache-2.0 adapted code retain their original terms.

`dotnet/Licenses/PACKAGES.json` records all 43 resolved Core/CLI/UI NuGet dependencies, including transitive packages. License/copyright/author notices, Skia/HarfBuzz/ANGLE original notices, MS-PL terms for Svg.Custom, and Inter's SIL OFL are shipped under `Licenses/ThirdParty` with both hosts. This does not relicense legacy code or third-party Box tools. The restore-time license inventory check rejects an unreviewed ID/version. No commercial Avalonia Accelerate/XPF components are referenced. Changing dependencies requires updating the reviewed inventory and shipped terms.

Tag-only automatic builds follow the reference release workflows and avoid building the same revision once for its branch push and again for its release tag. macOS checkout includes an authenticated GitHub API snapshot fallback for direct git transport failures, bound to the exact triggering commit.

## macOS 26.7.3 CoreVideo compatibility

Independent Swift reproduction (run 44251487820) on the replacement Mac returns -6661 from `CVDisplayLinkCreateWithActiveCGDisplays`, but success from `CVDisplayLinkCreateWithCGDisplay(CGMainDisplayID())`, callback registration and start, both in runner and Aqua sessions. This isolates the failure from .NET/application logic. Upstream context: https://github.com/AvaloniaUI/Avalonia/issues/18895 .

The source-built `app/Native/CoreVideoCompatibility.c` adapter re-exports the system framework and preserves its normal aggregate result. Only invalid-argument retries a real, online main-display link. The macOS packaging script redirects Avalonia.Native's CoreVideo dependency to this adapter and re-signs both local libraries ad hoc. System libraries are untouched. Modified-component notice, original MIT terms and adapter source ship with the bundle. Other CoreVideo users remain bound to the system framework. Review/remove this adapter when updating Avalonia; retain the real rendered UI gate.
