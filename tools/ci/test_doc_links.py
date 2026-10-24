"""Contracts for the dependency-free documentation link check."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('doc_links', Path(__file__).with_name('doc_links.py'))
doc_links = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(doc_links)


class DocumentationLinkTests(unittest.TestCase):
    def check(self, files):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name, text in files.items():
                (root / name).parent.mkdir(parents=True, exist_ok=True)
                (root / name).write_text(text)
            return doc_links.broken_links(root, [name for name in files if name.endswith('.md')])

    def test_valid_files_anchors_and_duplicate_headings_resolve(self):
        failures = self.check({
            'docs/guide.md': '# Own the build, desktop and server\n## Notes\n## Notes\n',
            'README.md': '[a](docs/guide.md#own-the-build-desktop-and-server) [b](docs/guide.md#notes-1) '
                         '[c](#readme) ![d](docs/guide.md) [e](https://example.com/missing)\n# Readme\n',
        })
        self.assertEqual([], failures)

    def test_missing_files_and_anchors_are_reported_with_line_numbers(self):
        failures = self.check({'docs/guide.md': '# Title\n', 'README.md': 'x\n[a](docs/old.md)\n[b](docs/guide.md#gone)\n'})
        self.assertEqual(['README.md:2: missing target docs/old.md', 'README.md:3: missing anchor #gone in docs/guide.md'], failures)

    def test_code_examples_and_ignored_local_outputs_are_not_links(self):
        failures = self.check({'README.md': '```sh\n[x](missing.md)\n```\n`[y](missing.md)`\n[z](artifacts/exports/App.app)\n'})
        self.assertEqual([], failures)

    def test_links_cannot_escape_the_repository(self):
        failures = self.check({'README.md': '[x](../outside.md)\n'})
        self.assertEqual(1, len(failures))
        self.assertIn('leaves the repository', failures[0])


if __name__ == '__main__':
    unittest.main()
