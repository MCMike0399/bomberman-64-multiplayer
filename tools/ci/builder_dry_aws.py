#!/usr/bin/env python3
"""Rehearsal AWS CLI fence: read-only service operations, explicit account profile/region."""
import os
from pathlib import Path
import subprocess
import sys

import builder_namespace as names


def validate(arguments):
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
    operation=arguments[position+1]
    if not operation.startswith(('describe-','list-','get-','head-','simulate-')):
        raise ValueError('Noncreating rehearsal refuses AWS operation: '+operation)
    return arguments


if __name__=='__main__':
    try:arguments=validate(sys.argv[1:])
    except ValueError as error:raise SystemExit(str(error))
    original=Path(os.environ['BUILDER_REAL_AWS']).resolve()
    if original==Path(sys.argv[0]).resolve():raise SystemExit('Recursive rehearsal CLI path')
    raise SystemExit(subprocess.run([str(original),*arguments]).returncode)
