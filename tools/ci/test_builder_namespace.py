import unittest
import builder_namespace as names


class BuilderNamespace(unittest.TestCase):
    def test_scope_is_exact_and_disjoint_from_the_existing_watchdog(self):
        self.assertEqual(names.PREFIX,'sancus-builder-')
        self.assertEqual(names.TAG_PREFIX,'sancus-builder:')
        self.assertEqual(names.RUN_TAG,'sancus-builder:run')
        self.assertEqual(names.EXPIRY_TAG,'sancus-builder:expires')
        self.assertFalse(names.RUN_TAG.startswith('sancus:'))

    def test_every_generated_aws_name_and_tag_excludes_old_terms(self):
        run=names.run_id('20261030','09e454b','1234abcd')
        values=[names.IDENTITY_STACK,names.CI_ROLE,names.BOUNDARY,run,names.RUN_TAG,names.EXPIRY_TAG,
                names.instance_name('windows')]
        values += [names.resource_name(run,suffix)for suffix in ['agent','profile','sg','windows-expiry']]
        for value in values:
            with self.subTest(value=value):
                self.assertNotIn('gpu',value);self.assertNotIn('blast',value)
                self.assertFalse(value.startswith('sancus:'))

    def test_the_one_windows_instance_is_named_exactly_sancus_builder(self):
        # 2026-11-13: Windows only, one instance; ownership stays on the run tag, not the Name.
        self.assertEqual(names.instance_name('windows'),'sancus-builder')
        self.assertEqual(names.instance_name(),'sancus-builder')
        self.assertEqual(names.TARGETS,{'windows-x64':'Windows'});self.assertEqual(names.SYSTEMS,('Windows',))
        with self.assertRaises(ValueError):names.instance_name('linux')

    def test_name_tag_allows_exactly_the_bare_instance_name_and_keeps_the_prefix_rules(self):
        def template(name_tag=None,**properties):
            tags=[{'Key':names.RUN_TAG,'Value':'x'}]+([{'Key':'Name','Value':name_tag}]if name_tag is not None else[])
            return {'Resources':{'R':{'Type':'AWS::EC2::Instance','Properties':{**properties,'Tags':tags}}}}
        for value in ('sancus-builder','sancus-builder-windows'):
            with self.subTest(value=value):names.validate_aws_names(template(value))
        for value in ('sancus-builderx','sancus-builder-gpu','sancus-builder-blast','sancus','sancus-runner','Sancus-builder'):
            with self.subTest(value=value),self.assertRaises(ValueError):names.validate_aws_names(template(value))
        # Named properties (roles, groups, buckets, schedules) still require the prefix.
        for key in ('RoleName','GroupName','Name','BucketName'):
            with self.subTest(key=key),self.assertRaises(ValueError):names.validate_aws_names(template(**{key:'sancus-builder'}))

    def test_unrelated_or_broad_names_never_authorize_builder_resources(self):
        for run in ['sancus-runner','sancus-prod','sancus-builder-ci','blast-gpu-20261029']:
            with self.subTest(run=run),self.assertRaises(ValueError):names.resource_name(run,'agent')
        run=names.run_id('20261030','09e454b','1234abcd')
        for suffix in ['gpu-agent','blast-agent','../role']:
            with self.subTest(suffix=suffix),self.assertRaises(ValueError):names.resource_name(run,suffix)


if __name__=='__main__':unittest.main()
