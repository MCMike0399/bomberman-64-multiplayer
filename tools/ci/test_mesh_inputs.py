import gzip
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import mesh_inputs


class MeshInputTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup); self.root = Path(self.temp.name)
        for name, counts in [('HELMET', {'helmet/vertex': 2, 'helmet/packed': 1}), ('PATCH', {'patch/vertex': 2, 'patch/packed': 1})]:
            patch = mock.patch.object(mesh_inputs,name,counts); patch.start(); self.addCleanup(patch.stop)

    def fixture(self, path, bit='3f000000', source='a'*40, provenance='native-a'):
        path.mkdir(); records=[{'sequence':0,'mesh':'','kind':'header','payload':{'schemaVersion':1,'shared':{'sourceIdentity':source,'engineIdentity':'pinned','recipeId':'six'},'provenance':{'device':provenance}}}]
        blob=bytearray(); meshes=[]
        def record(mesh,kind,payload):records.append({'sequence':len(records),'mesh':mesh,'kind':kind,'payload':payload})
        for index,name in enumerate(mesh_inputs.NAMES):
            prefix='helmet' if index<2 else 'patch';record(name,'begin',{})
            for i in range(2):record(name,prefix+'/vertex',{'position':[bit,'00000000']})
            data=b'packed'+bytes([index]);sha=hashlib.sha256(data).hexdigest()
            record(name,prefix+'/packed',{'offset':len(blob),'length':len(data),'sha256':sha});blob.extend(data)
            result={'name':name,'identicalToCapturedMesh':True,'generatedSha256':sha,'cachedSha256':sha}
            record(name,'end',result);meshes.append(result)
        record(mesh_inputs.NAMES[-1],'complete',{'meshes':6,'records':len(records)+1,'binaryBytes':len(blob)})
        self.save(path,records,blob,meshes);return records,blob,meshes

    def save(self,path,records,blob,meshes):
        for i,row in enumerate(records):row['sequence']=i
        if records[-1]['kind']=='complete':records[-1]['payload']['records']=len(records)
        raw=gzip.compress(('\n'.join(json.dumps(r)for r in records)+'\n').encode());(path/'mesh-inputs.jsonl.gz').write_bytes(raw);(path/'mesh-inputs.bin').write_bytes(blob)
        (path/'mesh-inputs-summary.json').write_text(json.dumps({'records':len(records),'traceSha256':hashlib.sha256(raw).hexdigest(),'binarySha256':hashlib.sha256(blob).hexdigest(),'meshes':meshes}))

    def test_complete_equal_inputs_allow_different_device_provenance(self):
        self.fixture(self.root/'a');self.fixture(self.root/'b',provenance='native-b')
        self.assertEqual(mesh_inputs.compare(self.root/'a',self.root/'b',self.root/'out'),0)

    def test_raw_float_bits_remain_different(self):
        self.fixture(self.root/'a');self.fixture(self.root/'b',bit='3f000001')
        self.assertEqual(mesh_inputs.compare(self.root/'a',self.root/'b',self.root/'out'),1)
        report=json.loads((self.root/'out/report.json').read_text());self.assertIsNotNone(report['firstManagedDifference'])

    def test_missing_vertex_cannot_pass_recounted_footer(self):
        rows,blob,meshes=self.fixture(self.root/'a');rows.pop(2);self.save(self.root/'a',rows,blob,meshes)
        with self.assertRaisesRegex(ValueError,'Incomplete builder'):mesh_inputs.load(self.root/'a')

    def test_segment_corruption_cannot_pass_rehashed_outer_blob(self):
        rows,blob,meshes=self.fixture(self.root/'a');blob[0]^=1;self.save(self.root/'a',rows,blob,meshes)
        with self.assertRaisesRegex(ValueError,'segment hash'):mesh_inputs.load(self.root/'a')

    def test_missing_completion_fails(self):
        rows,blob,meshes=self.fixture(self.root/'a');rows.pop();self.save(self.root/'a',rows,blob,meshes)
        with self.assertRaisesRegex(ValueError,'Incomplete mesh trace'):mesh_inputs.load(self.root/'a')

    def test_cached_mismatch_cannot_be_hidden_by_true_flag(self):
        rows,blob,meshes=self.fixture(self.root/'a');meshes[0]['cachedSha256']='f'*64;self.save(self.root/'a',rows,blob,meshes)
        with self.assertRaisesRegex(ValueError,'changed captured'):mesh_inputs.load(self.root/'a')

    def test_different_source_is_incompatible(self):
        self.fixture(self.root/'a');self.fixture(self.root/'b',source='b'*40)
        with self.assertRaisesRegex(ValueError,'Incompatible'):mesh_inputs.compare(self.root/'a',self.root/'b',self.root/'out')
