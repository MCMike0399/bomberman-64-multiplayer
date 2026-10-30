import unittest

import render_fingerprint as fingerprint


class RenderingFingerprint(unittest.TestCase):
    def test_rendering_inputs_engine_templates_and_launcher_are_selected(self):
        for path in ('scripts/rendering/ArtMaterials.cs','scripts/arena/models/BomberModel.cs',
                     'assets/models/bomber.res','assets/shaders/water.gdshader','assets/fonts/font.ttf',
                     'src/Bomber.DesktopLauncher/RendererLaunchGuard.cs','project.godot','export_presets.cfg',
                     'tools/ci/install_godot.py','Bomberman64Multiplayer.csproj'):
            with self.subTest(path=path):self.assertTrue(fingerprint.selected(path))
        for path in ('docs/releases.md','assets/testing/scenarios.json','tools/ci/builder_audit.py'):
            with self.subTest(path=path):self.assertFalse(fingerprint.selected(path))

    def test_only_release_identity_is_normalized_not_renderer_or_quality(self):
        before=b'application/version="0.45.0"\nrendering/driver="vulkan"\n'
        after=b'application/version="0.45.1"\nrendering/driver="vulkan"\n'
        self.assertEqual(fingerprint.normalized('export_presets.cfg',before),fingerprint.normalized('export_presets.cfg',after))
        self.assertNotEqual(fingerprint.normalized('export_presets.cfg',before),
                            fingerprint.normalized('export_presets.cfg',before.replace(b'vulkan',b'd3d12')))
        self.assertEqual(fingerprint.normalized('project.godot',before),before)


if __name__=='__main__':unittest.main()
