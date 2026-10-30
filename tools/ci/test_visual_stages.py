"""Native scene selection and retained metadata are qualification inputs, not images."""
import json
import hashlib
import shutil
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import mesh_inputs
import native_cohort
import test_mesh_inputs

SCENES = (
    ('sunken-temple', 'full-sunken-temple-high-v2'),
    ('rock-garden', 'full-rock-garden-high-v2'),
    ('up-and-down', 'full-up-down-high-v2'),
    ('top-rules', 'full-top-rules-high-v2'),
    ('blizzard-battle', 'full-blizzard-battle-high-v2'),
    ('greedy-trap', 'full-greedy-trap-high-v2'),
    ('circuit-garden', 'full-circuit-garden-high-v2'),
)
LABELS = ('warmup', 'base-a', 'base-b', 'dof-off', 'reflection-off')


def scene_files(folder, stage, identity):
    folder.mkdir(parents=True, exist_ok=True)
    records = []
    for label in LABELS:
        inventory_path = folder / ('parity-' + label + '-inventory.json')
        binary_path = folder / ('parity-' + label + '-inventory.bin')
        if not inventory_path.exists():
            payload = stage.encode(); binary_path.write_bytes(payload)
            inventory_path.write_text(json.dumps({'binary': binary_path.name, 'unsupported': [], 'nodes': [
                {'data': {'offset': 0, 'length': len(payload), 'sha256': hashlib.sha256(payload).hexdigest()}}]}))
        binding = {'json': inventory_path.name, 'jsonSha256': hashlib.sha256(inventory_path.read_bytes()).hexdigest(),
                   'binary': binary_path.name, 'binarySha256': hashlib.sha256(binary_path.read_bytes()).hexdigest()}
        recipe = {'stage': identity, 'stageId': stage, 'preset': 'High', 'msaaSamples': 2, 'conditions': {'sceneId': identity}, 'authority': {'tick': 120},
                  'resourceInventory': binding, 'camera': {'position': [1, 2, 3]}, 'settings': {'quality': 'High'}, 'shaders': ['same-shader']}
        (folder / ('parity-' + label + '-recipe.json')).write_text(json.dumps(recipe))
        records.append({'label': label, 'recipe': recipe})
    (folder / 'visual-parity-observations.json').write_text(json.dumps(
        {'experimentStage': stage, 'noisePolicy': 'integer', 'inputsOnly': True, 'records': records}))


class StageSelection(unittest.TestCase):
    def test_collector_cli_exposes_stage_selection(self):
        result = subprocess.run([sys.executable, str(Path(__file__).with_name('visual_parity.py')), '--help'],
                                capture_output=True, text=True, check=True)
        self.assertIn('--stage', result.stdout)
        self.assertIn('circuit-garden', result.stdout)


class StageMetadata(unittest.TestCase):
    def test_every_selected_stage_has_the_same_recipe_and_condition_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            for stage, identity in SCENES:
                with self.subTest(stage=stage):
                    folder = Path(temporary) / stage
                    scene_files(folder, stage, identity)
                    recipe = mesh_inputs.scene(folder, stage)
                    self.assertEqual(recipe['stage'], identity)
                    self.assertEqual(recipe['stage'], recipe['conditions']['sceneId'])
                    self.assertEqual(recipe['stageId'], stage)

    def test_missing_recipe_cannot_qualify_a_scene(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary) / 'scene'
            scene_files(folder, 'rock-garden', 'full-rock-garden-high-v2')
            (folder / 'parity-base-b-recipe.json').unlink()
            with self.assertRaisesRegex(ValueError, 'Missing/invalid scene'):
                mesh_inputs.scene(folder, 'rock-garden')

    def test_repeating_the_old_up_down_label_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary) / 'scene'
            scene_files(folder, 'circuit-garden', 'full-circuit-garden-high-v2')
            observation = json.loads((folder / 'visual-parity-observations.json').read_text())
            for row in observation['records']:
                row['recipe']['stage'] = 'up-and-down'
                (folder / ('parity-' + row['label'] + '-recipe.json')).write_text(json.dumps(row['recipe']))
            (folder / 'visual-parity-observations.json').write_text(json.dumps(observation))
            with self.assertRaisesRegex(ValueError, 'Recipe stage differs'):
                mesh_inputs.scene(folder, 'circuit-garden')

    def test_missing_authority_inputs_cannot_qualify_matching_metadata(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary) / 'scene'
            scene_files(folder, 'rock-garden', 'full-rock-garden-high-v2')
            path = folder / 'visual-parity-observations.json'; observed = json.loads(path.read_text())
            for row in observed['records']:
                del row['recipe']['authority']
                (folder / ('parity-' + row['label'] + '-recipe.json')).write_text(json.dumps(row['recipe']))
            path.write_text(json.dumps(observed))
            with self.assertRaisesRegex(ValueError, 'Missing scene input'):
                mesh_inputs.scene(folder, 'rock-garden')


