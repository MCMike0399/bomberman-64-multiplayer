import json
from pathlib import Path
import tempfile
import unittest

import builder_restore


class PreserveLaterState(unittest.TestCase):
    def test_recovery_restores_missing_plan_without_rewinding_journal(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);backup=root/'backup';state=root/'state';backup.mkdir();state.mkdir()
            plan={'account':'524329886851','region':'us-east-1','controllerRunId':'123','controllerRunAttempt':'1','controllerJobId':'campaign'}
            (backup/'plan.json').write_text(json.dumps(plan));(backup/'creation-manifest.jsonl').write_text('old intent')
            (state/'creation-manifest.jsonl').write_text('later observed resource identities')
            result=builder_restore.restore(state,backup,{'GITHUB_RUN_ID':'123','GITHUB_RUN_ATTEMPT':'1','GITHUB_JOB':'campaign'})
            self.assertIn('plan.json',result)
            self.assertEqual((state/'creation-manifest.jsonl').read_text(),'later observed resource identities')
            self.assertTrue((state/'state-restored.json').exists())

    def test_another_job_cannot_supply_deletion_authority(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);(root/'plan.json').write_text(json.dumps({'account':'524329886851','region':'us-east-1','controllerRunId':'old'}))
            with self.assertRaises(ValueError):builder_restore.restore(root/'state',root,{'GITHUB_RUN_ID':'new'})
