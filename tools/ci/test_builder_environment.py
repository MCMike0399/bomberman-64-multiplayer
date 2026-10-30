import copy
import unittest

import builder_environment as environment


def settings():
    return ({'deployment_branch_policy':{'protected_branches':False,'custom_branch_policies':True},
             'protection_rules':[{'type':'branch_policy'}]},
            {'branch_policies':[{'name':'main','type':'branch'}]},
            {'use_default':False,'include_claim_keys':environment.CLAIMS,'use_immutable_subject':True,
             'sub_claim_prefix':'repo:MCMike0399@40257172/bomberman-64-multiplayer@1426399566'})


class ApprovalBoundary(unittest.TestCase):
    def test_exact_user_main_and_workflow_claims_pass(self):
        self.assertTrue(environment.verify(*settings())['passed'])

    def test_no_protection_rules_beyond_the_branch_policy_also_pass(self):
        env,branches,oidc=settings();env['protection_rules']=[]
        self.assertTrue(environment.verify(env,branches,oidc)['passed'])

    def test_any_human_reviewer_or_wait_rule_fails_closed(self):
        for rules in ([{'type':'required_reviewers','reviewers':[{'type':'User','reviewer':{'id':environment.REVIEWER_ID}}]}],
                      [{'type':'required_reviewers','reviewers':[{'type':'User','reviewer':{'id':1}}]}],
                      [{'type':'wait_timer','wait_timer':5}]):
            env,branches,oidc=settings();env['protection_rules']=rules
            with self.assertRaises(RuntimeError):environment.verify(env,branches,oidc)

    def test_tag_or_wildcard_or_extra_branch_never_passes(self):
        for rows in ([{'name':'main','type':'tag'}],[{'name':'*','type':'branch'}],
                     [{'name':'main','type':'branch'},{'name':'feature','type':'branch'}]):
            env,branches,oidc=settings();branches['branch_policies']=rows
            with self.assertRaises(RuntimeError):environment.verify(env,branches,oidc)

    def test_environment_only_subject_or_recycled_identity_never_passes(self):
        for change in ({'include_claim_keys':['repo','context']},{'use_default':True},
                       {'use_immutable_subject':False},{'sub_claim_prefix':'repo:other/repo'}):
            env,branches,oidc=settings();oidc.update(change)
            with self.assertRaises(RuntimeError):environment.verify(env,branches,oidc)


if __name__=='__main__':unittest.main()
