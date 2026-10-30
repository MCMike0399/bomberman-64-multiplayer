#!/usr/bin/env python3
"""Build the Windows/Linux visual sign-off pack against the measured Mac-to-Mac envelope.

Two subcommands:

  manifest  Scan campaign lane directories and write a small JSON input manifest.
  pack      Read that manifest and write a fresh, self-contained review pack.

The pack is evidence for a human reviewer. It never chooses a threshold, never
masks, aligns, resizes or blurs pixels, and never emits a pass/fail field. The
pattern class of every heatmap is a documented, deterministic, ADVISORY heuristic;
the heatmap itself is always shown so the reviewer can override it.

Exact PNG validation/decoding and per-channel statistics are reused from
compare_visual_parity.py (see docs/visual-parity-comparator.md).
"""
import argparse
import hashlib
import html
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys

TOOL_VERSION = '1'
HERE = Path(__file__).resolve().parent
_SPEC = importlib.util.spec_from_file_location('compare_visual_parity', HERE / 'compare_visual_parity.py')
pixels = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(pixels)
Image = pixels.Image
if Image is not None:
    from PIL import ImageChops, ImageDraw, ImageFilter, ImageFont

# Keep in step with tools/ci/mesh_inputs.STAGES (a unit test checks this).
STAGES = ('sunken-temple', 'rock-garden', 'up-and-down', 'top-rules',
          'blizzard-battle', 'greedy-trap', 'circuit-garden')
# One PNG per trial per run directory, as written by the visual-parity playtest.
TRIALS = ('base-a-0', 'base-a-1', 'base-b-0', 'dof-off-0', 'reflection-off-0')
ROLES = ('mac-reference', 'hosted-envelope', 'm5-envelope', 'windows-vulkan', 'linux-vulkan', 'windows-d3d12')
ROLE_LABELS = {'mac-reference': 'Mac reference', 'hosted-envelope': 'Hosted Mac (envelope)',
               'm5-envelope': 'M5 Mac (envelope)', 'windows-vulkan': 'Windows Vulkan',
               'linux-vulkan': 'Linux Vulkan', 'windows-d3d12': 'Windows forced D3D12'}
# (pair id, candidate role, reference role, section)
PAIRS = (('envelope', 'hosted-envelope', 'm5-envelope', 'primary'),
         ('windows-vulkan-vs-mac', 'windows-vulkan', 'mac-reference', 'primary'),
         ('linux-vulkan-vs-mac', 'linux-vulkan', 'mac-reference', 'primary'),
         ('windows-d3d12-vs-mac', 'windows-d3d12', 'mac-reference', 'd3d12'),
         ('windows-d3d12-vs-windows-vulkan', 'windows-d3d12', 'windows-vulkan', 'd3d12'))
PAIR_COLORS = {'envelope': '#1baf7a', 'windows-vulkan-vs-mac': '#2a78d6', 'linux-vulkan-vs-mac': '#eb6834',
               'windows-d3d12-vs-mac': '#eda100', 'windows-d3d12-vs-windows-vulkan': '#e87ba4'}
PAIR_LABELS = {'envelope': 'Hosted Mac vs M5 (envelope)', 'windows-vulkan-vs-mac': 'Windows Vulkan vs Mac',
               'linux-vulkan-vs-mac': 'Linux Vulkan vs Mac', 'windows-d3d12-vs-mac': 'Windows D3D12 vs Mac',
               'windows-d3d12-vs-windows-vulkan': 'Windows D3D12 vs Windows Vulkan'}
CONTROLS = {'dof': 'dof-off-0', 'mirror': 'reflection-off-0'}

# Advisory classifier parameters. They describe heatmap SHAPE only and are never
# acceptance thresholds; changing them changes a label, not any measurement.
REGION_MIN_PIXELS = 64          # a "region" component has at least this many pixels
REGION_MIN_SHARE = 0.5          # ...holds at least half of all changed pixels
REGION_MIN_FILL = 0.5           # ...and fills at least half of its bounding box
REGION_MAX_FRACTION = 0.5       # region rule applies only when < 50% of the image changed
SHIFT_MIN_FRACTION = 0.98       # global shift: nearly every pixel changed...
SHIFT_MAX_SPREAD = 0.5          # ...with (p90 - p10) <= 0.5 * p50
GRID_PERIODS = (8, 16, 32)
GRID_MIN_RATIO = 2.0            # block-boundary gradient energy vs elsewhere
SPARSE_MAX_FRACTION = 0.01      # sparse: < 1% of pixels changed
EDGE_TOP_SHARE = 0.10           # "edge" = top 10% reference-gradient pixels (rank based)
EDGE_MIN_ENRICHMENT = 1.5       # changed pixels sit on edges >= 1.5x more than chance
SPECKLE_MAX_COMPONENT = 16


