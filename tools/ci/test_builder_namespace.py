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
                names.instance_name('windows'),names.instance_name('windows',names.REMAINING_SLOT)]
        values += [names.resource_name(run,suffix)for suffix in ['agent','profile','sg','windows-expiry','remain-expiry']]
        for value in values:
            with self.subTest(value=value):
                self.assertNotIn('gpu',value);self.assertNotIn('blast',value)
                self.assertFalse(value.startswith('sancus:'))

    def test_the_core_lane_keeps_the_bare_sancus_builder_name(self):
        # 2026-11-13: Windows only; ownership stays on the run tag, not the Name.
        # 2026-11-17: two parallel Windows lanes; windows-x64 stays the only qualified target.
        self.assertEqual(names.instance_name('windows'),'sancus-builder')
        self.assertEqual(names.instance_name(),'sancus-builder')
        self.assertEqual(names.instance_name('windows',names.REMAINING_SLOT),'sancus-builder-remaining')
        self.assertEqual(names.TARGETS,{'windows-x64':'Windows'});self.assertEqual(names.SYSTEMS,('Windows',))
        with self.assertRaises(ValueError):names.instance_name('linux')
        with self.assertRaises(ValueError):names.instance_name('windows','windows-x64')

    def test_two_ordered_windows_lane_slots_with_fitting_child_names(self):
        self.assertEqual(list(names.SLOTS),['windows-x64-core','windows-x64-remaining'])
        self.assertEqual([row['lane']for row in names.SLOTS.values()],['core','remaining'])
        self.assertTrue(all(row['target']=='windows-x64'and row['os']=='Windows'for row in names.SLOTS.values()))
        self.assertEqual([names.slot_logical(slot,'Instance')for slot in names.SLOTS],['WindowsInstance','WindowsRemainingInstance'])
        self.assertEqual([names.slot_logical(slot,'Expiry')for slot in names.SLOTS],['WindowsExpiry','WindowsRemainingExpiry'])
        self.assertEqual(names.slot_for_logical('WindowsRemainingInstance'),names.REMAINING_SLOT)
        self.assertIsNone(names.slot_for_logical('LinuxInstance'))
        # New campaigns use a 7-character SHA and a 16-hex nonce: 48-character run IDs.
        run=names.run_id('20261117','5ba336f','1234567890abcdef');self.assertEqual(len(run),48)
        for row in names.SLOTS.values():
            with self.subTest(expiry=row['expiry']):self.assertLessEqual(len(names.child_name(run,row['expiry'])),64)
        with self.assertRaisesRegex(ValueError,'64'):names.child_name(run,'remaining-lane-expiry')
        self.assertEqual(names.LANES,{'core':names.CORE_SLOT,'remaining':names.REMAINING_SLOT})

    def test_pre_lane_guest_objects_are_keyed_by_the_exact_instance(self):
        self.assertEqual(names.instance_object('i-0123456789abcdef0','bootstrap-ready.json'),
                         'results/instances/i-0123456789abcdef0/bootstrap-ready.json')
        for instance,name in (('../x','worker-exit.json'),('i-0123456789abcdef0','manifest.json'),('windows-x64','worker-log.bin')):
            with self.subTest(instance=instance,name=name),self.assertRaises(ValueError):names.instance_object(instance,name)
        self.assertNotIn('instances',names.SLOTS)

    def test_handoff_carries_exactly_the_core_files_the_gate_reads(self):
        import inspect,gpu_release_gates
        source=inspect.getsource(gpu_release_gates.stage)
        self.assertIn("core/'campaign-result.json'",source);self.assertIn("core/'high-vulkan/visual-parity-native.json'",source)
        self.assertEqual(source.count('core/'),2)
        self.assertEqual(names.HANDOFF_FILES,('gpu-campaign-windows-x64-core/campaign-result.json',
                                              'gpu-campaign-windows-x64-core/high-vulkan/visual-parity-native.json'))

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
