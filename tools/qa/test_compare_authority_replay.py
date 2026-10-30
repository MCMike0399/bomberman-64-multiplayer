"""Replay comparison must catch altered state, lost ticks and dishonest coverage."""
import copy
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('authority_replay', Path(__file__).with_name('compare_authority_replay.py'))
replay = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(replay)


class AuthorityReplayTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.counter = 0

    def fixture(self):
        header = {'kind': 'header', 'schemaVersion': 1, 'shared': {
            'recipeId': 'tiny', 'recipeSha256': 'a' * 64, 'sourceIdentity': 'b' * 40, 'engineIdentity': 'pinned',
            'release': {'version': '1.0.0', 'build': '1', 'protocol': 6, 'token': '6/1.0.0/1'},
            'cases': [{'id': 'motion', 'lastTick': 4}]}, 'provenance': {
            'platform': 'macOS', 'os': 'fixture', 'architecture': 'arm64', 'runtime': '10.0.fixture', 'engine': 'pinned',
            'authorityAssemblySha256': 'c' * 64, 'entryAssemblySha256': 'd' * 64}}
        start = {'kind': 'case-start', 'id': 'motion', 'initialTick': 2, 'lastTick': 4,
                 'options': {'HumanPlayers': 1}, 'stage': {'t': 'stage', 'k': 0, 'd': {}},
                 'snapshot': {'t': 'state', 'k': 2, 'd': {}}}
        ticks = [{'kind': 'tick', 'id': 'motion', 'tick': tick, 'inputs': [{'slot': 0, 'input': {'x': 1}}],
                  'messages': [{'t': 'state', 'k': tick, 'd': {'players': [{'x': 0.1, 'alive': True}]}}],
                  'latestSnapshotTick': tick, 'canary': {'sin': '3f000000', 'randomInt': tick}} for tick in (3, 4)]
        end = {'kind': 'case-end', 'id': 'motion', 'lastTick': 4, 'snapshots': 2}
        return [header, start, *ticks, end]

    def write(self, name, records, complete=True, sort=True):
        path = self.root / name
        lines = [json.dumps(record, separators=(',', ':'), sort_keys=sort).encode() + b'\n' for record in records]
        if complete:
            footer = {'kind': 'complete', 'caseCount': 1, 'totalTicks': 2, 'dataRecords': len(records) - 1,
                      'dataSha256': hashlib.sha256(b''.join(lines[1:])).hexdigest()}
            lines.append(json.dumps(footer).encode() + b'\n')
        path.write_bytes(gzip.compress(b''.join(lines), mtime=0))
        return path

    def compare(self, left, right):
        self.counter += 1
        return replay.compare(left, right, self.root / f'output-{self.counter}')

    def test_object_order_and_platform_binary_provenance_can_differ(self):
        a = self.fixture(); b = copy.deepcopy(a)
        b[0]['provenance'].update(platform='Windows', architecture='x64', authorityAssemblySha256='e' * 64)
        report, code = self.compare(self.write('a.gz', a), self.write('b.gz', b, sort=False))
        self.assertEqual(0, code); self.assertTrue(report['observableEqual']); self.assertTrue(report['canaryEqual'])
        self.assertIn('authorityAssemblySha256', report['permittedProvenanceDifferences'])

    def test_first_float_difference_keeps_field_tick_and_bits(self):
        a = self.fixture(); b = copy.deepcopy(a); b[2]['messages'][0]['d']['players'][0]['x'] = 0.10000001
        report, code = self.compare(self.write('a.gz', a), self.write('b.gz', b))
        self.assertEqual(1, code); difference = report['firstObservableDifference']
        self.assertEqual('3', difference['tick']); self.assertEqual('$.messages[0].d.players[0].x', difference['path'])
        self.assertNotEqual(difference['expected']['asFloat32Bits'], difference['actual']['asFloat32Bits'])
        self.assertTrue(report['canaryEqual'])

    def test_math_canary_difference_is_separate_from_authority_outputs(self):
        a = self.fixture(); b = copy.deepcopy(a); b[2]['canary']['sin'] = '3f000001'
        report, code = self.compare(self.write('a.gz', a), self.write('b.gz', b))
        self.assertEqual(1, code); self.assertTrue(report['observableEqual']); self.assertFalse(report['canaryEqual'])
        self.assertEqual('$.canary.sin', report['firstCanaryDifference']['path'])

    def test_meaningful_message_order_is_preserved(self):
        a = self.fixture(); a[2]['messages'].append({'t': 'appearance', 'k': 3, 'd': {'index': 1}})
        b = copy.deepcopy(a); b[2]['messages'].reverse()
        self.assertEqual(1, self.compare(self.write('a.gz', a), self.write('b.gz', b))[1])

    def test_missing_duplicate_or_reordered_ticks_are_invalid_even_with_new_digest(self):
        for operation in ('missing', 'duplicate', 'reordered'):
            with self.subTest(operation=operation):
                a = self.fixture(); b = copy.deepcopy(a)
                if operation == 'missing': del b[2]
                elif operation == 'duplicate': b.insert(3, copy.deepcopy(b[2]))
                else: b[2], b[3] = b[3], b[2]
                self.assertEqual(2, self.compare(self.write('a.gz', a), self.write('b.gz', b))[1])

    def test_missing_output_and_false_snapshot_coverage_are_invalid(self):
        a = self.fixture(); b = copy.deepcopy(a); b[2]['messages'] = []
        self.assertEqual(2, self.compare(self.write('a.gz', a), self.write('b.gz', b))[1])
        b = copy.deepcopy(a); b[-1]['snapshots'] = 99
        self.assertEqual(2, self.compare(self.write('a.gz', a), self.write('c.gz', b))[1])

    def test_recipe_source_or_release_mismatch_never_qualifies(self):
        for field, value in (('recipeSha256', 'f' * 64), ('sourceIdentity', 'e' * 40), ('engineIdentity', 'other')):
            a = self.fixture(); b = copy.deepcopy(a); b[0]['shared'][field] = value
            self.assertEqual(2, self.compare(self.write('a.gz', a), self.write('b.gz', b))[1])

    def test_completion_digest_truncation_and_duplicate_keys_are_rejected(self):
        a = self.write('a.gz', self.fixture())
        incomplete = self.write('incomplete.gz', self.fixture(), complete=False)
        self.assertEqual(2, self.compare(a, incomplete)[1])
        raw = gzip.decompress(a.read_bytes()).replace(b'0.1', b'0.2', 1)
        (self.root / 'tampered.gz').write_bytes(gzip.compress(raw))
        self.assertEqual(2, self.compare(a, self.root / 'tampered.gz')[1])
        (self.root / 'truncated.gz').write_bytes(a.read_bytes()[:-10])
        self.assertEqual(2, self.compare(a, self.root / 'truncated.gz')[1])
        with self.assertRaisesRegex(ValueError, 'duplicate JSON key'): replay.parse('{"kind":"tick","kind":"complete"}')

    def test_negative_zero_is_not_silently_rounded_away(self):
        difference = replay.first_difference(replay.parse('-0'), replay.parse('0'))
        self.assertEqual('80000000', difference['expected']['asFloat32Bits'])
        self.assertEqual('00000000', difference['actual']['asFloat32Bits'])

    def test_existing_output_cannot_overwrite_inputs(self):
        a = self.write('a.gz', self.fixture())
        before = a.read_bytes()
        with self.assertRaises(FileExistsError): replay.compare(a, a, self.root)
        self.assertEqual(before, a.read_bytes())


if __name__ == '__main__':
    unittest.main()
