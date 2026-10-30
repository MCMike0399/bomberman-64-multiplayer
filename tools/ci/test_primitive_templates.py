import hashlib,json,tempfile,unittest
from pathlib import Path
import primitive_templates

class PrimitiveTemplateTests(unittest.TestCase):
    def check_source(self,text,suffix='.cs'):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'scripts').mkdir();(root/'scripts'/('Hidden'+suffix)).write_text(text)
            return primitive_templates.check(root)
    def test_explicit_regeneration_is_red(self):
        self.assertTrue(self.check_source('var mesh=new SphereMesh {Radius=.5f};'))
    def test_target_typed_regeneration_is_red(self):
        self.assertTrue(self.check_source('static readonly CylinderMesh Mesh=new(){Height=1};'))
    def test_mutation_or_type_test_is_red(self):
        self.assertTrue(self.check_source('if(mesh is PlaneMesh plane)plane.Size=size;'))
    def test_dynamic_bypass_is_red(self):
        self.assertTrue(self.check_source('ClassDB.Instantiate(type);'))
    def test_scene_resource_is_red(self):
        self.assertTrue(self.check_source('[sub_resource type="TorusMesh" id="1"]','.tscn'))
    def test_request_metadata_is_allowed(self):
        self.assertFalse(self.check_source('var mesh=PrimitiveTemplates.Get(kind,properties);'))

    def test_changed_resource_is_red(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'scripts').mkdir();assets=root/'assets/models/primitives';assets.mkdir(parents=True)
            (assets/'x.res').write_bytes(b'changed')
            (assets/'hashes.json').write_text(json.dumps({'x':hashlib.sha256(b'original').hexdigest()}))
            self.assertTrue(primitive_templates.check(root))
    def test_missing_manifest_is_red_when_factory_exists(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);factory=root/primitive_templates.AUTHORING;factory.parent.mkdir(parents=True);factory.write_text('')
            self.assertTrue(primitive_templates.check(root))

if __name__=='__main__':unittest.main()
