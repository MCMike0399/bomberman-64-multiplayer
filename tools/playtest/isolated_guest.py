"""Run a prepared game payload inside the disposable QA desktop, never on the host."""
import json
import os
from pathlib import Path
import plistlib
import pwd
import re
import signal
import subprocess
import tempfile
import time
import urllib.request


def ensure_guest():
    model = subprocess.check_output(['sysctl', '-n', 'hw.model'], text=True).strip()
    if not model.startswith('VirtualMac'):
        raise RuntimeError('Guest launcher refuses to run the game on a physical Mac')
    return model


def main():
    model = ensure_guest()
    payload = Path(__file__).resolve().parent
    request = json.loads((payload / 'request.json').read_text())
    output = Path('/Volumes/My Shared Files/evidence')
    server = None
    game = None
    environment = {'model': model, 'platform': subprocess.check_output(['sw_vers'], text=True),
                   'uid': os.getuid(), 'consoleUser': subprocess.check_output(
                       ['stat', '-f', '%Su', '/dev/console'], text=True).strip()}
    if environment['consoleUser'] != pwd.getpwuid(os.getuid()).pw_name or os.getuid() == 0:
        raise RuntimeError('Guest agent must run as the logged-in desktop user; real focus tests need an Aqua session')
    gpu = subprocess.run(['system_profiler', 'SPDisplaysDataType', '-json'],
                         capture_output=True, text=True, timeout=30)
    environment['graphics'] = json.loads(gpu.stdout) if gpu.returncode == 0 else gpu.stderr
    (output / 'guest-environment.json').write_text(json.dumps(environment, indent=2) + '\n')
    # Copy the signed bundle to a real guest filesystem. The shared payload is read-only.
    with tempfile.TemporaryDirectory(prefix='blast-qa-') as temporary:
        app = Path(temporary) / 'Game.app'
        subprocess.run(['ditto', str(payload / 'Game.app'), str(app)], check=True)
        with (app / 'Contents/Info.plist').open('rb') as stream:
            executable = plistlib.load(stream)['CFBundleExecutable']
        try:
            origin = None
            if request['network']:
                server_dir = Path(temporary) / 'server'
                subprocess.run(['ditto', str(payload / 'server'), str(server_dir)], check=True)
                with (output / 'server.log').open('w') as log:
                    server = subprocess.Popen([str(server_dir / 'Bomber.Server'), '--urls',
                                               'http://127.0.0.1:0'], cwd=server_dir,
                                              stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
                deadline = time.monotonic() + 30
                while time.monotonic() < deadline:
                    if server.poll() is not None:
                        raise RuntimeError('Guest server exited before readiness')
                    match = re.search(r'Now listening on:\s+(http://127\.0\.0\.1:\d+)',
                                      (output / 'server.log').read_text(errors='replace'))
                    if match:
                        try:
                            with urllib.request.urlopen(match.group(1) + '/realtime/v1/games', timeout=1) as response:
                                if any(g['id'] == 'blast' for g in json.load(response)['value']):
                                    origin = match.group(1)
                                    break
                        except (OSError, ValueError):
                            pass
                    time.sleep(.1)
                if origin is None:
                    raise RuntimeError('Guest server did not become ready')
            command = [str(app / 'Contents/MacOS' / executable), *request['engineArgs'], '--',
                       '--playtest=' + request['scenario'], '--playtest-output=' + str(output)]
            if request['windowed']:
                command.append('--display-windowed')
            if request['script']:
                command.append('--playtest-file=' + str(payload / 'script.json'))
            if request.get('coldEffects'):
                command.append('--playtest-cold-effects')
            if origin:
                command.append('--playtest-server=' + origin)
            (output / 'guest-command.json').write_text(json.dumps(command, indent=2) + '\n')
            game = subprocess.Popen(command, cwd=temporary, start_new_session=True)
            code = game.wait(timeout=request['timeout'])
            if server and server.poll() is not None:
                raise RuntimeError('Guest server exited during the game trial')
            return code
        finally:
            for process in (game, server):
                if process:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    process.wait()


if __name__ == '__main__':
    raise SystemExit(main())
