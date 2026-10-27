"""Run the persisted-display witness after its parent exits, retaining independent native evidence."""
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import time


def inspect(receipt, parent):
    child = receipt.get('summary', {})
    checks = child.get('checks', [])
    if (receipt.get('passed') is not True or receipt.get('parentExited') is not True or receipt.get('exitCode') != 0 or
            receipt.get('runtimeErrors') != [] or receipt.get('timeout') != 90 or
            not re.fullmatch(r'[a-f0-9]{64}', receipt.get('preferencesSha256', '')) or
            child.get('schemaVersion') != 1 or child.get('passed') is not True or child.get('displayRestoration') is not True or
            child.get('scenario') != 'display-settings' or child.get('completedScenarios') != ['display-settings'] or
            child.get('catalogSha256') != parent.get('catalogSha256') or child.get('release') != parent.get('release') or
            child.get('platform') != parent.get('platform') or child.get('renderingDriver') != parent.get('renderingDriver') or
            child.get('renderingMethod') != 'forward_plus' or not child.get('adapter') or
            child.get('screenshotMode') != 'graphical' or not child.get('screenshots') or
            type(child.get('assertions')) is not int or child['assertions'] < 6 or len(checks) != child['assertions'] or
            any(check.get('passed') is not True for check in checks)):
        raise RuntimeError('Missing or invalid sequential fresh-display restoration evidence')
    return receipt


def run(folder, command, parent, parent_process, cwd=None, environment=None):
    if not parent.get('displayRestartRequested'):
        return None
    if parent_process.poll() is None:
        raise RuntimeError('Display restart must wait for the parent process to exit')
    output = folder / 'restart'
    request = json.loads((output / 'request.json').read_text())
    identifier = request['fixtureId']
    preference = Path(request['preferencesPath'])
    if (not re.fullmatch(r'[a-f0-9]{32}', identifier) or not preference.is_absolute() or preference.is_symlink() or
            preference.name != 'qa-preferences-' + identifier + '.cfg' or
            hashlib.sha256(preference.read_bytes()).hexdigest() != request['preferencesSha256']):
        raise RuntimeError('Display restart preference fixture does not match the parent')
    engine = command[:command.index('--')]
    if '--write-movie' in engine:
        index = engine.index('--write-movie')
        del engine[index:index + 2]
    child_command = [*engine, '--', '--playtest=display-settings', '--playtest-restore-display',
                     '--playtest-preferences=' + identifier, '--playtest-output=' + str(output)]
    process = None
    started = time.monotonic()
    receipt = {'passed': False, 'parentExited': True, 'timeout': 90, 'exitCode': None,
               'preferencesSha256': request['preferencesSha256'], 'command': child_command}
    try:
        with (output / 'runtime.log').open('w') as log:
            process = subprocess.Popen(child_command, cwd=cwd, env=environment, stdout=log,
                                       stderr=subprocess.STDOUT, start_new_session=os.name == 'posix')
            receipt['exitCode'] = process.wait(timeout=90)
        text = (output / 'runtime.log').read_text(errors='replace')
        receipt['runtimeErrors'] = [line for line in text.splitlines() if re.search(
            r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|Unhandled Exception|FATAL:)', line)]
        receipt['summary'] = json.loads((output / 'summary.json').read_text())
        receipt['passed'] = True
        try:
            inspect(receipt, parent)
        except Exception:
            receipt['passed'] = False
            raise
        return receipt
    finally:
        if process is not None and process.poll() is None:
            if os.name == 'posix': os.killpg(process.pid, signal.SIGKILL)
            else: subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], capture_output=True)
            process.wait(timeout=10)
        preference.unlink(missing_ok=True)
        receipt['seconds'] = time.monotonic() - started
        (folder / 'display-restart.json').write_text(json.dumps(receipt, indent=2) + '\n')
