import copy
import json
import unittest

import builder_controller_identity as controller
import builder_identity


class ScheduleCleanupDelta(unittest.TestCase):
    def test_only_the_existing_exact_dated_schedule_clause_changes(self):
        before=builder_identity.policy(staging=True);after=controller.policy()
        expected=copy.deepcopy(before)
        clause=next(row for row in expected['Statement']if row['Sid']=='ManageOwnedSchedule')
        clause['Action'].append('scheduler:UpdateSchedule')
        self.assertEqual(after,expected)
        self.assertEqual(clause['Resource'],'arn:aws:scheduler:us-east-1:524329886851:schedule/sancus-builder-20??????-*/sancus-builder-20??????-*')

    def test_boundary_fits_and_standing_identity_and_trust_are_unchanged(self):
        before=builder_identity.template(staging=True);after=controller.template()
        self.assertEqual(set(after['Resources']),{'Boundary','ControllerRole'})
        self.assertEqual(after['Resources']['ControllerRole']['Properties']['AssumeRolePolicyDocument'],
                         before['Resources']['ControllerRole']['Properties']['AssumeRolePolicyDocument'])
        self.assertLessEqual(len(json.dumps(after['Resources']['Boundary']['Properties']['PolicyDocument'],separators=(',',':'))),6144)


if __name__=='__main__':unittest.main()
