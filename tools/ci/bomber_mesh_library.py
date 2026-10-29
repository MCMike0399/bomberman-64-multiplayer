#!/usr/bin/env python3
"""Reject stale or corrupted Mac-authored geometry before packaging."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ('scripts/arena/models/BomberModel.cs', 'scripts/arena/models/BomberModel.Mishaps.cs',
           'scripts/arena/models/BomberMeshLibrary.cs')
KEYS = {'torso', 'helmet', 'face-rim', 'face', 'eye-left', 'eye-right', 'squint-left', 'squint-right'} | {
    f'spiral-{side}-{frame:02d}' for side in ('left', 'right') for frame in range(12)}


def check(root=ROOT):
    manifest = json.loads((root / 'assets/models/bomber-meshes.json').read_text())
    if manifest.get('schemaVersion') != 1 or manifest['producer']['platform'] != 'macOS' or not manifest['producer']['engine'].startswith('ed1daf0bf'):
        raise ValueError('Geometry requires the pinned Mac authoring engine')
    digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    if digest(root / 'assets/models/bomber-meshes.res') != manifest['librarySha256']:
        raise ValueError('Canonical geometry file hash differs')
    if set(manifest['sourceFiles']) != set(SOURCES):
        raise ValueError('Canonical geometry source roster differs')
    for source in SOURCES:
        if digest(root / source) != manifest['sourceFiles'][source]:
            raise ValueError('Regenerate canonical geometry after editing ' + source)
    meshes = manifest['meshes']
    if len(meshes) != 32 or {m['key'] for m in meshes} != KEYS or [m['index'] for m in meshes] != list(range(32)):
        raise ValueError('Canonical geometry must retain all 32 authored meshes')
    for mesh in meshes:
        if len(bytes.fromhex(mesh['packedSurfaceSha256'])) != 32:
            raise ValueError('Invalid packed surface hash')
    return manifest


if __name__ == '__main__':
    check()
    print('PASS canonical Mac geometry: source hashes, resource hash and 32-mesh roster')
