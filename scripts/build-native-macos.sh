#!/usr/bin/env bash
# Native Mac runner requires only .NET + macOS built-ins, without PowerShell.
set -euo pipefail
version="${REQUESTED_VERSION:-}"
if [ -n "${RELEASE_TAG:-}" ]; then version="${RELEASE_TAG#v}"; fi
if [ -z "$version" ]; then version="0.1.0-preview.${GITHUB_RUN_NUMBER:?}"; fi
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.-]+)?$ ]] || { echo 'Invalid version'; exit 1; }
channel=osx
if [[ "$version" == *-* ]]; then channel=osx-preview; fi
rm -rf publish Releases DinoSaurList.iconset
# App and CLI share the installation/runtime/updater location.
dotnet publish dotnet/app/DinoSaurList.App.csproj -c Release -r osx-arm64 --self-contained true -p:Version="$version" -o publish
dotnet publish dotnet/cli/DinoSaurList.Cli.csproj -c Release -r osx-arm64 --self-contained true -p:Version="$version" -o publish
mkdir -p DinoSaurList.iconset
sips -s format png IcoFolder/main.ico --out icon-source.png
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" icon-source.png --out "DinoSaurList.iconset/icon_${size}x${size}.png"
  double=$((size * 2))
  sips -z "$double" "$double" icon-source.png --out "DinoSaurList.iconset/icon_${size}x${size}@2x.png"
done
iconutil -c icns DinoSaurList.iconset -o DinoSaurList.icns
dotnet vpk pack --packId DinoSaurListNative --packTitle DinoSaurList --packVersion "$version" --packDir publish --mainExe DinoSaurList --icon DinoSaurList.icns --channel "$channel"
shopt -s nullglob
packages=(Releases/*-full.nupkg)
[ "${#packages[@]}" -eq 1 ] || { echo 'Expected one native full package'; exit 1; }
if unzip -Z -1 "${packages[0]}" | grep -Ei 'Box\.Automation\.WSD|BoxFast|BoxAutomate|Secrets\.cs|base_client\.py'; then echo 'Legacy credential file in package'; exit 1; fi
./scripts/native-install-smoke-macos.sh