class NativeStageCohort(unittest.TestCase):
    def setUp(self):
        # Reuse the parser's small synthetic trace format; these are unit data,
        # never native/package or graphics receipts.
        self.fixture = test_mesh_inputs.MeshInputTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.root

    def cohort(self):
        source = self.root / 'source'
        for index, target in enumerate(('macos-arm64', 'windows-x64', 'linux-x64')):
            binding = {'passed': True, 'revision': 'a' * 40, 'archiveSha256': str(index + 1) * 64, 'downloadSha256': str(index + 4) * 64}
            replay = source / ('native-contracts-' + target) / 'replay'
            replay.mkdir(parents=True)
            (replay / 'authority-replay-native.json').write_text(json.dumps(binding))
            for stage, identity in SCENES:
                folder = source / ('native-contracts-' + target) / 'inputs' / stage
                folder.mkdir(parents=True)
                self.fixture.canonical_fixture(folder / 'run-0', payload=stage.encode())
                scene_files(folder / 'run-0', stage, identity)
                report = {'passed': True, 'graphicsQualified': False, 'nativeCI': True,
                          'target': target, 'stage': stage, 'revision': 'a' * 40,
                          'mode': 'mesh-inputs-diagnostic', 'errors': [],
                          'archiveSha256': binding['archiveSha256'], 'downloadSha256': binding['downloadSha256'],
                          'runs': [{'index': 0, 'exitCode': 0, 'assertions': 7, 'validationErrors': [], 'runtimeErrors': [],
                                    'meshInputsValidated': True, 'sceneMetadataValidated': True}]}
                (folder / 'mesh-inputs-native.json').write_text(json.dumps(report))
        return source

    def test_all_seven_require_three_matching_native_collections(self):
        result = native_cohort.compare_inputs(self.cohort(), self.root / 'result')
        self.assertTrue(result['passed'])
        self.assertFalse(result['graphicsQualified'])
        self.assertEqual([s['stage'] for s in result['stages']], [s[0] for s in SCENES])
        self.assertTrue(all(s['passed'] for s in result['stages']))
        self.assertEqual(len(result['inputs']), 14)

    def test_different_replay_source_cannot_qualify_matching_scene_files(self):
        source = self.cohort()
        for path in source.glob('*/replay/authority-replay-native.json'):
            report = json.loads(path.read_text()); report['revision'] = 'b' * 40
            path.write_text(json.dumps(report))
        result = native_cohort.compare_inputs(source, self.root / 'result')
        self.assertFalse(result['passed'])
        self.assertFalse(any(s['passed'] for s in result['stages']))

    def test_missing_stage_inventory_is_red_and_other_stages_are_still_compared(self):
        source = self.cohort()
        (source / 'native-contracts-linux-x64/inputs/rock-garden/run-0/parity-base-a-inventory.bin').unlink()
        result = native_cohort.compare_inputs(source, self.root / 'result')
        self.assertFalse(result['passed'])
        self.assertEqual([s['stage'] for s in result['stages'] if not s['passed']], ['rock-garden'])
        self.assertEqual(len(result['inputs']), 14)

    def test_absent_target_stage_cannot_be_counted_as_not_applicable(self):
        source = self.cohort()
        shutil.rmtree(source / 'native-contracts-windows-x64/inputs/circuit-garden')
        result = native_cohort.compare_inputs(source, self.root / 'result')
        self.assertFalse(result['passed'])
        self.assertEqual([s['stage'] for s in result['stages'] if not s['passed']], ['circuit-garden'])

    def test_one_changed_buffer_stays_red_even_with_honest_new_hashes(self):
        source = self.cohort()
        folder = source / 'native-contracts-linux-x64/inputs/greedy-trap/run-0'
        binary = folder / 'parity-base-a-inventory.bin'
        data = bytearray(binary.read_bytes()); data[0] ^= 1; binary.write_bytes(data)
        path = folder / 'parity-base-a-inventory.json'; inventory = json.loads(path.read_text())
        inventory['nodes'][0]['data']['sha256'] = hashlib.sha256(data).hexdigest()
        path.write_text(json.dumps(inventory))
        path = folder / 'parity-base-a-recipe.json'; recipe = json.loads(path.read_text())
        recipe['resourceInventory'].update(jsonSha256=hashlib.sha256((folder/'parity-base-a-inventory.json').read_bytes()).hexdigest(),
                                          binarySha256=hashlib.sha256(data).hexdigest())
        path.write_text(json.dumps(recipe))
        path = folder / 'visual-parity-observations.json'; observation = json.loads(path.read_text())
        next(r for r in observation['records'] if r['label'] == 'base-a')['recipe'] = recipe
        path.write_text(json.dumps(observation))
        result = native_cohort.compare_inputs(source, self.root / 'result')
        self.assertFalse(result['passed'])
        self.assertEqual([s['stage'] for s in result['stages'] if not s['passed']], ['greedy-trap'])

    def test_actual_game_state_difference_is_not_hidden_by_equal_mesh_bytes(self):
        source = self.cohort()
        folder = source / 'native-contracts-windows-x64/inputs/blizzard-battle/run-0'
        path = folder / 'parity-base-a-recipe.json'; recipe = json.loads(path.read_text())
        recipe['authority']['tick'] = 121; path.write_text(json.dumps(recipe))
        path = folder / 'visual-parity-observations.json'; observation = json.loads(path.read_text())
        next(r for r in observation['records'] if r['label'] == 'base-a')['recipe'] = recipe
        path.write_text(json.dumps(observation))
        result = native_cohort.compare_inputs(source, self.root / 'result')
        self.assertFalse(result['passed'])
        self.assertEqual([s['stage'] for s in result['stages'] if not s['passed']], ['blizzard-battle'])
