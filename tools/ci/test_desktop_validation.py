"""Keep incomplete multiplayer, stale catalogs and discarded renderer errors out of release receipts."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

import desktop_packages as desktop
import desktop_validation as validation
from test_desktop_packages import write_desktop, add_display_restart, SHA, RELEASE


class DesktopValidationTests(unittest.TestCase):
    def test_roster_growth_preserves_all_cases_within_thirty_jobs(self):
        for count in range(21, 26):
            tours = tuple('tour-%02d' % i for i in range(1, count + 1))
            parts = validation.software_parts(tours)
            self.assertLessEqual(sum(len(groups) for _, groups in parts.values()), 30)
            for target, (_, groups) in parts.items():
                # Grouping shares only a runner: cases remain distinct entries
                # consumed one at a time by the fresh-process desktop executor.
                cases = [case for group in groups for case in group]
                expected = ['controller-popups', *tours, 'menu-preview', 'water-surface']
                with self.subTest(tours=count, target=target):
                    self.assertCountEqual(cases, expected)
                    self.assertEqual(len(cases), len(set(cases)))
                    self.assertEqual([case for case in cases if case in tours], list(tours))
                    self.assertTrue(all(isinstance(case, str) for case in cases))

    def test_graphics_and_saved_startup_cannot_be_omitted_from_promotion(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_desktop(directory)
            item = desktop.names('windows-x64')
            for name in (item['graphical'], item['startup']):
                original = (directory / name).read_text()
                (directory / name).write_text('{}')
                with self.subTest(name=name), self.assertRaises(RuntimeError):
                    desktop.inspect(directory, SHA, RELEASE)
                (directory / name).write_text(original)
            startup = json.loads((directory / item['startup']).read_text())
            startup['cases'].pop()
            with self.assertRaisesRegex(RuntimeError, 'startup matrix'):
                validation.inspect_startup(startup, 'windows-x64', SHA, RELEASE, desktop.digest(directory / item['archive']))

    def test_promotion_rejects_partial_stale_or_erroneous_gameplay_evidence(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_desktop(directory)
            target = 'windows-x64'
            original = json.loads((directory / desktop.names(target)['smoke']).read_text())
            settings = desktop.TARGETS[target]
            validation.inspect_receipt(original, settings, SHA, RELEASE)
            for field, value in (('runtimeErrors', ['ERROR: failed native extension']),
                                 ('completedScenarios', []), ('catalogSha256', '0' * 64),
                                 ('release', {}), ('assertions', 0), ('platform', 'Linux')):
                broken = copy.deepcopy(original)
                broken['cases'][1][field] = value
                with self.subTest(field=field), self.assertRaises(RuntimeError):
                    validation.inspect_receipt(broken, settings, SHA, RELEASE)
            missing_network = copy.deepcopy(original)
            missing_network['cases'] = missing_network['cases'][:1]
            with self.assertRaisesRegex(RuntimeError, 'multiplayer'):
                validation.inspect_receipt(missing_network, settings, SHA, RELEASE)

    def test_frame_stall_diagnostics_never_qualify_a_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_desktop(directory)
            settings = desktop.TARGETS['windows-x64']
            receipt = json.loads((directory / desktop.names('windows-x64')['smoke']).read_text())
            receipt['cases'][0]['frameStallMs'] = 800
            with self.assertRaises(RuntimeError):
                validation.inspect_receipt(receipt, settings, SHA, RELEASE)

    def test_headless_suite_can_only_include_headless_capable_journeys(self):
        scenarios = validation.catalog()
        for scenario in validation.HEADLESS:
            self.assertTrue(scenarios[scenario]['headless'], scenario)
        self.assertIn('all', validation.GRAPHICAL)
        self.assertEqual(['controller-popups', 'menu-preview', 'water-surface'],
                         [case for case in validation.SHARDED_GRAPHICAL if case not in validation.TOURS])

    def test_graphical_report_requires_positive_renderer_adapter_and_capture_evidence(self):
        settings = desktop.TARGETS['windows-x64']
        report = {'passed': True, 'platform': 'Windows', 'release': dict(RELEASE, revision=SHA),
                  'scenario': 'all', 'catalogSha256': validation.catalog_digest(),
                  'completedScenarios': validation.expanded('all', validation.catalog()),
                  'assertions': 200, 'checks': [{'passed': True}] * 200, 'runtimeErrors': [], 'screenshotMode': 'graphical',
                  'renderingDriver': 'vulkan', 'renderingMethod': 'forward_plus',
                  'adapter': 'Target GPU', 'screenshots': ['capture.png']}
        add_display_restart(report)
        validation.inspect_report(report, 'all', settings, SHA, RELEASE, True)
        for field, value in (('renderingDriver', 'opengl3'), ('adapter', ''), ('screenshots', [])):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                validation.inspect_report(dict(report, **{field: value}), 'all', settings, SHA, RELEASE, True)


if __name__ == '__main__':
    unittest.main()
