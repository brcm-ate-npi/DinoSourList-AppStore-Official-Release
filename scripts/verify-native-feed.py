#!/usr/bin/env python3
"""Validate both native release feeds before changing the server's live feed."""
import hashlib
import json
import re
import sys
from pathlib import Path


def verify(source, expected_version=""):
    source = Path(source).resolve()
    versions = set()
    for platform in ("win", "osx"):
        feeds = [p for p in (source / ("releases." + platform + ".json"), source / ("releases." + platform + "-preview.json")) if p.is_file()]
        if not feeds:
            raise ValueError("A " + platform + " feed is required")
        if len(feeds) == 2:
            # Stable releases expose the identical manifest on the old preview
            # channel so existing installations can migrate without reinstalling.
            if json.loads(feeds[0].read_text()) != json.loads(feeds[1].read_text()):
                raise ValueError("Stable and preview compatibility feeds must match")
        with feeds[0].open() as stream:
            assets = json.load(stream)["Assets"]
        full = [a for a in assets if a.get("Type") == "Full"]
        if not full:
            raise ValueError("Full release missing in " + feeds[0].name)
        for asset in full:
            name = asset["FileName"]
            if not re.fullmatch(r"DinoSaurListNative-[A-Za-z0-9.-]+-" + platform + r"(?:-preview)?-full\.nupkg", name):
                raise ValueError("Invalid package filename")
            package = source / name
            if package.is_symlink() or not package.is_file():
                raise ValueError("Referenced package is missing or a symlink: " + name)
            if asset["Size"] != package.stat().st_size:
                raise ValueError("Package size mismatch: " + name)
            digest = hashlib.sha256()
            with package.open("rb") as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(block)
            if digest.hexdigest().lower() != asset["SHA256"].lower():
                raise ValueError("Package SHA-256 mismatch: " + name)
            if expected_version and asset["Version"] != expected_version:
                raise ValueError("Unexpected package version: " + name)
            versions.add(asset["Version"])
    if len(versions) != 1:
        raise ValueError("Windows and macOS versions must agree")
    return versions.pop()


if __name__ == "__main__":
    print("Verified Windows/macOS package feeds: " + verify(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else ""))
