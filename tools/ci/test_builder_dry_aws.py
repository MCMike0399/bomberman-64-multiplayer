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
