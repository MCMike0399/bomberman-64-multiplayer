import json
from pathlib import Path
import struct
import tempfile
import unittest

import depth_probe


class DepthProbeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)
        self.metadata = {'diagnosticOnly': True, 'width': 384, 'height': 256, 'bytes': 384*256*16,
                         'planes': [{'pixel': [192,128], 'worldDepth': 10}],
                         'engine': {'hash': depth_probe.ENGINE_HASH, 'string': '4.7.2-stable (official)'}}
        (self.path/'depth-resources.json').write_text(json.dumps(self.metadata))
        (self.path/'depth-samples.bin').write_bytes(struct.pack('<4f', .9,.9,.45,1)*384*256)

    def test_retains_resolve_defect_without_turning_collection_into_qualification(self):
        result = depth_probe.inspect(self.path)
        self.assertAlmostEqual(result['planeCenters'][0]['resolvedMinusSample0'], -.45, places=6)
        self.assertGreater(result['planeCenters'][0]['reconstructedWorldDepth'], 50)

    def test_rejects_truncated_or_unwritten_compute_output(self):
        for data in [b'bad', bytes(self.metadata['bytes'])]:
            (self.path/'depth-samples.bin').write_bytes(data)
            with self.assertRaises(ValueError): depth_probe.inspect(self.path)

    def test_wrong_engine_cannot_claim_pinned_resource_evidence(self):
        self.metadata['engine']['hash'] = 'wrong'
        (self.path/'depth-resources.json').write_text(json.dumps(self.metadata))
        with self.assertRaisesRegex(ValueError, 'pinned release'): depth_probe.inspect(self.path)
