#!/usr/bin/env python3
"""Bounded old-package shutdown controls; always diagnostic, never graphics qualification."""
import argparse
import ctypes
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import time
import zipfile

import crossplay_runtime
import desktop_packages as desktop
import download_formats
import startup_lifecycle_state as native_state

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('startup_desktop', ROOT / 'tools/packaging/startup-desktop.py')
startup = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(startup)
CASES = [('headless-low', 'low', True, False), ('native-low-fullscreen', 'low', False, False),
         ('native-high-fullscreen', 'high', False, False), ('native-early-quit', 'low', False, True)]


def game_environment():
    # A minidump includes process memory: never give the game CI/provider credentials.
    return {k: v for k, v in os.environ.items() if not re.search(
        r'TOKEN|SECRET|PASSWORD|CREDENTIAL|KEY|^AWS_|^ACTIONS_|^GITHUB_|^GH_', k, re.I)
        and k != 'APPIMAGE_EXTRACT_AND_RUN'}


def write_windows_dump(pid, folder, image):
    import msvcrt
    from ctypes import wintypes
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    debug = ctypes.WinDLL(str(Path(os.environ['SystemRoot']) / 'System32/dbghelp.dll'), use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    debug.MiniDumpWriteDump.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.HANDLE,
                                       wintypes.DWORD, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
    debug.MiniDumpWriteDump.restype = wintypes.BOOL
    handle = kernel.OpenProcess(0x0410, False, pid)  # QUERY_INFORMATION | VM_READ; this owned child only.
    if not handle: raise ctypes.WinError(ctypes.get_last_error())
    try:
        try:
            kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
            path = ctypes.create_unicode_buffer(32768); length = wintypes.DWORD(len(path))
            if not kernel.QueryFullProcessImageNameW(handle, 0, path, ctypes.byref(length)):
                raise ctypes.WinError(ctypes.get_last_error())
            if not os.path.samefile(path.value, image): raise ValueError('Live process image differs from owned installed image')
            layout = native_state.layout_for_image(image)
            modules = (wintypes.HMODULE * 1024)(); needed = wintypes.DWORD()
            kernel.K32EnumProcessModules.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.HMODULE), wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
            if not kernel.K32EnumProcessModules(handle, modules, ctypes.sizeof(modules), ctypes.byref(needed)):
                raise ctypes.WinError(ctypes.get_last_error())
            kernel.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
            def read(address, size):
                data = ctypes.create_string_buffer(size); actual = ctypes.c_size_t()
                if not kernel.ReadProcessMemory(handle, address, data, size, ctypes.byref(actual)) or actual.value != size:
                    raise ctypes.WinError(ctypes.get_last_error())
                return data.raw
            state = native_state.snapshot(read, modules[0], layout)
            state['atomic'] = False  # ReadProcessMemory does not suspend every target thread.
        except Exception as error: state = {'readError': str(error)}
        (folder / 'native-pool-state.json').write_text(json.dumps(state, indent=2) + '\n')
        with (folder / 'native.dmp').open('wb') as dump:
            if not debug.MiniDumpWriteDump(handle, pid, msvcrt.get_osfhandle(dump.fileno()),
                                           0x1000, None, None, None):  # Normal stacks + thread information, not full heap.
                raise ctypes.WinError(ctypes.get_last_error())
    finally: kernel.CloseHandle(handle)

