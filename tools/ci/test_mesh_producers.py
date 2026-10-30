"""New mesh producers must be reviewed, even when existing template files are unchanged."""
import json,tempfile,unittest
from pathlib import Path
import mesh_producers

class ProducerInventoryTests(unittest.TestCase):
    def run_source(self,text):
        with tempfile.TemporaryDirectory()as directory:
            root=Path(directory);(root/'scripts').mkdir();(root/'scripts/New.cs').write_text(text)
            data=root/'assets/models/primitives';data.mkdir(parents=True);(data/'requests.json').write_text('{"requests":{}}')
            inventory=root/mesh_producers.INVENTORY;inventory.parent.mkdir(parents=True)
            inventory.write_text(json.dumps({'sites':[],'finiteTemplateDomains':{},'parameterDomainsSource':'assets/models/primitives/requests.json'}))
            return mesh_producers.check(root)
    def test_new_array_mesh_is_rejected(self):self.assertTrue(self.run_source('var mesh=new ArrayMesh();'))
    def test_target_typed_generator_is_rejected(self):self.assertTrue(self.run_source('SurfaceTool surface=new();'))
    def test_new_commit_path_is_rejected(self):self.assertTrue(self.run_source('return builder.Commit();'))
    def test_new_gameplay_box_call_is_rejected(self):self.assertTrue(self.run_source('Box(parent,position,size,material);'))
    def test_new_surface_upload_is_rejected(self):self.assertTrue(self.run_source('mesh.AddSurfaceFromArrays(type,arrays);'))
    def test_comments_and_strings_are_not_producers(self):self.assertFalse(self.run_source('// new ArrayMesh();\nstring description="builder.Commit()";'))
    def test_registered_source_and_finite_domains_match(self):self.assertEqual(mesh_producers.check(),[])

if __name__=='__main__':unittest.main()
