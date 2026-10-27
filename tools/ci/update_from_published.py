#!/usr/bin/env python3
"""Release gate: actually update the latest published client to this candidate.

Players receive a release through the in-app updater, so this runs the
production downloader/installer (tests/Bomber.Update.Tests) from the latest
stable published app to the candidate: successful relaunch, deliberate
failed-launch rollback, and relaunch with saved High/fullscreen before layout.
The previous app is downloaded anonymously and authenticated through the same
signed updater manifest players trust. Run it only on a disposable macOS VM.
"""
import argparse
import contextlib
import datetime
import json
import os
from pathlib import Path
import plistlib
import re
import subprocess
import sys
import tempfile
import time

from promote_release import ARCHIVE
from verify_published import api, digest, download, verify_update_manifest

ROOT = Path(__file__).resolve().parents[2]
FOOTER = re.compile(r'^PASS actual macOS updater: [^\r\n]+$', re.MULTILINE)
CASES = ('successful-install', 'failed-launch-rollback', 'successful-install-high-fullscreen')
ATTEMPTS = 3
CRASH_REPORT_WAIT = 45


def version_tuple(value):
    return tuple(int(part) for part in value.split('.'))


def identity(app):
    info = plistlib.loads((app / 'Contents/Info.plist').read_bytes())
    return {'version': info['CFBundleShortVersionString'], 'build': info['CFBundleVersion']}


def fetch_previous(repository, token, destination, version=None):
    """Download and authenticate the latest (or a named) published client; return its identity."""
    release = api(f'repos/{repository}/releases/' + (f'tags/v{version}' if version else 'latest'), token)
    if release.get('draft') or release.get('prerelease'):
        raise RuntimeError('Update baseline must be a published stable release')
    version = release['tag_name'].removeprefix('v')
    assets = {item['name']: item['browser_download_url'] for item in release.get('assets', [])}
    for name in (ARCHIVE, 'Blast-Circuit-update.json', 'Blast-Circuit-update.json.sig'):
        if name not in assets:
            raise RuntimeError('Latest published release lacks ' + name)
        download(assets[name], destination / name)
    verify_update_manifest(destination, version)
    return {'version': version, 'tag': release['tag_name'], 'sha256': digest(destination / ARCHIVE)}


@contextlib.contextmanager
def no_background_updates():
    """Keep the fixture's old app from fetching a newer published release mid-transaction.

    An older baseline would otherwise download/stage the latest release while the
    fixture installs the candidate. The guest's preferences are restored afterward;
    the fixture's High/fullscreen case still writes its own display settings.
    """
    path = Path.home() / 'Library/Application Support/Godot/app_userdata/Blast Circuit/preferences.cfg'
    original = path.read_bytes() if path.exists() else None
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text('[player]\n\nautomaticUpdates=false\n')
    try:
        yield
    finally:
        if original is None:
            path.unlink(missing_ok=True)
        else:
            path.write_bytes(original)


def crash_reports(since):
    found = {}
    for path in (Path.home() / 'Library/Logs/DiagnosticReports').glob('Blast Circuit*'):
        if path.stat().st_mtime >= since:
            text = path.read_text(errors='replace')
            found[path.name] = [line[:300] for line in text.splitlines()
                                if re.search(r'"exception"|"termination"', line)][:4]
    return found


def diagnostics(attempt_started, gate_started, wait=CRASH_REPORT_WAIT):
    """Explain a failed attempt: newest game log tails and any native crash since the gate began.

    ReportCrash writes .ips files asynchronously (observed ~30 s late), so wait
    for one before treating a failure as crash-free.
    """
    deadline = time.monotonic() + wait
    while not (crashes := crash_reports(gate_started)) and time.monotonic() < deadline:
        time.sleep(3)
    found = {'gameLogs': {}, 'crashReports': crashes}
    logs = Path.home() / 'Library/Application Support/Godot/app_userdata/Blast Circuit/logs'
    for path in sorted(logs.glob('*.log'), key=lambda item: item.stat().st_mtime)[-3:]:
        if path.stat().st_mtime >= attempt_started:
            found['gameLogs'][path.name] = path.read_text(errors='replace').splitlines()[-25:]
    return found


def run_transactions(candidate, previous_app, folder, timeout, project):
    folder.mkdir()
    log = folder / 'update-test.log'
    evidence = folder / 'transactions'
    command = ['dotnet', 'run', '--project', str(project), '--',
               str(candidate), str(previous_app), str(evidence), '--high-fullscreen']
    with log.open('w') as stream:
        result = subprocess.run(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT, timeout=timeout)
    text = log.read_text(errors='replace')
    if result.returncode != 0 or len(FOOTER.findall(text)) != 1:
        raise RuntimeError(f'Updater transactions failed (exit {result.returncode}); last lines: ' + ' | '.join(text.splitlines()[-12:]))
    summary = json.loads((evidence / 'summary.json').read_text())
    cases = summary.get('cases', [])
    if summary.get('passed') is not True or [case.get('name') for case in cases] != list(CASES):
        raise RuntimeError('Updater receipt does not contain all passing transactions')
    return cases


