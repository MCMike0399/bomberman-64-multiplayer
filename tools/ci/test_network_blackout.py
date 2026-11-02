import json
from pathlib import Path
import tempfile
import unittest
from network_blackout import validate_measurement


class MeasurementContractTests(unittest.TestCase):
    def receipt(self, **changes):
        return {'measurementCompleted': True, 'scenario': 'blackout-10',
                'checks': {'feedback': False, 'control': True}, 'conforms': False,
                'visibleSamples': [{'client': 2, 'serverTime': 1}],
                'maxVisibleSnapshotGapSeconds': [.1, .1, 10], **changes}

    def verify(self, data, strict=False):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'measurement.json'
            path.write_text(json.dumps(data))
            return validate_measurement(path, 'blackout-10', strict)

    def test_baseline_retains_red_behavior_without_claiming_resilience(self):
        evidence = self.verify(self.receipt())
        self.assertFalse(evidence['conforms'])
        self.assertEqual(evidence['failedBehaviorChecks'], ['feedback'])
        with self.assertRaises(RuntimeError): self.verify(self.receipt(), strict=True)

    def test_contradictory_missing_or_wrong_case_receipt_is_fatal(self):
        for change in ({'conforms': True}, {'measurementCompleted': False}, {'checks': {}},
                       {'scenario': 'normal'}, {'visibleSamples': []}, {'maxVisibleSnapshotGapSeconds': [0]}):
            with self.subTest(change=change), self.assertRaises(RuntimeError): self.verify(self.receipt(**change))

    def test_fixed_candidate_requires_all_behavior_assertions(self):
        self.assertTrue(self.verify(self.receipt(checks={'feedback': True, 'control': True}, conforms=True), strict=True)['conforms'])


if __name__ == '__main__': unittest.main()
