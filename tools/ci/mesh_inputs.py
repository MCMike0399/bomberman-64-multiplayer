#!/usr/bin/env python3
"""Validate complete builder observations and locate input differences; no release approval."""
import argparse
from collections import Counter
import gzip
import hashlib
import json
from pathlib import Path
import struct

STAGES = ('sunken-temple', 'rock-garden', 'up-and-down', 'top-rules',
          'blizzard-battle', 'greedy-trap', 'circuit-garden')

NAMES = ('torso', 'helmet', 'face-rim', 'face', 'left-eye', 'right-eye')
HELMET = {'helmet/parameters': 1, 'helmet/point': 3840, 'helmet/vertex': 5520,
          'helmet/pre-index': 1, 'helmet/indexed': 1, 'helmet/packed': 1}
PATCH = {'patch/parameters': 1, 'patch/outline': 6656, 'patch/point': 6656, 'patch/vertex': 9600,
         'patch/pre-normal': 1, 'patch/post-normal': 1, 'patch/indexed': 1, 'patch/packed': 1}
BINARY = {'helmet/pre-index', 'helmet/indexed', 'helmet/packed', 'patch/pre-normal',
          'patch/post-normal', 'patch/indexed', 'patch/packed'}


def require(ok, message):
    if not ok: raise ValueError(message)


def load(directory):
    summary = json.loads((directory / 'mesh-inputs-summary.json').read_text())
    trace = directory / 'mesh-inputs.jsonl.gz'; blob = (directory / 'mesh-inputs.bin').read_bytes()
    require(hashlib.sha256(trace.read_bytes()).hexdigest() == summary['traceSha256'], 'Trace file hash differs')
    require(hashlib.sha256(blob).hexdigest() == summary['binarySha256'], 'Trace binary hash differs')
    records = []
    with gzip.open(trace, 'rt') as stream:
        for line in stream:
            require(len(records) < 120000 and len(line) < 10000, 'Oversized mesh trace')
            row = json.loads(line); require(row['sequence'] == len(records), 'Missing/reordered mesh observation')
            records.append(row)
    require(records and records[0]['kind'] == 'header' and records[-1]['kind'] == 'complete', 'Incomplete mesh trace')
    require(records[0]['payload']['schemaVersion'] == 1, 'Unknown mesh trace schema')
    require(summary['records'] == len(records) == records[-1]['payload']['records'], 'Mesh trace record count differs')
    require(records[-1]['payload']['binaryBytes'] == len(blob), 'Mesh trace binary size differs')
    require(records[-1]['payload']['meshes'] == 6 and [m['name'] for m in summary['meshes']] == list(NAMES), 'Mesh roster differs')
    cursor = 0; position = 1; managed = []; stages = []
    for index, name in enumerate(NAMES):
        require(records[position]['kind'] == 'begin' and records[position]['mesh'] == name, 'Missing mesh start/order')
        managed.append(records[position]); position += 1; counts = Counter(); packed = None
        while position < len(records)-1 and records[position]['kind'] != 'end':
            row = records[position]; position += 1
            require(row['mesh'] == name, 'Mesh observations cross a boundary'); counts[row['kind']] += 1
            if row['kind'] in BINARY:
                data = row['payload']; length = data['length']
                require(type(length) is int and length > 0 and data['offset'] == cursor and cursor+length <= len(blob), 'Invalid/partial mesh segment')
                segment = blob[cursor:cursor+length]; cursor += length
                require(hashlib.sha256(segment).hexdigest() == data['sha256'], 'Mesh segment hash differs')
                stages.append({'mesh': name, 'kind': row['kind'], 'sha256': data['sha256'], 'bytes': segment})
                if row['kind'].endswith('/packed'): packed = data['sha256']
            else: managed.append(row)
        require(counts == Counter(HELMET if index < 2 else PATCH), 'Incomplete builder calls/stages: ' + name)
        end = records[position]; position += 1; result = end['payload']
        require(end['kind'] == 'end' and end['mesh'] == name and result == summary['meshes'][index], 'Mesh completion differs')
        require(result['generatedSha256'] == packed, 'Generated mesh differs from observed packed segment')
        require(result['identicalToCapturedMesh'] is (result['generatedSha256'] == result['cachedSha256']), 'Dishonest builder/captured equality')
        if summary.get('meshSource') == 'canonical-mac-resource':
            require(isinstance(result.get('canonicalSha256'), str) and len(result['canonicalSha256']) == 64 and
                    result['cachedSha256'] == result['canonicalSha256'], 'Captured mesh differs from canonical Mac resource')
        else:
            require(result['identicalToCapturedMesh'] is True, 'Observed builder changed captured procedural mesh')
    require(position == len(records)-1 and cursor == len(blob), 'Extra mesh data or missing segment')
    return {'header': records[0]['payload'], 'managed': managed, 'stages': stages, 'capturedMeshes': summary['meshes'], 'meshSource': summary.get('meshSource', 'procedural')}


