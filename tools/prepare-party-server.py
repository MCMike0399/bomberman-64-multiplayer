#!/usr/bin/env python3
"""Prepare a Docker context from the verified portable server ZIP, never source."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def prepare(output):
    metadata = json.loads((ROOT / 'artifacts/server-build-metadata.json').read_text())
    inventory = metadata['files']
    archive = ROOT / 'artifacts/Blast-Circuit-server-dotnet10.zip'
    expected = (archive.with_suffix('.zip.sha256')).read_text().split()[0]
    if hashlib.sha256(archive.read_bytes()).hexdigest() != expected:
        raise RuntimeError('Server ZIP checksum mismatch; rerun managed QA.')
    if output.exists():
        raise RuntimeError('Use a new output directory; existing deliveries are preserved.')
    prefix = 'Blast-Circuit-server-dotnet10/'
    with zipfile.ZipFile(archive) as zipped:
        names = zipped.namelist()
        if len(names) != len(inventory) or set(names) != {prefix + name for name in inventory}:
            raise RuntimeError('Server ZIP does not match its verified inventory.')
        for name, digest in inventory.items():
            relative = Path(name)
            if relative.is_absolute() or any(part.startswith('.') for part in relative.parts):
                raise RuntimeError('Invalid server delivery path.')
            if hashlib.sha256(zipped.read(prefix + name)).hexdigest() != digest:
                raise RuntimeError('Server file checksum mismatch: ' + name)
        output.mkdir(parents=True)
        for name in inventory:
            target = output / 'server' / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(zipped.read(prefix + name))
    for name in ('Dockerfile', 'compose.yaml', '.dockerignore'):
        shutil.copyfile(ROOT / 'deploy/party' / name, output / name)
    shutil.copyfile(ROOT / 'artifacts/server-build-metadata.json', output / 'server-build-metadata.json')
    (output / '.env').write_text('BLAST_VERSION=' + metadata['version'] + '-' + metadata['revision'][:12] + '\n')
    print(f'Prepared {len(inventory)} verified runtime files in {output}')
    print('Start on the server: docker compose up -d --build --wait')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    prepare(parser.parse_args().output.resolve())
