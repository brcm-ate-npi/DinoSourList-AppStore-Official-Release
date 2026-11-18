#!/usr/bin/env bash
# Recovery for direct github.com git connectivity failures, using GitHub's API archive.
# Invoked inline from the workflow: this file is documentation/testable source of the same steps.
set -euo pipefail
: "${GITHUB_WORKSPACE:?}" "${RUNNER_TEMP:?}" "${GITHUB_REPOSITORY:?}" "${GITHUB_SHA:?}" "${GH_TOKEN:?}"
[[ "$GITHUB_SHA" =~ ^[a-f0-9]{40}$ ]] || exit 1
snapshot="$RUNNER_TEMP/native-source-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT"
mkdir -p "$snapshot"
curl --fail --location --silent --show-error --retry 3 --retry-all-errors --connect-timeout 20 --max-time 180 \
  -H "Authorization: Bearer $GH_TOKEN" -H 'Accept: application/vnd.github+json' \
  "https://api.github.com/repos/$GITHUB_REPOSITORY/tarball/$GITHUB_SHA" -o "$snapshot/source.tar.gz"
mkdir -p "$snapshot/files"
tar -xzf "$snapshot/source.tar.gz" --strip-components 1 -C "$snapshot/files"
[ -f "$snapshot/files/dotnet/global.json" ] || { echo 'Unexpected repository snapshot'; exit 1; }
# Preserve old Git metadata outside the source tree; no stale tracked/untracked files enter the build.
if [ -e "$GITHUB_WORKSPACE/.git" ]; then mv "$GITHUB_WORKSPACE/.git" "$snapshot/previous-git"; fi
rsync -a --delete "$snapshot/files/" "$GITHUB_WORKSPACE/"
echo "Restored source snapshot for $GITHUB_SHA using the GitHub API."
