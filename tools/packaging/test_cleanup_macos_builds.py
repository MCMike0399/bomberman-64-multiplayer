import importlib.util
from pathlib import Path
import plistlib
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("cleanup", Path(__file__).resolve().parent / "cleanup-macos-builds.py")
cleanup = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(cleanup)


class CleanupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.installed = self.bundle("Applications/Blast Circuit.app", "0.18.0")
        self.export = self.bundle("artifacts/exports/Blast Circuit.app", "0.18.0")

    def bundle(self, name, version="0.17.0", identity=cleanup.BUNDLE_ID):
        path = self.root / name
        (path / "Contents/MacOS").mkdir(parents=True)
        (path / "Contents/MacOS/Blast Circuit").write_text("payload")
        (path / "Contents/Info.plist").write_bytes(plistlib.dumps({
            "CFBundleIdentifier": identity, "CFBundlePackageType": "APPL",
            "CFBundleExecutable": "Blast Circuit", "CFBundleShortVersionString": version}))
        return path

    def test_only_matching_payloads_removed_and_receipts_preserved(self):
        backup = self.bundle("artifacts/renamed-backup")
        unrelated = self.bundle("artifacts/Other.app", identity="other.app")
        receipt = self.root / "artifacts/receipt.json"
        receipt.write_text("{}")
        rows = cleanup.plan([self.root], self.installed, self.export)
        self.assertEqual({row["action"] for row in rows}, {"keep-installed", "keep-export", "remove"})
        with patch.object(cleanup, "LSREGISTER", self.root / "missing-lsregister"):
            cleanup.apply(rows, self.installed, "0.18.0")
        self.assertFalse(backup.exists())
        for path in [unrelated, receipt, self.installed, self.export]:
            self.assertTrue(path.exists())

    def test_running_update_keeps_its_rollback_bundle(self):
        backup = self.bundle("Applications/.blast-update-fixture/previous.app")
        lock = self.root / "Applications/.blast-update-lock"
        lock.mkdir()
        rows = cleanup.plan([self.root], self.installed, self.export, True)
        self.assertEqual("keep-active-update", next(row["action"] for row in rows if row["path"] == str(backup)))
        with patch.object(cleanup, "LSREGISTER", self.root / "missing-lsregister"):
            cleanup.apply(rows, self.installed, "0.18.0")
        self.assertTrue(backup.is_dir())
        lock.rmdir()
        rows = cleanup.plan([self.root], self.installed, self.export, True)
        self.assertEqual("remove", next(row["action"] for row in rows if row["path"] == str(backup)))

    def test_wrong_installed_version_aborts_before_removal(self):
        backup = self.bundle("artifacts/Old.app")
        rows = cleanup.plan([self.root], self.installed, self.export, True)
        with self.assertRaises(RuntimeError):
            cleanup.apply(rows, self.installed, "0.19.0")
        self.assertTrue(backup.exists())
        self.assertTrue(self.export.exists())

    def test_changed_identity_aborts_entire_selection(self):
        backup = self.bundle("artifacts/Old.app")
        rows = cleanup.plan([self.root], self.installed, self.export, True)
        info = backup / "Contents/Info.plist"
        value = plistlib.loads(info.read_bytes())
        value["CFBundleIdentifier"] = "other.app"
        info.write_bytes(plistlib.dumps(value))
        with self.assertRaises(RuntimeError):
            cleanup.apply(rows, self.installed, "0.18.0")
        self.assertTrue(self.export.exists())
        self.assertTrue(backup.exists())

    def test_symlink_and_metadata_only_receipt_not_selected(self):
        target = self.bundle("outside/Old.app")
        artifacts = self.root / "artifacts"
        (artifacts / "alias.app").symlink_to(target, target_is_directory=True)
        receipt = artifacts / "receipt/Contents"
        receipt.mkdir(parents=True)
        (receipt / "Info.plist").write_bytes((target / "Contents/Info.plist").read_bytes())
        rows = cleanup.plan([artifacts], self.installed, self.export, True)
        self.assertEqual([row["path"] for row in rows], [str(self.export)])

    def test_unregistered_renamed_backup_is_removed(self):
        backup = self.bundle("artifacts/renamed-backup")
        rows = cleanup.plan([self.root], self.installed, self.export)
        executable = self.root / "lsregister"
        executable.touch()
        missing = subprocess.CompletedProcess([], 1, "", f"failed to scan {backup}: -10814\n from spotlight")
        registered = subprocess.CompletedProcess([], 0, "", "")
        with patch.object(cleanup, "LSREGISTER", executable), patch.object(
                cleanup.subprocess, "run", side_effect=[missing, registered]) as run:
            cleanup.apply(rows, self.installed, "0.18.0")
        self.assertFalse(backup.exists())
        self.assertTrue(self.installed.exists())
        self.assertEqual(run.call_args_list[-1].args[0], [str(executable), "-f", str(self.installed)])

    def test_unknown_unregister_error_preserves_bundle(self):
        backup = self.bundle("artifacts/renamed-backup")
        rows = cleanup.plan([self.root], self.installed, self.export)
        executable = self.root / "lsregister"
        executable.touch()
        failed = subprocess.CompletedProcess([], 1, "", f"failed to scan {backup}: -50 from spotlight\n")
        with patch.object(cleanup, "LSREGISTER", executable), patch.object(
                cleanup.subprocess, "run", return_value=failed) as run:
            with self.assertRaises(subprocess.CalledProcessError):
                cleanup.apply(rows, self.installed, "0.18.0")
        self.assertTrue(backup.exists())
        self.assertEqual(run.call_count, 1)


if __name__ == "__main__":
    unittest.main()
