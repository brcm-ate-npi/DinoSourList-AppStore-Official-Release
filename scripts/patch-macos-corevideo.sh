#!/usr/bin/env bash
set -euo pipefail
publish_dir="${1:?publish directory required}"
native="$publish_dir/libAvaloniaNative.dylib"
adapter="$publish_dir/libDinoCoreVideo.dylib"
[ -f "$native" ] || { echo 'Avalonia native library missing'; exit 1; }
source_file="$(cd "$(dirname "$0")/.." && pwd)/dotnet/app/Native/CoreVideoCompatibility.c"
# This compatibility adapter is intentionally limited to the deprecated API used by Avalonia.
xcrun clang -dynamiclib -arch arm64 -mmacosx-version-min=11.0 -Werror -Wno-deprecated-declarations \
  "$source_file" -framework CoreGraphics -Xlinker -reexport_framework -Xlinker CoreVideo \
  -Wl,-install_name,@rpath/libDinoCoreVideo.dylib -o "$adapter"
framework=$(otool -L "$native" | awk '/CoreVideo.framework/ {print $1; exit}')
[ -n "$framework" ] || { echo 'Expected CoreVideo dependency missing; review the adapter for this Avalonia version'; exit 1; }
# Redirect only Avalonia.Native's dependency. Other libraries continue using Apple's framework directly.
install_name_tool -change "$framework" '@loader_path/libDinoCoreVideo.dylib' "$native"
codesign --force --sign - "$adapter"
codesign --force --sign - "$native"
probe="$RUNNER_TEMP/dinosaurlist-corevideo-probe"
xcrun clang -arch arm64 -mmacosx-version-min=11.0 -Werror -Wno-deprecated-declarations \
  "$(dirname "$source_file")/CoreVideoCompatibilityProbe.c" -framework CoreVideo -o "$probe"
"$probe" "$(cd "$publish_dir" && pwd)/libDinoCoreVideo.dylib"
mkdir -p "$publish_dir/Licenses/MacCompatibility"
cp "$source_file" "$publish_dir/Licenses/MacCompatibility/"
cat > "$publish_dir/Licenses/MacCompatibility/NOTICE.txt" <<'NOTICE'
DinoSaurList's macOS build modifies the CoreVideo load command of libAvaloniaNative.dylib
(Avalonia 12.1.2) to use libDinoCoreVideo.dylib. Avalonia's original MIT terms and attribution
are retained in Licenses/ThirdParty. The adapter source is included here.
It calls Apple's original aggregate display-link API first. On invalid-argument only,
it retries Apple's main-display API when that display is online. It neither simulates
success nor changes system libraries. Remove this adapter after adopting an upstream fix.
NOTICE
