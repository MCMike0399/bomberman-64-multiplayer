import datetime
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import native_execution as execution


class DisposableContext(unittest.TestCase):
    def task(self):return {'schemaVersion':1,'executionContext':'aws-ssm-interactive-desktop',
        'account':'524329886851','region':'us-east-1','source':'a'*40,'target':'linux-x64',
        'runId':'sancus-builder-20261030-8762d11-1234567890abcdef','instanceId':'i-owned',
        'expiresAtUtc':(datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(hours=1)).isoformat()}

    def check(self,task,identity=None):
        with tempfile.TemporaryDirectory()as directory:
            path=Path(directory)/'task.json';path.write_text(json.dumps(task))
            with patch.dict(os.environ,{'SANCUS_BUILDER_TASK':str(path)},clear=True),patch.object(execution,'instance_identity',return_value=identity or{
                    'accountId':'524329886851','region':'us-east-1','instanceId':'i-owned','imageId':'ami-pinned'}),patch.object(execution.subprocess,'check_output',return_value='a'*40+'\n'):
                return execution.context('a'*40,'linux-x64',directory)

    def test_owned_ssm_host_has_distinct_provenance_without_actions_flag(self):
        result=self.check(self.task());self.assertEqual(result['kind'],'aws-ssm-interactive-desktop')
        self.assertEqual(result['instanceId'],'i-owned');self.assertNotIn('token',json.dumps(result).lower())

    def test_mismatched_actual_host_never_qualifies(self):
        with self.assertRaisesRegex(RuntimeError,'IMDS'):self.check(self.task(),{'accountId':'524329886851','region':'us-east-1','instanceId':'i-protected'})

    def test_stale_source_target_and_expiry_are_refused(self):
        for key,value in [('source','b'*40),('target','windows-x64'),('expiresAtUtc','2020-01-01T00:00:00Z')]:
            with self.subTest(key=key),self.assertRaises(RuntimeError):self.check({**self.task(),key:value})

    def test_ordinary_local_machine_is_not_a_disposable_host(self):
        with patch.dict(os.environ,{},clear=True),self.assertRaises(RuntimeError):execution.context('a'*40,'linux-x64','.')


if __name__=='__main__':unittest.main()
