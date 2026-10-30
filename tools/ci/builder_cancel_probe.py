"""Free hosted cancellation timestamps; never fabricate a real instance request."""
import argparse
import datetime
import json
from pathlib import Path
import signal
import time


def mark(state):
    state=Path(state);state.mkdir(parents=True,exist_ok=True)
    now=datetime.datetime.now(datetime.timezone.utc)
    source=state/'cancellation.json';cancel=json.loads(source.read_text())if source.exists()else{}
    result={'terminationStepEnteredAtUtc':now.isoformat(),'actualTerminationRequested':False,
            'scope':'Free probe has no instances. This measures step entry, not an EC2 termination.'}
    if cancel.get('signalObservedAtUtc'):
        result['cancelToTerminationStepSeconds']=(now-datetime.datetime.fromisoformat(cancel['signalObservedAtUtc'])).total_seconds()
    (state/'probe-termination.json').write_text(json.dumps(result,indent=2)+'\n')
    return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('--mark-termination',action='store_true');args=parser.parse_args()
    if args.mark_termination:mark(args.state);raise SystemExit(0)
    args.state.mkdir(parents=True,exist_ok=True)
    def cancel(number,frame):
        result={'signalObservedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'signal':number,
                'scope':'Signal observed by the actual hosted step entry process; not a synthetic cloud termination.'}
        (args.state/'cancellation.json').write_text(json.dumps(result,indent=2)+'\n')
        raise SystemExit(130)
    signal.signal(signal.SIGINT,cancel);signal.signal(signal.SIGTERM,cancel)
    deadline=time.monotonic()+600
    while time.monotonic()<deadline:time.sleep(1)
    raise SystemExit('No cancellation signal observed inside the bounded free probe')
