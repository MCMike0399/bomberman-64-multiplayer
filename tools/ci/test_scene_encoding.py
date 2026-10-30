"""Exact semantic scene inputs with independently validated raw transport bindings."""
import hashlib,json,struct,unittest
from pathlib import Path
import mesh_inputs
import test_mesh_inputs
from test_visual_stages import scene_files,LABELS

class SceneEncodingTests(unittest.TestCase):
    def setUp(self):
        self.fixture=test_mesh_inputs.MeshInputTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups)
        self.root=self.fixture.root;self.a=self.root/'a';self.b=self.root/'b'
        for folder in (self.a,self.b):
            self.fixture.canonical_fixture(folder)
            scene_files(folder,'up-and-down','full-up-down-high-v2')
    def update_recipe(self,folder,label,recipe):
        (folder/f'parity-{label}-recipe.json').write_text(json.dumps(recipe))
        path=folder/'visual-parity-observations.json';observed=json.loads(path.read_text())
        next(row for row in observed['records']if row['label']==label)['recipe']=recipe
        path.write_text(json.dumps(observed))
    def bind(self,folder,label):
        recipe=json.loads((folder/f'parity-{label}-recipe.json').read_text())
        for kind in ('json','binary'):
            path=folder/recipe['resourceInventory'][kind]
            recipe['resourceInventory'][kind+'Sha256']=hashlib.sha256(path.read_bytes()).hexdigest()
        self.update_recipe(folder,label,recipe)
    def reencode(self,folder,newline,sort_keys=False):
        for label in LABELS:
            path=folder/f'parity-{label}-inventory.json';data=json.loads(path.read_text())
            path.write_bytes(json.dumps(data,indent=2,sort_keys=sort_keys).replace('\n',newline).encode())
            self.bind(folder,label)
    def compare(self):return mesh_inputs.compare_captured(self.a,self.b,self.root/'result',stage='up-and-down',expected_revision='a'*40)
    def test_crlf_only_with_honest_bindings_preserves_exact_scene_inputs(self):
        self.reencode(self.a,'\n');self.reencode(self.b,'\r\n')
        self.assertEqual(self.compare(),0)
        report=json.loads((self.root/'result/report.json').read_text())
        self.assertEqual(len(report['trials']),5)
        self.assertTrue(all(t['binaryEqual']and t['inventoryEqual']for t in report['trials']))

    def test_map_order_and_indentation_are_not_render_inputs(self):
        self.reencode(self.a,'\n');self.reencode(self.b,'\r\n',sort_keys=True)
        self.assertEqual(self.compare(),0)
    def test_corrupted_bytes_without_new_binding_fail_before_comparison(self):
        path=self.b/'parity-base-b-inventory.json';path.write_bytes(path.read_bytes()+b' ')
        with self.assertRaisesRegex(ValueError,'file hash differs'):self.compare()
    def test_missing_binding_is_rejected(self):
        recipe=json.loads((self.b/'parity-dof-off-recipe.json').read_text())
        del recipe['resourceInventory']['jsonSha256'];self.update_recipe(self.b,'dof-off',recipe)
        with self.assertRaisesRegex(ValueError,'file hash differs'):self.compare()
    def test_mismatched_binding_is_rejected(self):
        recipe=json.loads((self.b/'parity-warmup-recipe.json').read_text())
        recipe['resourceInventory']['binarySha256']='0'*64;self.update_recipe(self.b,'warmup',recipe)
        with self.assertRaisesRegex(ValueError,'file hash differs'):self.compare()
    def test_one_float32_ulp_in_nonbaseline_graph_remains_red(self):
        for folder in (self.a,self.b):
            path=folder/'parity-dof-off-inventory.json';data=json.loads(path.read_text());data['nodes'][0]['renderScalar']=1.0
            path.write_text(json.dumps(data));self.bind(folder,'dof-off')
        path=self.b/'parity-dof-off-inventory.json';data=json.loads(path.read_text())
        data['nodes'][0]['renderScalar']=struct.unpack('<f',bytes.fromhex('0100803f'))[0]
        path.write_text(json.dumps(data));self.bind(self.b,'dof-off')
        self.assertEqual(self.compare(),1)
        report=json.loads((self.root/'result/report.json').read_text())
        self.assertIn('dof-off',report['firstInventoryDifference']['path'])
    def test_honestly_rebound_nonbaseline_buffer_remains_red(self):
        path=self.b/'parity-reflection-off-inventory.bin';data=bytearray(path.read_bytes());data[0]^=1;path.write_bytes(data)
        path=self.b/'parity-reflection-off-inventory.json';graph=json.loads(path.read_text())
        graph['nodes'][0]['data']['sha256']=hashlib.sha256(data).hexdigest()
        path.write_text(json.dumps(graph));self.bind(self.b,'reflection-off')
        self.assertEqual(self.compare(),1)
        report=json.loads((self.root/'result/report.json').read_text())
        self.assertFalse(next(row for row in report['trials']if row['label']=='reflection-off')['binaryEqual'])
    def test_lists_retain_meaningful_order(self):
        for folder in (self.a,self.b):
            path=folder/'parity-base-b-inventory.json';data=json.loads(path.read_text());data['resources'][0]['ordered']=['left','right']
            path.write_text(json.dumps(data));self.bind(folder,'base-b')
        path=self.b/'parity-base-b-inventory.json';data=json.loads(path.read_text());data['resources'][0]['ordered'].reverse()
        path.write_text(json.dumps(data));self.bind(self.b,'base-b')
        self.assertEqual(self.compare(),1)
    def test_missing_nonbaseline_buffer_cannot_be_ignored(self):
        (self.b/'parity-warmup-inventory.bin').unlink()
        with self.assertRaises(ValueError):self.compare()
    def test_signed_zero_in_a_graph_is_not_normalized(self):
        for folder,value in ((self.a,0.0),(self.b,-0.0)):
            path=folder/'parity-base-b-inventory.json';data=json.loads(path.read_text());data['nodes'][0]['renderScalar']=value
            path.write_text(json.dumps(data));self.bind(folder,'base-b')
        self.assertEqual(self.compare(),1)

if __name__=='__main__':unittest.main()
