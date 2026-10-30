"""Synthetic witnesses for the visual sign-off pack; Pillow is an optional developer dependency."""
import importlib.util
import json
from pathlib import Path
import re
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('build_signoff_pack', Path(__file__).with_name('build_signoff_pack.py'))
pack = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(pack)
try:
    from PIL import Image
except ImportError:
    Image = None

SIZE = (64, 48)
CONDITIONS = {'sceneId': 'synthetic-v1', 'tick': 1, 'render': {'preset': 'High', 'msaaSamples': 2}}
DEVICES = {'mac-reference': ('macos-arm64', 'metal', 'Apple M5 (Apple9)'),
           'm5-envelope': ('macos-arm64', 'metal', 'Apple M5 (Apple9)'),
           'hosted-envelope': ('macos-arm64', 'metal', 'Apple Paravirtual device (Apple5)'),
           'windows-vulkan': ('windows-x64', 'vulkan', 'Tesla T4'),
           'linux-vulkan': ('linux-x64', 'vulkan', 'Tesla T4'),
           'windows-d3d12': ('windows-x64', 'd3d12', 'NVIDIA Tesla T4')}


def textured(size=SIZE):
    image = Image.new('RGBA', size)
    image.putdata([((x * 7 + y * 3) % 256, (x * y) % 256, (y * 11) % 256, 255) for y in range(size[1]) for x in range(size[0])])
    return image


def write_lane(root, role, image, conditions=CONDITIONS, source='abc', trials=pack.TRIALS, extra_png=False):
    target, api, adapter = DEVICES[role]
    lane = root / role
    run = lane / 'run-0'
    run.mkdir(parents=True)
    (lane / 'visual-parity-native.json').write_text(json.dumps({
        'target': target, 'revision': source,
        'runs': [{'device': {'driver': api, 'adapter': adapter, 'deviceType': 'DiscreteGpu', 'engine': '4.7.2'}}],
        'comparisons': [{'name': 'run-0/control-dof', 'passed': True}, {'name': 'run-0/control-mirror', 'passed': True}]}))
    for trial in trials:
        image.save(run / f'parity-{trial}.png')
        base = trial.rsplit('-', 1)[0]
        (run / f'parity-{base}-conditions.json').write_text(json.dumps(conditions))
        (run / f'parity-{base}-recipe.json').write_text(json.dumps({'stage': 'up-and-down'}))
    if extra_png:
        image.save(run / 'parity-unexpected-0.png')
    return lane


