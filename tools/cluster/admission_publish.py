#!/usr/bin/env python3
"""Publish fresh cluster pressure to optional placement filters, without changing operator drains."""
import argparse
import json
from pathlib import Path
import subprocess
import time


def placement(state, now):
    if type(state.get('at')) not in (int,float) or not -5 <= now-state['at'] <= 60:
        raise ValueError('Cluster observation is stale')
    excluded = set()
    for node in state.get('nodes',[]):
        recommendation = state.get('admission',{}).get(node['name'])
        if not recommendation or not recommendation.get('overloaded'):
            continue
        fleet = node.get('fleet',{})
        for server in fleet.get('value',[]):
            if server.get('nodeId') == fleet.get('nodeId'):
                excluded.add(server['serverId'])
    return {'at':state['at'],'excludedServers':sorted(excluded)}


def publish(source, destination, peer):
    value = placement(json.loads(source.read_text()),time.time())
    text = json.dumps(value)
    destination.parent.mkdir(parents=True,exist_ok=True)
    temp = destination.with_suffix('.tmp');temp.write_text(text);temp.replace(destination)
    # Atomic replacement is visible through a directory bind mount on both nodes.
    script = "import json,pathlib,sys;d=json.load(sys.stdin);p=pathlib.Path.home()/'Developer/blast-circuit-cluster/admission/placement.json';p.parent.mkdir(parents=True,exist_ok=True);t=p.with_suffix('.tmp');t.write_text(json.dumps(d));t.replace(p)"
    subprocess.run(['ssh','-o','BatchMode=yes','-o','ConnectTimeout=8',peer,'python3 -c '+"'"+script.replace("'","'\\''")+"'"],
                   input=text,text=True,capture_output=True,timeout=15,check=True)
    return value


if __name__=='__main__':
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--state',type=Path,required=True)
    ap.add_argument('--output',type=Path,required=True)
    ap.add_argument('--peer',default='pi')
    ap.add_argument('--once',action='store_true')
    args=ap.parse_args()
    while True:
        try: publish(args.state,args.output,args.peer)
        except Exception as error:
            print(type(error).__name__,flush=True)
            if args.once: raise SystemExit(1)
        if args.once: break
        time.sleep(10)
