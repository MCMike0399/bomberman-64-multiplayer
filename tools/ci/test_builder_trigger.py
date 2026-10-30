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

    def selected(self, mode, hold):
        import contextlib, json, os, runpy, tempfile
        from pathlib import Path
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary)
            def output(command, **kwargs):
                if command[:2]==['git','rev-parse']:return 'a'*40+'\n'
                if 'git/ref/heads/main' in command[2]:return json.dumps({'object':{'sha':'a'*40}})
                if '/artifacts?' in command[2]:return json.dumps([{'artifacts':[{'name':n}for n in trigger.REQUIRED_ARTIFACTS]}])
                if '/actions/runs/' in command[2]:return json.dumps(self.run)
                raise AssertionError(command)
            env={'REQUESTED_MODE':mode,'VERIFICATION_RUN':'123','SANCUS_BUILDER_PAID_HOLD':hold,
                 'GITHUB_OUTPUT':str(root/'outputs'),'GITHUB_STEP_SUMMARY':str(root/'summary')}
            with contextlib.chdir(root),patch.dict(os.environ,env),patch.object(trigger.subprocess,'check_output',side_effect=output),patch.object(trigger.release_gpu_policy,'derive',return_value={'requiresGpu':True}):
                runpy.run_path(str(Path(trigger.__file__)),run_name='__main__')
            return (root/'outputs').read_text(),(root/'summary').read_text()

    def test_paid_hold_refuses_before_any_environment_job_is_selected(self):
        output,summary=self.selected('qualify','true')
        self.assertIn('run=false',output)
        self.assertIn('hold',summary.lower())

    def test_free_modes_are_available_under_the_paid_hold(self):
        for mode in ('audit','rehearse'):
            with self.subTest(mode=mode):
                output,_=self.selected(mode,'true')
                self.assertIn('run=true',output)
                self.assertIn('approval-plan={}',output)

    def test_unheld_selector_discloses_bound_plan_before_approval(self):
        import json
        output,summary=self.selected('qualify','false')
        self.assertIn('run=true',output)
        row=next(line for line in output.splitlines()if line.startswith('approval-plan='))
        plan=json.loads(row.split('=',1)[1])
        self.assertEqual(plan['source'],'a'*40)
        self.assertEqual(plan['verificationRunId'],'123')
        self.assertEqual(len(plan['instances']),2)
        self.assertIn('$5.29',summary)
        for item in plan['instances']:self.assertIn(item['ami'],summary)
