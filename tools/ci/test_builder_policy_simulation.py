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


class PermissionAuditCases(unittest.TestCase):
    def test_every_audited_permission_has_positive_and_negative_simulations(self):
        import builder_policy_simulation as simulation
        rows=simulation.permission_audit_cases()
        self.assertIn(('owned-sg-ingress-never','ec2:AuthorizeSecurityGroupIngress',False),[(row[0],row[1],row[4])for row in rows])
        for action in ('scheduler:TagResource','scheduler:GetSchedule','iam:GetPolicy',
                       'iam:ListAttachedRolePolicies','s3:TagResource'):
            outcomes={expected for _,name,_,_,expected in rows if name==action}
            self.assertEqual(outcomes,{True,False},action)
        self.assertIn(('no-ssm-secrets','ssm:GetParameter'),[(row[0],row[1])for row in rows])