@unittest.skipUnless(Image is not None, 'optional Pillow unavailable; run in the documented parity venv')
class SignoffPackTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def run_pack(self, images, **lane_options):
        lanes = {role: write_lane(self.root / 'lanes', role, image, **lane_options.get(role, {}))
                 for role, image in images.items()}
        manifest_path = self.root / 'manifest.json'
        self.assertEqual(pack.main(['manifest', '--campaign', 'test', '--output', str(manifest_path),
                                    *[a for role, lane in lanes.items() for a in ('--lane', f'{role}={lane}')]]), 0)
        self.assertEqual(pack.main(['pack', '--manifest', str(manifest_path), '--output', str(self.root / 'out')]), 0)
        return json.loads((self.root / 'out/summary.json').read_text())

    def scene(self, summary, trial='base-a-0'):
        return next(s for s in summary['scenes'] if s['stage'] == 'up-and-down' and s['trial'] == trial)

    def test_identical_images_have_zero_histogram(self):
        image = textured()
        summary = self.run_pack({role: image for role in pack.ROLES})
        pair = self.scene(summary)['pairs']['windows-vulkan-vs-mac']
        self.assertEqual(pair['stats']['changedPixels'], 0)
        self.assertEqual(pair['stats']['histogram'][0], SIZE[0] * SIZE[1])
        self.assertEqual(sum(pair['stats']['histogram'][1:]), 0)
        self.assertEqual(pair['pattern']['class'], 'identical')
        for name in ('index.html', 'README.md', pair['heatmap'], self.scene(summary)['sideBySide']):
            self.assertTrue((self.root / 'out' / name).is_file(), name)

    def test_single_pixel_change_is_sparse(self):
        reference, candidate = textured(), textured()
        candidate.putpixel((20, 20), (0, 0, 0, 255))
        summary = self.run_pack({'mac-reference': reference, 'windows-vulkan': candidate})
        pair = self.scene(summary)['pairs']['windows-vulkan-vs-mac']
        self.assertEqual(pair['stats']['changedPixels'], 1)
        self.assertEqual(pair['pattern']['class'], 'sparse-speckle')
        self.assertTrue(pair['pattern']['advisory'])

    def test_block_region_change_is_region(self):
        reference, candidate = textured(), textured()
        for x in range(10, 22):
            for y in range(10, 22):
                candidate.putpixel((x, y), (255, 0, 255, 255))
        summary = self.run_pack({'mac-reference': reference, 'linux-vulkan': candidate})
        pair = self.scene(summary)['pairs']['linux-vulkan-vs-mac']
        self.assertEqual(pair['pattern']['class'], 'feature-region')
        self.assertEqual(pair['pattern']['features']['largestComponentBox'], [10, 10, 22, 22])

    def test_tile_grid_and_global_shift_classes(self):
        flat = Image.new('L', (64, 64), 0)
        tiles = Image.new('L', (64, 64))
        tiles.putdata([(x // 8 * 3 + y // 8 * 5) % 7 + 1 for y in range(64) for x in range(64)])
        self.assertEqual(pack.classify(tiles)['class'], 'tile-block')
        self.assertEqual(pack.classify(Image.new('L', (64, 64), 3))['class'], 'global-shift')
        self.assertEqual(pack.classify(flat)['class'], 'identical')

    def test_components_are_eight_connected(self):
        image = Image.new('L', (8, 8))
        for point in ((1, 1), (2, 2), (3, 3), (6, 1)):
            image.putpixel(point, 5)
        self.assertEqual(sorted(g[0] for g in pack._components(image)), [1, 3])

    def test_d3d12_is_kept_separate(self):
        reference, other = textured(), textured()
        other.putpixel((1, 1), (9, 9, 9, 255))
        summary = self.run_pack({'mac-reference': reference, 'windows-vulkan': reference, 'windows-d3d12': other})
        pairs = self.scene(summary)['pairs']
        self.assertEqual({p['section'] for k, p in pairs.items() if 'd3d12' in k}, {'d3d12'})
        self.assertFalse(any('d3d12' in p['candidate'] for p in pairs.values() if p['section'] == 'primary'))
        self.assertEqual(pairs['windows-d3d12-vs-windows-vulkan']['stats']['changedPixels'], 1)
        self.assertEqual(pairs['windows-vulkan-vs-mac']['stats']['changedPixels'], 0)
        self.assertIn('d3d12SideBySide', self.scene(summary))

    def test_missing_role_and_stage_are_reported(self):
        summary = self.run_pack({'mac-reference': textured(), 'windows-vulkan': textured()},
                                **{'windows-vulkan': {'trials': ('base-a-0',)}})
        self.assertIn('linux-vulkan', summary['missingLanes'])
        self.assertIn('hosted-envelope', summary['missingLanes'])
        self.assertEqual(self.scene(summary)['pairs']['linux-vulkan-vs-mac']['status'], 'missing-input')
        reasons = {(g['role'], g['stage'], g['trial']) for g in summary['gaps']}
        self.assertIn(('windows-vulkan', 'up-and-down', 'base-b-0'), reasons)
        self.assertIn(('mac-reference', 'rock-garden', 'base-a-0'), reasons)

    def test_mismatched_scene_is_not_compared(self):
        summary = self.run_pack({'mac-reference': textured(), 'windows-vulkan': textured()},
                                **{'windows-vulkan': {'conditions': dict(CONDITIONS, tick=2)}})
        pair = self.scene(summary)['pairs']['windows-vulkan-vs-mac']
        self.assertEqual(pair['status'], 'no-matching-reference')
        self.assertIn('tick', pair['differingFields'])
        self.assertNotIn('stats', pair)

    def test_extra_files_listed_and_tampered_capture_rejected(self):
        lanes = {r: write_lane(self.root / 'lanes', r, textured(), extra_png=(r == 'linux-vulkan'))
                 for r in ('mac-reference', 'linux-vulkan')}
        manifest = pack.build_manifest(lanes)
        self.assertEqual([Path(e['path']).name for e in manifest['extras']], ['parity-unexpected-0.png'])
        path = self.root / 'm.json'
        pack.write_json(path, pack._relativize(manifest, self.root.resolve()))
        Image.new('RGBA', SIZE).save(lanes['linux-vulkan'] / 'run-0/parity-base-a-0.png')
        with self.assertRaisesRegex(ValueError, 'sha256 differs'):
            pack.build_pack(path, self.root / 'bad')
        self.assertEqual(pack.main(['pack', '--manifest', str(path), '--output', str(self.root / 'bad2')]), 2)
        with self.assertRaises(ValueError):
            pack.build_manifest({'not-a-role': lanes['linux-vulkan']})

    def test_summary_has_no_threshold_or_verdict(self):
        reference, candidate = textured(), textured()
        candidate.putpixel((3, 3), (0, 0, 0, 255))
        summary = self.run_pack({role: (candidate if role == 'linux-vulkan' else reference) for role in pack.ROLES})
        keys = set()

        def walk(value):
            if isinstance(value, dict):
                for key, item in value.items():
                    keys.add(key)
                    walk(item)
            elif isinstance(value, list):
                for item in value:
                    walk(item)
        walk(summary)
        self.assertFalse([k for k in keys if re.search(r'pass|fail|verdict|accept|approv|gate', k, re.I)])
        self.assertIsNone(summary['appliedThreshold'])
        self.assertIsNone(summary['appliedMask'])
        self.assertIn('no threshold', (self.root / 'out/README.md').read_text().lower())

    def test_campaign2_all_stages_layout(self):
        core = self.root / 'gpu-campaign-windows-x64-core'
        vulkan = write_lane(core, 'windows-vulkan', textured())
        d3d12 = write_lane(core, 'windows-d3d12', textured())
        stages = core / 'all-stages'
        runs = []
        for stage in ('rock-garden', 'bogus-stage'):
            run = stages / stage / 'run-0'
            run.mkdir(parents=True)
            for trial in pack.TRIALS:
                textured().save(run / f'parity-{trial}.png')
                (run / f'parity-{trial.rsplit("-", 1)[0]}-conditions.json').write_text(json.dumps(CONDITIONS))
            (run / 'parity-base-a-recipe.json').write_text(json.dumps({'stage': 'up-and-down', 'stageId': stage}))
            runs.append({'stage': stage, 'index': 0, 'device': {'driver': 'vulkan', 'adapter': 'Tesla T4'}})
        (stages / 'stage-captures.json').write_text(json.dumps({'driver': 'vulkan', 'runs': runs}))
        manifest = pack.build_manifest({'windows-vulkan': vulkan, 'windows-d3d12': d3d12})
        keys = {(c['role'], c['stage']) for c in manifest['captures']}
        self.assertIn(('windows-vulkan', 'rock-garden'), keys)
        self.assertNotIn(('windows-d3d12', 'rock-garden'), keys)
        self.assertIn(('windows-d3d12', 'rock-garden', 'no all-stages directory for this lane/API'),
                      {(g['role'], g['stage'], g['reason']) for g in manifest['gaps']})
        self.assertEqual([e['stage'] for e in manifest['extras']], ['bogus-stage'])
        (stages / 'rock-garden/run-0/parity-base-a-recipe.json').write_text(json.dumps({'stageId': 'top-rules'}))
        with self.assertRaisesRegex(ValueError, 'stageId'):
            pack.build_manifest({'windows-vulkan': vulkan})

    def test_stage_list_matches_collector(self):
        source = (Path(__file__).resolve().parents[1] / 'ci/mesh_inputs.py').read_text()
        match = re.search(r'STAGES = \(([^)]*)\)', source)
        self.assertEqual(tuple(re.findall(r"'([^']+)'", match.group(1))), pack.STAGES)


if __name__ == '__main__':
    unittest.main()
