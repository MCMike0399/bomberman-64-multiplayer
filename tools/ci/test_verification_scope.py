"""Exercise documentation filtering against real Git push ranges, including renames."""
from pathlib import Path
import subprocess
import tempfile
import unittest

from verification_scope import runtime_required


class VerificationScopeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.git('init', '-q')
        self.git('config', 'user.name', 'Fixture')
        self.git('config', 'user.email', 'fixture@example.invalid')
        self.commit('docs/start.md', 'initial')
        self.base = self.git('rev-parse', 'HEAD').strip()

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.root, stderr=subprocess.DEVNULL, text=True)

    def commit(self, name, text):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        self.git('add', '.')
        self.git('commit', '-qm', 'fixture')

    def test_documentation_push_skips_runtime_but_manual_unknown_and_empty_ranges_do_not(self):
        self.assertTrue(runtime_required(self.base, self.root))
        self.commit('docs/start.md', 'changed')
        self.commit('README.md', 'documented')
        self.assertFalse(runtime_required(self.base, self.root))
        for base in ('', '0' * 40, 'a' * 40, 'HEAD~1', '--help'):
            with self.subTest(base=base):
                self.assertTrue(runtime_required(base, self.root))

    def test_multi_commit_push_with_source_change_requires_builds(self):
        self.commit('src/Program.cs', 'runtime')
        self.commit('docs/start.md', 'only the last commit is documentation')
        self.assertTrue(runtime_required(self.base, self.root))

    def test_renaming_source_into_docs_still_requires_builds(self):
        self.commit('src/Program.cs', 'code')
        base = self.git('rev-parse', 'HEAD').strip()
        self.git('mv', 'src/Program.cs', 'docs/Program.md')
        self.git('commit', '-qm', 'rename')
        self.assertTrue(runtime_required(base, self.root))

    def test_workflows_instructions_and_assets_require_builds(self):
        for name in ('.github/workflows/ci.yml', 'AGENTS.md', 'CLAUDE.md', 'docs/fixture.json', 'assets/art.md'):
            self.commit(name, 'changed')
            self.assertTrue(runtime_required(self.base, self.root), name)


if __name__ == '__main__':
    unittest.main()
