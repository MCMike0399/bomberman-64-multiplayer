from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import native_cohort


class RetainedEvidenceBoundary(unittest.TestCase):
    def test_old_artifact_installs_are_not_copied_as_replay_evidence(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary)
            for target in ('macos-arm64','windows-x64','linux-x64'):
                replay=root/'source'/('native-contracts-'+target)/'replay'
                (replay/'installed/Blast Circuit.app').mkdir(parents=True)
                (replay/'installed/Blast Circuit.app/payload').write_bytes(b'redundant install')
                (replay/'actual-records.jsonl').write_text('original replay records')
            with patch.object(native_cohort.authority_replay,'aggregate',return_value=0),patch.object(native_cohort.mesh_inputs,'compare',return_value=0),patch.object(native_cohort.mesh_inputs,'compare_captured',return_value=0):
                result=native_cohort.aggregate(root/'source',root/'output')
            self.assertTrue(result['passed'])
            for target in ('macos-arm64','windows-x64','linux-x64'):
                replay=root/'output/replay'/target
                self.assertFalse((replay/'installed').exists())
                self.assertEqual((replay/'actual-records.jsonl').read_text(),'original replay records')
