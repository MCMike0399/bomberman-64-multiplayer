import unittest

import builder_guard as guard

KEY = guard.key('0.45.0', 'f' * 64)
CLEAN = {'passed': True, 'resourceCleanupVerified': True, 'leftovers': [], 'errors': [], 'operatorCleanupRequired': False}


def attempt(identity, conclusion, status='completed', key=KEY):
    return {'id': identity, 'status': status, 'conclusion': conclusion, 'key': key}


class OncePerVersionAndFingerprint(unittest.TestCase):
    def test_first_attempt_and_other_fingerprints_are_allowed(self):
        self.assertIsNone(guard.dedupe([], KEY, automatic=True))
        other = guard.key('0.45.0', 'e' * 64)
        self.assertIsNone(guard.dedupe([attempt(1, 'failure', key=other)], KEY, automatic=True))

    def test_passing_qualification_is_never_repeated_even_by_an_operator(self):
        for automatic in (True, False):
            reason = guard.dedupe([attempt(2, 'failure'), attempt(1, 'success')], KEY, automatic, diagnosed='2')
            self.assertIn('passing qualification already exists', reason)

    def test_failed_attempt_is_never_retried_automatically(self):
        for conclusion in ('failure', 'cancelled', 'timed_out'):
            self.assertIn('needs diagnosis', guard.dedupe([attempt(1, conclusion)], KEY, automatic=True, diagnosed='1'))

    def test_operator_retry_must_name_the_latest_diagnosed_failure(self):
        attempts = [attempt(4, 'failure'), attempt(3, 'cancelled')]
        self.assertIn('diagnosed-failure-run=4', guard.dedupe(attempts, KEY, automatic=False))
        self.assertIn('diagnosed-failure-run=4', guard.dedupe(attempts, KEY, automatic=False, diagnosed='3'))
        self.assertIsNone(guard.dedupe(attempts, KEY, automatic=False, diagnosed='4'))

    def test_running_attempt_blocks_a_duplicate(self):
        self.assertIn('still running', guard.dedupe([attempt(5, None, status='in_progress')], KEY, automatic=False, diagnosed='5'))

    def test_key_requires_version_and_full_fingerprint(self):
        for version, fingerprint in (('', 'f' * 64), ('0.45.0', 'f' * 63), ('0.45.0', None)):
            with self.assertRaises(ValueError):
                guard.key(version, fingerprint)


class FailClosedOnCleanup(unittest.TestCase):
    def check(self, artifacts, outcome=CLEAN, status='completed'):
        runs = [{'id': 9, 'status': 'completed'}, {'id': 8, 'status': status}, {'id': 7, 'status': 'completed'}]
        table = {9: [{'name': 'builder-oidc-audit'}], 8: artifacts,
                 7: [{'name': 'builder-outcome-7-1'}]}
        reads = []
        def read(identity, name):
            reads.append((identity, name)); return outcome
        return guard.outcome_refusal(runs, lambda identity: table[identity], read), reads

    def test_latest_builder_run_with_a_clean_outcome_passes_and_audit_runs_are_skipped(self):
        reason, reads = self.check([{'name': 'builder-rehearsal-8-1'}, {'name': 'builder-outcome-8-1'}, {'name': 'builder-outcome-8-2'}])
        self.assertIsNone(reason)
        self.assertEqual(reads, [(8, 'builder-outcome-8-2')])

    def test_any_leftover_unverified_id_error_or_operator_cleanup_refuses(self):
        for change in ({'leftovers': [{'type': 'AWS::EC2::Instance', 'id': 'i-1', 'finalState': 'UNVERIFIED_AFTER_RUNNER_OR_PHASE_FAILURE'}]},
                       {'passed': False}, {'resourceCleanupVerified': False}, {'errors': [{'error': 'x'}]},
                       {'operatorCleanupRequired': True}, {'leftovers': None}):
            with self.subTest(change=change):
                reason, _ = self.check([{'name': 'builder-outcome-8-1'}], {**CLEAN, **change})
                self.assertIn('not a verified zero-leftover cleanup', reason)

    def test_missing_or_expired_outcome_and_running_builder_refuse(self):
        reason, _ = self.check([{'name': 'builder-intent-8-1'}, {'name': 'builder-state-8-1-final'}])
        self.assertIn('no independent outcome report', reason)
        reason, _ = self.check([{'name': 'builder-stopped-8-1'}, {'name': 'builder-outcome-8-1', 'expired': True}])
        self.assertIn('no independent outcome report', reason)
        reason, _ = self.check([{'name': 'builder-rehearsal-8-1'}], status='in_progress')
        self.assertIn('still running', reason)


