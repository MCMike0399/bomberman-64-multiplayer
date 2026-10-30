#!/usr/bin/env python3
"""Prove the game shutdown drain against the pinned SDK's low-priority exit hang."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import time

ROOT = Path(__file__).resolve().parents[2]
PROBE = ROOT / 'tools/qa/shutdown_probe'


def run(engine, output,resources=False):
    output.mkdir(parents=True, exist_ok=False)
    version = subprocess.check_output([str(engine), '--version'], text=True, timeout=10).strip()
    if not version.startswith('4.7.2.stable.mono.official.ed1daf0bf'):
        raise ValueError('Use the pinned official4.7.2 .NET SDK')
    with (output / 'build.log').open('w') as log:
        subprocess.run(['dotnet', 'build', str(PROBE / 'BlastWorkerExitProbe.csproj'), '--nologo', '-m:1'],
                       cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)
    report = {'scope': 'SDK scheduling regression only; not game/graphics qualification', 'version': version,
              'engineSha256': hashlib.sha256(engine.read_bytes()).hexdigest(), 'watchdogEnabled': False,
              'drainSourceSha256': hashlib.sha256((ROOT / 'scripts/app/ShutdownDrain.cs').read_bytes()).hexdigest(), 'cases': []}
    if resources:
        report['scope']='SDK resource-finalizer lifetime regression only; expected failed control is not a clean game exit'
        report['finalizerSourceSha256']=hashlib.sha256((ROOT/'scripts/app/ManagedFinalizerDrain.cs').read_bytes()).hexdigest()
    for mode in (['resource-pending','resource-fenced']if resources else['low-pending','low-fenced']):
        command = [str(engine), '--headless', '--path', str(PROBE), '--script', str(PROBE / 'probe.gd'), '--', mode]
        start = time.monotonic(); process = None
        with (output / (mode + '.log')).open('w') as log:
            try:
                process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=os.name == 'posix')
                code = process.wait(timeout=8); timed = False
            except subprocess.TimeoutExpired:
                timed = True
                if os.name == 'posix': os.killpg(process.pid, signal.SIGKILL)
                else: subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], capture_output=True, timeout=15)
                code = process.wait(timeout=10)
        text = (output / (mode + '.log')).read_text(errors='replace')
        errors = [line for line in text.splitlines() if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|FATAL:)', line)]
        marker = 'BLAST_POOL_PROBE quit mode=' + mode + ' jobs=32'
        drains = [json.loads(line.split('BLAST_DRAIN_RESULT ', 1)[1]) for line in text.splitlines() if line.startswith('BLAST_DRAIN_RESULT ')]
        passed = marker in text and not errors and (timed if mode == 'low-pending' else not timed and code == 0 and
            len(drains) == 1 and drains[0]['Completed'] and 'BLAST_PRIOR_TASKS_COMPLETE 32' in text and
            'BLAST_SHUTDOWN_ENGINE_POLICY_GUARD_OK' in text)
        if resources:
            errors=[line for line in text.splitlines()if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|FATAL:)',line)or'mutex lock failed'in line]
            marker='BLAST_RESOURCE_PROBE quit mode='+mode+' objects=21000'
            pending=re.search(r'BLAST_RESOURCE_PENDING (\d+)',text)
            drains=[json.loads(line.split('BLAST_FINALIZERS_RESULT ',1)[1])for line in text.splitlines()if line.startswith('BLAST_FINALIZERS_RESULT ')]
            observed_failure=not timed and code!=0 and any(value in text for value in ('mutex lock failed','csharp_lang && !csharp_lang->script_bindings.is_empty()'))
            passed=marker in text and pending is not None and int(pending[1])>0 and (
                observed_failure if mode=='resource-pending'else not timed and code==0 and not errors and len(drains)==1 and drains[0]['Completed'])
        record = {'mode': mode, 'expectedHang': mode == 'low-pending', 'exitCode': code, 'timedOut': timed,
                  'passed': passed, 'seconds': time.monotonic() - start, 'errors': errors, 'drain': drains}
        if resources:record.update(expectedNativeFailure=mode=='resource-pending',cleanGameExitClaim=False)
        report['cases'].append(record)
        print(json.dumps({**record,'errors':errors[:3],'errorCount':len(errors),'rawLog':mode+'.log'}),flush=True)
    report['passed'] = all(case['passed'] for case in report['cases'])
    (output / 'shutdown-probe.json').write_text(json.dumps(report, indent=2) + '\n')
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--resources-only',action='store_true',help='Pair a deliberate pending-resource failed control with the actual managed-finalizer fence')
    args = parser.parse_args()
    raise SystemExit(run(args.godot.resolve(), args.output.resolve(),args.resources_only))
