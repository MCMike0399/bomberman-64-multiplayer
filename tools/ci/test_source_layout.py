"""Contracts for the C# source layout check."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('source_layout', Path(__file__).with_name('source_layout.py'))
source_layout = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(source_layout)


class SourceLayoutTests(unittest.TestCase):
    def check(self, files):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name, text in files.items():
                (root / name).parent.mkdir(parents=True, exist_ok=True)
                (root / name).write_text(text)
            return source_layout.check(root, [Path(name) for name in files])

    def test_mirrored_namespaces_and_concern_partials_pass(self):
        self.assertEqual([], self.check({
            'src/Bomber.Game/Simulation/Stages/StageLayout.cs':
                'namespace Bomber.Game.Simulation.Stages;\npublic sealed class StageLayout {}\n',
            'src/Bomber.Game/Simulation/BlastSession.cs':
                'namespace Bomber.Game.Simulation;\npublic sealed partial class BlastSession {}\n',
            'src/Bomber.Game/Simulation/BlastSession/Bombs/BlastSession.Kicks.cs':
                'namespace Bomber.Game.Simulation;\npublic sealed partial class BlastSession {}\n',
            'scripts/input_devices/ControllerHub.cs':
                'namespace BlastCircuit.InputDevices;\npublic partial class ControllerHub {}\n',
            'scripts/ui/hud/BattleHud.cs': 'namespace BlastCircuit.UI.Hud;\npublic partial class BattleHud {}\n',
            'scripts/testing/PlaytestDriver.cs': 'namespace BlastCircuit.Testing;\npublic partial class PlaytestDriver {}\n',
            'scripts/testing/journeys/stages/RockGardenPlaytest.cs':
                'namespace BlastCircuit.Testing;\npublic partial class PlaytestDriver {}\n',
            'src/Bomber.Server/Program.cs': 'var app = 1;\n',
            'tests/Bomber.Game.Tests/Bombs/KickTimingChecks.cs': 'static class KickTimingChecks {}\n',
        }))

    def test_mismatched_namespace_name_and_godot_dependency_fail(self):
        failures = self.check({
            'src/Bomber.Game/Online/RoomRules.cs': 'namespace Bomber.Game;\npublic static class RoomRules {}\n',
            'src/Bomber.Game/Simulation/BlastSim.cs':
                'namespace Bomber.Game.Simulation;\npublic enum CellKind {}\npublic sealed class CellDef {}\n',
            'src/Bomber.Client/Sessions/Probe.cs':
                'using Godot;\nnamespace Bomber.Client.Sessions;\npublic sealed class Probe {}\n',
        })
        self.assertIn('src/Bomber.Game/Online/RoomRules.cs: namespace Bomber.Game should be Bomber.Game.Online', failures)
        self.assertTrue(any(f.startswith('src/Bomber.Game/Simulation/BlastSim.cs: file name') for f in failures))
        self.assertTrue(any('must not depend on Godot' in f for f in failures))

    def test_partial_must_follow_main_namespace_and_folder_casing(self):
        failures = self.check({
            'src/Bomber.Game/Simulation/BlastSession.cs':
                'namespace Bomber.Game.Simulation;\npublic sealed partial class BlastSession {}\n',
            'src/Bomber.Game/Simulation/BlastSession/Bots/BlastSession.Bots.cs':
                'namespace Bomber.Game.Simulation.BlastSession.Bots;\npublic sealed partial class BlastSession {}\n',
            'scripts/Arena/ArenaView.cs': 'namespace BlastCircuit.Arena;\npublic partial class ArenaView {}\n',
        })
        self.assertTrue(any('BlastSession.Bots.cs: namespace' in f and 'should be Bomber.Game.Simulation' in f
                            for f in failures))
        self.assertIn('scripts/Arena: scripts/ folders must be lowercase snake_case', failures)

    def test_crowded_folders_are_reported(self):
        files = {f'tests/Bomber.Game.Tests/Check{i}.cs': f'static class Check{i} {{}}\n'
                 for i in range(source_layout.MAX_FILES + 1)}
        self.assertEqual([f'tests/Bomber.Game.Tests: {source_layout.MAX_FILES + 1} C# files exceed '
                          f'{source_layout.MAX_FILES}; group them by concern'], self.check(files))


if __name__ == '__main__':
    unittest.main()
