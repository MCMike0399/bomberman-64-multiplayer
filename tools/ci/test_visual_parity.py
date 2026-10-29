"""Feature-negative controls must preserve focus and reveal the intended effect."""
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import visual_parity


class VisualControlTests(unittest.TestCase):
    def report(self, focus=0, far=10, water=0):
        return {'encodingCompatible': True, 'rois': [
            {'rectangle': {'name': name}, 'differingPixels': count, 'exactEqual': count == 0}
            for name, count in [('focused-stone', focus), ('far-scenery', far), ('water', water)]]}

    def test_dof_control_requires_real_background_change_and_unchanged_focus(self):
        self.assertTrue(visual_parity.effect_control_valid(self.report(), 'dof', 3))
        self.assertFalse(visual_parity.effect_control_valid(self.report(far=0), 'dof', 3))
        self.assertFalse(visual_parity.effect_control_valid(self.report(focus=1), 'dof', 3))

    def test_missing_mirror_cannot_pass_a_global_difference_score(self):
        self.assertTrue(visual_parity.effect_control_valid(self.report(far=0, water=10), 'mirror', 3))
        self.assertFalse(visual_parity.effect_control_valid(self.report(focus=1000, far=0, water=0), 'mirror', 3))
        self.assertFalse(visual_parity.effect_control_valid(self.report(far=1, water=100), 'mirror', 3))

    def test_invalid_encoding_regions_or_diagnostic_contract_is_rejected(self):
        report = self.report(); report['encodingCompatible'] = False
        self.assertFalse(visual_parity.effect_control_valid(report, 'dof', 3))
        report = self.report(); report['rois'].pop()
        self.assertFalse(visual_parity.effect_control_valid(report, 'dof', 3))
        self.assertFalse(visual_parity.effect_control_valid(self.report(), 'dof', 0))
        self.assertFalse(visual_parity.effect_control_valid(self.report(), 'unknown', 3))


class VisualCollectionTests(unittest.TestCase):
    def test_failed_dof_retains_mirror_and_baseline_results(self):
        report = {'comparisons': [], 'errors': []}
        controls = [VisualControlTests().report(focus=1), VisualControlTests().report(far=0, water=0)]
        with tempfile.TemporaryDirectory() as temp, \
             mock.patch.object(visual_parity, 'pair', return_value=({}, 0)), \
             mock.patch.object(visual_parity.pixels, 'compare', side_effect=[(r, 3) for r in controls]):
            visual_parity.compare_captures(Path(temp), 'run-0', report)
        self.assertEqual(len(report['comparisons']), 4)
        self.assertEqual(len(report['errors']), 2)
        self.assertIn('dof', report['errors'][0])
        self.assertIn('mirror', report['errors'][1])

    def test_baseline_difference_is_failed_without_discarding_controls(self):
        report = {'comparisons': [], 'errors': []}
        controls = [VisualControlTests().report(), VisualControlTests().report(far=0, water=10)]
        with tempfile.TemporaryDirectory() as temp, \
             mock.patch.object(visual_parity, 'pair', side_effect=[({}, 0), ({}, 1)]), \
             mock.patch.object(visual_parity.pixels, 'compare', side_effect=[(r, 3) for r in controls]):
            visual_parity.compare_captures(Path(temp), 'run-0', report)
        self.assertEqual(len(report['comparisons']), 4)
        self.assertEqual(len(report['errors']), 1)
        self.assertIn('fresh-target', report['errors'][0])


class VulkanSampleSupportTests(unittest.TestCase):
    def inventory(self, color=(1,4), depth=(1,4)):
        result = 'deviceName = test GPU\n'
        for key, values in [('framebufferColorSampleCounts',color),('framebufferDepthSampleCounts',depth),
                            ('sampledImageColorSampleCounts',color),('sampledImageDepthSampleCounts',depth)]:
            result += f'\t{key}: count = {len(values)}\n' + ''.join(f'\t\tSAMPLE_COUNT_{v}_BIT\n' for v in values)
        return result

    def test_four_sample_maximum_does_not_imply_two_sample_support(self):
        report = visual_parity.vulkan_msaa_support(self.inventory())
        self.assertFalse(report['supportsRequired'])
        self.assertEqual(report['fallbackFromPinnedDriverPolicy'], 1)

    def test_required_sample_must_work_for_both_attachments_and_fetches(self):
        self.assertTrue(visual_parity.vulkan_msaa_support(self.inventory((1,2,4),(1,2,4)))['supportsRequired'])
        self.assertFalse(visual_parity.vulkan_msaa_support(self.inventory((1,2,4),(1,4)))['supportsRequired'])

    def test_ambiguous_or_truncated_capabilities_fail(self):
        for text in ['', self.inventory() + self.inventory(), self.inventory().replace('count = 2','count = 3',1)]:
            with self.assertRaises(ValueError): visual_parity.vulkan_msaa_support(text)


class HardwareAdapterTests(unittest.TestCase):
    def test_initialized_adapter_must_be_the_requested_hardware(self):
        self.assertTrue(visual_parity.hardware_adapter_matches({'adapter':'NVIDIA T4','deviceType':'DiscreteGpu'},'NVIDIA'))
        for adapter,kind in [('Microsoft Basic Render Driver','IntegratedGpu'),('NVIDIA WARP','IntegratedGpu'),('NVIDIA shim','Cpu'),('llvmpipe','Cpu'),('AMD device','DiscreteGpu')]:
            self.assertFalse(visual_parity.hardware_adapter_matches({'adapter':adapter,'deviceType':kind},'NVIDIA'))
