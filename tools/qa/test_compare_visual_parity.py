"""Tiny offline PNG witnesses; Pillow remains an optional developer dependency."""
import contextlib
import copy
import importlib.util
import io
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zlib

SPEC = importlib.util.spec_from_file_location('visual_parity', Path(__file__).with_name('compare_visual_parity.py'))
parity = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(parity)
try:
    from PIL import Image, PngImagePlugin
except ImportError:
    Image = None


@unittest.skipUnless(Image is not None, 'optional Pillow unavailable; run in documented parity venv')
class ComparisonTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.number = 0
        self.a = self.png('a.png')
        self.b = self.png('b.png')

    def png(self, name, pixels=None, mode='RGBA', size=(4, 3), **options):
        path = self.root / name
        image = Image.new(mode, size, (10, 20, 30, 255) if mode == 'RGBA' else (10, 20, 30))
        if pixels is not None:
            image.putdata(pixels)
        image.save(path, **options)
        return path

    def manifest(self, path, platform='macOS', api='Metal'):
        with Image.open(path) as image:
            width, height = image.size
        return {'schemaVersion': 1, 'pngSha256': parity.sha256(path.read_bytes()),
                'conditions': {'sceneId': 'water-high-v1', 'assetsSha256': 'a' * 64,
                               'stateId': 'fixture-v1:seed=17', 'tick': 90, 'width': width, 'height': height,
                               'render': {'preset': 'High', 'msaaSamples': 2, 'dof': True, 'settingsSha256': 'b' * 64},
                               'cameraSha256': 'c' * 64, 'shaderTimeSeconds': 3, 'animationTimeSeconds': 3,
                               'temporal': {'historyId': 'reset-at-frame-0', 'warmupFrames': 16, 'jitterId': 'sequence-v1:index=16'},
                               'capture': {'surface': 'viewport-post-tonemap', 'colorContract': 'srgb-straight-alpha-v1'}},
                'provenance': {'platform': platform, 'api': api, 'device': 'Apple fixture GPU', 'driver': 'fixture-driver',
                               'engineIdentity': 'Godot-4.7.2-stable:official', 'templateSha256': 'd' * 64,
                               'packageSha256': 'e' * 64, 'sourceIdentity': 'git:fixture-source', 'runtimeIdentity': '10.0-fixture'}}

    def json_file(self, name, value):
        path = self.root / name
        path.write_text(json.dumps(value))
        return path

    def run_pair(self, left=None, right=None, diagnostic=False, rois=None, manifests=None):
        left, right = left or self.a, right or self.b
        self.number += 1
        output = self.root / f'out-{self.number}'
        options = {'diagnostic_only': diagnostic}
        if not diagnostic:
            ma, mb = manifests or (self.manifest(left), self.manifest(right))
            options.update(reference_manifest=self.json_file('a.json', ma), candidate_manifest=self.json_file('b.json', mb))
        if rois is not None:
            options['rois'] = self.json_file('rois.json', rois)
        report, code = parity.compare(left, right, output, **options)
        self.assertEqual(report, json.loads((output / 'report.json').read_text()))
        return report, code, output

    def test_compression_and_text_do_not_change_decoded_equality(self):
        info = PngImagePlugin.PngInfo()
        info.add_text('Comment', 'irrelevant storage metadata')
        self.png('b.png', compress_level=0, pnginfo=info)
        report, code, _ = self.run_pair()
        self.assertEqual(0, code)
        a, b = report['inputs'].values()
        self.assertNotEqual(a['fileSha256'], b['fileSha256'])
        self.assertEqual(a['decodedSha256'], b['decodedSha256'])
        self.assertTrue(report['exactEqual'])
        self.assertTrue(report['evidenceEligible'])

    def test_rgb_explicit_alpha_normalization(self):
        rgb = self.png('rgb.png', mode='RGB')
        report, code, _ = self.run_pair(right=rgb)
        self.assertEqual(0, code)
        self.assertIn('alpha=255', report['inputs']['candidate']['normalization'])
        self.assertEqual(report['inputs']['reference']['decodedSha256'], report['inputs']['candidate']['decodedSha256'])

    def test_one_pixel_and_alpha_difference_and_diagnostic_determinism(self):
        pixels = [(10, 20, 30, 255)] * 12
        pixels[6] = (10, 20, 30, 254)
        self.png('b.png', pixels)
        report, code, output = self.run_pair(rois=[{'name': 'defect', 'x': 2, 'y': 1, 'width': 1, 'height': 1}])
        self.assertEqual(1, code)
        self.assertFalse(report['exactEqual'])
        self.assertTrue(report['compatibleInputs'])
        whole = report['wholeImage']
        self.assertEqual(1, whole['differingPixels'])
        self.assertEqual(1 / 12, whole['differingFraction'])
        self.assertEqual([2, 1, 3, 2], whole['boundingBox'])
        self.assertEqual({'max': 1, 'mean': 1 / 12, 'histogram': {'0': 11, '1': 1}}, whole['channels']['A'])
        self.assertEqual(0, whole['channels']['R']['max'])
        self.assertEqual(1, report['rois'][0]['differingPixels'])
        heatmap = output / 'amplified-diagnostic-difference.png'
        with Image.open(heatmap) as image:
            self.assertEqual((16, 16, 16), image.getpixel((2, 1)))
            self.assertEqual((0, 0, 0), image.getpixel((0, 0)))
        _, _, output2 = self.run_pair()
        self.assertEqual(heatmap.read_bytes(), (output2 / heatmap.name).read_bytes())

    def test_removed_feature_is_reported_globally_and_in_roi(self):
        pixels = [(10, 20, 30, 255)] * 12
        pixels[5:7] = [(80, 120, 160, 255)] * 2
        self.png('a.png', pixels)
        rois = [{'name': 'feature', 'x': 1, 'y': 1, 'width': 2, 'height': 1},
                {'name': 'unchanged', 'x': 0, 'y': 0, 'width': 4, 'height': 1}]
        report, code, _ = self.run_pair(rois=rois)
        self.assertEqual(1, code)
        self.assertEqual(2, report['wholeImage']['differingPixels'])
        self.assertEqual([2, 0], [r['differingPixels'] for r in report['rois']])
        report, code, _ = self.run_pair(rois=rois[1:])
        self.assertEqual(1, code)  # An unchanged ROI cannot hide the removed feature.
        self.assertEqual(2, report['wholeImage']['differingPixels'])

    def test_rearrangement_with_equal_mean_is_not_equal(self):
        colors = [(0, 0, 0, 255), (200, 200, 200, 255)] * 6
        self.png('a.png', colors)
        self.png('b.png', list(reversed(colors)))
        report, code, _ = self.run_pair()
        self.assertEqual(1, code)
        self.assertEqual(12, report['wholeImage']['differingPixels'])
        self.assertEqual(200, report['wholeImage']['channels']['R']['mean'])

    def test_dimension_mismatch_has_no_partial_comparison(self):
        self.png('b.png', size=(2, 3))
        report, code, output = self.run_pair()
        self.assertEqual(2, code)
        self.assertIsNone(report['exactEqual'])
        self.assertNotIn('wholeImage', report)
        self.assertFalse((output / 'amplified-diagnostic-difference.png').exists())

    def test_unsupported_modes_and_16_bit_rgb_are_rejected(self):
        for mode in ('L', 'P', 'I;16'):
            with self.subTest(mode=mode):
                path = self.root / 'unsupported.png'
                Image.new(mode, (2, 2)).save(path)
                report, code, _ = self.run_pair(right=path, diagnostic=True)
                self.assertEqual(2, code)
                self.assertIsNone(report['exactEqual'])
        # Real 16-bit RGB PNG: Pillow would otherwise silently expose it as 8-bit RGB.
        def chunk(kind, payload):
            return struct.pack('>I', len(payload)) + kind + payload + struct.pack('>I', zlib.crc32(kind + payload))
        data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 16, 2, 0, 0, 0))
        data += chunk(b'IDAT', zlib.compress(b'\0' + b'\x12\x34' * 3)) + chunk(b'IEND', b'')
        path.write_bytes(data)
        report, code, _ = self.run_pair(right=path, diagnostic=True)
        self.assertEqual(2, code)
        self.assertEqual(16, report['inputs']['candidate']['png']['bitDepth'])

    def test_color_encoding_mismatch_is_separate_from_pixel_equality(self):
        info = PngImagePlugin.PngInfo()
        info.add(b'gAMA', struct.pack('>I', 45455))
        self.png('b.png', pnginfo=info)
        report, code, _ = self.run_pair()
        self.assertEqual(2, code)
        self.assertTrue(report['exactEqual'])
        self.assertFalse(report['encodingCompatible'])
        self.assertFalse(report['evidenceEligible'])
        self.assertEqual(45455, report['inputs']['candidate']['png']['color']['gammaTimes100000'])
        self.png('a.png', pnginfo=info)
        self.assertEqual(0, self.run_pair()[1])

    def test_ambiguous_color_profiles_and_metadata_are_rejected(self):
        self.png('b.png', icc_profile=b'fixture-profile-not-interpreted')
        report, code, _ = self.run_pair(diagnostic=True)
        self.assertEqual(2, code)
        self.assertEqual(parity.sha256(b'fixture-profile-not-interpreted'), report['inputs']['candidate']['png']['color']['iccSha256'])
        for chunk, payload in ((b'cICP', b'\1\r\0\1'), (b'sBIT', b'\7\7\7\7'),
                               (b'sRGB', b'\5'), (b'cHRM', b'\0' * 32)):
            with self.subTest(chunk=chunk):
                info = PngImagePlugin.PngInfo()
                info.add(chunk, payload)
                self.png('b.png', pnginfo=info)
                self.assertEqual(2, self.run_pair(diagnostic=True)[1])
        info = PngImagePlugin.PngInfo()
        info.add(b'sRGB', b'\0')
        info.add(b'gAMA', struct.pack('>I', 100000))
        self.png('b.png', pnginfo=info)
        self.assertEqual(2, self.run_pair(diagnostic=True)[1])
        self.png('b.png', mode='RGB', transparency=(10, 20, 30))
        self.assertEqual(2, self.run_pair(diagnostic=True)[1])

    def test_decompression_safeguards_and_corrupt_png(self):
        with patch.object(Image, 'MAX_IMAGE_PIXELS', 10):
            report, code, _ = self.run_pair(diagnostic=True)
            self.assertEqual(2, code)
            self.assertTrue(report['errors'])
        self.b.write_bytes(self.b.read_bytes()[:-10])
        self.assertEqual(2, self.run_pair(diagnostic=True)[1])

    def test_cross_platform_provenance_is_recorded_and_permitted(self):
        a, b = self.manifest(self.a), self.manifest(self.b, 'Windows', 'D3D12')
        b['provenance'].update(device='Other GPU', driver='Other driver', templateSha256='f' * 64,
                               packageSha256='0' * 64, runtimeIdentity='10.0-other-arch')
        report, code, _ = self.run_pair(manifests=(a, b))
        self.assertEqual(0, code)
        self.assertEqual(b['provenance'], report['inputs']['candidate']['manifest']['provenance'])
        self.assertIn('provenance.packageSha256', report['permittedProvenanceDifferences'])

    def test_incompatible_conditions_and_shared_identities_remain_measured(self):
        a, original = self.manifest(self.a), self.manifest(self.b)
        changes = [(('conditions', 'sceneId'), 'other'), (('conditions', 'tick'), 91),
                   (('conditions', 'render', 'preset'), 'Low'), (('conditions', 'render', 'msaaSamples'), 0),
                   (('conditions', 'render', 'dof'), False), (('conditions', 'shaderTimeSeconds'), 4),
                   (('conditions', 'capture', 'surface'), 'desktop'),
                   (('provenance', 'engineIdentity'), 'other'), (('provenance', 'sourceIdentity'), 'other')]
        for keys, value in changes:
            with self.subTest(keys=keys):
                b = copy.deepcopy(original)
                item = b
                for key in keys[:-1]:
                    item = item[key]
                item[keys[-1]] = value
                report, code, _ = self.run_pair(manifests=(a, b))
                self.assertEqual(2, code)
                self.assertTrue(report['exactEqual'])
                self.assertFalse(report['evidenceEligible'])
                self.assertIn('.'.join(keys), ' '.join(report['errors']))

    def test_missing_wrong_type_stale_unknown_and_extra_manifest_fields(self):
        a = self.manifest(self.a)
        variants = []
        for parent, key in ((None, 'pngSha256'), ('conditions', 'cameraSha256'), ('provenance', 'driver')):
            b = self.manifest(self.b)
            del (b if parent is None else b[parent])[key]
            variants.append(b)
        for key, value in (('schemaVersion', 2), ('schemaVersion', True), ('pngSha256', '0' * 64), ('mask', [])):
            b = self.manifest(self.b)
            b[key] = value
            variants.append(b)
        for key, value in (('tick', True), ('width', 5), ('shaderTimeSeconds', float('nan'))):
            b = self.manifest(self.b)
            b['conditions'][key] = value
            variants.append(b)
        for b in variants:
            with self.subTest(b=b):
                report, code, _ = self.run_pair(manifests=(a, b))
                self.assertEqual(2, code)
                self.assertFalse(report['evidenceEligible'])

    def test_diagnostic_equality_and_difference_never_become_eligible(self):
        for same in (True, False):
            if not same:
                self.png('b.png', [(0, 0, 0, 0)] * 12)
            report, code, _ = self.run_pair(diagnostic=True)
            self.assertEqual(3, code)
            self.assertEqual(same, report['exactEqual'])
            self.assertFalse(report['compatibleInputs'])
            self.assertFalse(report['evidenceEligible'])
            self.assertEqual('unavailable', report['metadataCompatibility'])

    def test_missing_manifests_and_diagnostic_with_manifests(self):
        report, code = parity.compare(self.a, self.b, self.root / 'missing')
        self.assertEqual(2, code)
        self.assertTrue(report['exactEqual'])
        report, code = parity.compare(self.a, self.b, self.root / 'mixed', diagnostic_only=True,
                                     reference_manifest=self.json_file('a.json', self.manifest(self.a)))
        self.assertEqual(2, code)
        self.assertFalse(report['evidenceEligible'])

    def test_bad_roi_definitions(self):
        valid = {'name': 'one', 'x': 0, 'y': 0, 'width': 1, 'height': 1}
        invalid = [[valid, valid], {}, [dict(valid, x=-1)], [dict(valid, width=5)],
                   [dict(valid, height=0)], [dict(valid, x=0.5)], [dict(valid, y=True)],
                   [dict(valid, mask=True)], [dict(valid, name='')], [{'name': 'one'}]]
        for value in invalid:
            with self.subTest(value=value):
                report, code, _ = self.run_pair(rois=value)
                self.assertEqual(2, code)
                self.assertTrue(report['exactEqual'])
                self.assertFalse(report['evidenceEligible'])

    def test_paired_manifest_rois_and_conflicts(self):
        a, b = self.manifest(self.a), self.manifest(self.b)
        roi = {'name': 'whole', 'x': 0, 'y': 0, 'width': 4, 'height': 3}
        a['rois'] = b['rois'] = [roi]
        report, code, _ = self.run_pair(manifests=(a, b))
        self.assertEqual(0, code)
        self.assertEqual(12, report['rois'][0]['area'])
        self.assertEqual(2, self.run_pair(manifests=(a, b), rois=[roi])[1])
        b['rois'] = []
        self.assertEqual(2, self.run_pair(manifests=(a, b))[1])

    def test_duplicate_json_keys_rejected(self):
        path = self.root / 'duplicate.json'
        path.write_text('{"schemaVersion": 1, "schemaVersion": 2}')
        with self.assertRaisesRegex(ValueError, 'duplicate JSON key'):
            parity.read_json(path)
        path.write_text('{"time": 1e999}')
        with self.assertRaisesRegex(ValueError, 'non-finite JSON number'):
            parity.read_json(path)

    def test_cli_exit_codes_and_inputs_not_mutated(self):
        before = self.a.read_bytes(), self.b.read_bytes()
        a = self.json_file('a.json', self.manifest(self.a))
        b = self.json_file('b.json', self.manifest(self.b))
        for code, flags in ((0, ['--reference-manifest', str(a), '--candidate-manifest', str(b)]),
                            (2, []), (3, ['--diagnostic-only'])):
            with contextlib.redirect_stdout(io.StringIO()):
                actual = parity.main([str(self.a), str(self.b), '--output', str(self.root / f'cli-{code}'), *flags])
            self.assertEqual(code, actual)
        result = subprocess.run([sys.executable, str(Path(parity.__file__)), str(self.a), str(self.b),
                                 '--output', str(self.root / 'subprocess'), '--diagnostic-only'],
                                capture_output=True, text=True)
        self.assertEqual(3, result.returncode, result.stderr)
        self.assertFalse(json.loads(result.stdout)['evidenceEligible'])
        self.assertEqual(before, (self.a.read_bytes(), self.b.read_bytes()))
        self.png('b.png', [(0, 0, 0, 0)] * 12)
        self.json_file('b.json', self.manifest(self.b))
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, parity.main([str(self.a), str(self.b), '--output', str(self.root / 'cli-1'),
                                            '--reference-manifest', str(a), '--candidate-manifest', str(b)]))
        before = self.a.read_bytes(), self.b.read_bytes()
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(2, parity.main([str(self.a), str(self.b), '--output', str(self.root)]))
        self.assertEqual(before, (self.a.read_bytes(), self.b.read_bytes()))


if __name__ == '__main__':
    unittest.main()
