import unittest

import builder_dry_aws as guard


class NoMutationRehearsal(unittest.TestCase):
    def arguments(self,service,operation):return ['--profile','sancus','--region','us-east-1',service,operation]

    def test_read_and_simulation_operations_are_allowed(self):
        for service,operation in [('sts','get-caller-identity'),('ec2','describe-instances'),('iam','simulate-custom-policy')]:
            self.assertEqual(guard.validate(self.arguments(service,operation)),self.arguments(service,operation))

    def test_allocation_deletion_upload_and_update_are_blocked(self):
        for service,operation in [('cloudformation','create-stack'),('ec2','run-instances'),('ec2','terminate-instances'),
                                  ('s3api','put-object'),('s3api','delete-bucket'),('scheduler','update-schedule'),('ssm','send-command')]:
            with self.subTest(operation=operation),self.assertRaises(ValueError):guard.validate(self.arguments(service,operation))

    def test_implicit_or_foreign_profile_and_region_are_blocked(self):
        for args in (['ec2','describe-instances'],['--profile','other','--region','us-east-1','sts','get-caller-identity']):
            with self.assertRaises(ValueError):guard.validate(args)


if __name__=='__main__':unittest.main()
