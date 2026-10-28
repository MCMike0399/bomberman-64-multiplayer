"""Feature-negative controls must preserve focus and reveal the intended effect."""
import unittest

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