def difference(a, b, path='$'):
    if type(a) != type(b): return {'path': path, 'expected': a, 'actual': b}
    if isinstance(a, dict):
        if a.keys() != b.keys(): return {'path': path, 'reason': 'fields differ'}
        for key in a:
            found = difference(a[key], b[key], path+'.'+key)
            if found: return found
    elif isinstance(a, list):
        if len(a) != len(b): return {'path': path, 'reason': 'length differs'}
        for i, (x, y) in enumerate(zip(a, b)):
            found = difference(x, y, path+f'[{i}]')
            if found: return found
    elif a != b:
        result = {'path': path, 'expected': a, 'actual': b}
        if isinstance(a, str) and isinstance(b, str) and len(a) == len(b) == 8:
            try: result.update(expectedFloat32=struct.unpack('!f', bytes.fromhex(a))[0], actualFloat32=struct.unpack('!f', bytes.fromhex(b))[0])
            except ValueError: pass
        return result
    return None


def compare(reference, candidate, output):
    output.mkdir(parents=True, exist_ok=False)
    a, b = load(reference), load(candidate)
    require(a['header']['shared'] == b['header']['shared'], 'Incompatible source/engine/recipe')
    managed = difference(a['managed'], b['managed']); stages = []
    for x, y in zip(a['stages'], b['stages']):
        require((x['mesh'], x['kind']) == (y['mesh'], y['kind']), 'Stage ordering differs')
        if x['bytes'] != y['bytes']:
            first = next((i for i, (u, v) in enumerate(zip(x['bytes'], y['bytes'])) if u != v), min(len(x['bytes']),len(y['bytes'])))
            stages.append({'mesh': x['mesh'], 'kind': x['kind'], 'referenceSha256': x['sha256'], 'candidateSha256': y['sha256'], 'firstByte': first})
    report = {'claim': 'Builder/input diagnostics only; no pixel or release qualification', 'compatibleInputs': True,
              'equalInputs': managed is None and not stages, 'firstManagedDifference': managed, 'differentStages': stages,
              'referenceProvenance': a['header']['provenance'], 'candidateProvenance': b['header']['provenance']}
    (output / 'report.json').write_text(json.dumps(report, indent=2)+'\n')
    return 0 if report['equalInputs'] else 1


def scene(directory, stage):
    """Validate the actual selected scene and every retained recipe identity."""
    require(stage in STAGES, 'Unknown controlled stage')
    identity = 'full-up-down-high-v2' if stage == 'up-and-down' else 'full-' + stage + '-high-v2'
    try:
        observed = json.loads((directory / 'visual-parity-observations.json').read_text())
        require(isinstance(observed, dict), 'Invalid scene observation')
        require(observed.get('experimentStage') == stage and observed.get('noisePolicy') == 'integer',
                'Selected scene/noise differs from the requested stage: ' + stage)
        labels = ('warmup', 'base-a', 'base-b', 'dof-off', 'reflection-off')
        records = observed.get('records', [])
        require(isinstance(records, list) and all(isinstance(row, dict) for row in records) and
                [row.get('label') for row in records] == list(labels), 'Missing/reordered scene recipe')
        baseline = None; bindings = []
        for row in records:
            recipe = json.loads((directory / ('parity-' + row['label'] + '-recipe.json')).read_text())
            require(isinstance(recipe, dict) and isinstance(recipe.get('conditions'), dict), 'Invalid scene recipe')
            for key in ('authority', 'camera', 'settings', 'shaders', 'resourceInventory'):
                require(recipe.get(key) and isinstance(recipe[key], (dict, list)), 'Missing scene input: ' + key)
            require(recipe.get('preset') == 'High' and recipe.get('msaaSamples') == 2, 'Scene recipe is not original High/2x')
            require(recipe == row.get('recipe'), 'Recipe file differs from retained observation')
            require(recipe.get('stage') == recipe.get('conditions', {}).get('sceneId') == identity and
                    recipe.get('stageId') == stage, 'Recipe stage differs from conditions/selected stage')
            binding = recipe.get('resourceInventory', {})
            label = row['label']
            require(binding.get('json') == 'parity-' + label + '-inventory.json' and
                    binding.get('binary') == 'parity-' + label + '-inventory.bin', 'Unbound inventory file locator')
            for key in ('json', 'binary'):
                require(hashlib.sha256((directory / binding[key]).read_bytes()).hexdigest() == binding.get(key + 'Sha256'),
                        'Scene inventory file hash differs: ' + label)
            bindings.append({'label': label, 'inventory': binding, 'conditions': recipe['conditions']})
            if label == 'base-a': baseline = recipe
        return dict(baseline, trialInventoryBindings=bindings)
    except (OSError, KeyError, TypeError) as error:
        raise ValueError('Missing/invalid scene evidence for ' + stage + ': ' + str(error)) from error


