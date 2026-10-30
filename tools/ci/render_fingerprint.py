"""Conservative, reviewable rendering identity; release numbers are not rendering inputs."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT=Path(__file__).resolve().parents[2]
EXACT={'project.godot','export_presets.cfg','icon.svg','Bomberman64Multiplayer.csproj','Directory.Build.props','tools/ci/install_godot.py'}
PREFIXES=('scripts/app/','scripts/arena/','scripts/rendering/','scripts/ui/','scenes/',
          'src/Bomber.DesktopLauncher/','src/Bomber.Game/Simulation/Stages/')


def selected(name):
    return name in EXACT or name.startswith(PREFIXES)or name.startswith('assets/')and not name.startswith('assets/testing/')


def normalized(name,data):
    if name=='export_presets.cfg':
        # These four fields affect OS package identity only. Every other preset
        # byte, including template/driver/options, participates in the hash.
        return re.sub(rb'^application/(?:short_version|version|product_version|file_version)="[^"]*"$',
                      b'application/release_identity="<excluded>"',data,flags=re.MULTILINE)
    return data


def calculate(root=ROOT):
    names=subprocess.check_output(['git','ls-files','-z'],cwd=root).decode().split('\0')
    files=[]
    for name in sorted(filter(selected,filter(None,names))):
        data=(root/name).read_bytes()
        files.append({'path':name,'sha256':hashlib.sha256(normalized(name,data)).hexdigest(),
                      'normalization':'release-identity-only'if name=='export_presets.cfg'else'none'})
    digest=hashlib.sha256(json.dumps(files,sort_keys=True,separators=(',',':')).encode()).hexdigest()
    return {'schemaVersion':1,'sha256':digest,'files':files,
            'policy':'Non-blocking signal: patch rendering changes warn; major/minor releases require exact-source GPU receipts.'}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path)
    args=parser.parse_args();data=calculate();encoded=json.dumps(data,indent=2)+'\n'
    if args.output:args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(encoded)
    else:print(encoded,end='')
