#!/usr/bin/env python3
"""FREE pre-flight: run the real Linux user-data package and AWS CLI section in ubuntu:24.04.

No AWS credentials and no S3 calls: the generated section ends before the first
owned-bucket request. Two fault controls prove that a missing apt package and a
changed CLI digest fail before unzip, and that the ERR trap copies the log tail
to the console target with the failure marker.
"""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import time

import builder_bootstrap

RUN='sancus-builder-20261030-0000000-0123456789abcdef'
MSI={'url':'https://awscli.amazonaws.com/AWSCLIV2.msi','sha256':'3'*64}
ROOT='/opt/sancus-builder'
MISSING='sancus-builder-preflight-missing-package'
CASES=('success','missing-package','digest-mismatch')


def section(case='success'):
    script=builder_bootstrap.userdata({'runId':RUN},'1'*64,'2'*64,MSI)['Linux']
    text=builder_bootstrap.linux_package_section(script)
    if 's3api'in text or '--bucket'in text:raise ValueError('Pre-flight section must not reach the owned bucket')
    if case=='missing-package':
        text=text.replace('apt-get install -y ','apt-get install -y '+MISSING+' ',1)
    elif case=='digest-mismatch':
        text=text.replace(builder_bootstrap.LINUX_AWS_CLI['sha256'],'0'*64,1)
    elif case!='success':raise ValueError('Unknown pre-flight case')
    return text,builder_bootstrap.linux_packages(script)


def runner(packages):
    """Container entrypoint: run the exact section, then inspect its results."""
    quoted=' '.join(packages)
    return f'''#!/bin/bash
set -uo pipefail
export SANCUS_BUILDER_CONSOLE=/preflight/console.txt
: > /preflight/console.txt
bash /preflight/section.sh
status=$?
cp {ROOT}/bootstrap.log /preflight/bootstrap.log 2>/dev/null || true
printf '%s\\n' "$status" > /preflight/status.txt
test -e {ROOT}/awscli-install/aws && echo present > /preflight/unzipped.txt || echo absent > /preflight/unzipped.txt
test "$status" = 0 || exit "$status"
dpkg-query -W -f='${{Package}}\\t${{Status}}\\n' {quoted} > /preflight/packages.tsv
{builder_bootstrap.LINUX_AWS} --version > /preflight/aws-version.txt 2>&1
stat -c '%U:%G %a %n' {ROOT}/aws-cli {ROOT}/aws-cli/bin {builder_bootstrap.LINUX_AWS} > /preflight/aws-ownership.txt
find {ROOT}/aws-cli \\( ! -user root -o \\( ! -type l -perm /022 \\) \\) -print > /preflight/aws-writable.txt
test ! -e {ROOT}/awscli-install
'''


def run_case(case,output,image,platform):
    directory=Path(output).resolve()/case;directory.mkdir(parents=True,exist_ok=False)
    text,packages=section(case)
    (directory/'section.sh').write_text(text);(directory/'run.sh').write_text(runner(packages))
    started=time.monotonic()
    result=subprocess.run(['docker','run','--rm','--platform',platform,'-v',f'{directory}:/preflight',image,
                           'bash','/preflight/run.sh'],capture_output=True,text=True,timeout=1800)
    (directory/'docker.log').write_text(result.stdout+result.stderr)
    read=lambda name:(directory/name).read_text()if (directory/name).exists()else''
    console=read('console.txt')
    row={'case':case,'exitCode':result.returncode,'seconds':round(time.monotonic()-started,1),
         'consoleFailedMarker':'SANCUS-BUILDER-BOOTSTRAP-FAILED status='in console,
         'consoleLogTailLines':sum(1 for line in console.splitlines()if line.startswith('SANCUS-BUILDER-BOOTSTRAP-FAILED| ')),
         'cliUnzipped':read('unzipped.txt').strip()}
    errors=[]
    if case=='success':
        rows=[line.split('\t')for line in read('packages.tsv').splitlines()]
        row['packages']={name:status for name,status in rows}
        row['awsVersion']=read('aws-version.txt').strip();row['awsOwnership']=read('aws-ownership.txt').splitlines()
        if result.returncode:errors.append('Real package/CLI section failed')
        if sorted(row['packages'])!=sorted(packages)or any(status!='install ok installed'for status in row['packages'].values()):
            errors.append('Not every apt package resolved and installed')
        if not row['awsVersion'].startswith('aws-cli/2.38.5 '):errors.append('Pinned AWS CLI did not run')
        if read('aws-writable.txt').strip()or not all(line.startswith('root:root ')for line in row['awsOwnership']):
            errors.append('AWS CLI directory is not root-owned and non-writable by others')
        if console:errors.append('Success wrote a failure to the console')
    else:
        if result.returncode==0:errors.append('Fault control unexpectedly passed')
        if not row['consoleFailedMarker']or row['consoleLogTailLines']<1:errors.append('ERR trap did not copy the log tail to the console')
        if case=='missing-package'and MISSING not in console:errors.append('Console tail lacks the apt failure')
        if case=='digest-mismatch'and row['cliUnzipped']!='absent':errors.append('CLI bundle was unzipped before its digest verified')
    row['errors']=errors;row['passed']=not errors
    return row


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--image',default='ubuntu:24.04');parser.add_argument('--platform',default='linux/amd64')
    parser.add_argument('--case',choices=CASES,action='append')
    args=parser.parse_args(argv)
    args.output.mkdir(parents=True,exist_ok=True)
    rows=[run_case(case,args.output,args.image,args.platform)for case in (args.case or CASES)]
    receipt={'schemaVersion':1,'image':args.image,'platform':args.platform,'awsCli':builder_bootstrap.LINUX_AWS_CLI,
             'cases':rows,'passed':all(row['passed']for row in rows),
             'scope':'Free container pre-flight of the generated Linux user-data package/CLI section only; no AWS, NVIDIA, desktop or game proof.'}
    (args.output/'preflight-linux.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(json.dumps(receipt,indent=2))
    return 0 if receipt['passed']else 1


if __name__=='__main__':sys.exit(main())
