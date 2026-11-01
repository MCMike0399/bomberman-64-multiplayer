"""Linux evidence for a packaged game that exits non-zero or without a summary.

The AppImage type-2 runtime reports a child killed by any signal as exit 127
(runtime.c: `WIFEXITED(status) ? WEXITSTATUS(status) : EXIT_EXECERROR`), so the
real cause has to come from the kernel log, a core-dump handler and memory
pressure. Nothing here changes the artifact under test: the game only inherits
a core-size limit raised to the hard limit. Capture is best effort and never
changes a scenario's verdict.
"""
import json
import re
import resource
import shutil
import subprocess
import time
from pathlib import Path

GAME_NAMES = ('BlastCircuit', 'Blast-Circuit', 'Godot')
# Kernel lines that name the signal the runtime hid. The OOM killer sends SIGKILL.
KERNEL_SIGNALS = (
    (re.compile(r'Out of memory: Killed process (?P<pid>\d+) \((?P<comm>[^)]*)\)'), 'SIGKILL', 'kernel OOM killer'),
    (re.compile(r'oom-kill:.*task=(?P<comm>[^,]*),pid=(?P<pid>\d+)'), 'SIGKILL', 'kernel OOM killer'),
    (re.compile(r'(?P<comm>\S+)\[(?P<pid>\d+)\]: segfault at'), 'SIGSEGV', 'kernel segfault report'),
    (re.compile(r'traps: (?P<comm>\S+)\[(?P<pid>\d+)\] (?:general protection|trap (?:invalid opcode|int3|divide error))'),
     'SIGSEGV/SIGILL/SIGTRAP/SIGFPE', 'kernel trap report'),
)


def memory():
    """MemTotal/MemAvailable/SwapFree in KiB, or {} when /proc is unavailable."""
    try:
        rows = dict(line.split(':', 1) for line in Path('/proc/meminfo').read_text().splitlines() if ':' in line)
        return {key: int(rows[key].split()[0]) for key in ('MemTotal', 'MemAvailable', 'SwapFree') if key in rows}
    except (OSError, ValueError, IndexError):
        return {}


def read(path):
    try:
        return Path(path).read_text().strip()
    except OSError as error:
        return 'unavailable: ' + str(error)


def enable_cores():
    """preexec_fn for the game process: allow a core dump up to the existing hard limit."""
    _, hard = resource.getrlimit(resource.RLIMIT_CORE)
    resource.setrlimit(resource.RLIMIT_CORE, (hard, hard))


def before():
    return {'startEpoch': time.time(), 'memoryKiBBefore': memory(),
            'corePattern': read('/proc/sys/kernel/core_pattern'),
            'coreLimitHard': resource.getrlimit(resource.RLIMIT_CORE)[1]}


def run(command, timeout=60):
    """Run as this user, then with non-interactive sudo; never prompt. Returns output or the reasons."""
    if not shutil.which(command[0]):
        return {'command': command, 'error': command[0] + ' is not installed'}
    reasons = []
    for prefix in ([], ['sudo', '-n']):
        if prefix and not shutil.which('sudo'):
            continue
        try:
            result = subprocess.run(prefix + command, capture_output=True, text=True, timeout=timeout)
        except (OSError, subprocess.SubprocessError) as error:
            reasons.append(' '.join(prefix + command) + ': ' + str(error))
            continue
        if result.returncode == 0:
            return {'command': prefix + command, 'output': result.stdout}
        reasons.append(' '.join(prefix + command) + ' -> ' + str(result.returncode) + ': ' + result.stderr.strip()[:300])
    return {'command': command, 'error': '; '.join(reasons)}


def kernel_signals(text):
    found = []
    for line in text.splitlines():
        for pattern, signal, source in KERNEL_SIGNALS:
            match = pattern.search(line)
            if match:
                found.append({'signal': signal, 'source': source, 'pid': int(match.group('pid')),
                              'comm': match.group('comm'), 'line': line.strip()[:400]})
    return found


