"""Bootstrap acknowledgement from the disposable user, never graphics proof."""
import argparse
import ctypes
import getpass
import json
import os
from pathlib import Path

import builder_guest
import native_execution


def publish(manifest_path,target,output):
    manifest=json.loads(Path(manifest_path).read_text())
    descriptor={**manifest,'target':target}
    builder_guest.validate_inputs(manifest,descriptor)
    identity=native_execution.instance_identity()
    if identity['accountId']!=manifest['account']or identity['region']!=manifest['region']:
        raise ValueError('Bootstrap account/region differs')
    user=getpass.getuser();session=None
    if os.name=='nt':
        session_id=ctypes.c_ulong()
        if not ctypes.windll.kernel32.ProcessIdToSessionId(os.getpid(),ctypes.byref(session_id)):
            raise RuntimeError('Cannot observe the actual Windows session')
        session=session_id.value
        if session==0 or user!='BlastQa'or ctypes.windll.shell32.IsUserAnAdmin():
            raise RuntimeError('Bootstrap readiness requires the standard interactive user')
    elif os.getuid()==0 or user!='ubuntu':raise RuntimeError('Bootstrap readiness requires the disposable Ubuntu user')
    receipt={'schemaVersion':1,**{key:manifest[key]for key in ('runId','source','account','region')},
             'target':target,'instanceId':identity['instanceId'],'payloadManifestSha256':builder_guest.digest(manifest_path),
             'ready':True,'user':user,'sessionId':session,'hardwareQualified':False,
             'scope':'Owned user/bootstrap ready only; initialized renderer and 2x support still require the game controls.'}
    output=Path(output);output.write_text(json.dumps(receipt,indent=2)+'\n')
    builder_guest.object_client(manifest['runId']).call('put-object','--key','results/'+target+'/bootstrap-ready.json','--body',str(output))
    return receipt


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--manifest',type=Path,required=True)
    parser.add_argument('--target',choices=('windows-x64','linux-x64'),required=True);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();publish(args.manifest,args.target,args.output)