def collect_stack(process, folder, image):
    if os.name == 'nt':
        # DbgHelp can itself block; its helper must not defeat the test deadline.
        with (folder / 'dump-collector.log').open('w') as log:
            result = subprocess.run([sys.executable, str(Path(__file__).resolve()), '--dump-windows', str(process.pid),
                                     '--output', str(folder), '--image', str(image)], env=game_environment(), stdout=log,
                                    stderr=subprocess.STDOUT, timeout=30)
        dump = folder / 'native.dmp'
        if result.returncode or not dump.is_file() or dump.read_bytes()[:4] != b'MDMP':
            raise RuntimeError('Windows dump collection failed; inspect dump-collector.log')
        return {'kind': 'Windows minidump with thread information', 'pid': process.pid, 'file': 'native.dmp'}
    # Root cannot read a user's default FUSE mount. Supply the checksum-bound
    # archive ELF explicitly so GDB has CFI instead of guessing stack words.
    layout = native_state.layout_for_image(image)
    if layout['platform'] != 'linux-x64': raise ValueError('Wrong native image layout')
    code = ('import gdb,json,sys;sys.path.insert(0,' + repr(str(ROOT / 'tools/ci')) + ');'
            'import startup_lifecycle_state as state;'
            'layout=state.layout_for_image(' + repr(str(image)) + ');'
            'print("BLAST_NATIVE_POOL_STATE "+json.dumps(state.snapshot('
            'lambda address,size:bytes(gdb.selected_inferior().read_memory(address,size)),0,layout)))')
    with (folder / 'native-stack.log').open('w') as log:
        result = subprocess.run(['sudo', '-n', 'gdb', '-nx', '-q', '-batch',
                                 '-iex', 'set debuginfod enabled off', str(image), '-p', str(process.pid),
                                 '-ex', 'set pagination off', '-ex', 'info threads', '-ex', 'thread apply all bt 30',
                                 '-ex', 'python ' + code],
                                stdout=log, stderr=subprocess.STDOUT, timeout=30)
    text = (folder / 'native-stack.log').read_text(errors='replace')
    if result.returncode or '#0 ' not in text or 'Thread' not in text:
        raise RuntimeError('Native stack collection failed; inspect native-stack.log')
    return {'kind': 'gdb live thread stacks', 'pid': process.pid, 'file': 'native-stack.log'}


def inspect_case(record, text, capture):
    errors = [s for s in text.splitlines() if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|FATAL:)', s)]
    record.update(runtimeErrors=errors, runtimeWarnings=[s for s in text.splitlines() if re.search(r'(^|\s)WARNING:', s)],
                  smokeLines=[s for s in text.splitlines() if s.startswith('GODOT_SMOKE_')])
    if record['timedOut'] or record['exitCode'] != 0 or errors: return False
    if record['earlyQuit']: return True  # Only the engine's early-shutdown path; no successful-startup claim.
    matches = re.findall(r'^GODOT_SMOKE_OK menu snapshots=0 graphics=(\S+) window=(\S+) size=(\d+)x(\d+) adapter=(.*)$', text, re.M)
    if len(matches) != 1 or matches[0][0] != record['preset']: return False
    if record['headless']: return True
    return (bool(matches[0][4].strip()) and matches[0][1] == 'fullscreen' and min(map(int, matches[0][2:4])) >= 320 and
            capture.is_file() and capture.stat().st_size > 1024 and capture.read_bytes()[:8] == b'\x89PNG\r\n\x1a\n')


