"""Reject procedural PrimitiveMesh generation outside the explicit Mac authoring boundary."""
import re
import hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
AUTHORING=Path('scripts/rendering/PrimitiveTemplates.cs')
KINDS=r'(?:Box|Sphere|Cylinder|Torus|Plane|Capsule|Prism|Point|Quad|Text|TubeTrail|RibbonTrail)Mesh'

def check(root=ROOT):
    errors=[]
    for path in sorted((root/'scripts').rglob('*.cs')):
        relative=path.relative_to(root)
        if relative==AUTHORING:continue
        text=re.sub(r'"(?:\\.|[^"\\])*"','""',path.read_text())
        # Explicit and target-typed construction, plus casts/type tests which would
        # bypass immutable request metadata or mutate a loaded template.
        if re.search(r'\b'+KINDS+r'\b',text):errors.append(f'{relative}: primitive type outside Mac authoring boundary')
        if re.search(r'ClassDB\.Instantiate\s*\(',text):errors.append(f'{relative}: dynamic construction outside authoring boundary')
    for suffix in ('*.gd','*.tscn','*.tres'):
        for path in sorted(p for directory in ('scripts','scenes','assets') if (root/directory).exists() for p in (root/directory).rglob(suffix)):
            if any(p.startswith('.') or p in ('artifacts','burbuja-kernel','native') for p in path.relative_to(root).parts):continue
            if re.search(r'\b'+KINDS+r'\b',path.read_text()):errors.append(f'{path.relative_to(root)}: runtime primitive resource')
    manifest=root/'assets/models/primitives/hashes.json'
    if manifest.exists() or (root/AUTHORING).exists():
        try:
            hashes=json.loads(manifest.read_text())
            if not isinstance(hashes,dict):raise ValueError("hash map must be an object")
            resources={p.stem:p for p in manifest.parent.glob('*.res')}
            if not hashes or resources.keys()!=hashes.keys():raise ValueError('missing or unreferenced packed templates')
            for key,digest in hashes.items():
                if hashlib.sha256(resources[key].read_bytes()).hexdigest()!=digest:raise ValueError('resource hash differs: '+key)
        except (OSError,ValueError,TypeError) as error:errors.append('Primitive template manifest: '+str(error))
    return errors

if __name__=='__main__':
    errors=check()
    print('\n'.join(errors) if errors else 'Primitive runtime generation: none')
    raise SystemExit(bool(errors))