def sha256_file(path):
    with open(path, 'rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_json(path):
    return pixels.read_json(path)


def write_json(path, value):
    Path(path).write_text(json.dumps(value, indent=2, sort_keys=True, allow_nan=False) + '\n', encoding='utf-8')


# ---------------------------------------------------------------- manifest ---

def _lane_identity(lane):
    native_path = lane / 'visual-parity-native.json'
    require(native_path.is_file(), f'lane has no visual-parity-native.json: {lane}')
    native = read_json(native_path)
    runs = native.get('runs') or []
    require(runs and isinstance(runs[0].get('device'), dict), f'lane native report has no device: {native_path}')
    device = runs[0]['device']
    return native, {'target': native.get('target'), 'revision': native.get('revision'),
                    'adapter': device.get('adapter'), 'api': device.get('driver'),
                    'deviceType': device.get('deviceType'), 'engine': device.get('engine')}


def _stage_root(lane, api):
    """all-stages lives inside the lane (Mac reference) or beside it (GPU campaign core)."""
    for candidate in (lane / 'all-stages', lane.parent / 'all-stages'):
        report_path = candidate / 'stage-captures.json'
        if report_path.is_file():
            report = read_json(report_path)
            if report.get('driver') == api:
                return candidate, report
    return None, None


def _recorded_stage_id(run_dir):
    """Only recipe.stageId names the level; recipe.stage was mislabelled before 2026-10-30."""
    recipe = run_dir / 'parity-base-a-recipe.json'
    if not recipe.is_file():
        return None
    with recipe.open('rb') as stream:  # recipes can be tens of MB; the C parser keeps this fast
        return json.load(stream).get('stageId')


def _scan_run(run_dir, stage, role, run_index, identity, captures, gaps, extras):
    if not run_dir.is_dir():
        gaps.extend({'stage': stage, 'trial': t, 'role': role, 'reason': f'run directory missing: {run_dir}'}
                    for t in TRIALS)
        return
    for png in sorted(run_dir.glob('parity-*.png')):
        if png.stem[len('parity-'):] not in TRIALS:
            extras.append({'stage': stage, 'role': role, 'path': str(png), 'reason': 'unexpected capture name'})
    stage_id = _recorded_stage_id(run_dir)
    require(stage_id in (None, stage), f'recipe stageId {stage_id!r} differs from expected {stage!r}: {run_dir}')
    base_manifest = run_dir / 'parity-base-a-0.manifest.json'
    for trial in TRIALS:
        png = run_dir / f'parity-{trial}.png'
        if not png.is_file():
            gaps.append({'stage': stage, 'trial': trial, 'role': role, 'reason': f'capture missing: {png}'})
            continue
        digest = sha256_file(png)
        base = trial.rsplit('-', 1)[0]
        conditions_path = run_dir / f'parity-{base}-conditions.json'
        manifest_path = png.with_name(png.stem + '.manifest.json')
        provenance, provenance_from = None, None
        for candidate in (manifest_path, base_manifest):
            if candidate.is_file():
                data = read_json(candidate)
                if candidate == manifest_path:
                    require(data.get('pngSha256') == digest, f'stale capture manifest (PNG hash differs): {candidate}')
                provenance, provenance_from = data.get('provenance'), str(candidate)
                break
        if provenance is None:
            provenance = {'sourceIdentity': 'git:' + str(identity['revision']), 'engineIdentity': identity['engine'],
                          'api': identity['api'], 'platform': identity['target']}
            provenance_from = 'lane visual-parity-native.json (no capture manifest)'
        captures.append({
            'stage': stage, 'trial': trial, 'role': role, 'run': run_index, 'path': str(png), 'sha256': digest,
            'conditions': read_json(conditions_path) if conditions_path.is_file() else None,
            'conditionsPath': str(conditions_path) if conditions_path.is_file() else None,
            'recipeStageId': stage_id,
            'rois': str(run_dir / 'control-rois.json') if (run_dir / 'control-rois.json').is_file() else None,
            'os': provenance.get('platform') or identity['target'],
            'adapter': identity['adapter'], 'deviceType': identity['deviceType'],
            'api': identity['api'] or provenance.get('api'),
            'driver': provenance.get('driver'),
            'sourceIdentity': provenance.get('sourceIdentity'),
            'engineIdentity': provenance.get('engineIdentity'),
            'packageSha256': provenance.get('packageSha256'),
            'provenanceFrom': provenance_from})


def build_manifest(lanes, run_index=0, campaign=None):
    """lanes: {role: directory}. Returns the manifest dict (paths absolute)."""
    for role in lanes:
        require(role in ROLES, f'unknown role: {role}')
    captures, gaps, extras, lane_rows, controls = [], [], [], {}, []
    for role, lane in lanes.items():
        lane = Path(lane).resolve()
        native, identity = _lane_identity(lane)
        core_stage = native.get('stage') or 'up-and-down'
        require(core_stage in STAGES, f'unknown core stage {core_stage!r} in {lane}')
        # Collector-recorded repeat/control receipts, renamed so this pack carries no verdict fields.
        row = {'directory': str(lane), **identity, 'coreStage': core_stage,
               'collectorComparisons': [{'name': c.get('name'), 'collectorExitCode': c.get('exitCode'),
                                         'collectorEffectShown': c.get('passed')} for c in native.get('comparisons', [])]}
        _scan_run(lane / f'run-{run_index}', core_stage, role, run_index, identity, captures, gaps, extras)
        recorded = {c['name']: c.get('passed') for c in native.get('comparisons', []) if 'passed' in c}
        for name in CONTROLS:
            key = f'run-{run_index}/control-{name}'
            controls.append({'stage': core_stage, 'role': role, 'control': name,
                             'collectorRecorded': recorded.get(key), 'collectorReport': str(lane / key / 'report.json')
                             if (lane / key / 'report.json').is_file() else None})
        stage_root, stage_report = _stage_root(lane, identity['api'])
        row['allStages'] = str(stage_root) if stage_root else None
        covered = {core_stage}
        if stage_root:
            row['allStagesCollectionComplete'] = stage_report.get('collectionComplete')
            row['allStagesErrors'] = stage_report.get('errors', [])
            for child in sorted(p for p in stage_root.iterdir() if p.is_dir()):
                if child.name not in STAGES:
                    extras.append({'stage': child.name, 'role': role, 'path': str(child), 'reason': 'unknown stage directory'})
                    continue
                if child.name == core_stage:
                    extras.append({'stage': child.name, 'role': role, 'path': str(child),
                                   'reason': 'stage already supplied by the core lane; not used'})
                    continue
                covered.add(child.name)
                device = next((r.get('device') or {} for r in stage_report.get('runs', [])
                               if r.get('stage') == child.name and r.get('index') == run_index), {})
                stage_identity = dict(identity, adapter=device.get('adapter') or identity['adapter'],
                                      deviceType=device.get('deviceType') or identity['deviceType'],
                                      api=device.get('driver') or identity['api'])
                _scan_run(child / f'run-{run_index}', child.name, role, run_index, stage_identity, captures, gaps, extras)
        for stage in STAGES:
            if stage not in covered:
                reason = 'no all-stages directory for this lane/API' if not stage_root else 'stage directory missing'
                gaps.extend({'stage': stage, 'trial': t, 'role': role, 'reason': reason} for t in TRIALS)
        lane_rows[role] = row
    for role in ROLES:
        if role not in lanes:
            gaps.extend({'stage': s, 'trial': t, 'role': role, 'reason': 'lane not supplied'}
                        for s in STAGES for t in TRIALS)
    return {'schemaVersion': 1, 'kind': 'blast-signoff-pack-input', 'campaign': campaign, 'run': run_index,
            'expectedStages': list(STAGES), 'expectedTrials': list(TRIALS), 'expectedRoles': list(ROLES),
            'lanes': lane_rows, 'captures': captures, 'controls': controls, 'gaps': gaps, 'extras': extras}


def _relativize(manifest, base):
    def rel(path):
        return os.path.relpath(path, base) if path else path
    for capture in manifest['captures']:
        for key in ('path', 'conditionsPath', 'rois'):
            capture[key] = rel(capture[key])
    for control in manifest['controls']:
        control['collectorReport'] = rel(control['collectorReport'])
    for lane in manifest['lanes'].values():
        lane['directory'] = rel(lane['directory'])
        lane['allStages'] = rel(lane['allStages'])
    return manifest


# -------------------------------------------------------------- statistics ---

def percentile(histogram, percent):
    """Nearest-rank percentile of a 256-bin histogram (all pixels, zeros included)."""
    total = sum(histogram)
    rank, running = max(1, math.ceil(total * percent / 100)), 0
    for value, count in enumerate(histogram):
        running += count
        if running >= rank:
            return value
    raise ValueError('empty histogram')


def summarize(maximum, channel_stats):
    histogram = maximum.histogram()
    area = sum(histogram)
    changed = area - histogram[0]
    return {'area': area, 'changedPixels': changed, 'changedFraction': changed / area,
            'mean': sum(i * c for i, c in enumerate(histogram)) / area,
            'p50': percentile(histogram, 50), 'p90': percentile(histogram, 90), 'p99': percentile(histogram, 99),
            'p99_9': percentile(histogram, 99.9), 'max': max(i for i, c in enumerate(histogram) if c),
            'histogram': histogram, 'boundingBox': channel_stats['boundingBox'],
            'channels': {k: {'max': v['max'], 'mean': v['mean']} for k, v in channel_stats['channels'].items()}}


def _components(maximum):
    """8-connected components of nonzero pixels via run-length union-find."""
    width, height = maximum.size
    data = maximum.tobytes()
    parent, runs = [], []
    run_pattern = re.compile(rb'[^\x00]+')

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    previous = []
    for y in range(height):
        current = []
        row = data[y * width:(y + 1) * width]
        j = 0
        for match in run_pattern.finditer(row):
            start, end = match.span()
            label = len(parent)
            parent.append(label)
            runs.append((y, start, end))
            # Runs are [start, end). 8-connected when prev.end >= start and prev.start <= end.
            while j < len(previous) and previous[j][1] < start:
                j += 1
            k = j
            while k < len(previous) and previous[k][0] <= end:
                a, b = find(label), find(previous[k][2])
                if a != b:
                    parent[max(a, b)] = min(a, b)
                k += 1
            current.append((start, end, label))
        previous = current
    groups = {}
    for label, (y, start, end) in enumerate(runs):
        root = find(label)
        g = groups.setdefault(root, [0, start, y, end, y + 1])
        g[0] += end - start
        g[1], g[2], g[3], g[4] = min(g[1], start), min(g[2], y), max(g[3], end), max(g[4], y + 1)
    return list(groups.values())


def _edge_enrichment(maximum, reference):
    if reference is None:
        return None
    gradient = reference.convert('L').filter(ImageFilter.FIND_EDGES)
    histogram = gradient.histogram()
    area = sum(histogram)
    cutoff, running = 255, 0
    for value in range(255, -1, -1):  # rank-based: smallest value whose upper tail reaches the share
        running += histogram[value]
        cutoff = value
        if running >= area * EDGE_TOP_SHARE:
            break
    cutoff = max(cutoff, 1)
    edges = gradient.point(lambda v: 255 if v >= cutoff else 0)
    edge_count = area - edges.histogram()[0]
    changed = maximum.point(lambda v: 255 if v else 0)
    changed_count = area - changed.histogram()[0]
    if not edge_count or not changed_count:
        return None
    both = area - ImageChops.multiply(edges, changed).histogram()[0]
    return (both / changed_count) / (edge_count / area)


def _grid_ratio(maximum):
    width, height = maximum.size
    best = None
    for axis in ('x', 'y'):
        image = maximum if axis == 'x' else maximum.transpose(Image.Transpose.TRANSPOSE)
        w, h = image.size
        if w < 3:
            continue
        diff = ImageChops.difference(image.crop((1, 0, w, h)), image.crop((0, 0, w - 1, h)))
        row = diff.convert('F').resize((w - 1, 1), Image.Resampling.BOX)
        columns = list(row.get_flattened_data() if hasattr(row, 'get_flattened_data') else row.getdata())
        for period in GRID_PERIODS:
            if w - 1 < 2 * period:
                continue
            on = [v for i, v in enumerate(columns) if (i + 1) % period == 0]
            off = [v for i, v in enumerate(columns) if (i + 1) % period != 0]
            mean_on, mean_off = sum(on) / len(on), sum(off) / len(off)
            if mean_on <= 0:
                continue
            ratio = min(mean_on / mean_off, 1e6) if mean_off > 0 else 1e6
            if best is None or ratio > best['ratio']:
                best = {'ratio': ratio, 'period': period, 'axis': axis}
    return best


def _hotspots(maximum, rois):
    width, height = maximum.size
    total = sum(i * c for i, c in enumerate(maximum.histogram()))
    whole_mean = total / (width * height)
    cells = []
    for gy in range(4):
        for gx in range(4):
            box = (gx * width // 4, gy * height // 4, (gx + 1) * width // 4, (gy + 1) * height // 4)
            hist = maximum.crop(box).histogram()
            cells.append({'cell': [gx, gy], 'massShare': (sum(i * c for i, c in enumerate(hist)) / total) if total else 0})
    cells.sort(key=lambda c: (-c['massShare'], c['cell']))
    roi_ratios = {}
    for roi in rois or []:
        box = (roi['x'], roi['y'], roi['x'] + roi['width'], roi['y'] + roi['height'])
        if box[2] > width or box[3] > height:
            continue
        hist = maximum.crop(box).histogram()
        mean = sum(i * c for i, c in enumerate(hist)) / (roi['width'] * roi['height'])
        roi_ratios[roi['name']] = (mean / whole_mean) if whole_mean else None
    return {'top4x4Cells': cells[:3], 'roiMeanToWholeMean': roi_ratios}


def classify(maximum, reference=None, rois=None):
    """Advisory, deterministic pattern class of a max-channel |delta| map. First matching rule wins."""
    histogram = maximum.histogram()
    area = sum(histogram)
    changed = area - histogram[0]
    features = {'changedFraction': changed / area}
    if changed == 0:
        return {'class': 'identical', 'label': 'identical (no changed pixels)', 'rule': 'R0', 'advisory': True,
                'features': features}
    groups = _components(maximum)
    largest = max(groups, key=lambda g: g[0])
    box_area = (largest[3] - largest[1]) * (largest[4] - largest[2])
    features.update(componentCount=len(groups), largestComponentPixels=largest[0],
                    largestComponentShare=largest[0] / changed, largestComponentBoxFill=largest[0] / box_area,
                    largestComponentBox=[largest[1], largest[2], largest[3], largest[4]],
                    p10=percentile(histogram, 10), p50=percentile(histogram, 50), p90=percentile(histogram, 90),
                    edgeEnrichment=_edge_enrichment(maximum, reference), grid=_grid_ratio(maximum),
                    hotspots=_hotspots(maximum, rois))
    f, enrichment = features['changedFraction'], features['edgeEnrichment']

    def result(name, label, rule):
        return {'class': name, 'label': label, 'rule': rule, 'advisory': True, 'features': features}

    if (f >= SHIFT_MIN_FRACTION and features['p10'] > 0
            and features['p90'] - features['p10'] <= SHIFT_MAX_SPREAD * features['p50']):
        return result('global-shift', 'global shift (nearly every pixel, similar magnitude)', 'R1')
    if (f < REGION_MAX_FRACTION and largest[0] >= REGION_MIN_PIXELS and features['largestComponentShare'] >= REGION_MIN_SHARE
            and features['largestComponentBoxFill'] >= REGION_MIN_FILL):
        return result('feature-region', 'missing/extra feature region (one compact connected region)', 'R2')
    if features['grid'] and features['grid']['ratio'] >= GRID_MIN_RATIO:
        return result('tile-block', 'tile/block artifacts (delta steps on a %d-px grid)' % features['grid']['period'], 'R3')
    if f < SPARSE_MAX_FRACTION:
        if enrichment is None or enrichment >= EDGE_MIN_ENRICHMENT or largest[0] <= SPECKLE_MAX_COMPONENT:
            return result('sparse-speckle', 'sparse edge/AA speckle', 'R4')
        return result('sparse-speckle', 'sparse speckle (not edge-aligned)', 'R4')
    if enrichment is not None and enrichment >= EDGE_MIN_ENRICHMENT:
        return result('dense-edge', 'dense edge/AA variation (edge-aligned)', 'R5')
    named = sorted(((v, k) for k, v in features['hotspots']['roiMeanToWholeMean'].items() if v and v >= 1.5), reverse=True)
    where = (' - concentrated in ' + ', '.join(f'{k} ({v:.1f}x mean)' for v, k in named)) if named else ''
    return result('structured-shading', 'structured shading variation (dense, not edge-aligned' + where + ')', 'R6')


CLASSIFIER_RULES = [
    'R0 identical: no changed pixel.',
    f'R1 global-shift: >= {SHIFT_MIN_FRACTION:.0%} pixels changed, p10 > 0 and (p90 - p10) <= {SHIFT_MAX_SPREAD} x p50.',
    f'R2 feature-region: < {REGION_MAX_FRACTION:.0%} changed and the largest 8-connected component has >= {REGION_MIN_PIXELS} px, '
    f'>= {REGION_MIN_SHARE:.0%} of changed pixels and fills >= {REGION_MIN_FILL:.0%} of its bounding box.',
    f'R3 tile-block: mean |delta step| on a {"/".join(map(str, GRID_PERIODS))}-px pixel grid is >= {GRID_MIN_RATIO}x the mean elsewhere (either axis).',
    f'R4 sparse-speckle: < {SPARSE_MAX_FRACTION:.0%} changed; labelled edge/AA when changed pixels are >= {EDGE_MIN_ENRICHMENT}x '
    f'enriched on the top {EDGE_TOP_SHARE:.0%} reference-gradient pixels (or every component <= {SPECKLE_MAX_COMPONENT} px).',
    f'R5 dense-edge: otherwise, edge enrichment >= {EDGE_MIN_ENRICHMENT}.',
    'R6 structured-shading: otherwise (dense and not edge-aligned); named ROIs with >= 1.5x mean delta are listed.',
]


# --------------------------------------------------------------- rendering ---

def _lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def _hex(value):
    return tuple(int(value[i:i + 2], 16) for i in (1, 3, 5))


_ANCHORS = [_hex(h) for h in ('#0d366b', '#2a78d6', '#86b6ef', '#ffffff')]


def heat_color(delta):
    """Single global log scale: 0 -> black, 1..255 -> dark blue .. white. Same for every heatmap."""
    if delta == 0:
        return (0, 0, 0)
    t = math.log1p(delta) / math.log1p(255)
    t = (t - math.log1p(1) / math.log1p(255)) / (1 - math.log1p(1) / math.log1p(255))
    segment = min(int(t * 3), 2)
    return _lerp(_ANCHORS[segment], _ANCHORS[segment + 1], t * 3 - segment)


HEAT_LUT = [heat_color(i) for i in range(256)]


def save_heatmap(maximum, path):
    image = Image.frombytes('P', maximum.size, maximum.tobytes())
    image.putpalette([v for color in HEAT_LUT for v in color])
    image.convert('RGB').save(path, compress_level=6)


def _font(size):
    try:
        return ImageFont.load_default(size=size)
    except TypeError:
        return ImageFont.load_default()


def side_by_side(panels, path):
    """panels: list of (title lines, image or None). Pixels are pasted 1:1 (RGB; alpha not drawn)."""
    sizes = [img.size for _, img in panels if img is not None] or [(640, 400)]
    width, height = max(s[0] for s in sizes), max(s[1] for s in sizes)
    band, font, big = 132, _font(15), _font(36)
    canvas = Image.new('RGB', (width * len(panels), height + band), (24, 24, 24))
    draw = ImageDraw.Draw(canvas)
    for i, (lines, img) in enumerate(panels):
        x = i * width
        for n, line in enumerate(lines):
            draw.text((x + 8, 6 + n * 20), line, fill=(240, 239, 236), font=font)
        if img is None:
            draw.rectangle((x, band, x + width - 1, band + height - 1), fill=(60, 60, 60))
            draw.text((x + 24, band + height // 2 - 20), 'MISSING', fill=(240, 239, 236), font=big)
        else:
            canvas.paste(img.convert('RGB'), (x, band))
    canvas.save(path, compress_level=6)


def histogram_svg(series, title):
    """series: list of (pair id, 256-bin histogram). Log-count y, linear delta x. Zero bins are gaps."""
    width, height, left, bottom, top, right = 760, 280, 54, 36, 16, 12
    plot_w, plot_h = width - left - right, height - top - bottom
    peak = max((max(h[1:]) if any(h[1:]) else 1) for _, h in series) if series else 1
    peak = max(peak, max((h[0] for _, h in series), default=1))
    decades = max(1, math.ceil(math.log10(peak + 1)))

    def xy(value, count):
        return (left + plot_w * value / 255, top + plot_h * (1 - math.log10(count) / decades))

    out = [f'<svg class="hist" viewBox="0 0 {width} {height}" role="img" aria-label="{html.escape(title)}">']
    for d in range(decades + 1):
        y = top + plot_h * (1 - d / decades)
        out.append(f'<line x1="{left}" y1="{y:.1f}" x2="{width - right}" y2="{y:.1f}" class="grid"/>'
                   f'<text x="{left - 6}" y="{y + 4:.1f}" text-anchor="end">1e{d}</text>')
    for v in (0, 16, 32, 64, 96, 128, 160, 192, 224, 255):
        x = left + plot_w * v / 255
        out.append(f'<text x="{x:.1f}" y="{height - 18}" text-anchor="middle">{v}</text>')
    out.append(f'<text x="{left + plot_w / 2}" y="{height - 2}" text-anchor="middle">per-pixel max-channel |delta| (0..255); y = pixel count, log</text>')
    for pair, hist in series:
        dash = ' stroke-dasharray="6 3"' if pair == 'envelope' else ''
        path, pen = [], 'M'
        for value, count in enumerate(hist):
            if count:
                x, y = xy(value, count)
                path.append(f'{pen}{x:.1f},{y:.1f}')
                pen = 'L'
            else:
                pen = 'M'
        out.append(f'<path d="{" ".join(path)}" fill="none" stroke="{PAIR_COLORS[pair]}" stroke-width="2"{dash}/>')
    for value in range(256):  # hover targets wider than the marks
        text = '; '.join(f'{PAIR_LABELS[p]}: {h[value]}' for p, h in series)
        x = left + plot_w * (value - 0.5) / 255
        out.append(f'<rect x="{x:.1f}" y="{top}" width="{plot_w / 255:.2f}" height="{plot_h}" class="hit">'
                   f'<title>delta {value}: {html.escape(text)}</title></rect>')
    out.append('</svg>')
    def same_as(index):  # identical curves overlap exactly; say so instead of hiding one
        twins = [PAIR_LABELS[p] for p, h in series[:index] if h == series[index][1]]
        return f' (identical histogram to {twins[0]}; the curves overlap exactly)' if twins else ''
    legend = ''.join(f'<span class="key"><i style="border-color:{PAIR_COLORS[p]}{";border-style:dashed" if p == "envelope" else ""}"></i>'
                     f'{html.escape(PAIR_LABELS[p] + same_as(i))}</span>' for i, (p, _) in enumerate(series))
    return f'<div class="legend">{legend}</div>' + ''.join(out)


def heat_legend_svg():
    ticks = (0, 16, 32, 64, 128, 255)
    cells = ''.join(f'<rect x="{i * 2}" y="0" width="2" height="16" fill="rgb{HEAT_LUT[i]}"/>' for i in range(256))
    labels = ''.join(f'<text x="{v * 2}" y="30" text-anchor="middle">{v}</text>' for v in ticks)
    return (f'<svg class="heatlegend" viewBox="-10 0 532 34" role="img" aria-label="heatmap colour scale">{cells}{labels}</svg>'
            '<p class="sub">Heatmap colour = per-pixel max(|dR|,|dG|,|dB|,|dA|) on ONE fixed log scale for every heatmap in this pack '
            '(0 is pure black; any nonzero delta is visibly blue). Visualization only; statistics come from raw values.</p>')


# -------------------------------------------------------------------- pack ---

def _tool_identity():
    identity = {'toolVersion': TOOL_VERSION, 'toolSha256': sha256_file(__file__),
                'comparatorSha256': sha256_file(HERE / 'compare_visual_parity.py'), 'pillowVersion': pixels.PILLOW_VERSION}
    try:
        identity['gitCommit'] = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=HERE, text=True,
                                                        stderr=subprocess.DEVNULL).strip()
        status = subprocess.check_output(['git', 'status', '--porcelain', '--', str(Path(__file__).name),
                                          'compare_visual_parity.py'], cwd=HERE, text=True, stderr=subprocess.DEVNULL)
        identity['toolUncommitted'] = bool(status.strip())
    except (OSError, subprocess.CalledProcessError):
        identity['gitCommit'] = None
    return identity


def _validate_manifest(manifest, base):
    require(isinstance(manifest, dict) and manifest.get('schemaVersion') == 1
            and manifest.get('kind') == 'blast-signoff-pack-input', 'unknown sign-off manifest schema')
    for key in ('expectedStages', 'expectedTrials', 'captures', 'gaps', 'extras', 'controls', 'lanes'):
        require(key in manifest, f'manifest missing field: {key}')
    index = {}
    for capture in manifest['captures']:
        for key in ('stage', 'trial', 'role', 'path', 'sha256'):
            require(key in capture, f'capture missing field {key}: {capture}')
        require(capture['role'] in ROLES, f'unknown role: {capture["role"]}')
        require(capture['stage'] in manifest['expectedStages'], f'unexpected stage: {capture["stage"]}')
        require(capture['trial'] in manifest['expectedTrials'], f'unexpected trial: {capture["trial"]}')
        key = (capture['stage'], capture['trial'], capture['role'])
        require(key not in index, f'duplicate capture for {key}')
        path = (base / capture['path']).resolve()
        require(path.is_file(), f'capture file missing: {path}')
        require(sha256_file(path) == capture['sha256'], f'capture sha256 differs from manifest: {path}')
        index[key] = dict(capture, absolutePath=str(path))
    return index


def _flatten(value, prefix=''):
    if isinstance(value, dict):
        out = {}
        for key, item in value.items():
            out.update(_flatten(item, f'{prefix}.{key}' if prefix else key))
        return out
    return {prefix: value}


def like_for_like(a, b):
    """Return [] when both captures declare the same scene and source; else the differing fields."""
    if a.get('conditions') is None or b.get('conditions') is None:
        return ['conditions unavailable']
    left, right = _flatten(a['conditions']), _flatten(b['conditions'])
    fields = sorted(k for k in set(left) | set(right) if left.get(k) != right.get(k))
    fields += [k for k in ('sourceIdentity', 'engineIdentity') if a.get(k) != b.get(k)]
    return fields


def _panel_lines(capture, role):
    if capture is None:
        return [ROLE_LABELS[role], 'not available']
    return [f'{ROLE_LABELS[role]}  ({capture.get("os")})',
            f'GPU: {capture.get("adapter")} ({capture.get("deviceType")})',
            f'API: {capture.get("api")}   source: {capture.get("sourceIdentity")}',
            f'engine: {capture.get("engineIdentity")}',
            f'file: {Path(capture["path"]).name}  run-{capture.get("run")}',
            f'sha256: {capture["sha256"]}']


def build_pack(manifest_path, output):
    require(Image is not None, 'Pillow is required; use a local venv (docs/visual-parity-comparator.md)')
    manifest_path, output = Path(manifest_path).resolve(), Path(output)
    manifest = read_json(manifest_path)
    base = manifest_path.parent
    index = _validate_manifest(manifest, base)
    output.mkdir(parents=True, exist_ok=False)
    (output / 'images').mkdir()
    summary = {'schemaVersion': 1, 'kind': 'blast-signoff-pack',
               'scope': ('Review evidence for the human Windows/Linux visual sign-off. Each Windows/Linux Vulkan capture is '
                         'compared with the Mac reference of the same declared scene and source and shown beside the '
                         'measured hosted-Mac vs M5 envelope. No threshold, mask, alignment, resize, blur or quality '
                         'reduction is applied, and no acceptance outcome is computed. Pattern classes are advisory.'),
               'appliedThreshold': None, 'appliedMask': None, 'appliedAlignment': None,
               'tool': _tool_identity(), 'manifest': {'path': str(manifest_path), 'sha256': sha256_file(manifest_path),
                                                      'campaign': manifest.get('campaign')},
               'classifierRules': CLASSIFIER_RULES, 'inputs': [], 'lanes': manifest['lanes'],
               'gaps': list(manifest['gaps']), 'extras': manifest['extras'], 'scenes': [], 'controls': [], 'errors': []}
    summary['inputs'] = [{k: c[k] for k in ('stage', 'trial', 'role', 'path', 'sha256', 'adapter', 'api', 'os',
                                            'sourceIdentity', 'engineIdentity', 'packageSha256') if k in c}
                         for c in index.values()]
    for capture in index.values():
        if capture.get('conditions') is None:
            summary['gaps'].append({'stage': capture['stage'], 'trial': capture['trial'], 'role': capture['role'],
                                    'reason': 'capture has no recorded conditions; it cannot be matched like-for-like'})
    missing_lanes = sorted({g['role'] for g in manifest['gaps'] if g['reason'] == 'lane not supplied'})
    summary['missingLanes'] = missing_lanes

    decoded = {}

    def load(capture):
        path = capture['absolutePath']
        if path not in decoded:
            record = {'path': path}
            try:
                decoded[path] = (pixels.decode(path, record), record)
            except (OSError, ValueError, SyntaxError) as error:
                decoded[path] = (None, {'error': str(error), **record})
        return decoded[path]

    def rois_of(capture):
        if capture and capture.get('rois'):
            path = (base / capture['rois']).resolve()
            return read_json(path) if path.is_file() else None
        return None

    for stage in manifest['expectedStages']:
        decoded.clear()  # bound memory to one stage
        for trial in manifest['expectedTrials']:
            roles = {role: index.get((stage, trial, role)) for role in ROLES}
            if not any(roles.values()):
                continue
            scene = {'stage': stage, 'trial': trial, 'missingRoles': [r for r in ROLES if roles[r] is None],
                     'captures': {r: {'path': c['path'], 'sha256': c['sha256']} for r, c in roles.items() if c},
                     'pairs': {}}
            slug = f'{stage}--{trial}'
            reference = roles['mac-reference']
            for pair, candidate_role, reference_role, section in PAIRS:
                cand, ref = roles[candidate_role], roles[reference_role]
                entry = {'section': section, 'candidate': candidate_role, 'reference': reference_role}
                scene['pairs'][pair] = entry
                if cand is None or ref is None:
                    entry['status'] = 'missing-input'
                    entry['missing'] = [r for r, c in ((candidate_role, cand), (reference_role, ref)) if c is None]
                    continue
                mismatch = like_for_like(ref, cand)
                if pair == 'envelope' and reference is not None and not mismatch:
                    mismatch = ['envelope scene vs mac-reference: ' + f for f in like_for_like(reference, ref)]
                if mismatch:
                    entry['status'] = 'no-matching-reference'
                    entry['differingFields'] = mismatch
                    continue
                (ref_img, ref_rec), (cand_img, cand_rec) = load(ref), load(cand)
                if ref_img is None or cand_img is None:
                    entry['status'] = 'undecodable'
                    entry['errors'] = [r['error'] for r in (ref_rec, cand_rec) if 'error' in r]
                    summary['errors'].extend(entry['errors'])
                    continue
                if ref_img.size != cand_img.size:
                    entry['status'] = 'dimensions-differ'
                    entry['sizes'] = [list(ref_img.size), list(cand_img.size)]
                    continue
                stats, maximum = pixels.statistics(ImageChops.difference(ref_img, cand_img))
                entry['status'] = 'compared'
                entry['decodedSha256'] = {'reference': ref_rec['decodedSha256'], 'candidate': cand_rec['decodedSha256']}
                entry['stats'] = summarize(maximum, stats)
                entry['pattern'] = classify(maximum, ref_img, rois_of(reference) or rois_of(ref))
                heat = f'images/{slug}--heat--{pair}.png'
                save_heatmap(maximum, output / heat)
                entry['heatmap'] = heat
            envelope = scene['pairs']['envelope']
            for pair, entry in scene['pairs'].items():
                if pair != 'envelope' and entry.get('status') == 'compared' and envelope.get('status') == 'compared':
                    entry['patternSameAsEnvelope'] = entry['pattern']['class'] == envelope['pattern']['class']
            primary = [('mac-reference', reference), ('windows-vulkan', roles['windows-vulkan']), ('linux-vulkan', roles['linux-vulkan'])]
            scene['sideBySide'] = f'images/{slug}--side-by-side.png'
            side_by_side([(_panel_lines(c, r), load(c)[0] if c else None) for r, c in primary], output / scene['sideBySide'])
            if roles['windows-d3d12']:
                scene['d3d12SideBySide'] = f'images/{slug}--d3d12-side-by-side.png'
                d3d = [('mac-reference', reference), ('windows-d3d12', roles['windows-d3d12']), ('windows-vulkan', roles['windows-vulkan'])]
                side_by_side([(_panel_lines(c, r), load(c)[0] if c else None) for r, c in d3d], output / scene['d3d12SideBySide'])
            summary['scenes'].append(scene)
        for control in manifest['controls']:
            if control['stage'] != stage:
                continue
            on, off = index.get((stage, 'base-a-0', control['role'])), index.get((stage, CONTROLS[control['control']], control['role']))
            row = dict(control, wholeImageChangedPixels=None, roiChangedPixels=None)
            if on and off:
                (a, _), (b, _) = load(on), load(off)
                if a is not None and b is not None and a.size == b.size:
                    delta = ImageChops.difference(a, b)
                    stats, _ = pixels.statistics(delta)
                    row['wholeImageChangedPixels'] = stats['differingPixels']
                    rois = rois_of(on)
                    if rois:
                        row['roiChangedPixels'] = {r['name']: pixels.statistics(delta.crop(
                            (r['x'], r['y'], r['x'] + r['width'], r['y'] + r['height'])))[0]['differingPixels'] for r in rois}
            else:
                row['missing'] = 'base-a-0 or effect-off capture absent'
            summary['controls'].append(row)
    write_json(output / 'summary.json', summary)
    (output / 'index.html').write_text(render_html(summary), encoding='utf-8')
    (output / 'README.md').write_text(render_readme(summary), encoding='utf-8')
    return summary


# ------------------------------------------------------------------ output ---

def _fmt_stats(entry):
    s = entry['stats']
    return (f'<td>{s["changedPixels"]:,} ({s["changedFraction"] * 100:.4f}%)</td><td>{s["mean"]:.3f}</td>'
            f'<td>{s["p99"]}</td><td>{s["p99_9"]}</td><td>{s["max"]}</td>')


def _pair_rows(scene, pairs):
    rows = []
    for pair in pairs:
        entry = scene['pairs'][pair]
        label = html.escape(PAIR_LABELS[pair])
        if entry['status'] == 'compared':
            same = entry.get('patternSameAsEnvelope')
            same = '' if pair == 'envelope' else ('yes' if same else ('no' if same is False else 'n/a'))
            rows.append(f'<tr><td><i class="sw" style="background:{PAIR_COLORS[pair]}"></i>{label}</td>{_fmt_stats(entry)}'
                        f'<td>{html.escape(entry["pattern"]["label"])} <span class="sub">[{entry["pattern"]["rule"]}, advisory]</span></td><td>{same}</td></tr>')
        else:
            detail = entry.get('missing') or entry.get('differingFields') or entry.get('errors') or entry.get('sizes')
            status = 'no matching reference' if entry['status'] == 'no-matching-reference' else entry['status']
            rows.append(f'<tr class="gap"><td>{label}</td><td colspan="7"><strong>{html.escape(status)}</strong>: '
                        f'{html.escape(", ".join(map(str, detail or [])))}</td></tr>')
    return ('<table><thead><tr><th>Pair</th><th>Changed pixels</th><th>Mean</th><th>p99</th><th>p99.9</th><th>Max</th>'
            '<th>Pattern class</th><th>Same class as envelope</th></tr></thead><tbody>' + ''.join(rows) + '</tbody></table>')


def _heat_figures(scene, pairs):
    figures = []
    for pair in pairs:
        entry = scene['pairs'][pair]
        if entry.get('heatmap'):
            figures.append(f'<figure><a href="{entry["heatmap"]}"><img src="{entry["heatmap"]}" alt="{html.escape(PAIR_LABELS[pair])} heatmap" loading="lazy"></a>'
                           f'<figcaption>{html.escape(PAIR_LABELS[pair])}</figcaption></figure>')
        else:
            figures.append(f'<figure class="empty"><div>no heatmap ({html.escape(entry["status"])})</div>'
                           f'<figcaption>{html.escape(PAIR_LABELS[pair])}</figcaption></figure>')
    return '<div class="heat">' + ''.join(figures) + '</div>'


def _hist(scene, pairs, title):
    series = [(p, scene['pairs'][p]['stats']['histogram']) for p in pairs if scene['pairs'][p].get('status') == 'compared']
    return histogram_svg(series, title) if series else '<p class="sub">No comparable pair for a histogram.</p>'


def _gap_summary(gaps):
    grouped = {}
    for gap in gaps:
        grouped.setdefault((gap['role'], gap['reason']), set()).add(gap['stage'])
    return sorted((role, reason, sorted(stages)) for (role, reason), stages in grouped.items())


def render_html(summary):
    primary = ('envelope', 'windows-vulkan-vs-mac', 'linux-vulkan-vs-mac')
    d3d12 = ('windows-d3d12-vs-mac', 'windows-d3d12-vs-windows-vulkan', 'envelope')
    lanes = ''.join(f'<tr><td>{html.escape(ROLE_LABELS[r])}</td><td>{html.escape(str(l.get("adapter")))} ({html.escape(str(l.get("deviceType")))})</td>'
                    f'<td>{html.escape(str(l.get("api")))}</td><td>{html.escape(str(l.get("target")))}</td><td><code>{html.escape(str(l.get("revision")))}</code></td>'
                    f'<td><code>{html.escape(str(l.get("directory")))}</code></td></tr>' for r, l in summary['lanes'].items())
    gaps = ''.join(f'<li><strong>{html.escape(ROLE_LABELS.get(r, r))}</strong>: {html.escape(reason)} - {html.escape(", ".join(s))}</li>'
                   for r, reason, s in _gap_summary(summary['gaps'])) or '<li>none</li>'
    extras = ''.join(f'<li>{html.escape(e["role"])} / {html.escape(e["stage"])}: {html.escape(e["reason"])} <code>{html.escape(e["path"])}</code></li>'
                     for e in summary['extras']) or '<li>none</li>'
    body, d3d_body = [], []
    for scene in summary['scenes']:
        head = f'<h3 id="{scene["stage"]}--{scene["trial"]}">{html.escape(scene["stage"])} / {html.escape(scene["trial"])}</h3>'
        missing = (f'<p class="sub">Missing roles: {html.escape(", ".join(scene["missingRoles"]))}</p>' if scene['missingRoles'] else '')
        body.append(f'<section>{head}{missing}<a href="{scene["sideBySide"]}"><img class="sbs" src="{scene["sideBySide"]}" alt="side by side" loading="lazy"></a>'
                    f'{_heat_figures(scene, primary)}{_hist(scene, primary, scene["stage"] + " " + scene["trial"])}{_pair_rows(scene, primary)}</section>')
        if scene.get('d3d12SideBySide'):
            d3d_body.append(f'<section>{head}<a href="{scene["d3d12SideBySide"]}"><img class="sbs" src="{scene["d3d12SideBySide"]}" alt="D3D12 side by side" loading="lazy"></a>'
                            f'{_heat_figures(scene, d3d12)}{_hist(scene, d3d12, "D3D12 " + scene["stage"] + " " + scene["trial"])}{_pair_rows(scene, d3d12)}</section>')
    controls = ''.join(
        f'<tr><td>{html.escape(c["stage"])}</td><td>{html.escape(ROLE_LABELS[c["role"]])}</td><td>{html.escape(c["control"])}</td>'
        f'<td>{"recorded: " + ("effect present" if c["collectorRecorded"] else "effect NOT shown") if c["collectorRecorded"] is not None else "not recorded"}</td>'
        f'<td>{"-" if c["wholeImageChangedPixels"] is None else format(c["wholeImageChangedPixels"], ",")}</td>'
        f'<td>{html.escape(", ".join(f"{k}: {v:,}" for k, v in (c["roiChangedPixels"] or {}).items()) or c.get("missing", "-"))}</td></tr>'
        for c in summary['controls'])
    tool = summary['tool']
    return f'''<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Blast Circuit visual sign-off pack</title><style>
:root{{color-scheme:light dark;--bg:#fbfaf8;--fg:#1a1a19;--muted:#6b6a64;--line:#dcdad3}}
@media(prefers-color-scheme:dark){{:root{{--bg:#1a1a19;--fg:#f0efec;--muted:#c3c2b7;--line:#3a3a37}}}}
body{{margin:0 auto;max-width:1500px;padding:16px;font:14px/1.45 system-ui,sans-serif;background:var(--bg);color:var(--fg)}}
table{{border-collapse:collapse;width:100%;font-size:12px;margin:8px 0}}td,th{{border-bottom:1px solid var(--line);padding:5px;text-align:left;vertical-align:top}}
.sub{{color:var(--muted);font-size:12px}}code{{font-size:11px;overflow-wrap:anywhere}}img{{max-width:100%;display:block}}.sbs{{width:100%}}
.heat{{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin:8px 0}}figure{{margin:0}}figcaption{{font-size:12px}}
.empty div{{aspect-ratio:16/10;display:flex;align-items:center;justify-content:center;border:1px dashed var(--line);color:var(--muted)}}
.hist{{width:100%;max-width:760px}}.hist text,.heatlegend text{{fill:currentColor;font-size:10px}}.grid{{stroke:var(--line)}}.hit{{fill:transparent}}.hit:hover{{fill:rgba(128,128,128,.25)}}
.legend{{display:flex;gap:14px;flex-wrap:wrap;font-size:12px}}.key i{{display:inline-block;width:18px;border-top:2px solid;margin-right:4px;vertical-align:middle}}
.sw{{display:inline-block;width:10px;height:10px;border-radius:2px;margin-right:4px}}.gap td{{color:var(--muted)}}.heatlegend{{width:540px;max-width:100%}}
section{{border-top:1px solid var(--line);padding-top:8px;margin-top:16px}}.note{{border:1px solid var(--line);padding:10px;border-radius:6px}}
</style></head><body>
<h1>Blast Circuit visual sign-off pack</h1>
<div class="note"><p>{html.escape(summary['scope'])}</p>
<p><strong>No threshold, no mask, no alignment, no quality reduction was applied; this pack computes no acceptance outcome.</strong>
The reviewer judges each Windows/Linux Vulkan delta against the hosted-Mac vs M5 envelope for the same scene.
Pattern classes are advisory heuristics; the heatmap is authoritative for the human.
Windows forced-D3D12 is a separate section and never enters the primary comparison.</p>
<p class="sub">Campaign {html.escape(str(summary['manifest']['campaign']))}; manifest sha256 <code>{summary['manifest']['sha256']}</code>;
tool v{tool['toolVersion']} sha256 <code>{tool['toolSha256']}</code> at commit <code>{tool.get('gitCommit')}</code>{' (tool uncommitted)' if tool.get('toolUncommitted') else ''}; Pillow {tool['pillowVersion']}.
Full statistics and input hashes: <a href="summary.json">summary.json</a>. Scope: <a href="README.md">README.md</a>.</p></div>
<h2>Lanes</h2><table><thead><tr><th>Role</th><th>GPU adapter</th><th>API</th><th>Target</th><th>Source</th><th>Directory</th></tr></thead><tbody>{lanes}</tbody></table>
<p>Missing lanes: <strong>{html.escape(", ".join(summary['missingLanes']) or "none")}</strong></p>
<h2>Gaps (listed, never silently skipped)</h2><ul>{gaps}</ul><h2>Extra / unused files</h2><ul>{extras}</ul>
<h2>Heatmap colour scale</h2>{heat_legend_svg()}
<h2>Primary comparison: Windows Vulkan and Linux Vulkan vs Mac</h2>
<p class="sub">Side-by-side panels are 1:1 pixels (RGB drawn; alpha deltas are included in statistics). Heatmaps: Envelope | Windows Vulkan vs Mac | Linux Vulkan vs Mac.</p>
{''.join(body) or '<p>No scenes.</p>'}
<h2>Feature controls</h2><p class="sub">Effect-off capture vs base-a-0 of the same lane. "Recorded" is the collector's own control result; pixel counts are recomputed here. No new criterion is applied.</p>
<table><thead><tr><th>Stage</th><th>Lane</th><th>Control</th><th>Collector</th><th>Whole-image changed px</th><th>ROI changed px</th></tr></thead><tbody>{controls}</tbody></table>
<h2>Separate section: Windows forced Direct3D 12 (fallback)</h2>
<p class="sub">Not part of the primary Vulkan comparison. Heatmaps: D3D12 vs Mac | D3D12 vs Windows Vulkan | envelope (for scale).</p>
{''.join(d3d_body) or '<p>No D3D12 captures supplied.</p>'}
</body></html>
'''


def render_readme(summary):
    rules = '\n'.join(f'- {r}' for r in summary['classifierRules'])
    gaps = '\n'.join(f'- {ROLE_LABELS.get(r, r)}: {reason} ({", ".join(s)})' for r, reason, s in _gap_summary(summary['gaps'])) or '- none'
    return f'''# Visual sign-off pack

Generated by `tools/qa/build_signoff_pack.py` v{summary['tool']['toolVersion']} (campaign `{summary['manifest']['campaign']}`).
Open `index.html`; every statistic and input sha256 is in `summary.json`.

## Scope

{summary['scope']}

- No threshold, mask, alignment, resize, blur or quality reduction was applied.
  `summary.json` contains no pass/fail field; the reviewer signs off.
- Primary: Windows Vulkan and Linux Vulkan vs the Mac reference, shown with the
  hosted-Mac (Apple Paravirtual) vs M5 envelope of the same declared scene.
- A pair is compared only when both captures' recorded `conditions`, source and
  engine identity are equal; otherwise it is listed as "no matching reference".
- Windows forced D3D12 is a separate labelled section (vs Mac and vs Windows Vulkan).
- Heatmaps: per-pixel max channel |delta| (RGBA) on one fixed log colour scale.
- Histograms: per-pixel max-channel |delta| 0..255, log count, envelope dashed.
- Feature controls: collector-recorded result plus recomputed changed pixels.

## Advisory pattern classifier (first matching rule wins)

{rules}

## Gaps in this pack

{gaps}

## Expected campaign layout (input to `manifest`)

Each `--lane ROLE=DIR` points at a visual-parity collector output directory:

- `DIR/visual-parity-native.json` (target, revision, runs[0].device adapter/driver,
  comparisons incl. `run-N/control-dof|control-mirror` results) - required.
- `DIR/run-N/parity-<trial>.png` for trials {", ".join(TRIALS)} (core stage,
  `up-and-down` unless the native report names another), with
  `parity-<base>-conditions.json`, `parity-<base>-recipe.json`, optional
  `parity-<trial>.manifest.json` (its `pngSha256` must match) and `control-rois.json`.
- Other stages: `all-stages/<stage>/run-N/...` either inside `DIR` (hosted Mac
  reference workflow) or beside it (GPU campaign `gpu-campaign-<target>-core/`),
  used only when its `stage-captures.json` `driver` equals the lane's API.
- Missing captures, stages or lanes become listed gaps; unknown PNG names, unknown
  stage directories and duplicate stage sources are listed as extras. A capture
  whose sha256 differs from the manifest, or a stale capture manifest, is an error.
'''


# --------------------------------------------------------------------- CLI ---

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest='command', required=True)
    make = commands.add_parser('manifest', help='scan lane directories and write the input manifest')
    make.add_argument('--lane', action='append', required=True, metavar='ROLE=DIR',
                      help='repeatable; roles: ' + ', '.join(ROLES))
    make.add_argument('--run', type=int, default=0, help='run-N directory to use (default 0)')
    make.add_argument('--campaign', help='free-text campaign label recorded in the manifest')
    make.add_argument('--output', type=Path, required=True, help='new manifest JSON file (must not exist)')
    pack = commands.add_parser('pack', help='build the review pack from a manifest')
    pack.add_argument('--manifest', type=Path, required=True)
    pack.add_argument('--output', type=Path, required=True, help='new directory (must not exist)')
    args = parser.parse_args(argv)
    try:
        if args.command == 'manifest':
            lanes = {}
            for item in args.lane:
                role, sep, directory = item.partition('=')
                require(sep and role in ROLES, f'invalid --lane {item!r}; expected ROLE=DIR with ROLE in {ROLES}')
                require(role not in lanes, f'duplicate lane role: {role}')
                lanes[role] = directory
            require(not args.output.exists(), f'output exists: {args.output}')
            manifest = build_manifest(lanes, args.run, args.campaign)
            args.output.parent.mkdir(parents=True, exist_ok=True)
            write_json(args.output, _relativize(manifest, args.output.resolve().parent))
            print(json.dumps({'manifest': str(args.output), 'captures': len(manifest['captures']),
                              'gaps': len(manifest['gaps']), 'extras': len(manifest['extras'])}))
        else:
            summary = build_pack(args.manifest, args.output)
            compared = sum(1 for s in summary['scenes'] for p in s['pairs'].values() if p['status'] == 'compared')
            print(json.dumps({'index': str(args.output / 'index.html'), 'scenes': len(summary['scenes']),
                              'comparedPairs': compared, 'missingLanes': summary['missingLanes'],
                              'gaps': len(summary['gaps']), 'errors': summary['errors']}))
    except (OSError, ValueError) as error:
        print(f'Invalid sign-off pack input: {error}', file=sys.stderr)
        return 2
    return 0


if __name__ == '__main__':
    sys.exit(main())
