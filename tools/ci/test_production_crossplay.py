import json
from pathlib import Path
import tempfile
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


if __name__ == '__main__': unittest.main()
