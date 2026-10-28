#!/usr/bin/env python3
"""Record raw/resolved depth in a tiny pinned-engine scene; never product qualification."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[2]
ENGINE_HASH = 'ed1daf0bf001b61586d9930840f2f1394092c079'


def inspect(directory):
    metadata = json.loads((directory / 'depth-resources.json').read_text())
    data = (directory / 'depth-samples.bin').read_bytes()
    engine = metadata.get('engine', {})
    if engine.get('hash') != ENGINE_HASH or not engine.get('string', '').startswith('4.7.2-stable'):
        raise ValueError('Depth diagnostic engine differs from the pinned release')
    if metadata.get('diagnosticOnly') is not True or metadata.get('width') != 384 or metadata.get('height') != 256:
        raise ValueError('Unexpected depth probe dimensions or evidence scope')
    if len(data) != 384 * 256 * 16 or metadata.get('bytes') != len(data):
        raise ValueError('Depth buffer is truncated or has an unexpected layout')
    minimum = [1., 1., 1.]; maximum = [0., 0., 0.]; nonzero = [0, 0, 0]
    for sample0, sample1, resolved, sentinel in struct.iter_unpack('<4f', data):
        if sentinel != 1 or not all(math.isfinite(v) and 0 <= v <= 1 for v in (sample0, sample1, resolved)):
            raise ValueError('Depth compute output is incomplete or invalid')
        for i, value in enumerate((sample0, sample1, resolved)):
            minimum[i] = min(minimum[i], value); maximum[i] = max(maximum[i], value); nonzero[i] += value != 0
    result = []
    for plane in metadata.get('planes', []):
        x, y = plane['pixel']; sample = struct.unpack_from('<4f', data, (y * 384 + x) * 16)[:3]
        expected = (100 - plane['worldDepth']) / (100 - .1)
        result.append({'pixel': [x, y], 'worldDepth': plane['worldDepth'], 'expectedReverseOrthoDepth': expected,
                       'rawSample0': sample[0], 'rawSample1': sample[1], 'resolved': sample[2],
                       'resolvedMinusSample0': sample[2] - sample[0],
                       'reconstructedWorldDepth': 100 - sample[2] * (100 - .1)})
    return {'metadata': metadata, 'samplesSha256': hashlib.sha256(data).hexdigest(), 'planeCenters': result,
            'bufferSummary': {'minimumRaw0Raw1Resolved': minimum, 'maximumRaw0Raw1Resolved': maximum, 'nonzeroRaw0Raw1Resolved': nonzero}}


def run(godot, output, driver):
    output.mkdir(parents=True, exist_ok=False)
    with godot.open('rb') as binary:
        engine_sha = hashlib.file_digest(binary, 'sha256').hexdigest()
    report = {'diagnosticOnly': True, 'claim': 'Pinned-editor resource localization; not the shipped game, pixel parity, or release approval.',
              'collected': False, 'engineExecutableSha256': engine_sha, 'cases': []}
    try:
        for label, args in [('msaa-2x', []), ('msaa-off-control', ['--no-msaa']), ('msaa-2x-stage-history', ['--stage-history']), ('msaa-2x-clear-attachments', ['--clear-attachments'])]:
            case = output / label; case.mkdir()
            command = [str(godot), '--path', str(ROOT / 'tools/qa/depth_probe'), '--script', 'probe.gd', '--windowed',
                       '--rendering-method', 'forward_plus', '--rendering-driver', driver, '--audio-driver', 'Dummy', '--', str(case), *args]
            with (case / 'runtime.log').open('w') as log:
                process = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=120)
            raw = (case / 'runtime.log').read_text(errors='replace')
            if process.returncode or re.search(r'(^|\n)(ERROR:|SCRIPT ERROR:|FATAL:)', raw):
                raise RuntimeError('Depth probe did not complete cleanly: ' + label)
            evidence = inspect(case)
            if evidence['metadata']['rawSamples'] != (0 if '--no-msaa' in args else 1):
                raise ValueError('Depth probe used an unexpected sample count')
            if '--stage-history' in args:
                evidence['stageHistory'] = {stage: inspect(case / stage) for stage in ('pre-opaque', 'post-opaque')}
            if '--clear-attachments' in args:
                evidence['customClears'] = {kind: inspect(case / ('clear-' + kind)) for kind in ('depth', 'color')}
            report['cases'].append({'name': label, **evidence})
        report['collected'] = True
    except (OSError, ValueError, RuntimeError, KeyError, subprocess.SubprocessError) as error:
        report['error'] = str(error)
    (output / 'depth-probe.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report))
    return 0 if report['collected'] else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--driver', choices=('metal', 'vulkan'), required=True)
    options = parser.parse_args()
    raise SystemExit(run(options.godot.resolve(), options.output.resolve(), options.driver))
