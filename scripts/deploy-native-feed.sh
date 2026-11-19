#!/usr/bin/env bash
# Run on the NPI self-hosted Linux runner after both OS packages have been built.
set -euo pipefail
source_dir="$(realpath "${1:?release asset directory required}")"
target_dir="${2:-/var/www/dinosaurlist}"
# Verify both packages completely before exposing any new server feed or package bytes.
python3 "$(dirname "$0")/verify-native-feed.py" "$source_dir" "${3:-}"
mkdir -p "$target_dir"
chmod 755 "$target_dir"
# Upload package bytes before publishing a feed referencing them. Rename each file atomically.
copy_asset() {
  local file="$1" name
  name="$(basename "$file")"
  cp "$file" "$target_dir/.$name.uploading"
  chmod 644 "$target_dir/.$name.uploading"
  mv -f "$target_dir/.$name.uploading" "$target_dir/$name"
}
shopt -s nullglob
packages=("$source_dir"/*-full.nupkg)
feeds=("$source_dir"/releases.*.json)
[ "${#packages[@]}" -ge 2 ] && [ "${#feeds[@]}" -ge 2 ] || { echo 'Both OS packages and feeds are required'; exit 1; }
for file in "$source_dir"/*; do
  case "$(basename "$file")" in releases.*.json|assets.*.json|RELEASES*) continue;; esac
  [ -f "$file" ] && copy_asset "$file"
done
for file in "$source_dir"/releases.*.json "$source_dir"/assets.*.json "$source_dir"/RELEASES*; do copy_asset "$file"; done
# Match the server's bounded release history, independently for each OS/channel.
# Run only after complete publication; every live JSON/legacy feed reference is protected.
python3 "$(dirname "$0")/prune-native-feed.py" "$target_dir" --keep 2 --apply
python3 - "$target_dir" <<'PY'
import json, os, sys, tempfile
root = sys.argv[1]
files = [{'name': name} for name in sorted(os.listdir(root)) if not name.startswith('.') and name not in ('index.html', 'list.json') and os.path.isfile(os.path.join(root, name))]
fd, temp = tempfile.mkstemp(prefix='.list-', dir=root)
with os.fdopen(fd, 'w') as stream:
    json.dump(files, stream)
os.chmod(temp, 0o644)
os.replace(temp, os.path.join(root, 'list.json'))
PY
