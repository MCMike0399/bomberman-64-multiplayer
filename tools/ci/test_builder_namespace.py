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
                names.instance_name('windows'),names.instance_name('linux')]
        values += [names.resource_name(run,suffix)for suffix in ['agent','profile','sg','linux-expiry','windows-expiry']]
        for value in values:
            with self.subTest(value=value):
                self.assertNotIn('gpu',value);self.assertNotIn('blast',value)
                self.assertFalse(value.startswith('sancus:'))

    def test_unrelated_or_broad_names_never_authorize_builder_resources(self):
        for run in ['sancus-runner','sancus-prod','sancus-builder-ci','blast-gpu-20261029']:
            with self.subTest(run=run),self.assertRaises(ValueError):names.resource_name(run,'agent')
        run=names.run_id('20261030','09e454b','1234abcd')
        for suffix in ['gpu-agent','blast-agent','../role']:
            with self.subTest(suffix=suffix),self.assertRaises(ValueError):names.resource_name(run,suffix)


if __name__=='__main__':unittest.main()
