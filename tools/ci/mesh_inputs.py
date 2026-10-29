#!/usr/bin/env python3
"""Validate complete builder observations and locate input differences; no release approval."""
import argparse
from collections import Counter
import gzip
import hashlib
import json
from pathlib import Path
import struct

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
        require(result['identicalToCapturedMesh'] is True and result['generatedSha256'] == result['cachedSha256'] == packed, 'Observed builder changed captured mesh')
    require(position == len(records)-1 and cursor == len(blob), 'Extra mesh data or missing segment')
    return {'header': records[0]['payload'], 'managed': managed, 'stages': stages}


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


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path); parser.add_argument('candidate', type=Path); parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try: raise SystemExit(compare(args.reference,args.candidate,args.output))
    except (ValueError, KeyError, OSError, EOFError) as error: parser.exit(2,str(error)+'\n')
