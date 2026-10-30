import unittest
from pathlib import Path
import tempfile
from unittest.mock import patch

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

    def test_permissions_smoke_allows_owned_stack_calls_and_only_dry_run_launches(self):
        for service,operation in sorted(guard.SMOKE_MUTATIONS-{('ec2','run-instances')}):
            with self.subTest(operation=operation):
                self.assertEqual(guard.validate(self.arguments(service,operation),smoke=True),self.arguments(service,operation))
        launch=self.arguments('ec2','run-instances')
        self.assertEqual(guard.validate(launch+['--dry-run','--count','1'],smoke=True)[-3],'--dry-run')
        for arguments in (launch,launch+['--count','1'],launch+['--dry-run','--no-dry-run']):
            with self.subTest(arguments=arguments),self.assertRaises(ValueError):guard.validate(arguments,smoke=True)
        for service,operation in [('ec2','terminate-instances'),('ec2','create-security-group'),('iam','create-role'),
                                  ('scheduler','create-schedule'),('ssm','send-command'),('cloudformation','update-stack')]:
            with self.subTest(operation=operation),self.assertRaises(ValueError):guard.validate(self.arguments(service,operation),smoke=True)
        with self.assertRaises(ValueError):guard.validate(launch+['--dry-run'])

    def test_smoke_install_bakes_its_policy_into_the_guard(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);environment={'GITHUB_ENV':str(root/'env'),'GITHUB_PATH':str(root/'path'),'BUILDER_REAL_AWS':'/actual/aws'}
            with patch.dict(guard.os.environ,environment):
                self.assertIn('--smoke-policy',guard.install(root/'smoke',smoke=True).read_text())
                self.assertNotIn('--smoke-policy',guard.install(root/'plain').read_text())

    def test_implicit_or_foreign_profile_and_region_are_blocked(self):
        for args in (['ec2','describe-instances'],['--profile','other','--region','us-east-1','sts','get-caller-identity']):
            with self.assertRaises(ValueError):guard.validate(args)

    def test_hosted_install_persists_only_real_cli_path_not_credentials(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);environment={'GITHUB_ENV':str(root/'env'),'GITHUB_PATH':str(root/'path'),'BUILDER_REAL_AWS':'/actual/aws'}
            with patch.dict(guard.os.environ,environment):result=guard.install(root/'bin')
            self.assertTrue(result.is_file());self.assertIn('BUILDER_REAL_AWS=/actual/aws',(root/'env').read_text())
            self.assertNotIn('AWS_SECRET_ACCESS_KEY',(root/'env').read_text())


if __name__=='__main__':unittest.main()
