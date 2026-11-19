#!/usr/bin/env python3
"""Capture credential-free contracts from the unchanged Python implementation.

Run from repository root with Python 3. Do not import UI, Box or telemetry clients.
"""
import configparser
import importlib.util
import itertools
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('legacy_folder_parser', root / 'src/lib/folder_parser.py')
legacy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(legacy)
versions = ['0.0.0.0', '1.2', '1.2.0.0', '1.9.99.99', '1.10.0.0', '2.0.0.0', '10.0.0.0']
flows = json.loads((root / 'dotnet/tests/Fixtures/flows.json').read_text())
metadata = []
for row in flows:
    parser = configparser.ConfigParser(interpolation=None, strict=False)
    parser.read_string(row['flow'])
    sections = {section.lower(): dict(parser.items(section)) for section in parser.sections()}
    metadata.append({'id': row['id'], 'author': sections.get('author', {}).get('name', ''),
                     'category': sections.get('category', {}).get('cluster', 'MISC'),
                     'execution': sections.get('execution', {}).get('file', ''),
                     'autoInstall': sections.get('installation', {}).get('auto', '').lower() == 'true',
                     'autoDelete': sections.get('delete', {}).get('auto', '').lower() == 'true'})
folders = ['BandMaster_V-1.0.0.0_A-SuetLi', 'QuickMi2e-V-7.1.0.0_A-RoeyYee',
           'SbinValidation@master_V-2.0.0.0_A-RoeyJiea', 'A-Tool-V-1.2.3.4-A-Author-Name']
result = {'source': 'src/lib/folder_parser.py at fd4eca5; configparser over 28 real Flow samples',
          'versionComparisons': [{'a': a, 'b': b, 'result': legacy.compare_versions(a, b)} for a, b in itertools.product(versions, repeat=2)],
          'installedFolders': [legacy.parse_software_folder_name(f) for f in folders], 'flows': metadata}
(root / 'dotnet/tests/Fixtures/legacy-contract.json').write_text(json.dumps(result, indent=2, ensure_ascii=False) + '\n')
print('Captured 49 version comparisons, 4 installed names and 28 Flow metadata contracts.')
