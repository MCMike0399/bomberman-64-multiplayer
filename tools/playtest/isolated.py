"""A real macOS graphical desktop owned by QA, with no host input sharing."""
import hashlib
import contextlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
GUEST_OUTPUT = Path('/Volumes/My Shared Files/evidence')


def tart_path():
    configured = os.environ.get('TART') or shutil.which('tart')
    if configured:
        return configured
    local = Path.home() / '.local/share/blast-circuit-qa/tart.app/Contents/MacOS/tart'
    return str(local if local.is_file() else
               ROOT / 'artifacts/isolated-qa/bootstrap/tart.app/Contents/MacOS/tart')


def vm_command(tart, vm, payload, output):
    return [tart, 'run', vm, '--no-graphics', '--no-clipboard', '--no-audio',
            '--no-usb-accessories', '--net-host',
            '--dir=payload:' + str(payload) + ':ro', '--dir=evidence:' + str(output)]


@contextlib.contextmanager
def gpu_lease(exclusive):
    """All cooperating VM runners share the GPU; native timing owns it exclusively."""
    import fcntl
    with open(Path(tempfile.gettempdir()) / ('blast-circuit-gpu-' + str(os.getuid()) + '.lock'), 'a') as lock:
        try:
            fcntl.flock(lock, (fcntl.LOCK_EX if exclusive else fcntl.LOCK_SH) | fcntl.LOCK_NB)
        except BlockingIOError:
            raise RuntimeError('Another QA run owns the GPU; native measurements and VM runs must be sequential') from None
        yield


