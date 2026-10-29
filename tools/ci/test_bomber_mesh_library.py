import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import bomber_mesh_library as library


class CanonicalGeometryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        sources = {}
        for source in library.SOURCES:
            path = self.root / source; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('authored geometry')
            sources[source] = hashlib.sha256(path.read_bytes()).hexdigest()
        asset = self.root / 'assets/models/bomber-meshes.res'; asset.parent.mkdir(parents=True); asset.write_bytes(b'packed Mac surfaces')
        self.manifest = {'schemaVersion':1,'producer':{'platform':'macOS','engine':'ed1daf0bf'},'sourceFiles':sources,
                         'librarySha256':hashlib.sha256(asset.read_bytes()).hexdigest(),
                         'meshes':[{'key':key,'index':i,'packedSurfaceSha256':'a'*64} for i,key in enumerate(sorted(library.KEYS))]}
        self.save()

    def save(self):
        (self.root/'assets/models/bomber-meshes.json').write_text(json.dumps(self.manifest))

    def test_current_asset_passes_but_builder_edit_requires_regeneration(self):
        library.check(self.root)
        (self.root/library.SOURCES[0]).write_text('changed geometry')
        with self.assertRaisesRegex(ValueError,'Regenerate'): library.check(self.root)

    def test_corrupt_binary_cannot_pass(self):
        (self.root/'assets/models/bomber-meshes.res').write_bytes(b'changed surfaces')
        with self.assertRaisesRegex(ValueError,'file hash'): library.check(self.root)

    def test_hidden_spiral_frame_cannot_be_omitted(self):
        self.manifest['meshes'].pop(); self.save()
        with self.assertRaisesRegex(ValueError,'32 authored'): library.check(self.root)


if __name__ == '__main__': unittest.main()
