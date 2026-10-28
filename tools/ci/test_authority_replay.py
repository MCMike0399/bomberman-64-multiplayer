"""Native receipts must bind every replay and cannot hide failed repetitions."""
import gzip
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

import authority_replay as native


class NativeReplayTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def stream(self, path, platform, changed=False):
        provenance = {'platform': platform, 'os': 'fixture', 'architecture': 'fixture', 'runtime': '10', 'engine': 'pinned',
                      'authorityAssemblySha256': 'c' * 64, 'entryAssemblySha256': 'd' * 64}
        records = [
            {'kind': 'header', 'schemaVersion': 1, 'shared': {'recipeId': 'tiny', 'recipeSha256': 'a' * 64,
             'sourceIdentity': 'b' * 40, 'engineIdentity': 'pinned', 'release': {'version': '1.0.0', 'build': '1', 'protocol': 6, 'token': '6/1.0.0/1'},
             'cases': [{'id': 'motion', 'lastTick': 3}]}, 'provenance': provenance},
            {'kind': 'case-start', 'id': 'motion', 'initialTick': 2, 'lastTick': 3, 'options': {'HumanPlayers': 1},
             'stage': {'t': 'stage', 'k': 0, 'd': {}}, 'snapshot': {'t': 'state', 'k': 2, 'd': {}}},
            {'kind': 'tick', 'id': 'motion', 'tick': 3, 'inputs': [{'slot': 0}], 'messages': [{'t': 'state', 'k': 3, 'd': {'x': 1 if changed else 0}}],
             'latestSnapshotTick': 3, 'canary': {'sin': '00000000'}},
            {'kind': 'case-end', 'id': 'motion', 'lastTick': 3, 'snapshots': 1}]
        lines = [json.dumps(record, separators=(',', ':')).encode() + b'\n' for record in records]
        footer = {'kind': 'complete', 'caseCount': 1, 'totalTicks': 1, 'dataRecords': 3,
                  'dataSha256': hashlib.sha256(b''.join(lines[1:])).hexdigest()}
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(gzip.compress(b''.join(lines) + json.dumps(footer).encode() + b'\n', mtime=0))
        return provenance

    def cohort(self):
        for target, platform in [('macos-arm64', 'macOS'), ('windows-x64', 'Windows'), ('linux-x64', 'Linux')]:
            report = {'schemaVersion': 1, 'mode': 'diagnostic-only', 'passed': True, 'target': target, 'revision': 'b' * 40, 'requestedRevision': 'b' * 40,
                      'release': {'version': '1.0.0', 'build': '1', 'protocolRevision': 6, 'admissionToken': '6/1.0.0/1'},
                      'archiveSha256': 'e' * 64, 'downloadSha256': 'f' * 64,
                      'assemblyInventory': {'Bomber.Game.dll': 'c' * 64, 'Bomberman64Multiplayer.dll': 'd' * 64}, 'runs': []}
            for index in range(2):
                relative = f'run-{index}/authority-replay.jsonl.gz'; path = self.root / target / relative
                provenance = self.stream(path, platform)
                report['runs'].append({'file': relative, 'fileSha256': native.desktop.digest(path), 'index': index, 'exitCode': 0,
                                       'assertions': 10, 'runtimeErrors': [], 'validationErrors': [], 'provenance': provenance})
            (self.root / target / 'authority-replay-native.json').write_text(json.dumps(report))

    def change_report(self, target, edit):
        path = self.root / target / 'authority-replay-native.json'; report = json.loads(path.read_text()); edit(report); path.write_text(json.dumps(report))

    def test_complete_cohort_revalidates_repeats_and_compares_all_targets(self):
        self.cohort(); self.assertEqual(0, native.aggregate(self.root))
        report = json.loads((self.root / 'authority-replay-comparison.json').read_text())
        self.assertTrue(report['passed']); self.assertEqual(['macos-arm64', 'windows-x64', 'linux-x64'], report['targets'])

    def test_changed_output_is_rejected_even_with_a_passed_native_receipt(self):
        self.cohort(); self.stream(self.root / 'windows-x64/run-0/authority-replay.jsonl.gz', 'Windows', changed=True)
        with self.assertRaisesRegex(RuntimeError, 'output hash differs'): native.aggregate(self.root)

    def test_false_repeat_success_is_rejected_after_hashes_are_updated(self):
        self.cohort(); path = self.root / 'windows-x64/run-1/authority-replay.jsonl.gz'
        self.stream(path, 'Windows', changed=True)
        self.change_report('windows-x64', lambda report: report['runs'][1].update(fileSha256=native.desktop.digest(path)))
        with self.assertRaisesRegex(RuntimeError, 'falsely reports repeat equality'): native.aggregate(self.root)

    def test_missing_repetition_and_wrong_archive_module_binding_are_rejected(self):
        self.cohort(); self.change_report('macos-arm64', lambda report: report['runs'].pop())
        with self.assertRaisesRegex(RuntimeError, 'Missing fixed'): native.aggregate(self.root)
        self.cohort(); self.change_report('macos-arm64', lambda report: report['assemblyInventory'].update({'Bomber.Game.dll': '0' * 64}))
        with self.assertRaisesRegex(RuntimeError, 'archived inventory'): native.aggregate(self.root)

    def test_updater_modules_are_excluded_from_the_main_archive_inventory(self):
        with zipfile.ZipFile(self.root / 'Blast-Circuit-windows-x64.zip', 'w') as archive:
            archive.writestr('root/data/Bomber.Game.dll', b'actual game')
            archive.writestr('root/data/Bomberman64Multiplayer.dll', b'actual entry')
            archive.writestr('root/updater/Bomber.Game.dll', b'different helper module')
        inventory = native.assembly_inventory(self.root, 'windows-x64')
        self.assertEqual({hashlib.sha256(b'actual game').hexdigest()}, inventory['Bomber.Game.dll'])