@contextlib.contextmanager
def native_gpu_lease(vm):
    """Exclude isolated QA for the complete native timing interval; never stop others implicitly."""
    import fcntl
    lock_name = 'blast-circuit-vm-' + hashlib.sha256(vm.encode()).hexdigest()[:16] + '.lock'
    with gpu_lease(True), open(Path(tempfile.gettempdir()) / lock_name, 'a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise RuntimeError('Isolated QA owns the GPU; stop its VM before native Metal measurements') from None
        tart = tart_path()
        if not Path(tart).is_file():
            yield {'vm': vm, 'tartInstalled': False}
            return
        machines = json.loads(subprocess.check_output([tart, 'list', '--format', 'json'], text=True, timeout=15))
        running = [m['Name'] for m in machines if m.get('Source') == 'local' and m.get('State') != 'stopped']
        if running:
            raise RuntimeError('Stop running VMs before native Metal measurements: ' + ', '.join(running))
        yield {'vm': vm, 'tartInstalled': True, 'runningVms': running, 'qaVmLeaseHeld': True}


def relocate_summary(folder):
    """Retain the original receipt; translate only paths proven inside the guest output."""
    path = folder / 'summary.json'
    raw = path.read_text()
    summary = json.loads(raw)
    relocated = []
    for value in summary.get('screenshots', []):
        guest = Path(value)
        relative = guest.relative_to(GUEST_OUTPUT)
        if '..' in relative.parts:
            raise ValueError('Guest capture escapes the evidence directory')
        host = folder / relative
        host.resolve().relative_to(folder.resolve())
        relocated.append(str(host))
    summary['screenshots'] = relocated
    (folder / 'guest-summary.json').write_text(raw)
    path.write_text(json.dumps(summary, indent=2) + '\n')
    restart = folder / 'display-restart.json'
    if restart.exists():
        receipt = json.loads(restart.read_text())
        document = receipt.get('summary', {})
        relocated = []
        for value in document.get('screenshots', []):
            relative = Path(value).relative_to(GUEST_OUTPUT)
            if '..' in relative.parts: raise ValueError('Guest restart capture escapes the evidence directory')
            host = folder / relative
            host.resolve().relative_to((folder / 'restart').resolve())
            relocated.append(str(host))
        document['screenshots'] = relocated
        restart.write_text(json.dumps(receipt, indent=2) + '\n')


def inventory(app):
    for path in app.rglob('*'):
        if path.is_symlink():
            path.resolve().relative_to(app.resolve())
    return {p.relative_to(app).as_posix(): (
        {'symlink': os.readlink(p)} if p.is_symlink() else
        {'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
        for p in sorted(app.rglob('*')) if p.is_file() or p.is_symlink()}


def prepare(args, payload, folder, network, windowed):
    with (folder / 'prepare.log').open('w') as log:
        app = payload / 'Game.app'
        if args.app:
            # macOS signatures can use resource-fork/xattr seals; preserve the whole bundle.
            subprocess.run(['ditto', str(args.app.resolve()), str(app)],
                           stdout=log, stderr=subprocess.STDOUT, check=True, timeout=60)
        else:
            subprocess.run([os.sys.executable, str(ROOT / 'tools/packaging/package-macos.py'),
                            '--godot', args.godot, '--output', str(app)], cwd=ROOT,
                           stdout=log, stderr=subprocess.STDOUT, check=True, timeout=600)
        subprocess.run(['codesign', '--verify', '--deep', '--strict', str(app)],
                       stdout=log, stderr=subprocess.STDOUT, check=True, timeout=30)
        (folder / 'tested-app-files.json').write_text(json.dumps(inventory(app), indent=2) + '\n')
        if network:
            published = payload / 'published'
            subprocess.run(['dotnet', 'publish', 'src/Bomber.Server/Bomber.Server.csproj',
                            '-c', 'Release', '-r', 'osx-arm64', '--self-contained', 'true',
                            '-p:UseAppHost=true', '--nologo', '-m:1', '-o', str(published)],
                           cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=300)
            subprocess.run([os.sys.executable, str(ROOT / 'tools/ci/package_server.py'),
                            '--stage-source', str(published), '--stage-destination', str(payload / 'server')],
                           cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=60)
            shutil.copy2(published / 'Bomber.Server', payload / 'server/Bomber.Server')
            # Never expose transitive private configuration from the publish directory.
            shutil.rmtree(published)
            (folder / 'tested-server-files.json').write_text(json.dumps(inventory(payload / 'server'), indent=2) + '\n')
    shutil.copy2(Path(__file__).with_name('isolated_guest.py'), payload / 'guest.py')
    shutil.copy2(Path(__file__).with_name('display_restart.py'), payload / 'display_restart.py')
    if args.script:
        shutil.copy2(args.script, payload / 'script.json')
    engine_args = ['--max-fps', '30', '--fixed-fps', '30', '--disable-vsync']
    if windowed:
        engine_args.append('--windowed')
    if args.record:
        engine_args += ['--write-movie', str(GUEST_OUTPUT / 'gameplay.avi')]
    (payload / 'request.json').write_text(json.dumps({
        'scenario': args.scenario, 'timeout': args.timeout, 'network': network,
        'windowed': windowed, 'script': bool(args.script), 'coldEffects': getattr(args, 'cold_effects', False), 'engineArgs': engine_args}))


def run(args, folder, result, network, windowed):
    if platform.system() != 'Darwin' or platform.machine() != 'arm64':
        raise RuntimeError('The isolated desktop requires an Apple Silicon Mac')
    if args.server != 'http://127.0.0.1:18496':
        raise ValueError('Isolated QA owns its guest server; custom --server requires --desktop shared')
    tart = tart_path()
    if not Path(tart).is_file():
        raise RuntimeError('Tart is not installed. See tools/playtest/README.md#isolated-macos-desktop; no host game was launched')
    vm = args.vm
    import fcntl
    lock_name = 'blast-circuit-vm-' + hashlib.sha256(vm.encode()).hexdigest()[:16] + '.lock'
    with gpu_lease(False), open(Path(tempfile.gettempdir()) / lock_name, 'a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise RuntimeError('Another playtest owns this VM; wait for its receipt') from None
        machines = json.loads(subprocess.check_output([tart, 'list', '--format', 'json'], text=True, timeout=15))
        matches = [m for m in machines if m.get('Name') == vm and m.get('Source') == 'local']
        if len(matches) != 1 or matches[0].get('State') != 'stopped':
            raise RuntimeError('QA requires its existing, stopped VM: ' + vm + '; will not take over a running desktop')
        result['desktop'] = {'mode': 'isolated', 'vm': vm, 'hostInputSharing': False,
                             'physicalDeviceCoverage': False, 'nativeHostGpuCoverage': False}
        result['serverOrigin'] = None
        configuration = json.loads(subprocess.check_output(
            [tart, 'get', vm, '--format', 'json'], text=True, timeout=15))
        result['desktop']['configuration'] = {key: configuration[key] for key in ('OS', 'CPU', 'Memory', 'Display')}
        with tempfile.TemporaryDirectory(prefix='blast-qa-payload-') as temporary:
            payload = Path(temporary)
            prepare(args, payload, folder, network, windowed)
            command = vm_command(tart, vm, payload, folder)
            result['desktop']['command'] = command
            runtime = None
            observer = None
            try:
                with (folder / 'vm.log').open('w') as vm_log:
                    runtime = subprocess.Popen(command, stdout=vm_log, stderr=subprocess.STDOUT, start_new_session=True)
                    from host_observer import HostObserver
                    observer = HostObserver(runtime.pid, folder)
                    observer.start()
                    deadline = time.monotonic() + 180
                    last_probe = 'no response'
                    while time.monotonic() < deadline:
                        if runtime.poll() is not None:
                            raise RuntimeError('VM exited during startup; inspect vm.log')
                        try:
                            probe = subprocess.run([tart, 'exec', vm, '/usr/bin/true'],
                                                   capture_output=True, text=True, timeout=15)
                            if probe.returncode == 0:
                                break
                            last_probe = (probe.stderr or probe.stdout).strip()[-1000:]
                        except subprocess.TimeoutExpired:
                            last_probe = 'guest agent RPC timed out'
                        time.sleep(1)
                    else:
                        raise RuntimeError('Guest agent did not become ready within 180 seconds: ' + last_probe)
                    command = [tart, 'exec', vm, '/usr/bin/python3',
                               '/Volumes/My Shared Files/payload/guest.py']
                    result['command'] = command
                    from contracts import catalog, expanded
                    restart_budget = 90 if 'display-settings' in expanded(args.scenario, catalog()) else 0
                    with (folder / 'runtime.log').open('w') as log:
                        trial = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT,
                                               timeout=args.timeout + 90 + restart_budget)
                    result['exitCode'] = trial.returncode
                    if (folder / 'guest-command.json').is_file():
                        result['guestCommand'] = json.loads((folder / 'guest-command.json').read_text())
                        result['serverOrigin'] = next((value.split('=', 1)[1] for value in result['guestCommand']
                                                       if value.startswith('--playtest-server=')), None)
            finally:
                try:
                    if runtime:
                        # Only this invocation's VM process is ours; never stop unrelated guests.
                        from run import stop
                        stop(runtime)
                finally:
                    if observer:
                        result['desktop']['hostObservation'] = observer.finish()
            if result['desktop']['hostObservation']['vmBecameForeground']:
                raise RuntimeError('QA VM unexpectedly became the foreground host application')
            if (folder / 'summary.json').is_file():
                relocate_summary(folder)
