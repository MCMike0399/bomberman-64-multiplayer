import gzip
import hashlib
import json
import struct
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

    def canonical_fixture(self, path, bit='3f000000', payload=b'actual-scene-inputs'):
        rows, blob, meshes = self.fixture(path, bit=bit)
        for mesh in meshes: mesh['canonicalSha256'] = mesh['cachedSha256']
        self.save(path, rows, blob, meshes)
        summary = json.loads((path/'mesh-inputs-summary.json').read_text())
        summary['meshSource'] = 'canonical-mac-resource'
        (path/'mesh-inputs-summary.json').write_text(json.dumps(summary))
        (path/'parity-base-a-inventory.bin').write_bytes(payload)
        inventory = {'binary':'parity-base-a-inventory.bin','unsupported':[],
                     'nodes':[{'data':{'offset':0,'length':len(payload),'sha256':hashlib.sha256(payload).hexdigest()}}],
                     'resources':[{'id':0,'type':'ArrayMesh','properties':[]}]}
        (path/'parity-base-a-inventory.json').write_text(json.dumps(inventory))
        for label in ('warmup','base-b','dof-off','reflection-off'):
            (path/('parity-'+label+'-inventory.bin')).write_bytes(payload)
            copy=dict(inventory,binary='parity-'+label+'-inventory.bin')
            (path/('parity-'+label+'-inventory.json')).write_text(json.dumps(copy))


    def test_unused_builder_drift_stays_red_while_actual_canonical_inputs_match(self):
        self.canonical_fixture(self.root/'a'); self.canonical_fixture(self.root/'b',bit='3f000001')
        self.assertEqual(mesh_inputs.compare(self.root/'a',self.root/'b',self.root/'raw'),1)
        self.assertEqual(mesh_inputs.compare_captured(self.root/'a',self.root/'b',self.root/'captured'),0)

    def test_any_actual_scene_input_difference_remains_failure(self):
        self.canonical_fixture(self.root/'a'); self.canonical_fixture(self.root/'b',payload=b'actual-scene-inputt')
        self.assertEqual(mesh_inputs.compare_captured(self.root/'a',self.root/'b',self.root/'captured'),1)

    def test_corrupt_inventory_or_unbound_canonical_mesh_cannot_pass(self):
        self.canonical_fixture(self.root/'a'); self.canonical_fixture(self.root/'b')
        (self.root/'b/parity-base-a-inventory.bin').write_bytes(b'corrupt')
        with self.assertRaises(ValueError): mesh_inputs.compare_captured(self.root/'a',self.root/'b',self.root/'bad')
        summary=json.loads((self.root/'a/mesh-inputs-summary.json').read_text())
        summary['meshes'][0]['canonicalSha256']='0'*64
        (self.root/'a/mesh-inputs-summary.json').write_text(json.dumps(summary))
        with self.assertRaises(ValueError): mesh_inputs.load(self.root/'a')

    def test_cached_mismatch_cannot_be_hidden_by_true_flag(self):
        rows,blob,meshes=self.fixture(self.root/'a');meshes[0]['cachedSha256']='f'*64;self.save(self.root/'a',rows,blob,meshes)
        with self.assertRaisesRegex(ValueError,'builder/captured equality'):mesh_inputs.load(self.root/'a')

    def test_different_source_is_incompatible(self):
        self.fixture(self.root/'a');self.fixture(self.root/'b',source='b'*40)
        with self.assertRaisesRegex(ValueError,'Incompatible'):mesh_inputs.compare(self.root/'a',self.root/'b',self.root/'out')

    def test_matching_empty_inventories_are_not_complete_scene_inputs(self):
        for name in ('a', 'b'):
            folder = self.root / name; self.canonical_fixture(folder)
            (folder / 'parity-base-a-inventory.bin').write_bytes(b'')
            (folder / 'parity-base-a-inventory.json').write_text(json.dumps(
                {'binary': 'parity-base-a-inventory.bin', 'unsupported': [], 'nodes': [], 'resources': []}))
        with self.assertRaisesRegex(ValueError, 'Empty/incomplete scene inventory'):
            mesh_inputs.compare_captured(self.root/'a', self.root/'b', self.root/'out')

    def multimesh_fixture(self, path, floats):
        """One Transform3D+color MultiMesh of two instances (32 floats when complete)."""
        self.canonical_fixture(path)
        blob = bytearray(); properties = []
        def put(name, kind, raw):
            properties.append({'name': name, 'value': {'type': kind, 'data': {
                'offset': len(blob), 'length': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()}}}); blob.extend(raw)
        put('buffer', 'PackedFloat32Array', struct.pack('<ii', 32, len(floats)) + struct.pack('<%df' % len(floats), *floats))
        put('instance_count', 'Int', struct.pack('<ii', 2, 2))
        put('transform_format', 'Int', struct.pack('<ii', 2, 1))
        put('use_colors', 'Bool', struct.pack('<ii', 1, 1))
        put('use_custom_data', 'Bool', struct.pack('<ii', 1, 0))
        for label in mesh_inputs.TRIALS:
            (path/('parity-'+label+'-inventory.bin')).write_bytes(bytes(blob))
            (path/('parity-'+label+'-inventory.json')).write_text(json.dumps({
                'binary': 'parity-'+label+'-inventory.bin', 'unsupported': [], 'nodes': [{'mesh': {'reference': 0}}],
                'resources': [{'id': 0, 'type': 'MultiMesh', 'properties': properties}]}))

    def test_empty_multimesh_buffers_cannot_compare_equal(self):
        for name in ('a', 'b'): self.multimesh_fixture(self.root/name, [])
        with self.assertRaisesRegex(ValueError, 'complete instance buffer'):
            mesh_inputs.compare_captured(self.root/'a', self.root/'b', self.root/'out')

    def test_one_float_step_in_a_multimesh_basis_is_a_difference(self):
        rotation = [float(i) / 7 for i in range(32)]
        stepped = list(rotation); stepped[2] = struct.unpack('<f', struct.pack('<i', struct.unpack('<i', struct.pack('<f', stepped[2]))[0] + 1))[0]
        self.multimesh_fixture(self.root/'a', rotation); self.multimesh_fixture(self.root/'b', rotation)
        self.assertEqual(mesh_inputs.compare_captured(self.root/'a', self.root/'b', self.root/'same'), 0)
        self.multimesh_fixture(self.root/'c', stepped)
        self.assertEqual(mesh_inputs.compare_captured(self.root/'a', self.root/'c', self.root/'step'), 1)
