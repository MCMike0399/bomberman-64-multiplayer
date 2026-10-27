"""Compare old/current native uninstall handling of files added after the initial installation."""
import json
import contextlib
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]


@contextlib.contextmanager
def runtime_directory():
    root = Path(tempfile.mkdtemp(prefix='bci-'))
    try:
        yield root
    finally:
        deadline = time.monotonic() + 5
        while root.exists():
            try: shutil.rmtree(root)
            except OSError as error:
                if getattr(error, 'winerror', None) not in (5, 32, 33) or time.monotonic() >= deadline: raise
                time.sleep(.1)


def main():
    if platform.system() != 'Windows' or os.environ.get('GITHUB_ACTIONS') != 'true':
        raise RuntimeError('Installer probe requires a disposable Windows CI profile')
    output = ROOT / 'artifacts/windows-installer-probe'
    output.mkdir()
    compiler = shutil.which('ISCC') or str(Path(os.environ['ProgramFiles(x86)']) / 'Inno Setup 6/ISCC.exe')
    reports = []
    with runtime_directory() as root:
        payload = root / 'payload'
        payload.mkdir()
        shutil.copy2(sys.executable, payload / 'BlastCircuit.exe')
        (payload / 'original.txt').write_text('Original installed product file.')
        owned = ['BlastCircuit.exe', 'installed-files.txt', 'original.txt']
        (payload / 'installed-files.txt').write_text('\n'.join(owned) + '\n')
        for mode in ('old', 'current'):
            script = root / (mode + '.iss')
            script.write_bytes(subprocess.check_output(['git', 'show', '7ca5a9c:tools/packaging/windows-installer.iss'])
                               if mode == 'old' else (ROOT / 'tools/packaging/windows-installer.iss').read_bytes())
            folder = root / mode
            folder.mkdir()
            with (output / (mode + '-compile.log')).open('w') as log:
                subprocess.run([compiler, '/DGameDirectory=' + str(payload), '/DOutputDirectory=' + str(folder),
                                '/DGameVersion=0.44.0', '/DGameBuild=54', str(script)], stdout=log,
                               stderr=subprocess.STDOUT, check=True, timeout=180)
            installed = root / ('Installed ' + mode)
            subprocess.run([str(folder / 'Blast-Circuit-windows-x64-Setup.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES',
                            '/NORESTART', '/CURRENTUSER', '/DIR=' + str(installed), '/LOG=' + str(output / (mode + '-setup.log'))],
                           check=True, timeout=90)
            added = ['update-added.txt', 'extra-runtime/update-added.txt']
            for name in added:
                path = installed / name
                path.parent.mkdir(exist_ok=True)
                path.write_text('New product file delivered by a later signed update.')
            note = installed / 'unrelated-user-note.txt'
            note.write_text('Keep unrelated data.')
            outside = root / 'outside-keep.txt'
            outside.write_text('Keep outside data.')
            (installed / 'installed-files.txt').write_text('\n'.join(sorted([*owned, *added, '../outside-keep.txt'])) + '\n')
            subprocess.run([str(installed / 'unins000.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
                            '/LOG=' + str(output / (mode + '-uninstall.log'))], check=True, timeout=90)
            remaining = [name for name in [*owned, *added] if (installed / name).exists()]
            preserved = note.read_text() == 'Keep unrelated data.' and outside.read_text() == 'Keep outside data.'
            if not preserved or remaining != (added if mode == 'old' else []):
                raise RuntimeError(mode + ' uninstall did not produce the expected owned-file/preservation witness: ' + str(remaining))
            reports.append({'mode': mode, 'remainingProductFiles': remaining, 'unrelatedAndOutsidePreserved': preserved})
    (output / 'summary.json').write_text(json.dumps({'passed': True, 'reports': reports}, indent=2) + '\n')
    print('PASS native uninstall: old log misses added files; current inventory removes every product file and preserves unrelated/outside data')


if __name__ == '__main__': main()
