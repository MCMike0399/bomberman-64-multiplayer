#!/usr/bin/env python3
"""Import actual Mac authoring requests; never infer/generate geometry in Python."""
import argparse,hashlib,json,shutil
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('capture',type=Path);p.add_argument('--output',type=Path,default=Path('assets/models/primitives'));a=p.parse_args()
entries={}
for source in sorted(a.capture.glob('capture-*.json')):
 for key,entry in json.loads(source.read_text()).items():
  data=(a.capture/(key+'.res')).read_bytes()
  if hashlib.sha256(data).hexdigest()!=entry['sha256']:raise ValueError('Captured resource hash changed: '+key)
  if key in entries and entries[key]['sha256']!=entry['sha256']:raise ValueError('Conflicting Mac surfaces: '+key)
  entries[key]=entry
if not entries:raise ValueError('No captured requests')
a.output.mkdir(parents=True,exist_ok=True)
for key in entries:shutil.copyfile(a.capture/(key+'.res'),a.output/(key+'.res'))
# Refuse obsolete assets instead of silently leaving unreferenced resource files.
extra={f.stem for f in a.output.glob('*.res')}-entries.keys()
if extra:raise ValueError('Unreferenced output templates: '+str(sorted(extra)))
(a.output/'hashes.json').write_text(json.dumps({k:e['sha256'] for k,e in sorted(entries.items())},indent=2)+'\n')
(a.output/'requests.json').write_text(json.dumps({'schemaVersion':1,'engine':'4.7.2.stable.mono.official.ed1daf0bf','producer':'Mac authoring SDK, actual runtime requests, packed surface roundtrip verified','requests':dict(sorted(entries.items()))},indent=2)+'\n')
print('Imported',len(entries),'hash-pinned primitive requests')
