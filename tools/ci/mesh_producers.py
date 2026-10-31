"""Inventory every source mesh factory/generator and geometry-helper call; new sites require review."""
import argparse,base64,hashlib,json,re,struct
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
INVENTORY=Path('tools/ci/mesh_producers.json')
TOKENS=re.compile(r'(?s)\"\"\".*?\"\"\"|@\"(?:\"\"|[^\"])*\"|\"(?:\\.|[^\"\\])*\"|//[^\n]*|/\*.*?\*/')
PATTERN=re.compile(r'\b(?:ArrayMesh|SurfaceTool|ImmediateMesh|MeshLibrary)\s+\w+\s*=\s*new\s*\(|\bnew\s+(?:Godot\.)?(?:ArrayMesh|SurfaceTool|ImmediateMesh|MeshLibrary|SphereMesh|CylinderMesh|TorusMesh|BoxMesh|PlaneMesh|QuadMesh|CapsuleMesh|PrismMesh)\s*\(|\bPrimitiveTemplates\.(?:Get|With)\s*\(|\bResourceLoader\.Load\s*<\s*(?:ArrayMesh|Mesh|MeshLibrary)\s*>\s*\(|\b\w+\.(?:Commit|AddSurfaceFromArrays|MeshCreate|MeshAddSurface|Duplicate|SetInstanceTransform|SetInstanceColor|SetInstanceCustomData)\s*\(|\b(?:Box|Sphere|Cylinder|Cone|RoundedBox|Taper|Bead|ChamferBox)\s*\(')

def sites(root=ROOT):
    result=[]
    for path in sorted((root/'scripts').rglob('*.cs')):
        raw=path.read_text();masked=TOKENS.sub(lambda m:''.join('\n'if c=='\n'else' 'for c in m[0]),raw);counts={}
        for match in PATTERN.finditer(masked):
            start=match.start();end=match.end();depth=1
            while end<len(masked)and depth:
                if masked[end]=='(':depth+=1
                elif masked[end]==')':depth-=1
                end+=1
            expression=raw[start:end];kind=re.sub(r'\s+','',masked[start:match.end()-1]);counts[kind]=counts.get(kind,0)+1
            file=path.relative_to(root).as_posix();identity=f'{file}:{kind}:{counts[kind]}'
            shape=re.search(r'PrimitiveTemplates\.Get\(\s*"(\w+)"',expression)
            if shape:domain=shape[1]
            elif file.endswith('PrimitiveTemplates.cs'):domain='template-storage-and-portable-box'
            elif '/testing/'in file:domain='test-only'
            elif 'BomberModel'in file or 'BomberMeshLibrary'in file:domain='canonical-bomber-library'
            else:domain='owned-generator-or-helper'
            result.append({'id':identity,'file':file,'line':raw.count('\n',0,start)+1,'call':expression,
                           'callSha256':hashlib.sha256(re.sub(r'\s+','',expression).encode()).hexdigest(),'domain':domain})
    return result

def numeric(encoded):
    data=base64.b64decode(encoded);kind=struct.unpack_from('<I',data)[0]&0xffff
    if kind==2:return struct.unpack_from('<q'if len(data)==12 else'<i',data,4)[0]
    if kind==3:return struct.unpack_from('<d'if len(data)==12 else'<f',data,4)[0]
    if kind in(5,9):return list(struct.unpack('<'+'f'*(2 if kind==5 else 3),data[4:]))
    raise ValueError('Unexpected primitive parameter encoding')

def domains(root=ROOT):
    manifest=json.loads((root/'assets/models/primitives/requests.json').read_text());result={}
    for key,entry in manifest['requests'].items():
        result.setdefault(entry['type'],[]).append({'key':key,'parameters':{p['name']:numeric(p['variantBytes'])for p in entry['properties']}})
    return {kind:sorted(entries,key=lambda e:e['key'])for kind,entries in sorted(result.items())}

def float32(value):return struct.unpack('<f',struct.pack('<f',value))[0]

def literal(value,root=ROOT):
    value=value.strip()
    if re.fullmatch(r'-?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)[fF]?',value):
        return float32(float(value.rstrip('fF'))) if '.'in value or value.endswith(('f','F')) else int(value)
    if value in ('Vector2.One','Vector3.One'):return [1.]*(2 if value=='Vector2.One'else 3)
    vector=re.fullmatch(r'new Vector([23])\(([^()]*)\)',value)
    if vector:
        values=[literal(v,root)for v in vector[2].split(',')]
        return values if len(values)==int(vector[1])and all(v is not None for v in values)else None
    radius_expression=re.fullmatch(r'BlastStages\.MeteorBlastRadius(?:\s*-\s*([.\dfF]+))?',value)
    if radius_expression:
        source=(root/'src/Bomber.Game/Simulation/Stages/BlastStages.RockGarden.cs').read_text()
        level=re.search(r'MeteorFireLevel\s*=\s*(\d+)\s*;',source)
        recipe=re.search(r'MeteorBlastRadius\s*=\s*MeteorFireLevel\s*\*\s*([.\dfF]+)\s*\+\s*([.\dfF]+)\s*;',source)
        if not level or not recipe:raise ValueError('Meteor radius derivation changed; review its finite domain')
        radius=float32(float32(int(level[1])*literal(recipe[1],root))+literal(recipe[2],root))
        return float32(radius-literal(radius_expression[1],root))if radius_expression[1]else radius
    return None

