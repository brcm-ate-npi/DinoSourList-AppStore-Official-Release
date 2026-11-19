#!/usr/bin/env python3
"""Run after restore. Every app/Core/CLI dependency must have reviewed shipped terms."""
import json
from pathlib import Path
root = Path(__file__).resolve().parents[1]
folder = root / 'dotnet/Licenses'
reviewed = {(p['id'].lower(), p['version']): p for p in json.loads((folder/'PACKAGES.json').read_text())}
for project in ('core', 'cli', 'app'):
    assets = json.loads((root/'dotnet'/project/'obj/project.assets.json').read_text())
    for identity, data in assets['libraries'].items():
        if data['type'] != 'package':
            continue
        name, version = identity.rsplit('/', 1)
        item = reviewed.get((name.lower(), version))
        if item is None or not (folder/item['terms']).is_file():
            raise SystemExit('Unreviewed native dependency: ' + identity)
for name in ('INTER_OFL.txt', 'THIRD_PARTY_NOTICES.md'):
    assert (folder/name).stat().st_size > 100
print('Native dependency license inventory passed.')
