import json
from pathlib import Path
import tempfile
import unittest

import builder_report


class MissingCleanupIsRed(unittest.TestCase):
    def test_lost_runner_retains_exact_ids_as_unverified_leftovers(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);(root/'creation-manifest.jsonl').write_text(json.dumps({'event':'CREATED','type':'AWS::EC2::Instance','id':'i-owned'})+'\n')
            result=builder_report.report(root)
        self.assertFalse(result['passed']);self.assertTrue(result['operatorCleanupRequired'])
        self.assertEqual(result['leftovers'][0]['id'],'i-owned')

    def test_an_intent_without_a_response_is_not_a_clean_run(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);(root/'creation-manifest.jsonl').write_text(json.dumps({'event':'INTENT','type':'AWS::CloudFormation::Stack','id':'exact-run'})+'\n')
            self.assertFalse(builder_report.report(root)['passed'])

    def test_actual_empty_rehearsal_audit_is_distinct_from_gpu_qualification(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);(root/'cleanup-audit-only.json').write_text(json.dumps({'passed':True,'leftovers':[]}))
            result=builder_report.report(root)
        self.assertTrue(result['passed']);self.assertFalse(result['hardwareQualified'])
