#!/usr/bin/env python3
"""Rehearsal AWS CLI fence: read-only service operations, explicit account profile/region."""
import os
from pathlib import Path
import subprocess
import sys
import shutil

import builder_namespace as names


READ_PREFIXES=('describe-','list-','get-','head-','simulate-')
# FREE permissions smoke: the real CreateStack/DeleteStack, UpdateSchedule and
# owned-staging object calls, but RunInstances only as an authorization dry run.
SMOKE_MUTATIONS={('cloudformation','create-stack'),('cloudformation','delete-stack'),('scheduler','update-schedule'),
                 ('s3api','put-object'),('s3api','delete-objects'),('s3api','abort-multipart-upload'),
                 ('s3api','delete-bucket'),('ec2','run-instances')}


def split(arguments):
    options={'--profile','--region','--output','--cli-connect-timeout','--cli-read-timeout','--query'}
    flags={'--no-paginate','--no-cli-pager'};values={};position=0
    while position<len(arguments)and arguments[position].startswith('--'):
        option=arguments[position]
        if option in flags:position+=1;continue
        if option not in options or position+1>=len(arguments):raise ValueError('Unknown rehearsal CLI option')
        values[option]=arguments[position+1];position+=2
    if values.get('--profile')!='sancus'or values.get('--region')!=names.REGION:
        raise ValueError('Rehearsal requires explicit sancus/us-east-1')
    if len(arguments)<=position+1:raise ValueError('AWS service and operation required')
    return arguments[position],arguments[position+1],arguments[position+2:]


def validate(arguments,smoke=False):
    service,operation,rest=split(arguments)
    if operation.startswith(READ_PREFIXES):return arguments
    if smoke and (service,operation)in SMOKE_MUTATIONS:
        if (service,operation)==('ec2','run-instances')and('--dry-run'not in rest or'--no-dry-run'in rest):
            raise ValueError('Permissions smoke refuses RunInstances without --dry-run')
        return arguments
    raise ValueError(('Permissions smoke' if smoke else 'Noncreating rehearsal')+' refuses AWS operation: '+operation)


def install(directory,smoke=False):
    actual=os.environ.get('BUILDER_REAL_AWS')or shutil.which('aws')
    if not actual:raise ValueError('Actual AWS CLI missing')
    directory=Path(directory);directory.mkdir(parents=True,exist_ok=False)
    script=Path(__file__).resolve();guard=directory/'aws'
    policy="sys.argv.insert(1,'--smoke-policy')\n"if smoke else''
    guard.write_text('#!'+sys.executable+'\nimport runpy,sys\nsys.path.insert(0,'+repr(str(script.parent))+')\n'+policy+'runpy.run_path('+repr(str(script))+',run_name="__main__")\n')
    guard.chmod(0o700)
    with open(os.environ['GITHUB_ENV'],'a')as stream:stream.write('BUILDER_REAL_AWS='+str(Path(actual).resolve())+'\n')
    with open(os.environ['GITHUB_PATH'],'a')as stream:stream.write(str(directory.resolve())+'\n')
    return guard


if __name__=='__main__':
    if sys.argv[1:2]==['--install']:
        install(sys.argv[2],smoke=sys.argv[3:]==['--smoke']);raise SystemExit(0)
    smoke=sys.argv[1:2]==['--smoke-policy']
    try:arguments=validate(sys.argv[2 if smoke else 1:],smoke)
    except ValueError as error:raise SystemExit(str(error))
    original=Path(os.environ['BUILDER_REAL_AWS']).resolve()
    if original==Path(sys.argv[0]).resolve():raise SystemExit('Recursive rehearsal CLI path')
    raise SystemExit(subprocess.run([str(original),*arguments]).returncode)