def bind_templates(rows,domain,root=ROOT):
    bound=[]
    for original in rows:
        row=dict(original);kind=row['domain']
        if kind in domain:
            if kind=='BoxMesh':row['portableGenerator']='PrimitiveTemplates.PortableBox';row['referenceTemplates']=[e['key']for e in domain[kind]]
            else:
                # Tuple boundaries retain nested Vector constructors. Literal
                # constraints plus requested property order identify fixed sites;
                # source-bounded variables bind the explicitly listed finite union.
                call=row['call'];body=call[call.index(',')+1:-1];pieces=[];start=0;depth=0
                for i,c in enumerate(body):
                    if c=='(':depth+=1
                    elif c==')':depth-=1
                    elif c==','and depth==0:pieces.append(body[start:i]);start=i+1
                pieces.append(body[start:]);properties={}
                for piece in pieces:
                    match=re.fullmatch(r'\(\s*"([a-z_]+)"\s*,(.*)\)',piece.strip(),re.S)
                    if not match:raise ValueError('Unparsed primitive request: '+row['id'])
                    properties[match[1]]=literal(match[2],root)
                eligible=[e['key']for e in domain[kind]if list(e['parameters'])==list(properties)and all(v is None or e['parameters'].get(k)==v for k,v in properties.items())]
                if not eligible:raise ValueError('No captured template for producer: '+row['id'])
                row['capturedTemplates']=eligible
        bound.append(row)
    return bound

def domain_sources(root,rows):
    # Bounds/loop changes outside the immediate Get expression must also force
    # review; otherwise an unchanged helper call could acquire a new parameter.
    files={row['file']for row in rows}
    stage=root/'src/Bomber.Game/Simulation/Stages'
    files.update(p.relative_to(root).as_posix()for p in stage.glob('BlastStages*.cs'))
    return {name:hashlib.sha256((root/name).read_bytes()).hexdigest()for name in sorted(files)}

def check(root=ROOT):
    path=root/INVENTORY
    if not path.exists():return ['Mesh producer inventory is missing']
    try:
        recorded=json.loads(path.read_text());actual=bind_templates(sites(root),domains(root),root)
        wanted=[{k:v for k,v in row.items()if k!='line'}for row in actual]
        saved=[{k:v for k,v in row.items()if k!='line'}for row in recorded['sites']]
        errors=[]
        if wanted!=saved:errors.append('Mesh-producing call sites changed: review and update tools/ci/mesh_producers.json before shipping')
        if recorded.get('domainSources',{})!=domain_sources(root,actual):errors.append('Mesh parameter source bounds/generators changed without inventory review')
        if 'assets/models/primitives/requests.json' in recorded.get('parameterDomainsSource','') and recorded['finiteTemplateDomains']!=domains(root):errors.append('Captured primitive parameter domain changed without inventory review')
        return errors
    except (OSError,ValueError,KeyError,TypeError,struct.error) as error:return ['Mesh producer inventory invalid: '+str(error)]

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--write',action='store_true');args=parser.parse_args()
    if args.write:
        data={'schemaVersion':1,'scope':'Source-call census and explicit supported parameter domains; not a graphics or runtime qualification receipt.',
              'parameterDomainsSource':'assets/models/primitives/requests.json',
              'policy':{'BoxMesh':'Continuous finite-positive Vector3 dimensions: portable C# packed-unit-box generator, checked against every Mac reference. Template entries are regression references, not a runtime size whitelist.',
                        'SphereMesh':'Eight fixed literal configurations: shared art/menu sphere, snow, scenery beads/starfield, explosion and burnout caches.',
                        'CylinderMesh':'Finite source-constant/loop domain: literal effect and menu cylinders, fixed stage dimensions for miniatures, and bounded scenery taper/cone parameters. Every exact supported request is listed below; runtime refuses others.',
                        'PlaneMesh':'Fixed world/particle planes plus seven-stage menu footprint variants; all exact parameter sets listed below.',
                        'TorusMesh':'Fixed exit, throw, tutorial, meteor and Greedy rings; three orbital iterations, three cabinet rails and two result arches; all exact parameter sets listed below.',
                        'QuadMesh':'One fixed unit explosion star.',
                        'owned-generator-or-helper':'Own C# SurfaceTool/ArrayMesh generators and their helper calls, including continuous dimensions; no native PrimitiveMesh regeneration.',
                        'canonical-bomber-library':'Existing 32-mesh Mac-authored library; editable generators remain authoring sources and exported consumers load pinned bytes.',
                        'template-storage-and-portable-box':'Hash-checked resource storage, guarded Mac authoring, or portable box generation; not a procedural engine fallback.',
                        'test-only':'Explicit harness meshes; not product geometry.'},
              'sites':bind_templates(sites(),domains()),'finiteTemplateDomains':domains(),'domainSources':domain_sources(ROOT,sites())}
        (ROOT/INVENTORY).write_text(json.dumps(data,indent=2)+'\n');print('Recorded',len(data['sites']),'mesh-producing/helper sites')
    else:
        errors=check();print('\n'.join(errors)if errors else'Mesh producer inventory and parameter domains match');raise SystemExit(bool(errors))