def inventory(directory):
    """Validate every retained storage segment; never remove differing fields."""
    path = directory / 'parity-base-a-inventory.json'
    description = json.loads(path.read_text())
    require(isinstance(description.get('nodes'), list) and description['nodes'] and
            isinstance(description.get('resources'), list) and description['resources'], 'Empty/incomplete scene inventory')
    require(description.get('unsupported') == [], 'Inventory contains unsupported inputs')
    require(description.get('binary') == 'parity-base-a-inventory.bin', 'Unexpected inventory binary locator')
    blob = (directory / description['binary']).read_bytes()
    segments = []

    def visit(value):
        if isinstance(value, dict):
            if set(value) == {'offset', 'length', 'sha256'}:
                start, size = value['offset'], value['length']
                require(type(start) is int and type(size) is int and start >= 0 and size >= 0 and start + size <= len(blob), 'Invalid inventory segment')
                require(hashlib.sha256(blob[start:start+size]).hexdigest() == value['sha256'], 'Inventory segment hash differs')
                if size: segments.append((start, start+size))
            else:
                for child in value.values(): visit(child)
        elif isinstance(value, list):
            for child in value: visit(child)

    visit(description)
    cursor = 0
    for start, end in sorted(segments):
        require(start == cursor, 'Inventory has overlapping or unclassified bytes')
        cursor = end
    require(cursor == len(blob), 'Inventory binary has an unclassified tail')
    return description, blob


def compare_captured(reference, candidate, output, stage=None, expected_revision=None):
    """Strict actual scene inputs; fresh unused builders retain their own verdict."""
    output.mkdir(parents=True, exist_ok=False)
    a, b = load(reference), load(candidate)
    require(a['header']['shared'] == b['header']['shared'], 'Incompatible source/engine/recipe')
    require(a['meshSource'] == b['meshSource'] == 'canonical-mac-resource', 'Expected canonical geometry in both packages')
    if expected_revision is not None:
        require(a['header']['shared']['sourceIdentity'] == b['header']['shared']['sourceIdentity'] == expected_revision,
                'Collected source differs from native receipt')
    scene_delta = difference(scene(reference, stage), scene(candidate, stage)) if stage is not None else None
    x, xb = inventory(reference); y, yb = inventory(candidate)
    delta = difference(x, y)
    captured = lambda data: [{key: mesh[key] for key in ('name', 'cachedSha256', 'canonicalSha256')} for mesh in data['capturedMeshes']]
    mesh_delta = difference(captured(a), captured(b))
    equal = delta is None and xb == yb and mesh_delta is None and scene_delta is None
    report = {'claim': 'Exact collected CPU scene/storage inputs only; no GPU/image or release approval',
              'graphicsQualified': False, 'stage': stage, 'exactCapturedInputs': equal, 'firstSceneDifference': scene_delta, 'firstInventoryDifference': delta,
              'firstCapturedMeshDifference': mesh_delta, 'binaryEqual': xb == yb,
              'referenceInventorySha256': hashlib.sha256(xb).hexdigest(),
              'candidateInventorySha256': hashlib.sha256(yb).hexdigest(),
              'referenceProvenance': a['header']['provenance'], 'candidateProvenance': b['header']['provenance']}
    (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    return 0 if equal else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path); parser.add_argument('candidate', type=Path); parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try: raise SystemExit(compare(args.reference,args.candidate,args.output))
    except (ValueError, KeyError, OSError, EOFError) as error: parser.exit(2,str(error)+'\n')
