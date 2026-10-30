import unittest

import authority_replay


class ObservablePolicy(unittest.TestCase):
    def test_canary_only_difference_stays_raw_failed_but_observable_equality_passes(self):
        item={'exitCode':1,'compatibleInputs':True,'observableEqual':True,'canaryEqual':False}
        self.assertFalse(authority_replay.comparison_passes(item))
        self.assertTrue(authority_replay.comparison_passes(item,True))

    def test_actual_observable_difference_or_incompatible_inputs_always_fail(self):
        for item in ({'exitCode':1,'compatibleInputs':True,'observableEqual':False},
                     {'exitCode':2,'compatibleInputs':False,'observableEqual':True}):
            self.assertFalse(authority_replay.comparison_passes(item,True))
