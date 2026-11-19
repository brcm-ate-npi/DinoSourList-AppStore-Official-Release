#!/usr/bin/env bash
# Exercise an unmodified published app's automatic update in an isolated app/data directory.
set -euo pipefail
proof="$RUNNER_TEMP/dinosaurlist-update-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT"
evidence="$RUNNER_TEMP/native-update-evidence"
mkdir -p "$proof" "$evidence"
feed=https://npi-service.kor.broadcom.net/dinosaurlist/
old="${NATIVE_UPDATE_FROM:-0.1.0-preview.20261118.13}"
automatic="${NATIVE_EXPECT_STARTUP_APPLY:-false}"
curl --fail --location --silent --show-error --retry 3 --max-time 60 "${feed}releases.osx-preview.json" -o "$evidence/feed.json"
version=$(plutil -extract Assets.0.Version raw -o - "$evidence/feed.json")
file=$(plutil -extract Assets.0.FileName raw -o - "$evidence/feed.json")
sha=$(plutil -extract Assets.0.SHA256 raw -o - "$evidence/feed.json")
[[ "$version" != "$old" ]] || { echo 'No newer published Mac version exists yet.'; exit 1; }
[[ "$file" =~ ^DinoSaurListNative-[a-zA-Z0-9.-]+-osx-preview-full\.nupkg$ ]] || exit 1
url="https://github.com/brcm-ate-npi/DinoSourList-AppStore-Official-Release/releases/download/v$old/DinoSaurListNative-osx-preview-Portable.zip"
curl --fail --location --silent --show-error --retry 3 --retry-all-errors --connect-timeout 20 --max-time 180 "$url" -o "$proof/old.zip"
mkdir -p "$proof/install"
ditto -x -k "$proof/old.zip" "$proof/install"
app="$proof/install/DinoSaurList.app"
[ -d "$app" ] || app="$proof/install/DinoSaurListNative.app"
manifest=$(find "$app/Contents" -name sq.version -print -quit)
[ -f "$manifest" ] || { echo 'Published app manifest missing'; exit 1; }
cp "$manifest" "$evidence/before.version.xml"
grep -F "<version>$old</version>" "$manifest" >/dev/null
if pgrep -x DinoSaurList >/dev/null; then echo 'An existing DinoSaurList is running; refusing to disturb its shared update cache.'; exit 1; fi
# Cache is shared by bundle ID. Preserve it without deleting any pre-existing cache files.
cache="$HOME/Library/Caches/velopack/DinoSaurListNative"
lease="${cache}.proof-lease"
mkdir -p "$(dirname "$cache")"
mkdir "$lease" || { echo 'Another update proof owns the cache lease'; exit 1; }
backup=false
if [ -d "$cache" ]; then mv "$cache" "$proof/cache-before"; backup=true; fi
label="net.brcm.dinosaurlist.update.$GITHUB_RUN_ID.$GITHUB_RUN_ATTEMPT"
domain="gui/$(id -u)"
cleanup() {
  if [ -f "$HOME/Library/Logs/velopack_DinoSaurListNative.log" ]; then
    tail -n 500 "$HOME/Library/Logs/velopack_DinoSaurListNative.log" > "$evidence/velopack.log"
  fi
  launchctl bootout "$domain/$label" 2>/dev/null || true
  pids=$(pgrep -f "$proof/install/" || true)
  if [ -n "$pids" ]; then kill $pids 2>/dev/null || true; fi
  if [ -d "$cache" ]; then mv "$cache" "$proof/cache-after"; fi
  if $backup; then mv "$proof/cache-before" "$cache"; fi
  rmdir "$lease"
}
trap cleanup EXIT
export DINOSAURLIST_DATA_DIR="$proof/data"
mkdir -p "$DINOSAURLIST_DATA_DIR/Documents"
# Public test metadata only; the old app still performs its real startup update check.
cat > "$DINOSAURLIST_DATA_DIR/catalogue.json" <<'JSON'
{"fetchedAt":"2026-11-18T00:00:00Z","tools":[{"id":"1","folderName":"Preview-Author","name":"Preview","author":"Author","category":"MISC","packages":[{"fileId":"2","fileName":"v1.0.0-any.zip","version":"1.0.0","size":1,"sha1":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","platform":"any"}],"flow":{"sections":{"platform":{"supported":"any"}}},"files":[{"id":"3","name":"README.md","type":"file","sha1":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}],"warnings":[]}
JSON
printf 'TOOL: Preview\nAUTHOR: Author\nPURPOSE: Validate the native engineering toolbox.\nPreview fixture.\n' > "$DINOSAURLIST_DATA_DIR/Documents/3-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.md"
printf 'preserve-update-proof\n' > "$DINOSAURLIST_DATA_DIR/sentinel.txt"
start_app() {
  local mode="$1"
  local agent="$proof/$mode.plist"
  plutil -create xml1 "$agent"
  plutil -insert Label -string "$label" "$agent"
  plutil -insert ProgramArguments -array "$agent"
  plutil -insert ProgramArguments.0 -string "$app/Contents/MacOS/DinoSaurList" "$agent"
  plutil -insert ProgramArguments.1 -string '--data-dir' "$agent"
  plutil -insert ProgramArguments.2 -string "$DINOSAURLIST_DATA_DIR" "$agent"
  if [ "$mode" = capture ]; then
    plutil -insert ProgramArguments.3 -string '--smoke-test-output' "$agent"
    plutil -insert ProgramArguments.4 -string "$evidence/updated-ui" "$agent"
  fi
  plutil -insert EnvironmentVariables -dictionary "$agent"
  plutil -insert EnvironmentVariables.DINOSAURLIST_DATA_DIR -string "$DINOSAURLIST_DATA_DIR" "$agent"
  # launchd must not kill UpdateMac when the old application intentionally exits to apply.
  plutil -insert AbandonProcessGroup -bool YES "$agent"
  plutil -insert RunAtLoad -bool YES "$agent"
  plutil -insert ProcessType -string Interactive "$agent"
  plutil -insert StandardOutPath -string "$evidence/$mode.stdout.log" "$agent"
  plutil -insert StandardErrorPath -string "$evidence/$mode.stderr.log" "$agent"
  launchctl bootstrap "$domain" "$agent"
}
stop_app() {
  launchctl bootout "$domain/$label" 2>/dev/null || true
  pids=$(pgrep -f "$proof/install/" || true)
  if [ -n "$pids" ]; then kill $pids 2>/dev/null || true; fi
}
echo "Starting published $old; expecting automatic download of $version"
start_app download
if [ "$automatic" = true ]; then
  # Do not stop/relaunch the baseline: it must apply the published target itself.
  for ((second=0;second<240;second++)); do
    if [ -f "$manifest" ] && grep -F "<version>$version</version>" "$manifest" >/dev/null; then break; fi
    sleep 1
  done
  grep -F "<version>$version</version>" "$manifest" >/dev/null || { echo 'Startup did not automatically apply the newer release'; exit 1; }
  curl --fail --location --silent --show-error --retry 3 --max-time 120 "${feed}${file}" -o "$proof/expected.nupkg"
  actual=$(shasum -a 256 "$proof/expected.nupkg" | awk '{print toupper($1)}')
  [ "$actual" = "$sha" ] || { echo 'Published target package hash mismatch'; exit 1; }
  unzip -p "$proof/expected.nupkg" lib/app/Contents/MacOS/DinoSaurList.dll > "$proof/expected.dll"

else
for ((second=0;second<180;second++)); do
  [ ! -f "$cache/packages/$file" ] || break
  sleep 1
done
[ -f "$cache/packages/$file" ] || { echo 'Old app did not automatically download the newer package'; exit 1; }
actual=$(shasum -a 256 "$cache/packages/$file" | awk '{print toupper($1)}')
[ "$actual" = "$sha" ] || { echo 'Automatically downloaded package hash mismatch'; exit 1; }
# First start only downloads. It must not replace a running app.
grep -F "<version>$old</version>" "$manifest" >/dev/null
stop_app
echo "Package checksum verified; testing next-start automatic apply"
start_app apply
for ((second=0;second<90;second++)); do
  if [ -f "$manifest" ] && grep -F "<version>$version</version>" "$manifest" >/dev/null; then break; fi
  sleep 1
done
grep -F "<version>$version</version>" "$manifest" >/dev/null || { echo 'Next-start automatic application failed'; exit 1; }
fi
cp "$manifest" "$evidence/after.version.xml"
for ((second=0;second<60;second++)); do
  if ! pgrep -f "UpdateMac.*$proof/install/" >/dev/null; then break; fi
  sleep 1
done
if pgrep -f "UpdateMac.*$proof/install/" >/dev/null; then echo 'Updater is still applying the package'; exit 1; fi
if [ "$automatic" = true ]; then
  expected_dll=$(shasum -a 256 "$proof/expected.dll" | awk '{print $1}')
  installed_dll=$(shasum -a 256 "$app/Contents/MacOS/DinoSaurList.dll" | awk '{print $1}')
  [ "$expected_dll" = "$installed_dll" ] || { echo 'Automatically applied DLL differs from the published release'; exit 1; }
fi
stop_app
start_app capture
for ((second=0;second<60;second++)); do
  [ ! -s "$evidence/updated-ui.json" ] || break
  [ ! -s "$evidence/updated-ui.error.txt" ] || break
  sleep 1
done
[ -s "$evidence/updated-ui.json" ] && [ -s "$evidence/updated-ui.png" ]
grep -F "\"version\": \"$version\"" "$evidence/updated-ui.json" >/dev/null
grep -F preserve-update-proof "$DINOSAURLIST_DATA_DIR/sentinel.txt" >/dev/null

if [ "$automatic" = true ]; then
  printf '{"from":"%s","to":"%s","automaticDownload":true,"appliedOnFirstStart":true,"userRestartRequired":false,"dataPreserved":true,"sha256":"%s"}\n' "$old" "$version" "$actual" > "$evidence/update-result.json"
  cat "$evidence/update-result.json"
  printf "::notice title=Published Mac immediate startup update verified::%s to %s; single launch, automatic apply, published DLL checksum and data preservation passed\n" "$old" "$version"
else
printf '{"from":"%s","to":"%s","automaticDownload":true,"nextStartupApplied":true,"dataPreserved":true,"sha256":"%s"}\n' "$old" "$version" "$actual" > "$evidence/update-result.json"
cat "$evidence/update-result.json"
printf "::notice title=Published Mac automatic update verified::%s to %s; automatic download, SHA256, next-start apply, updated UI and sentinel passed\n" "$old" "$version"

fi
