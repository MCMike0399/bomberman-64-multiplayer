import unittest

import builder_policy_simulation as simulation


class PolicySimulation(unittest.TestCase):
    def test_actual_parameterized_aws_shape_uses_exact_child_decision(self):
        result={'EvaluationResults':[{'EvalActionName':'ec2:TerminateInstances',
                'EvalResourceName':'arn:aws:ec2:${Region}:${Account}:${ResourceType}/${ResourcePath}',
                'EvalDecision':'allowed','ResourceSpecificResults':[
                    {'EvalResourceName':'owned','EvalResourceDecision':'allowed'},
                    {'EvalResourceName':'protected','EvalResourceDecision':'implicitDeny'}]}]}
        self.assertEqual(simulation.decisions(result,'ec2:TerminateInstances','protected'),'implicitDeny')
        with self.assertRaises(RuntimeError):simulation.decisions(result,'ec2:TerminateInstances','missing')

    def test_ambiguous_results_are_not_proof(self):
        row={'EvalActionName':'x','EvalResourceName':'r','EvalDecision':'allowed'}
        with self.assertRaises(RuntimeError):simulation.decisions({'EvaluationResults':[row,row]},'x','r')

    def test_required_positive_and_negative_cases_exist(self):
        cases={row[0]:row for row in simulation.cases('i-protected')}
        self.assertTrue(cases['owned-termination'][-1])
        for name in ('protected-termination','untagged-termination','untagged-launch','larger-launch',
                     'old-watchdog-tag','old-run-tag','pass-controller','edit-controller','tag-existing-protected','create-unbounded-child'):
            self.assertFalse(cases[name][-1])
        self.assertIn('i-protected',cases['protected-termination'][2])


if __name__=='__main__':unittest.main()
