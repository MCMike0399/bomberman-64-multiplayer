import unittest
import release_gpu_policy as policy


class MajorOnlyGraphicsPolicy(unittest.TestCase):
    def test_minor_major_and_initial_releases_require_gpu(self):
        for previous,candidate in [('0.44.1','0.45.0'),('0.45.2','1.0.0'),(None,'0.1.0')]:
            with self.subTest(previous=previous,candidate=candidate):self.assertTrue(policy.decide(previous,candidate)['requiresGpu'])

    def test_patch_has_no_gpu_dependency(self):
        result=policy.decide('0.45.0','0.45.1','same','same')
        self.assertFalse(result['requiresGpu']);self.assertIsNone(result['warning'])

    def test_changed_rendering_in_patch_warns_without_blocking(self):
        result=policy.decide('0.45.0','0.45.1','old','new')
        self.assertFalse(result['requiresGpu']);self.assertIn('minor bump',result['warning'])

    def test_override_is_explicit_and_scoped_to_one_version(self):
        self.assertFalse(policy.decide('0.44.1','0.45.0',override={'version':'0.45.0','kind':'patch'})['requiresGpu'])
        with self.assertRaises(ValueError):policy.decide('0.44.1','0.45.0',override={'version':'0.44.2','kind':'patch'})

    def test_same_or_older_version_cannot_evade_policy(self):
        for candidate in ['0.45.0','0.44.9']:
            with self.assertRaises(ValueError):policy.decide('0.45.0',candidate)


if __name__=='__main__':unittest.main()
