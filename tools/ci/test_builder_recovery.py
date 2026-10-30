import json
from pathlib import Path
import tempfile
import unittest

import builder_journal as journal
import builder_recovery as recovery


class PriorRunRecovery(unittest.TestCase):
    def state(self,root,name):
        state=Path(root)/name;state.mkdir()
        plan={'account':'524329886851','region':'us-east-1','runId':'sancus-builder-20261030-8762d11-1234567890abcdef'}
        (state/'plan.json').write_text(json.dumps(plan))
        journal.append(state/'creation-manifest.jsonl',plan,'CREATED','AWS::EC2::Instance','i-owned',{})
        return state

    def test_exact_id_and_tag_arn_map_to_the_same_retained_state(self):
        with tempfile.TemporaryDirectory()as root:
            state=self.state(root,'owned')
            findings=[{'id':'i-owned'},{'id':'arn:aws:ec2:us-east-1:524329886851:instance/i-owned'}]
            self.assertEqual(recovery.match_states(findings,[state]),[state.resolve()])

    def test_unrelated_similar_name_or_unjournaled_resource_never_reaches_cleanup(self):
        with tempfile.TemporaryDirectory()as root:
            state=self.state(root,'owned');calls=[]
            with self.assertRaisesRegex(ValueError,'no prefix/tag-only'):
                recovery.recover([{'id':'sancus-builder-20261030-other'}],[state],lambda value:calls.append(value))
            self.assertEqual(calls,[])

    def test_ambiguous_state_copies_do_not_authorize_deletion(self):
        with tempfile.TemporaryDirectory()as root:
            states=[self.state(root,name)for name in ('one','two')]
            with self.assertRaises(ValueError):recovery.match_states([{'id':'i-owned'}],states)

    def test_successful_prior_cleanup_still_requires_new_discovery(self):
        with tempfile.TemporaryDirectory()as root:
            state=self.state(root,'owned')
            result=recovery.recover([{'id':'i-owned'}],[state],lambda value:{'resourcesGone':True})
            self.assertFalse(result['newLaunchAuthorized'])


if __name__=='__main__':unittest.main()
