import unittest

import builder_trigger as trigger


class CurrentMainOnly(unittest.TestCase):
    def setUp(self):self.run={'head_repository':{'full_name':trigger.REPOSITORY},'head_sha':'a'*40,'head_branch':'main',
                            'path':'.github/workflows/ci.yml','event':'push','status':'completed','conclusion':'success'}
    def test_exact_successful_current_main_is_eligible(self):self.assertTrue(trigger.eligible(self.run,'a'*40))
    def test_fork_pr_old_sha_and_failed_run_are_never_eligible(self):
        for change in ({'head_repository':{'full_name':'fork/repo'}},{'event':'pull_request'},
                       {'head_sha':'b'*40},{'conclusion':'failure'},{'head_branch':'other'}):
            with self.subTest(change=change):self.assertFalse(trigger.eligible({**self.run,**change},'a'*40))

    def test_docs_only_success_and_expired_native_inputs_do_not_queue_paid_work(self):
        self.assertFalse(trigger.complete_artifacts([{'name':'public-receipts'}]))
        rows=[{'name':name,'expired':False}for name in trigger.REQUIRED_ARTIFACTS]
        self.assertTrue(trigger.complete_artifacts(rows))
        rows[0]['expired']=True;self.assertFalse(trigger.complete_artifacts(rows))