class PermissionsSmokeHistory(unittest.TestCase):
    def test_smoke_outcome_is_the_latest_builder_outcome_and_never_a_paid_attempt(self):
        runs = [{'id': 9, 'status': 'completed'}, {'id': 8, 'status': 'completed'}]
        table = {9: [{'name': 'builder-smoke-intent-9-1'}, {'name': 'builder-smoke-9-1'}, {'name': 'builder-state-9-1-final'},
                     {'name': 'builder-outcome-9-1'}],
                 8: [{'name': 'builder-intent-8-1'}, {'name': 'builder-outcome-8-1'}]}
        reads = []
        unclean = {**CLEAN, 'passed': False}
        def read(identity, name):
            reads.append((identity, name)); return unclean if identity == 9 else CLEAN
        self.assertIn('not a verified zero-leftover', guard.outcome_refusal(runs, table.get, read))
        self.assertEqual(reads, [(9, 'builder-outcome-9-1')])
        self.assertIsNone(guard.outcome_refusal(runs, table.get, lambda identity, name: CLEAN))
        # Only builder-intent-* counts as paid allocation needing a ledger receipt.
        self.assertIsNone(guard.spend_refusal(runs[:1], {'recordedRuns': []}, 99, table.get))

    def test_smoke_without_an_outcome_report_refuses(self):
        runs = [{'id': 9, 'status': 'completed'}]
        reason = guard.outcome_refusal(runs, {9: [{'name': 'builder-smoke-intent-9-1'}]}.get, lambda *args: CLEAN)
        self.assertIn('no independent outcome report', reason)


class FailClosedOnSpend(unittest.TestCase):
    def test_unrecorded_paid_intent_refuses_and_recorded_or_current_runs_do_not(self):
        runs = [{'id': 3}, {'id': 2}, {'id': 1}]
        table = {3: [{'name': 'builder-intent-3-1'}], 2: [{'name': 'builder-rehearsal-2-1'}], 1: [{'name': 'builder-intent-1-1'}]}
        artifacts = lambda identity: table[identity]
        self.assertIn('1', guard.spend_refusal(runs, {'recordedRuns': []}, 3, artifacts))
        self.assertIsNone(guard.spend_refusal(runs, {'recordedRuns': [{'workflowRunId': '1', 'estimateUsd': 5}]}, 3, artifacts))
        self.assertIn('3', guard.spend_refusal(runs, {'recordedRuns': [{'workflowRunId': '1', 'estimateUsd': 5}]}, 99, artifacts))


class EndToEndSelection(unittest.TestCase):
    def history(self, runs, artifacts, files):
        def gh(path):
            if path.endswith('workflows/gpu-qualification.yml/runs?per_page=100'):
                return [{'workflow_runs': runs}]
            identity = int(path.split('runs/')[1].split('/')[0])
            return [{'artifacts': artifacts.get(identity, [])}]
        def download(identity, artifact, filename):
            if (identity, filename) not in files: raise FileNotFoundError(filename)
            return files[(identity, filename)]
        return gh, download

    def test_spend_cleanup_and_dedupe_are_all_consulted_and_current_run_is_excluded(self):
        runs = [{'id': 20, 'status': 'in_progress', 'created_at': '2026-11-01T00:00:00Z'},
                {'id': 11, 'status': 'completed', 'conclusion': 'failure', 'created_at': '2026-10-31T00:00:00Z'},
                {'id': 10, 'status': 'completed', 'conclusion': 'success', 'created_at': '2026-10-30T00:00:00Z'}]
        artifacts = {20: [{'name': 'builder-approval-plan'}],
                     11: [{'name': 'builder-approval-plan'}, {'name': 'builder-intent-11-1'}, {'name': 'builder-outcome-11-1'}],
                     10: [{'name': 'builder-rehearsal-10-1'}, {'name': 'builder-outcome-10-1'}]}
        files = {(11, 'builder-outcome.json'): CLEAN, (11, guard.KEY_FILE): KEY}
        gh, download = self.history(runs, artifacts, files)
        ledger = {'recordedRuns': [{'workflowRunId': '11', 'estimateUsd': 4.1}]}
        self.assertIn('needs diagnosis', guard.refusal(KEY, True, '', ledger, 20, gh, download))
        self.assertIsNone(guard.refusal(KEY, False, '11', ledger, 20, gh, download))
        self.assertIn('lack a recorded cost receipt', guard.refusal(KEY, False, '11', {'recordedRuns': []}, 20, gh, download))
        files[(11, 'builder-outcome.json')] = {**CLEAN, 'leftovers': [{'type': 'AWS::S3::Bucket', 'id': 'b'}]}
        self.assertIn('zero-leftover', guard.refusal(KEY, False, '11', ledger, 20, gh, download))

    def test_earlier_plan_without_a_key_fails_closed(self):
        runs = [{'id': 5, 'status': 'completed', 'conclusion': 'failure', 'created_at': '2026-10-31T00:00:00Z'}]
        gh, download = self.history(runs, {5: [{'name': 'builder-approval-plan'}]}, {})
        self.assertIn('no qualification key', guard.refusal(KEY, True, '', {'recordedRuns': []}, 6, gh, download))


if __name__ == '__main__':
    unittest.main()
