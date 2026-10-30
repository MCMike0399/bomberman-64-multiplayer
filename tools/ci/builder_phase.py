"""Bound a controller subprocess; the same hosted job owns fresh-auth always cleanup."""
import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
import datetime

import builder_timing


class PhaseCancelled(BaseException):pass


def stop(process):
    if process.poll()is not None:return
    try:os.killpg(process.pid,signal.SIGINT)
    except ProcessLookupError:return
    try:process.wait(timeout=5);return
    except subprocess.TimeoutExpired:pass
    try:os.killpg(process.pid,signal.SIGTERM)
    except ProcessLookupError:return
    try:process.wait(timeout=2);return
    except subprocess.TimeoutExpired:pass
    try:os.killpg(process.pid,signal.SIGKILL)
    except ProcessLookupError:return
    process.wait(timeout=5)


def run(command,budget,log,on_started=None,on_cancelled=None):
    started=time.monotonic();cancelled=False;timed_out=False;cancelled_at=None
    with Path(log).open('w')as stream:
        process=subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True)
        previous=signal.getsignal(signal.SIGTERM)
        previous_int=signal.getsignal(signal.SIGINT)
        def cancel(signum,frame):raise PhaseCancelled()
        signal.signal(signal.SIGTERM,cancel)
        try:
            if on_started:on_started(process)
            code=process.wait(timeout=budget)
        except subprocess.TimeoutExpired:
            timed_out=True;stop(process);code=124
        except (KeyboardInterrupt,PhaseCancelled):
            cancelled_at=datetime.datetime.now(datetime.timezone.utc).isoformat()
            if on_cancelled:on_cancelled(cancelled_at)
            # A second signal must not interrupt reaping this exact owned group.
            signal.signal(signal.SIGTERM,signal.SIG_IGN);signal.signal(signal.SIGINT,signal.SIG_IGN)
            cancelled=True;stop(process);code=130
        finally:
            signal.signal(signal.SIGTERM,previous);signal.signal(signal.SIGINT,previous_int)
    return {'exitCode':code,'cancelled':cancelled,'timedOut':timed_out,'childPid':process.pid,
            'childReaped':process.poll()is not None,'signalObservedAtUtc':cancelled_at,'seconds':round(time.monotonic()-started,3)}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('--operator',action='store_true');parser.add_argument('--artifact-id');parser.add_argument('--artifact-digest')
    parser.add_argument('phase',choices=tuple(builder_timing.PHASE_SECONDS));args=parser.parse_args()
    state=args.state.resolve();plan=json.loads((state/'plan.json').read_text())
    grant=builder_timing.grant(plan,args.phase,args.operator)
    command=[sys.executable,str(Path(__file__).with_name('builder_controller.py')),'--state',str(state),args.phase]
    if args.operator:command+=['--operator']
    if args.artifact_id:command+=['--artifact-id',args.artifact_id]
    if args.artifact_digest:command+=['--artifact-digest',args.artifact_digest]
    def observed(value):
        (state/'cancellation.json').write_text(json.dumps({'signalObservedAtUtc':value,'scope':'Actual controller phase signal observed'},indent=2)+'\n')
    result=run(command,grant['budgetSeconds'],state/('phase-'+args.phase+'.log'),on_cancelled=observed)
    (state/('phase-'+args.phase+'.json')).write_text(json.dumps({**grant,**result},indent=2)+'\n')
    if result['signalObservedAtUtc']:
        (state/'cancellation.json').write_text(json.dumps({'signalObservedAtUtc':result['signalObservedAtUtc'],'scope':'Actual controller phase signal observed'},indent=2)+'\n')
    raise SystemExit(result['exitCode'])