def run_update(candidate, previous_app, output, timeout, project, receipt, attempts=ATTEMPTS):
    """Run all transactions; retry only a failure with no native crash report.

    On hosted paravirtual GPUs a healthy relaunch occasionally misses the
    installer's 45 s health deadline (observed about one run in three). A crash
    is never retried: any crash report fails the gate at once, and every failed
    attempt stays in the receipt for review.
    """
    receipt['failedAttempts'] = []
    receipt['attempts'] = []
    gate_started = time.time()
    for attempt in range(1, attempts + 1):
        started = time.time()
        try:
            cases = run_transactions(candidate, previous_app, output / f'attempt-{attempt}', timeout, project)
        except (RuntimeError, subprocess.TimeoutExpired) as error:
            receipt['attempts'].append({'attempt': attempt, 'passed': False, 'seconds': round(time.time() - started, 3)})
            found = diagnostics(started, gate_started)
            receipt['failedAttempts'].append({'attempt': attempt, 'error': str(error), 'diagnostics': found})
            if found['crashReports']:
                raise RuntimeError('Candidate crashed during an update transaction: ' + ', '.join(found['crashReports'])) from None
            print(f'Update attempt {attempt} failed without a crash report: {error}', flush=True)
            continue
        # A crash in an earlier, retried attempt can be reported late; it still fails the gate.
        late = receipt['failedAttempts'] and diagnostics(started, gate_started)['crashReports']
        if late:
            raise RuntimeError('Candidate crashed during an earlier update attempt: ' + ', '.join(late))
        receipt['attempts'].append({'attempt': attempt, 'passed': True, 'seconds': round(time.time() - started, 3)})
        return cases
    raise RuntimeError(f'Updater transactions failed in all {attempts} attempts')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--app', type=Path, required=True, help='Candidate Blast Circuit.app (prefer one extracted from the release ZIP)')
    parser.add_argument('--output', type=Path, help='Fresh receipt directory (default: artifacts/qa/<time>-update-from-published)')
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY', 'MCMike0399/bomberman-64-multiplayer'))
    parser.add_argument('--timeout', type=int, default=900)
    parser.add_argument('--previous-version', help='Diagnose an update from this published version instead of the latest; '
                        'release gates always use the latest')
    parser.add_argument('--update-test-project', type=Path, default=ROOT / 'tests/Bomber.Update.Tests',
                        help='Project that builds the production updater transactions')
    args = parser.parse_args(argv)
    if sys.platform != 'darwin':
        parser.error('The updater transactions require macOS')
    model = subprocess.run(['sysctl', '-n', 'hw.model'], capture_output=True, text=True).stdout.strip()
    if not model.startswith('VirtualMac'):
        parser.error('Run in a disposable macOS VM (hosted CI or the QA guest); it changes guest preferences')
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ')
    output = (args.output or ROOT / 'artifacts/qa' / (stamp + '-update-from-published')).resolve()
    if output.exists() and any(output.iterdir()):
        parser.error('Use a fresh --output directory for every run')
    output.mkdir(parents=True, exist_ok=True)
    candidate = identity(args.app.resolve())
    token = os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN')
    receipt = {'schemaVersion': 1, 'gate': 'update-from-published', 'passed': False, 'candidate': candidate}
    started = time.monotonic()
    try:
        with tempfile.TemporaryDirectory(prefix='blast-update-gate-') as temporary:
            scratch = Path(temporary)
            receipt['previous'] = fetch_previous(args.repository, token, scratch, args.previous_version)
            if args.previous_version:
                receipt['baseline'] = 'explicit'
            previous = receipt['previous']['version']
            if previous == candidate['version']:
                # Not a release candidate: this version is live and no release will be created.
                receipt['skipped'] = 'candidate version is already the latest published release'
                print('SKIP update gate: v' + previous + ' is already published', flush=True)
            elif version_tuple(previous) > version_tuple(candidate['version']):
                raise RuntimeError(f'Candidate v{candidate["version"]} is older than published v{previous}')
            else:
                subprocess.run(['ditto', '-x', '-k', str(scratch / ARCHIVE), str(scratch / 'previous')], check=True)
                with no_background_updates():
                    receipt['cases'] = run_update(args.app.resolve(), scratch / 'previous/Blast Circuit.app', output,
                                                  args.timeout, args.update_test_project.resolve(), receipt)
                receipt['passed'] = True
                print(f'PASS update gate: v{previous} -> v{candidate["version"]}', flush=True)
    except Exception as error:
        receipt['error'] = str(error)
        print('FAIL update gate: ' + str(error), flush=True)
    receipt['seconds'] = round(time.monotonic() - started, 3)
    (output / 'summary.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return 0 if receipt['passed'] or receipt.get('skipped') else 1


if __name__ == '__main__':
    raise SystemExit(main())
