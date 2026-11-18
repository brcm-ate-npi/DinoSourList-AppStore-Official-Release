#!/usr/bin/env python3
"""Bound NPI package history per platform/channel; never delete live feed assets."""
import argparse
import json
import re
from pathlib import Path

PACKAGE = re.compile(r"DinoSaurListNative-(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)-(win|osx)(-preview)?-(full|delta)\.nupkg")


def version_key(version):
    core, separator, prerelease = version.partition("-")
    # SemVer compares numeric identifiers numerically, not by upload time or text.
    identifiers = tuple((0, int(p)) if p.isdigit() else (1, p) for p in prerelease.split("."))
    return tuple(int(p) for p in core.split(".")), not separator, identifiers


def candidates(root, keep=2):
    root = Path(root)
    if keep < 2:
        raise ValueError("Keep at least two versions per platform/channel")
    feeds = list(root.glob("releases.*.json"))
    if not feeds:
        raise ValueError("No live feeds; refusing to prune")
    protected = set()
    for feed in feeds:
        data = json.loads(feed.read_text())
        assets = data["Assets"]
        if not assets:
            raise ValueError("Empty live feed; refusing to prune")
        for asset in assets:
            name = asset["FileName"]
            if Path(name).name != name or not (root / name).is_file() or (root / name).is_symlink():
                raise ValueError("Missing or unsafe live feed asset: " + name)
            protected.add(name)
    # Legacy Velopack manifests are also public feeds.
    for manifest in root.glob("RELEASES*"):
        for line in manifest.read_text().splitlines():
            for token in line.split():
                if token.endswith(".nupkg"):
                    if Path(token).name != token or not (root / token).is_file():
                        raise ValueError("Missing legacy feed asset: " + token)
                    protected.add(token)
    groups = {}
    for package in root.glob("*.nupkg"):
        match = PACKAGE.fullmatch(package.name)
        if match and not package.is_symlink():
            version, platform, channel, kind = match.groups()
            groups.setdefault((platform, channel), {}).setdefault(version, []).append(package)
    removed = []
    for versions in groups.values():
        retained = set(sorted(versions, key=version_key, reverse=True)[:keep])
        for version, packages in versions.items():
            if version not in retained:
                removed.extend(p for p in packages if p.name not in protected)
    return sorted(removed)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory")
    parser.add_argument("--keep", type=int, default=2)
    parser.add_argument("--apply", action="store_true", help="Delete candidates (default: dry run)")
    args = parser.parse_args()
    for package in candidates(args.directory, args.keep):
        if args.apply:
            package.unlink()
        print(("Removed " if args.apply else "Would remove ") + package.name)
