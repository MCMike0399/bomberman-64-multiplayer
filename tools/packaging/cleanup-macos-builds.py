#!/usr/bin/env python3
"""Remove duplicate Blast Circuit app bundles after verifying the installed version.

Defaults to a JSON dry run. Receipts, ZIPs and unrelated bundles are preserved.
The current export is kept unless --include-export is explicitly supplied.
"""
import argparse
import json
import os
from pathlib import Path
import plistlib
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BUNDLE_ID = "io.github.mcmike0399.blastcircuit"
LSREGISTER = Path("/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister")


def bundle_info(path):
    """Require a real app payload, including for legacy renamed backup roots."""
    if path.is_symlink() or (path / "Contents").is_symlink():
        return None
    try:
        with (path / "Contents/Info.plist").open("rb") as stream:
            info = plistlib.load(stream)
    except (OSError, ValueError, plistlib.InvalidFileException):
        return None
    executable = info.get("CFBundleExecutable", "")
    if (info.get("CFBundleIdentifier") != BUNDLE_ID
            or info.get("CFBundlePackageType") != "APPL"
            or not executable or Path(executable).name != executable
            or not (path / "Contents/MacOS" / executable).is_file()):
        return None
    return {"path": str(path), "bundle_id": BUNDLE_ID,
            "version": info.get("CFBundleShortVersionString"),
            "build": info.get("CFBundleVersion")}


def discover(roots):
    found = {}
    for root in roots:
        if root.is_symlink():
            continue
        for directory, children, _ in os.walk(root, followlinks=False):
            path = Path(directory)
            info = bundle_info(path)
            if info:
                found[path] = info
                children[:] = []
            elif path.suffix == ".app":
                children[:] = []  # Never inspect another app's embedded payloads.
            else:
                children[:] = [child for child in children
                               if child not in {".git", ".godot", "node_modules", ".venv"}
                               and not (path / child).is_symlink()]
    return [found[path] for path in sorted(found)]


def plan(roots, installed, export, include_export=False):
    rows = discover(roots)
    for row in rows:
        path = Path(row["path"])
        active_update = any(parent.name.startswith(".blast-update-") and (parent.parent / ".blast-update-lock").is_dir()
                            for parent in path.parents)
        row["action"] = ("keep-active-update" if active_update else "keep-installed" if path == installed else
                         "keep-export" if path == export and not include_export else "remove")
    return rows


def apply(rows, installed, expected_version):
    current = bundle_info(installed)
    if not current or current["version"] != expected_version:
        raise RuntimeError(f"Installed app must be version {expected_version}: {installed}")
    # Validate the entire selection before changing either files or LaunchServices.
    for row in rows:
        if row["action"] != "remove":
            continue
        path = Path(row["path"])
        if path == installed or path in installed.parents or bundle_info(path) != {
                key: value for key, value in row.items() if key != "action"}:
            raise RuntimeError(f"Bundle changed or protected path selected: {path}")
    for row in rows:
        if row["action"] == "remove":
            path = Path(row["path"])
            if LSREGISTER.exists():
                result = subprocess.run([str(LSREGISTER), "-u", str(path)],
                                        capture_output=True, text=True)
                # Renamed backups may never have been registered as applications.
                # Accept only LaunchServices' explicit application-not-found result.
                lines = [line.strip() for line in (result.stdout + result.stderr).splitlines()
                         if line.strip()]
                absent = (result.returncode == 1 and " ".join(lines) ==
                          f"failed to scan {path}: -10814 from spotlight")
                if not absent:
                    result.check_returncode()
            shutil.rmtree(path)
    if LSREGISTER.exists():
        subprocess.run([str(LSREGISTER), "-f", str(installed)], check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--apply", action="store_true")
    mode.add_argument("--dry-run", action="store_true")
    parser.add_argument("--include-export", action="store_true")
    parser.add_argument("--expected-installed-version",
                        help="Required for --apply; verifies installation completed first")
    args = parser.parse_args()
    if args.apply and not args.expected_installed_version:
        parser.error("--apply requires --expected-installed-version")
    installed = Path.home() / "Applications/Blast Circuit.app"
    export = ROOT / "artifacts/exports/Blast Circuit.app"
    roots = [ROOT / "artifacts", Path.home() / "Applications", Path("/Applications"),
             Path.home() / "Downloads", Path.home() / "Desktop"]
    rows = plan(roots, installed, export, args.include_export)
    if args.apply:
        apply(rows, installed, args.expected_installed_version)
    print(json.dumps({"mode": "applied" if args.apply else "dry-run", "bundles": rows}, indent=2))


if __name__ == "__main__":
    main()
