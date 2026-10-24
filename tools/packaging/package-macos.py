#!/usr/bin/env python3
"""Export a self-contained Apple Silicon app, including the optional USB controller backend."""
import argparse
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def run(*args):
    subprocess.run([str(a) for a in args], cwd=ROOT, check=True)


def prepare_template(godot):
    """Thin official Universal 2 templates without modifying the installed template."""
    version = subprocess.check_output([godot, "--version"], text=True).strip().split(".")
    template_version = ".".join(version[:4]) + ".mono"
    source = Path.home() / "Library/Application Support/Godot/export_templates" / template_version / "macos.zip"
    if not source.exists():
        raise RuntimeError(f"Install matching .NET export templates in Godot first: missing {source}")
    target = ROOT / "artifacts/packaging/macos-arm64.zip"
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="blast-template-") as temporary:
        with zipfile.ZipFile(source) as original, zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as output:
            for entry in original.infolist():
                if entry.filename.endswith(".universal"):
                    binary = Path(temporary) / Path(entry.filename).name
                    binary.write_bytes(original.read(entry))
                    thin = binary.with_suffix(".arm64")
                    run("lipo", binary, "-thin", "arm64", "-output", thin)
                    entry.filename = entry.filename.removesuffix(".universal") + ".arm64"
                    output.writestr(entry, thin.read_bytes())
                else:
                    output.writestr(entry, original.read(entry))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", default=os.environ.get("GODOT", "/Applications/Godot_mono.app/Contents/MacOS/Godot"))
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/exports/Blast Circuit.app")
    args = parser.parse_args()
    app = args.output.resolve()
    if app.suffix != ".app":
        parser.error("--output must end in .app")
    app.parent.mkdir(parents=True, exist_ok=True)
    run('python3', 'tools/networking/bootstrap_webrtc.py')
    prepare_template(args.godot)
    # The release exporter publishes the complete C# dependency graph and .NET runtime.
    exported = subprocess.run(
        [args.godot, "--headless", "--path", str(ROOT), "--export-release", "macOS Metal", str(app)],
        cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
    )
    print(exported.stdout, flush=True)
    if exported.returncode or re.search(r"(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception)", exported.stdout):
        raise RuntimeError("Godot export failed; inspect the export log. A bundle directory alone is not a successful build.")
    if not list(app.rglob("Bomberman64Multiplayer.dll")):
        raise RuntimeError("Export omitted the managed game assembly.")
    info = app / "Contents/Info.plist"
    with info.open("rb") as stream:
        plist = plistlib.load(stream)
    assert plist["CFBundleIdentifier"] == "io.github.mcmike0399.blastcircuit"
    assert plist["LSSupportsGameMode"] is True
    native = ROOT / "native/macos"
    if (native / "libSDL3.dylib").exists():
        names = ["libSDL3.dylib", "libusb-1.0.dylib", "SDL-LICENSE.txt", "libusb-LICENSE.txt"]
        for name in names:
            if not (native / name).is_file():
                raise RuntimeError(f"Incomplete controller backend: missing {name}; run tools/controllers/bootstrap-sdl.sh")
        destination = app / "Contents/Frameworks/Controllers"
        destination.mkdir(parents=True, exist_ok=True)
        for name in names:
            shutil.copy2(native / name, destination / name)
        for name in names[:2]:
            run("codesign", "--force", "--sign", "-", destination / name)
        (destination / "SOURCE.txt").write_text(
            "SDL 3.4.18 (zlib), libusb 1.0.29 (LGPL-2.1-or-later).\n"
            "Unmodified archives, checksums, patch sources, and rebuild instructions:\n"
            "https://github.com/MCMike0399/bomberman-64-multiplayer/tree/main/tools/controllers\n"
            "Libraries are dynamically linked and can be replaced/rebuilt; re-sign the app after changes.\n"
        )
    # Include notices in the bundle, but never the private kernel checkout or reference media.
    notices = app / "Contents/Resources/Notices"
    notices.mkdir(parents=True, exist_ok=True)
    for license_file in (ROOT / 'addons/webrtc_native').glob('LICENSE*'):
        shutil.copy2(license_file, notices / license_file.name)
    (notices / 'WebRTC-SOURCE.txt').write_text(
        'Godot webrtc-native 1.2.2-stable (MIT), including libdatachannel (MPL-2.0) and usrsctp (BSD).\n'
        'Corresponding source, submodule revisions and build instructions:\n'
        'https://github.com/godotengine/webrtc-native/tree/1.2.2-stable\n'
        'Unmodified binary archive and release information:\n'
        'https://github.com/godotengine/webrtc-native/releases/tag/1.2.2-stable\n'
        'The dynamic extension can be rebuilt/replaced; re-sign the app after replacement.\n'
    )
    shutil.copy2(ROOT / "LICENSE", notices / "Blast-Circuit-MIT.txt")
    shutil.copy2(ROOT / "src/Bomber.Game/Simulation/LICENSE", notices / "Simulation-MIT.txt")
    for name in ["Godot-MIT.txt", "Godot-COPYRIGHT.txt"]:
        shutil.copy2(ROOT / "docs/licenses" / name, notices / name)
    for font_license in sorted((ROOT / "assets/fonts").glob("OFL-*.txt")):
        shutil.copy2(font_license, notices / font_license.name)
    runtime_config = next(app.rglob("Bomberman64Multiplayer.runtimeconfig.json"))
    runtime_version = json.loads(runtime_config.read_text())["runtimeOptions"]["includedFrameworks"][0]["version"]
    packages = Path(os.environ.get("NUGET_PACKAGES", str(Path.home() / ".nuget/packages")))
    runtime_package = packages / "microsoft.netcore.app.runtime.osx-arm64" / runtime_version
    for name in ["LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"]:
        shutil.copy2(runtime_package / name, notices / ("DotNet-" + name))
    # Native library additions invalidate the exporter signature. Retain runtime entitlements.
    entitlements = app.parent / "runtime-entitlements.plist"
    with entitlements.open("wb") as stream:
        plistlib.dump({
            "com.apple.security.cs.allow-jit": True,
            "com.apple.security.cs.allow-unsigned-executable-memory": True,
            "com.apple.security.cs.disable-library-validation": True,
        }, stream)
    run("codesign", "--force", "--deep", "--sign", "-", "--entitlements", entitlements, app)
    run("codesign", "--verify", "--deep", "--strict", "--verbose=2", app)
    run("plutil", "-lint", info)
    print(f"\nBuilt: {app}\nLocal ad-hoc signature; no Apple notarization is claimed.")
    print(f'QA: python3 tools/playtest/run.py --app "{app}" --scenario all --start-server')


if __name__ == "__main__":
    main()
