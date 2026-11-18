import unittest

import gpu_campaign


class QualificationBudgets(unittest.TestCase):
    def test_linux_full_suite_has_room_beyond_the_previous_whole_command_deadline(self):
        self.assertEqual(gpu_campaign.LINUX_GRAPHICAL_SUITE_BUDGET_SECONDS, 3600)

    def test_windows_remaining_lane_suite_budget_covers_the_measured_full_suite(self):
        # 2026-11-17: Windows runs the complete suite on its own parallel lane (Linux measured ~4,000 s).
        self.assertEqual(gpu_campaign.WINDOWS_GRAPHICAL_SUITE_BUDGET_SECONDS, 5400)
        import inspect
        self.assertIn('else WINDOWS_GRAPHICAL_SUITE_BUDGET_SECONDS', inspect.getsource(gpu_campaign.run))
        self.assertNotIn('else 1800', inspect.getsource(gpu_campaign.run))

    def test_only_canonical_windows_vulkan_high_is_a_qualifying_driver(self):
        self.assertTrue(gpu_campaign.high_driver_qualifies('windows-x64', 'vulkan'))
        self.assertFalse(gpu_campaign.high_driver_qualifies('windows-x64', 'd3d12'))
        self.assertTrue(gpu_campaign.high_driver_qualifies('linux-x64', 'vulkan'))

    def test_failed_d3d12_repeat_is_retained_but_vulkan_failure_blocks(self):
        run = {'exitCode': 0, 'validationErrors': [], 'runtimeErrors': []}
        technical, diagnostics = [], []
        gpu_campaign.record_high_verdict('windows-x64', 'd3d12',
                                         {'passed': False, 'runs': [run, run], 'errors': ['pixel repeat differs']},
                                         technical, diagnostics)
        self.assertEqual(technical, [])
        self.assertEqual(diagnostics, [{'driver': 'd3d12', 'scope': 'diagnostic-only', 'passed': False,
                                       'errors': ['pixel repeat differs']}])
        gpu_campaign.record_high_verdict('windows-x64', 'vulkan',
                                         {'passed': False, 'runs': [run, run], 'errors': ['strict repeat differs']},
                                         technical, diagnostics)
        self.assertEqual(technical, [False])
        self.assertFalse(all(technical))

    def test_runtime_or_validation_errors_still_fail_even_for_diagnostic_driver(self):
        run = {'exitCode': 0, 'validationErrors': [], 'runtimeErrors': ['ERROR: renderer failed']}
        with self.assertRaisesRegex(RuntimeError, 'capture execution failed'):
            gpu_campaign.record_high_verdict('windows-x64', 'd3d12',
                                             {'passed': False, 'runs': [run, run], 'errors': []}, [], [])

    def test_unknown_target_driver_cannot_be_silently_downgraded_to_diagnostic(self):
        run = {'exitCode': 0, 'validationErrors': [], 'runtimeErrors': []}
        with self.assertRaisesRegex(RuntimeError, 'Unexpected GPU target/driver classification'):
            gpu_campaign.record_high_verdict('windows-x64', 'opengl3',
                                             {'passed': True, 'runs': [run, run], 'errors': []}, [], [])


if __name__ == '__main__':
    unittest.main()