def coredumps(text):
    """Rows of `coredumpctl list`: TIME PID UID GID SIG COREFILE EXE [SIZE]."""
    rows = []
    for line in text.splitlines():
        match = re.search(r'\s(?P<pid>\d+)\s+\d+\s+\d+\s+(?P<sig>SIG[A-Z0-9]+|\d+)\s+\S+\s+(?P<exe>\S+)', line)
        if match:
            rows.append({'pid': int(match.group('pid')), 'signal': match.group('sig'), 'exe': match.group('exe')})
    return rows


def game_process(name):
    return any(part.lower() in name.lower() for part in GAME_NAMES)


def after(folder, state, exit_code, summary_present):
    """Record memory for every run; on an abnormal exit also save kernel/core-dump evidence."""
    folder = Path(folder)
    record = {'schemaVersion': 1, **state, 'memoryKiBAfter': memory(), 'exitCode': exit_code,
              'summaryPresent': summary_present, 'abnormal': exit_code != 0 or not summary_present,
              'stderrCapturedIn': 'runtime.log (stderr is merged into stdout)'}
    if not record['abnormal']:
        return record
    record['note'] = ('The AppImage runtime reports a child killed by any signal as 127; the signal below is '
                      'inferred from kernel and core-dump records, not from the exit status.')
    since = '@' + str(int(state['startEpoch']) - 5)
    kernel = run(['journalctl', '-k', '--since', since, '--no-pager', '-o', 'short-iso'])
    if 'output' not in kernel:
        fallback = run(['dmesg', '-T'])
        kernel = {**fallback, 'journalError': kernel.get('error'), 'scope': 'whole kernel ring buffer'}
    (folder / 'linux-kernel-log.txt').write_text(kernel.get('output') or ('unavailable: ' + str(kernel.get('error'))) + '\n')
    record['kernelLog'] = {key: value for key, value in kernel.items() if key != 'output'}
    record['kernelSignals'] = kernel_signals(kernel.get('output', ''))
    listed = run(['coredumpctl', 'list', '--since', since, '--no-pager'])
    record['coredumpList'] = {key: value for key, value in listed.items() if key != 'output'}
    dumps = coredumps(listed.get('output', ''))
    record['coredumps'] = dumps
    details = [listed.get('output') or 'coredumpctl list: ' + str(listed.get('error'))]
    for row in [row for row in dumps if game_process(row['exe'])][:3]:
        # Text only: `info` includes the handler's stack trace when systemd-coredump has symbols.
        info = run(['coredumpctl', 'info', str(row['pid']), '--no-pager'], timeout=120)
        details.append(info.get('output') or 'coredumpctl info ' + str(row['pid']) + ': ' + str(info.get('error')))
    (folder / 'linux-coredumps.txt').write_text('\n\n'.join(details) + '\n')
    # Ubuntu's default handler is apport; it logs the signal even when it keeps no report.
    apport = run(['tail', '-n', '80', '/var/log/apport.log'])
    record['apportLog'] = (apport.get('output') or '')[-6000:] or apport.get('error')
    reports = []
    for directory in ('/var/crash', '/var/lib/apport/coredump'):
        try:
            reports += [str(path) for path in Path(directory).iterdir() if path.stat().st_mtime >= state['startEpoch'] - 5]
        except OSError:
            pass
    record['newCrashReports'] = sorted(reports)
    game = [row for row in dumps if game_process(row['exe'])]
    named = [row for row in record['kernelSignals'] if game_process(row['comm'])] or record['kernelSignals']
    if game:
        record['inferredSignal'] = {'signal': game[-1]['signal'], 'source': 'coredumpctl', 'pid': game[-1]['pid']}
    elif named:
        record['inferredSignal'] = {key: named[-1][key] for key in ('signal', 'source', 'pid', 'comm')}
    else:
        record['inferredSignal'] = None
    (folder / 'linux-crash-capture.json').write_text(json.dumps(record, indent=2) + '\n')
    return record
