#!/usr/bin/env python3
"""Offline exact RGBA8 comparison; see docs/visual-parity-comparator.md."""
import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import re
import struct
import sys
import warnings

try:
    from PIL import Image, ImageChops, __version__ as PILLOW_VERSION
except ImportError:
    Image = None
    PILLOW_VERSION = None


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_json(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, f'duplicate JSON key: {key}')
            result[key] = value
        return result

    def invalid_constant(value):
        raise ValueError(f'non-finite JSON constant: {value}')

    def finite_float(value):
        result = float(value)
        require(math.isfinite(result), f'non-finite JSON number: {value}')
        return result

    return json.loads(Path(path).read_text(encoding='utf-8'), object_pairs_hook=unique,
                      parse_constant=invalid_constant, parse_float=finite_float)


def png_metadata(data):
    """Inspect headers/chunk identities only. Pillow validates and decodes PNG."""
    require(data[:8] == b'\x89PNG\r\n\x1a\n', 'input must be PNG')
    require(len(data) >= 33 and data[8:16] == b'\0\0\0\rIHDR', 'missing/invalid PNG IHDR')
    width, height, depth, color, compression, filtering, interlace = struct.unpack('>IIBBBBB', data[16:29])
    chunks, color_chunks, offset = [], {}, 8
    # Store only small color descriptors, never compressed image/profile payloads.
    sizes = {b'sRGB': 1, b'gAMA': 4, b'cHRM': 32}
    while offset < len(data):
        require(offset + 12 <= len(data), 'truncated PNG chunk')
        size, kind = struct.unpack('>I4s', data[offset:offset + 8])
        require(offset + size + 12 <= len(data), 'truncated PNG chunk payload')
        require(kind[:1].islower() or kind in (b'IHDR', b'PLTE', b'IDAT', b'IEND'),
                f'unknown critical PNG chunk: {kind!r}')
        name = kind.decode('ascii')
        if kind in sizes or kind == b'iCCP':
            require(name not in chunks, f'duplicate PNG color chunk: {name}')
        chunks.append(name)
        if kind in sizes:
            require(size == sizes[kind], f'invalid {name} length')
            value = data[offset + 8:offset + 8 + size]
            color_chunks[name] = list(struct.unpack('>' + ('B' if kind == b'sRGB' else 'I' * (size // 4)), value))
        offset += size + 12
        if kind == b'IEND':
            require(offset == len(data), 'trailing PNG data')
            break
    require(chunks and chunks[-1] == 'IEND', 'missing PNG IEND')
    return {'width': width, 'height': height, 'bitDepth': depth, 'colorType': color,
            'compressionMethod': compression, 'filterMethod': filtering, 'interlace': interlace, 'chunks': chunks,
            'color': {'sRGBIntent': color_chunks.get('sRGB', [None])[0],
                      'gammaTimes100000': color_chunks.get('gAMA', [None])[0],
                      'chromaticitiesTimes100000': color_chunks.get('cHRM'), 'iccSha256': None}}, chunks


def decode(path, record):
    data = Path(path).read_bytes()
    record.update(fileSha256=sha256(data), png=None, decodedSha256=None)
    metadata, chunks = png_metadata(data)
    record['png'] = metadata
    require(metadata['bitDepth'] == 8 and metadata['colorType'] in (2, 6),
            'only 8-bit RGB/RGBA PNG (color types 2/6) is supported')
    with warnings.catch_warnings():
        warnings.simplefilter('error', Image.DecompressionBombWarning)
        with Image.open(io.BytesIO(data)) as source:
            source.verify()
        with Image.open(io.BytesIO(data)) as source:
            require(source.format == 'PNG' and source.mode in ('RGB', 'RGBA'), 'unsupported Pillow format/mode')
            source.load()
            record['mode'] = source.mode
            profile = source.info.get('icc_profile')
            if profile is not None:
                metadata['color']['iccSha256'] = sha256(profile)
            # These require color/alpha/orientation semantics outside this tool's scope.
            unsupported = sorted(set(chunks) & {'iCCP', 'cICP', 'mDCV', 'cLLI', 'sBIT', 'tRNS', 'eXIf', 'acTL'})
            require(not unsupported, 'unsupported/ambiguous PNG metadata: ' + ', '.join(unsupported))
            color = metadata['color']
            require(color['sRGBIntent'] is None or color['sRGBIntent'] in range(4), 'invalid sRGB intent')
            require(color['gammaTimes100000'] is None or color['gammaTimes100000'] > 0, 'invalid gamma')
            require(color['chromaticitiesTimes100000'] in
                    (None, [31270, 32900, 64000, 33000, 30000, 60000, 15000, 6000]),
                    'unsupported cHRM (only sRGB primaries/white point supported)')
            if color['sRGBIntent'] is not None:
                require(color['gammaTimes100000'] in (None, 45455), 'conflicting sRGB/gAMA')
            image = source.convert('RGBA') if source.mode == 'RGB' else source.copy()
            record['normalization'] = 'RGB -> RGBA with alpha=255' if source.mode == 'RGB' else 'none (RGBA)'
    record['decodedSha256'] = sha256(image.tobytes())
    return image


def statistics(delta, origin=(0, 0)):
    channels, maximum = {}, Image.new('L', delta.size)
    for name, band in zip('RGBA', delta.split()):
        histogram = band.histogram()
        channels[name] = {'max': max(i for i, count in enumerate(histogram) if count),
                          'mean': sum(i * count for i, count in enumerate(histogram)) / (delta.width * delta.height),
                          'histogram': {str(i): count for i, count in enumerate(histogram) if count}}
        maximum = ImageChops.lighter(maximum, band)
    area = delta.width * delta.height
    count = area - maximum.histogram()[0]
    box = maximum.getbbox()
    if box:
        box = [box[0] + origin[0], box[1] + origin[1], box[2] + origin[0], box[3] + origin[1]]
    return {'area': area, 'exactEqual': count == 0, 'differingPixels': count,
            'differingFraction': count / area, 'boundingBox': box, 'channels': channels}, maximum


def validate_rois(value, size):
    require(type(value) is list, 'ROIs must be a list')
    names = set()
    for roi in value:
        require(type(roi) is dict and set(roi) == {'name', 'x', 'y', 'width', 'height'}, 'invalid ROI fields')
        name = roi['name']
        require(type(name) is str and bool(name.strip()) and name not in names, 'empty/duplicate ROI name')
        names.add(name)
        require(all(type(roi[k]) is int for k in ('x', 'y', 'width', 'height')), 'ROI coordinates must be integers')
        x, y, w, h = (roi[k] for k in ('x', 'y', 'width', 'height'))
        require(x >= 0 and y >= 0 and w > 0 and h > 0 and x + w <= size[0] and y + h <= size[1],
                f'ROI out of bounds: {name}')
    return value


def fields(value, specification, label):
    require(type(value) is dict and set(value) == set(specification), f'{label}: missing/unknown fields')
    for key, kind in specification.items():
        item = value[key]
        if kind == 'hash':
            valid = type(item) is str and re.fullmatch('[0-9a-f]{64}', item)
        elif kind == 'number':
            valid = (type(item) is int or (type(item) is float and math.isfinite(item))) and item >= 0
        else:
            valid = type(item) is kind
            if kind is str:
                valid = valid and bool(item.strip())
            elif kind is int:
                valid = valid and item >= 0
        require(valid, f'{label}.{key}: invalid value/type')


def validate_manifest(manifest, record):
    require(type(manifest) is dict, 'manifest must be an object')
    require(type(manifest.get('schemaVersion')) is int and manifest['schemaVersion'] == 1, 'unknown manifest schemaVersion')
    fields({k: v for k, v in manifest.items() if k != 'rois'},
           {'schemaVersion': int, 'pngSha256': 'hash', 'conditions': dict, 'provenance': dict}, 'manifest')
    require(manifest['pngSha256'] == record['fileSha256'], 'manifest PNG hash mismatch (stale capture)')
    c = manifest['conditions']
    fields(c, {'sceneId': str, 'assetsSha256': 'hash', 'stateId': str, 'tick': int,
               'width': int, 'height': int, 'render': dict, 'cameraSha256': 'hash',
               'shaderTimeSeconds': 'number', 'animationTimeSeconds': 'number',
               'temporal': dict, 'capture': dict}, 'conditions')
    fields(c['render'], {'preset': str, 'msaaSamples': int, 'dof': bool, 'settingsSha256': 'hash'}, 'render')
    require(c['render']['msaaSamples'] in (0, 2, 4, 8), 'render.msaaSamples: expected 0, 2, 4 or 8')
    fields(c['temporal'], {'historyId': str, 'warmupFrames': int, 'jitterId': str}, 'temporal')
    fields(c['capture'], {'surface': str, 'colorContract': str}, 'capture')
    fields(manifest['provenance'], {'platform': str, 'api': str, 'device': str, 'driver': str,
                                  'engineIdentity': str, 'templateSha256': 'hash', 'packageSha256': 'hash',
                                  'sourceIdentity': str, 'runtimeIdentity': str}, 'provenance')
    require((c['width'], c['height']) == (record['png']['width'], record['png']['height']), 'manifest dimensions mismatch')
    validate_rois(manifest.get('rois', []), (c['width'], c['height']))


def different_fields(a, b, prefix):
    result = []
    for key in sorted(a):
        path = prefix + '.' + key
        if type(a[key]) is dict:
            result.extend(different_fields(a[key], b[key], path))
        elif a[key] != b[key]:
            result.append(path)
    return result


def compare(reference, candidate, output, reference_manifest=None, candidate_manifest=None,
            rois=None, diagnostic_only=False):
    """Write a fresh report directory; return (JSON report, documented exit code)."""
    output = Path(output)
    output.mkdir(parents=True, exist_ok=False)  # Never overwrite inputs or stale receipts.
    report = {'schemaVersion': 1, 'pillowVersion': PILLOW_VERSION,
              'mode': 'diagnostic-only' if diagnostic_only else 'manifest-checked',
              'claim': 'Offline channel comparison only; not release qualification or physical-display parity.',
              'metadataCompatibility': 'unavailable', 'compatibleInputs': False, 'evidenceEligible': False,
              'encodingCompatible': None, 'exactEqual': None, 'inputs': {}, 'errors': [], 'rois': []}
    errors, images = report['errors'], []
    for label, path in (('reference', reference), ('candidate', candidate)):
        record = {'path': str(path)}
        report['inputs'][label] = record
        try:
            require(Image is not None, 'Pillow is required; install it in a local venv (see documentation)')
            images.append(decode(path, record))
        except (OSError, ValueError, SyntaxError, struct.error, Image.DecompressionBombError if Image else ValueError,
                Image.DecompressionBombWarning if Image else ValueError) as error:
            errors.append(f'{label}: {error}')
    if len(images) == 2:
        a, b = images
        report['encodingCompatible'] = report['inputs']['reference']['png']['color'] == report['inputs']['candidate']['png']['color']
        if a.size != b.size:
            errors.append('dimensions differ; no alignment, resizing or cropping performed')
        else:
            # Whole image always comes first, before manifests or ROI breakdowns.
            delta = ImageChops.difference(a, b)
            report['wholeImage'], maximum = statistics(delta)
            report['exactEqual'] = report['wholeImage']['exactEqual']
            filename = 'amplified-diagnostic-difference.png'
            maximum.point([min(255, i * 16) for i in range(256)]).convert('RGB').save(output / filename, compress_level=9)
            report['diagnosticImage'] = {'file': filename, 'formula': 'grayscale min(255, 16 * max(abs(RGBA delta)))',
                                         'purpose': 'Amplified diagnostic only; never a replacement reference.'}
            try:
                roi_list = []
                if diagnostic_only:
                    require(reference_manifest is None and candidate_manifest is None,
                            'diagnostic-only does not accept manifests; omit them or use default mode')
                else:
                    require(reference_manifest is not None and candidate_manifest is not None, 'both manifests are required')
                    manifests = [read_json(path) for path in (reference_manifest, candidate_manifest)]
                    for label, manifest in zip(('reference', 'candidate'), manifests):
                        report['inputs'][label]['manifest'] = manifest
                    for label, manifest in zip(('reference', 'candidate'), manifests):
                        validate_manifest(manifest, report['inputs'][label])
                    left, right = manifests
                    mismatches = different_fields(left['conditions'], right['conditions'], 'conditions')
                    for field in ('sourceIdentity', 'engineIdentity'):
                        if left['provenance'][field] != right['provenance'][field]:
                            mismatches.append('provenance.' + field)
                    report['permittedProvenanceDifferences'] = [field for field in different_fields(
                        left['provenance'], right['provenance'], 'provenance')
                        if field not in ('provenance.sourceIdentity', 'provenance.engineIdentity')]
                    errors.extend('manifest mismatch: ' + field for field in mismatches)
                    roi_list = left.get('rois', [])
                    require(roi_list == right.get('rois', []), 'manifest ROI definitions differ')
                require(rois is None or not roi_list, 'use either manifest ROIs or --rois, not both')
                roi_list = read_json(rois) if rois is not None else roi_list
                for roi in validate_rois(roi_list, a.size):
                    x, y, w, h = (roi[k] for k in ('x', 'y', 'width', 'height'))
                    stats, _ = statistics(delta.crop((x, y, x + w, y + h)), (x, y))
                    report['rois'].append({'rectangle': roi, **stats})
            except (OSError, ValueError) as error:
                errors.append(str(error))
    if report['encodingCompatible'] is False:
        errors.append('PNG color encoding differs (decoded channel equality is separate)')
    if not diagnostic_only:
        report['compatibleInputs'] = not errors
        report['evidenceEligible'] = not errors
        report['metadataCompatibility'] = 'compatible' if not errors else 'incompatible'
    code = 2 if errors else (3 if diagnostic_only else (0 if report['exactEqual'] else 1))
    report['exitCode'] = code
    (output / 'report.json').write_text(json.dumps(report, indent=2, sort_keys=True, allow_nan=False) + '\n', encoding='utf-8')
    return report, code


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, required=True, help='new directory (must not already exist)')
    parser.add_argument('--reference-manifest', type=Path)
    parser.add_argument('--candidate-manifest', type=Path)
    parser.add_argument('--rois', type=Path, help='JSON array of named pixel rectangles')
    parser.add_argument('--diagnostic-only', action='store_true', help='no manifest eligibility; completed diagnostics exit 3')
    args = parser.parse_args(argv)
    try:
        report, code = compare(**vars(args))
    except (OSError, ValueError) as error:
        print(f'Invalid comparison: {error}', file=sys.stderr)
        return 2
    print(json.dumps({'report': str(args.output / 'report.json'), 'exactEqual': report['exactEqual'],
                      'evidenceEligible': report['evidenceEligible'], 'exitCode': code}))
    return code


if __name__ == '__main__':
    sys.exit(main())
