#!/usr/bin/env bash
set -euo pipefail
export DINOSAURLIST_DATA_DIR="$RUNNER_TEMP/dinosaurlist-native-data"
mkdir -p "$DINOSAURLIST_DATA_DIR/Documents"
cat > "$DINOSAURLIST_DATA_DIR/catalogue.json" <<'JSON'
{"fetchedAt":"2026-11-18T00:00:00Z","tools":[{"id":"1","folderName":"Preview-Author","name":"Preview","author":"Author","category":"MISC","packages":[{"fileId":"2","fileName":"v1.0.0-any.zip","version":"1.0.0","size":1,"sha1":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","platform":"any"}],"flow":{"sections":{"platform":{"supported":"any"}}},"files":[{"id":"3","name":"README.md","type":"file","sha1":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}],"warnings":[]}
JSON
cat > "$DINOSAURLIST_DATA_DIR/Documents/3-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.md" <<'MARKDOWN'
TOOL: Preview
AUTHOR: Author
PURPOSE: Validate the native engineering toolbox.
## Overview
Preview fixture.
MARKDOWN
shopt -s nullglob
setups=(Releases/*-Setup.pkg)
[ "${#setups[@]}" -eq 1 ] || { echo 'Expected one setup package'; exit 1; }
# Validate the actual .pkg payload and install the portable .app in an isolated user directory.
# This runner has no passwordless sudo; do not alter its system applications.
pkgutil --payload-files "${setups[0]}" >/dev/null
portables=(Releases/*-Portable.zip)
[ "${#portables[@]}" -eq 1 ] || { echo 'Expected one portable archive'; exit 1; }
install_root="$RUNNER_TEMP/native-user-install"
mkdir -p "$install_root"
ditto -x -k "${portables[0]}" "$install_root"
app="$install_root/DinoSaurList.app/Contents/MacOS/DinoSaurList"
if [ ! -f "$app" ]; then app="$install_root/DinoSaurListNative.app/Contents/MacOS/DinoSaurList"; fi
[ -f "$app" ] || { echo 'Installed app missing'; exit 1; }
evidence="$RUNNER_TEMP/native-installed-evidence"
mkdir -p "$evidence"
prefix="$evidence/installed-ui"
# LaunchServices puts the Cocoa app in the logged-in GUI session; direct Mach-O execution
# from a background runner can fail to obtain the native display-link render timer.
system_profiler SPDisplaysDataType | grep -E 'Displays:|Display Type:|Online:|Resolution:' || true
bundle="${app%/Contents/MacOS/DinoSaurList}"
open -W -n "$bundle" --args --data-dir "$DINOSAURLIST_DATA_DIR" --smoke-test-output "$prefix" &
app_pid=$!
trap 'kill "$app_pid" 2>/dev/null || true' EXIT
finished=false
for ((second=0; second<60; second++)); do
  if ! kill -0 "$app_pid" 2>/dev/null; then finished=true; break; fi
  sleep 1
done
if [ "$finished" != true ]; then echo 'Installed app timed out'; exit 1; fi
wait "$app_pid"
trap - EXIT
[ -s "$prefix.png" ] && [ -s "$prefix.json" ] || { echo 'Installed UI evidence missing'; exit 1; }
grep -Eq '"visible": true' "$prefix.json"
grep -Eq '"tools": 1' "$prefix.json"
cli="$(dirname "$app")/DinoSaurList.Cli"
"$cli" catalogue --data-dir "$DINOSAURLIST_DATA_DIR" > "$evidence/installed-cli.json"
"$cli" chat --query Preview --data-dir "$DINOSAURLIST_DATA_DIR" > "$evidence/installed-assistant.json"
grep -q Preview "$evidence/installed-assistant.json"
