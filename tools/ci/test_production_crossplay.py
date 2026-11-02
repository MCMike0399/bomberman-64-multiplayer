import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch
import production_crossplay as qa


class ProductionBoundaryTests(unittest.TestCase):
    def origin(self):
        return {'origin': qa.ORIGIN, 'revision': qa.SOURCE, 'release': qa.RELEASE,
                'publishedTag': 'v0.45.0', 'serverArchiveSha256': 'a' * 64}

    def test_exact_published_origin(self):
        with patch.object(qa.crossplay, 'read_json', return_value={'release': dict(qa.RELEASE, revision=qa.SOURCE)}):
            qa.check_origin(self.origin())

    def test_other_origin_or_source_or_tag_refused_before_network(self):
        for key, value in [('origin', 'https://fake.trycloudflare.com'), ('revision', 'b' * 40), ('publishedTag', 'v0.44.1')]:
            origin = self.origin(); origin[key] = value
            with self.subTest(key=key), patch.object(qa.crossplay, 'read_json') as read:
                with self.assertRaises(RuntimeError): qa.check_origin(origin)
                read.assert_not_called()

    def test_wrong_live_release_refused(self):
        with patch.object(qa.crossplay, 'read_json', return_value={'release': {'version': '0.44.1'}}):
            with self.assertRaises(RuntimeError): qa.check_origin(self.origin())

    def test_unpublished_release_refused_before_download(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(qa.public, 'api', return_value={
                'draft': True, 'published_at': None, 'target_commitish': qa.SOURCE}), patch.object(qa.public, 'download') as download:
            with self.assertRaises(RuntimeError): qa.prepare(Path(directory), 'linux-x64')
            download.assert_not_called()

    def test_committed_requires_three_identical_raw_witnesses(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            for name in ('crossplay.json', 'crossplay-actions.json', 'crossplay-committed.json'):
                (output / name).write_text('{"passed":true}')
            qa.committed(output)
            (output / 'crossplay-committed.json').write_text('{}')
            (output / 'exit.json').write_text('{}')
            with self.assertRaises(RuntimeError): qa.committed(output)


class RealWriterOrderTests(unittest.TestCase):
    def test_committed_actions_then_finish_then_final_witness(self):
        # Frozen game CrossPlatformQuickPlaytest.cs202-206: the final witness
        # cannot exist until the operator has passed the barrier and sent finish.
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            ready = threading.Event()
            finish = threading.Event()
            payload = '{"passed":true,"hosting":"cluster"}'
            def writer():
                (output / 'crossplay-committed.json').write_text(payload)
                (output / 'crossplay-actions.json').write_text(payload)
                ready.set()
                finish.wait(2)
                if (output / 'finish').exists():
                    (output / 'crossplay.json').write_text(payload)
            worker = threading.Thread(target=writer)
            worker.start()
            self.assertTrue(ready.wait(1))
            try:
                with patch.object(qa.time, 'monotonic', side_effect=[0, 0, 61]), patch.object(qa.time, 'sleep'):
                    qa.committed(output)
                self.assertFalse((output / 'crossplay.json').exists())
                (output / 'finish').touch()
                finish.set()
                worker.join(1)
                self.assertFalse(worker.is_alive())
                self.assertEqual(len({(output / name).read_bytes() for name in
                    ('crossplay-committed.json', 'crossplay-actions.json', 'crossplay.json')}), 1)
            finally:
                finish.set()
                worker.join(1)


class RealPublicManifestTests(unittest.TestCase):
    def setUp(self):
        self.inventory = json.loads((Path(__file__).parent / 'fixtures/v0450-public-release.json').read_text())['assets']
        self.archive = 'Blast-Circuit-macos-arm64.zip'
        self.sidecar = self.archive + '.sha256'
        self.assertIn(self.archive, self.inventory)
        self.assertNotIn(self.sidecar, self.inventory)

    def test_real_mac_manifest_shape_accepts_bound_sidecar(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / self.sidecar).write_text(self.inventory[self.archive]['sha256'] + '  ' + self.archive + '\n')
            receipt = qa.verify_download(root, self.sidecar, self.inventory)
            self.assertEqual(receipt['sourceArchiveSha256'], self.inventory[self.archive]['sha256'])
            self.assertEqual(receipt['binding'], 'checksum-text-matches-signed-zip-digest')

    def test_real_mac_manifest_shape_rejects_wrong_digest_or_filename(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for text in ('0' * 64 + '  ' + self.archive + '\n', self.inventory[self.archive]['sha256'] + '  other.zip\n'):
                (root / self.sidecar).write_text(text)
                with self.assertRaises(RuntimeError): qa.verify_download(root, self.sidecar, self.inventory)

    def test_other_unlisted_download_cannot_use_sidecar_exception(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'other.zip.sha256').write_text('0' * 64)
            with self.assertRaises(KeyError): qa.verify_download(root, 'other.zip.sha256', self.inventory)


if __name__ == '__main__': unittest.main()