def run(target, directory, output, revision, selected=None):
    if os.environ.get('GITHUB_ACTIONS') != 'true' or platform.system() != crossplay_runtime.SYSTEMS[target][0]:
        raise RuntimeError('Use the disposable native target CI runner')
    if selected and (len(set(selected)) != len(selected) or set(selected) - {case[0] for case in CASES}):
        raise ValueError('Unknown or repeated diagnostic selection')
    output.mkdir(parents=True, exist_ok=False)
    data = crossplay_runtime.metadata(directory, target)
    if data['revision'] != revision: raise ValueError('Wrong frozen payload revision')
    download = download_formats.inspect(directory, target, revision, data['release'])
    report = {'mode': 'diagnostic-only', 'graphicsQualified': False, 'passed': False, 'payloadSource': revision,
              'runnerSource': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
              'target': target, 'os': platform.platform(), 'download': download, 'cases': []}
    executable = None
    try:
        executable = crossplay_runtime.install(directory, target, output)
        image = executable
        if target == 'linux-x64':
            image = output / 'unwind-image' / 'BlastCircuit.x86_64'; image.parent.mkdir()
            with zipfile.ZipFile(directory / desktop.names(target)['archive']) as archive:
                name = desktop.names(target)['root'] + '/' + desktop.TARGETS[target]['executable']
                image.write_bytes(archive.read(name))
            if desktop.digest(image) != data['files'][desktop.TARGETS[target]['executable']]:
                raise ValueError('Unwind ELF differs from frozen archive metadata')
            report['unwindImage'] = {'sourceArchiveEntry': name, 'sha256': desktop.digest(image),
                                     'scope': 'Original frozen input, not an extra result-bundle file'}
        for name, preset, headless, early in CASES:
            if selected and name not in selected: continue
            folder = output / name; folder.mkdir()
            pref = folder / 'preferences.cfg'
            pref.write_text('[player]\nautomaticUpdates=false\n[video]\ngraphicsPreset="' + preset + '"\nfullscreen=true\nresolution=Vector2i(0, 0)\n')
            command = [str(executable), '--verbose', '--rendering-driver', crossplay_runtime.SYSTEMS[target][2],
                       '--log-file', str(folder / 'godot.log')]
            if headless: command += ['--headless']
            if early: command += ['--quit-after', '2', '--windowed']
            command += ['--', '--smoke-menu', '--smoke-seconds=4', '--smoke-preferences=' + str(pref), '--smoke-output=' + str(folder)]
            record = {'name': name, 'preset': preset, 'headless': headless, 'earlyQuit': early, 'command': command,
                      'timeoutSeconds': 240, 'timedOut': False, 'exitCode': None, 'passed': False}
            report['cases'].append(record); start = time.monotonic()
            def snapshot(process):
                record['timeoutObservedAtSeconds'] = round(time.monotonic() - start, 3)
                try: record['nativeStack'] = collect_stack(process, folder, image)
                except Exception as error: record['stackCollectionError'] = str(error)
            try:
                with (folder / 'runtime.log').open('w') as log:
                    result = startup.run_startup(command, folder, game_environment(), log, on_timeout=snapshot)
                record['exitCode'] = result.returncode
            except subprocess.TimeoutExpired: record['timedOut'] = True
            except Exception as error: record['error'] = str(error)
            record['seconds'] = round(time.monotonic() - start, 3)
            text = (folder / 'runtime.log').read_text(errors='replace')
            # The independent engine log can reveal stdout buffering without hiding either stream.
            engine_log = folder / 'godot.log'
            record['engineLogSmokeLines'] = [s for s in engine_log.read_text(errors='replace').splitlines()
                                             if 'GODOT_SMOKE_' in s] if engine_log.exists() else []
            record['passed'] = inspect_case(record, text, folder / 'menu.png')
            print(json.dumps(record), flush=True)
            (output / 'startup-lifecycle.json').write_text(json.dumps(report, indent=2) + '\n')
        report['passed'] = bool(report['cases']) and all(x['passed'] for x in report['cases'])
    finally:
        if target == 'windows-x64' and executable is not None and (executable.parent / 'unins000.exe').is_file():
            try:
                result = subprocess.run([str(executable.parent / 'unins000.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'], timeout=120)
                report['uninstallExitCode'] = result.returncode
                if result.returncode: report['passed'] = False
            except subprocess.SubprocessError as error:
                report.update(passed=False, uninstallError=str(error))
        report['files'] = [{'path': str(p.relative_to(output)), 'sha256': desktop.digest(p)}
                           for p in sorted(output.rglob('*')) if p.is_file() and p.name != 'startup-lifecycle.json' and
                           not {'installed', 'unwind-image'}.intersection(p.relative_to(output).parts)]
        (output / 'startup-lifecycle.json').write_text(json.dumps(report, indent=2) + '\n')
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    if sys.argv[1:2] == ['--dump-windows']:
        write_windows_dump(int(sys.argv[2]), Path(sys.argv[4]), Path(sys.argv[6]))
        raise SystemExit(0)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=('windows-x64', 'linux-x64'), required=True)
    parser.add_argument('--expected-revision', required=True)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--case', choices=[c[0] for c in CASES], action='append')
    args = parser.parse_args()
    raise SystemExit(run(args.target, args.directory.resolve(), args.output.resolve(), args.expected_revision, args.case))
