#!/usr/bin/env python3
"""Prevent legacy credentials from entering the native source or release workflow.

Existing legacy files are intentionally not scanned here: rotation/remediation of
already published credentials is a separate operational task. Never print values.
"""
from pathlib import Path
import re
import gzip
import hashlib
import json
import sys

root = Path(__file__).resolve().parents[1]
patterns = [
    re.compile(rb'gh[pousr]_[A-Za-z0-9_]{20,}'),
    re.compile(rb'github_pat_[A-Za-z0-9_]{20,}'),
    re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
    re.compile(rb'(?i)(?:password|client_secret|private_key|access_token|refresh_token)\s*[=:]\s*["\'][^"\'\r\n]{8,}["\']'),
]
failed = []
for path in [* (root / 'dotnet').rglob('*'), * (root / '.github' / 'workflows').rglob('*.yml')]:
    if not path.is_file() or any(p in {'bin', 'obj', 'TestResults'} for p in path.parts):
        continue
    if path.suffix.lower() in {'.dll', '.exe', '.pdb', '.pfx', '.key', '.pem'}:
        failed.append(str(path.relative_to(root)))
        continue
    if path.name == 'english-nltk.json.gz':
        manifest = json.loads(path.with_name('manifest.json').read_text())
        if hashlib.sha256(path.read_bytes()).hexdigest() != manifest['sha256']:
            failed.append(str(path.relative_to(root)))
        if any(p.search(gzip.decompress(path.read_bytes())) for p in patterns):
            failed.append(str(path.relative_to(root)))
        continue
    if path.suffix not in {'.cs', '.json', '.csproj', '.props', '.targets', '.axaml', '.yml'}:
        continue
    body = path.read_bytes()
    if any(p.search(body) for p in patterns):
        failed.append(str(path.relative_to(root)))
if failed:
    print('Credential or binary guard failed in: ' + ', '.join(sorted(set(failed))), file=sys.stderr)
    sys.exit(1)
print('Native source credential guard passed.')
