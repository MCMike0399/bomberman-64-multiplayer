#!/usr/bin/env python3
"""Compare complete observable replay streams; never a full private-state certificate."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import re
import struct
import sys


class Number(str):
    """Retain JSON numeric spelling, including negative zero; never round state."""


def require(value, message):
    if not value:
        raise ValueError(message)


def unique(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'duplicate JSON key: ' + key)
        result[key] = value
    return result


def parse(line):
    def invalid(value): raise ValueError('nonfinite JSON number: ' + value)
    return json.loads(line, object_pairs_hook=unique, parse_int=Number, parse_float=Number, parse_constant=invalid)


def integer(value):
    require(type(value) is Number and re.fullmatch(r'0|[1-9][0-9]*', value), 'expected nonnegative integer')
    return int(value)


def hash_field(value):
    require(type(value) is str and re.fullmatch('[0-9a-f]{64}', value), 'invalid SHA256')


def load(path):
    path = Path(path); records = []; digest = hashlib.sha256(); decoded = 0
    with path.open('rb') as source:
        file_sha = hashlib.file_digest(source, 'sha256').hexdigest()
    with (gzip.open(path, 'rb') if path.suffix == '.gz' else path.open('rb')) as stream:
        for line in stream:
            decoded += len(line)
            require(len(line) <= 4_000_000 and decoded <= 100_000_000 and len(records) < 10000, 'replay exceeds bounded input size')
            require(line.endswith(b'\n'), 'incomplete replay line')
            record = parse(line)
            require(type(record) is dict and type(record.get('kind')) is str, 'missing record kind')
            require(not records or records[-1]['kind'] != 'complete', 'records follow completion')
            if record['kind'] not in ('header', 'complete'):
                digest.update(line)
            records.append(record)
    require(len(records) >= 4 and records[0]['kind'] == 'header' and records[-1]['kind'] == 'complete', 'missing replay header/completion')
    header, footer = records[0], records[-1]
    require(integer(header.get('schemaVersion')) == 1, 'unsupported replay schema')
    require(type(header.get('shared')) is dict and type(header.get('provenance')) is dict, 'missing replay identities')
    shared = header['shared']; provenance = header['provenance']
    require(type(shared.get('recipeId')) is str and shared['recipeId'], 'missing recipe identity')
    hash_field(shared.get('recipeSha256'))
    require(type(shared.get('engineIdentity')) is str and shared['engineIdentity'], 'missing engine policy identity')
    release = shared.get('release'); require(type(release) is dict, 'missing release identity')
    for field in ('version', 'build', 'token'):
        require(type(release.get(field)) is str and release[field], 'missing release field: ' + field)
    require(integer(release.get('protocol')) > 0, 'missing protocol identity')
    require(type(shared.get('sourceIdentity')) is str and re.fullmatch('[0-9a-f]{40}', shared['sourceIdentity']), 'missing exact source identity')
    for field in ('platform', 'os', 'architecture', 'runtime', 'engine'):
        require(type(provenance.get(field)) is str and provenance[field], 'missing runtime provenance: ' + field)
    for field in ('authorityAssemblySha256', 'entryAssemblySha256'):
        hash_field(provenance.get(field))
    require(integer(footer.get('dataRecords')) == len(records) - 2, 'record count differs from completion')
    require(footer.get('dataSha256') == digest.hexdigest(), 'replay data digest mismatch')
    cases = shared.get('cases'); require(type(cases) is list and 0 < len(cases) <= 4, 'missing bounded case plan')
    position = 1; total_ticks = 0; names = set()
    for case in cases:
        require(type(case) is dict and type(case.get('id')) is str and case['id'] not in names, 'invalid/duplicate case')
        names.add(case['id']); planned = integer(case.get('lastTick'))
        require(planned <= 900 and position < len(records) - 1, 'missing/oversized replay case')
        start = records[position]; position += 1
        require(start.get('kind') == 'case-start' and start.get('id') == case['id'], 'case start/order mismatch')
        initial = integer(start.get('initialTick'))
        require(integer(start.get('lastTick')) == planned and initial < planned, 'case tick plan mismatch')
        require(type(start.get('stage')) is dict and type(start.get('snapshot')) is dict, 'missing initial stage/snapshot')
        require(start['stage'].get('t') == 'stage' and start['snapshot'].get('t') == 'state', 'invalid initial observable messages')
        last_snapshot = integer(start['snapshot'].get('k')); snapshots = 0
        require(last_snapshot <= initial, 'initial snapshot from the future')
        humans = integer(start.get('options', {}).get('HumanPlayers')); require(0 < humans <= 8, 'invalid human roster')
        for tick in range(initial + 1, planned + 1):
            require(position < len(records) - 1, 'missing authority tick')
            record = records[position]; position += 1
            require(record.get('kind') == 'tick' and record.get('id') == case['id'] and integer(record.get('tick')) == tick, 'missing/duplicate/reordered authority tick')
            require(type(record.get('inputs')) is list and record['inputs'] and type(record.get('messages')) is list, 'missing ordered input/message stream')
            require(type(record.get('canary')) is dict, 'missing separate math/RNG canary')
            require({integer(item.get('slot')) for item in record['inputs']} == set(range(humans)), 'input roster coverage differs')
            for message in record['messages']:
                require(type(message) is dict and type(message.get('t')) is str and integer(message.get('k')) <= tick, 'invalid/future observable message')
                if message['t'] == 'state':
                    last_snapshot = integer(message['k']); snapshots += 1
            require(integer(record.get('latestSnapshotTick')) == last_snapshot, 'missing or inconsistent snapshot emission')
            total_ticks += 1
        require(position < len(records) - 1, 'missing case completion')
        end = records[position]; position += 1
        require(end.get('kind') == 'case-end' and end.get('id') == case['id'] and integer(end.get('lastTick')) == planned, 'case completion mismatch')
        require(snapshots > 0 and integer(end.get('snapshots')) == snapshots, 'case snapshot count differs or is empty')
    require(position == len(records) - 1 and integer(footer.get('caseCount')) == len(cases) and integer(footer.get('totalTicks')) == total_ticks, 'incomplete or extra replay coverage')
    return {'path': str(path), 'fileSha256': file_sha, 'header': header, 'records': records[1:-1]}


def describe(value):
    if type(value) is Number:
        result = {'rawNumber': str(value)}
        for width, code in ((32, '!f'), (64, '!d')):
            try: result['asFloat' + str(width) + 'Bits'] = struct.pack(code, float(value)).hex()
            except (OverflowError, ValueError): result['asFloat' + str(width) + 'Bits'] = None
        return result
    if type(value) in (str, bool) or value is None:
        return value
    return {'type': type(value).__name__, 'length': len(value)}


def first_difference(left, right, path='$'):
    if type(left) is not type(right):
        return {'path': path, 'expected': describe(left), 'actual': describe(right), 'reason': 'type'}
    if type(left) is dict:
        if left.keys() != right.keys():
            return {'path': path, 'expectedKeys': sorted(left), 'actualKeys': sorted(right), 'reason': 'fields'}
        for key in sorted(left):
            difference = first_difference(left[key], right[key], path + '.' + key)
            if difference: return difference
    elif type(left) is list:
        for index, (a, b) in enumerate(zip(left, right)):
            difference = first_difference(a, b, f'{path}[{index}]')
            if difference: return difference
        if len(left) != len(right):
            return {'path': path, 'expectedLength': len(left), 'actualLength': len(right), 'reason': 'length/order'}
    elif left != right:
        return {'path': path, 'expected': describe(left), 'actual': describe(right), 'reason': 'number/spelling' if type(left) is Number else 'value'}
    return None


def compare(reference, candidate, output):
    output = Path(output); output.mkdir(parents=True, exist_ok=False)
    report = {'schemaVersion': 1, 'scope': 'Observable messages and independent math/RNG canaries only; not hidden-state equivalence or release approval.',
              'compatibleInputs': False, 'observableEqual': None, 'canaryEqual': None, 'exactEqual': None,
              'firstObservableDifference': None, 'firstCanaryDifference': None, 'errors': [], 'inputs': {}}
    try:
        streams = [load(path) for path in (reference, candidate)]
        for label, stream in zip(('reference', 'candidate'), streams):
            report['inputs'][label] = {key: stream[key] for key in ('path', 'fileSha256')}
            report['inputs'][label]['provenance'] = stream['header']['provenance']
        left, right = streams
        mismatch = first_difference(left['header']['shared'], right['header']['shared'], '$.shared')
        require(mismatch is None, 'incompatible shared recipe/source/release: ' + json.dumps(mismatch))
        require(len(left['records']) == len(right['records']), 'different replay record coverage')
        report['compatibleInputs'] = True
        report['permittedProvenanceDifferences'] = [key for key in sorted(left['header']['provenance'].keys() | right['header']['provenance'].keys())
            if left['header']['provenance'].get(key) != right['header']['provenance'].get(key)]
        for index, (a, b) in enumerate(zip(left['records'], right['records'])):
            context = {'record': index, 'case': a.get('id'), 'tick': str(a.get('tick')) if 'tick' in a else None}
            observed_a = {key: value for key, value in a.items() if key != 'canary'}
            observed_b = {key: value for key, value in b.items() if key != 'canary'}
            if report['firstObservableDifference'] is None:
                difference = first_difference(observed_a, observed_b)
                if difference: report['firstObservableDifference'] = dict(context, **difference)
            if report['firstCanaryDifference'] is None:
                difference = first_difference(a.get('canary'), b.get('canary'), '$.canary')
                if difference: report['firstCanaryDifference'] = dict(context, **difference)
        report['comparedDataRecords'] = len(left['records'])
        report['observableEqual'] = report['firstObservableDifference'] is None
        report['canaryEqual'] = report['firstCanaryDifference'] is None
        report['exactEqual'] = report['observableEqual'] and report['canaryEqual']
    except (OSError, ValueError, EOFError, TypeError, KeyError) as error:
        report['errors'].append(str(error))
    code = 2 if report['errors'] else (0 if report['exactEqual'] else 1)
    report['exitCode'] = code
    (output / 'report.json').write_text(json.dumps(report, indent=2, sort_keys=True) + '\n')
    return report, code


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path); parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(argv)
    try: report, code = compare(**vars(args))
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr); return 2
    print(json.dumps({'report': str(args.output / 'report.json'), 'observableEqual': report['observableEqual'], 'canaryEqual': report['canaryEqual'], 'exitCode': code}))
    return code


if __name__ == '__main__':
    sys.exit(main())
