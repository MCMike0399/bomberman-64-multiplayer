"""Report even pre-game worker failures; process exit alone never qualifies a run."""
import argparse
import json
from pathlib import Path

import builder_guest
import native_execution


def publish(manifest_path,target,code,log,output):
    manifest=json.loads(Path(manifest_path).read_text());identity=native_execution.instance_identity()
    if identity['accountId']!=manifest['account']or identity['region']!=manifest['region']:
        raise ValueError('Worker outcome account/region differs')
    client=builder_guest.object_client(manifest['runId']);log=Path(log)
    data={'schemaVersion':1,**{key:manifest[key]for key in ('runId','source','account','region')},'target':target,
          'instanceId':identity['instanceId'],'exitCode':int(code),'hardwareQualified':False,
          'scope':'Worker process outcome; inspect retained game receipts before any qualification claim.'}
    if log.is_file():
        data.update(logSha256=builder_guest.digest(log),logSize=log.stat().st_size)
        client.call('put-object','--key','results/'+target+'/worker-log.bin','--body',str(log))
    output=Path(output);output.write_text(json.dumps(data,indent=2)+'\n')
    client.call('put-object','--key','results/'+target+'/worker-exit.json','--body',str(output))
    return data


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--manifest',type=Path,required=True)
    parser.add_argument('--target',choices=('windows-x64','linux-x64'),required=True)
    parser.add_argument('--code',type=int,required=True);parser.add_argument('--log',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    publish(args.manifest,args.target,args.code,args.log,args.output)
