"""Freeze bootstrap inputs before EC2 allocation; no installation on the coordinator."""
import hashlib
import json
from pathlib import Path
import shutil
import urllib.request
import zipfile

import install_godot

SDK_VERSION='10.0.403'
DRIVER_BUCKET='ec2-windows-nvidia-drivers'
DRIVER_KEY='latest/596.86__grid_win10_win11_server2022_server2025_dch_64bit_international_aws_swl.exe'
PUBLIC=(
    {'role':'git','target':'windows-x64','fileName':'Git-2.56.0.4-64-bit.exe','url':'https://github.com/git-for-windows/git/releases/download/v2.56.0.windows.4/Git-2.56.0.4-64-bit.exe','algorithm':'sha256','hash':'4961a4a8cc778d8b6e00a5819e5fe4d078b754555aff7aedc2d77a5d74c2de80'},
    {'role':'pwsh','target':'windows-x64','fileName':'PowerShell-7.6.9-win-x64.msi','url':'https://github.com/PowerShell/PowerShell/releases/download/v7.6.9/PowerShell-7.6.9-win-x64.msi','algorithm':'sha256','hash':'50d33c7494aaf82b987cfd4de3f232d04f91fdd4c0deeca8a669cced3e7cf8f9'},
    {'role':'python','target':'windows-x64','fileName':'python-3.13.17-amd64.exe','url':'https://github.com/actions/python-versions/releases/download/3.13.17-39711892467/python-3.13.17-win32-x64.zip','algorithm':'sha256','hash':'06b07d3df1dd2634c2b08005e5a768bde4918ea0f36cd6bba21ae19136c9174e','member':'python-3.13.17-amd64.exe'},
    {'role':'audio','target':'windows-x64','fileName':'VBCABLE_Driver_Pack45.zip','url':'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip','algorithm':'sha256','hash':'b950e39f01af1d04ea623c8f6d8eb9b6ea5c477c637295fabf20631c85116bfb'},
)


def digest(path,algorithm='sha256'):
    with Path(path).open('rb')as stream:return hashlib.file_digest(stream,algorithm).hexdigest()


def fetch(spec,directory):
    directory=Path(directory);directory.mkdir(parents=True,exist_ok=True)
    if not spec['url'].startswith('https://'):raise ValueError('Bootstrap inputs require authenticated HTTPS origins')
    destination=directory/spec['fileName']
    if destination.exists():raise FileExistsError('Never replace a frozen tool input')
    downloaded=destination.with_suffix(destination.suffix+'.download')
    try:
        with urllib.request.urlopen(spec['url'],timeout=60)as response,downloaded.open('wb')as stream:
            shutil.copyfileobj(response,stream,1024*1024)
        if digest(downloaded,spec['algorithm'])!=spec['hash']:raise ValueError('Official tool archive digest differs: '+spec['fileName'])
        if spec.get('member'):
            with zipfile.ZipFile(downloaded)as archive:
                with archive.open(spec['member'])as source,destination.open('wb')as stream:shutil.copyfileobj(source,stream)
        else:downloaded.replace(destination)
    finally:downloaded.unlink(missing_ok=True)
    return {'role':spec['role'],'target':spec['target'],'fileName':spec['fileName'],'path':str(destination.resolve()),
            'sha256':digest(destination),'origin':spec['url'],'originHashAlgorithm':spec['algorithm'],'originHash':spec['hash']}


def sdk_specs(metadata):
    candidates=[sdk for release in metadata['releases']for sdk in [release.get('sdk',{}),*release.get('sdks',[])]if sdk.get('version')==SDK_VERSION]
    if not candidates:raise ValueError('Pinned SDK version is absent from Microsoft release metadata')
    files={item['rid']:item for sdk in candidates for item in sdk['files']if item['rid']in ('win-x64','linux-x64')
           and(item['url'].endswith('.zip')if item['rid']=='win-x64'else item['url'].endswith('.tar.gz'))}
    if set(files)!={'win-x64','linux-x64'}:raise ValueError('Both exact SDK archives required')
    return [{'role':'dotnet','target':'windows-x64'if rid=='win-x64'else'linux-x64','fileName':item['url'].rsplit('/',1)[-1],
             'url':item['url'],'algorithm':'sha512','hash':item['hash']}for rid,item in sorted(files.items())]


def pillow_specs(metadata):
    selected=[]
    for target,python,platform in [('windows-x64','cp313','win_amd64'),('linux-x64','cp312','manylinux')]:
        matches=[item for item in metadata['urls']if item['filename'].endswith('.whl')and
                 ('-'+python+'-'+python+'-')in item['filename']and platform in item['filename']and
                 ('x86_64'in item['filename']if target=='linux-x64'else True)]
        if len(matches)!=1:raise ValueError('One exact Pillow wheel required for '+target)
        item=matches[0];selected.append({'role':'pillow','target':target,'fileName':item['filename'],'url':item['url'],
                                       'algorithm':'sha256','hash':item['digests']['sha256']})
    return selected


def freeze(directory,aws):
    directory=Path(directory);directory.mkdir(parents=True,exist_ok=False);specs=list(PUBLIC)
    for target,asset in [('windows-x64','mono_win64.zip'),('linux-x64','mono_linux_x86_64.zip')]:
        filename=f'Godot_v{install_godot.VERSION}-stable_{asset}'
        specs.append({'role':'godot','target':target,'fileName':filename,'url':f'https://github.com/godotengine/godot/releases/download/{install_godot.VERSION}-stable/{filename}',
                      'algorithm':'sha512','hash':install_godot.HASHES[asset]})
    for label,url,reader in [('dotnet','https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json',sdk_specs),
                             ('pillow','https://pypi.org/pypi/Pillow/13.0.0/json',pillow_specs)]:
        with urllib.request.urlopen(url,timeout=60)as response:metadata=json.load(response)
        (directory/(label+'-origin.json')).write_text(json.dumps(metadata,indent=2)+'\n');specs.extend(reader(metadata))
    inputs=[fetch(spec,directory)for spec in specs]
    # Existing exact-key vendor read only. This AWS-owned reference is never a
    # campaign bucket or a deletion target; run staging uses ExpectedBucketOwner.
    driver=directory/'nvidia-grid.exe'
    aws('s3api','get-object','--bucket',DRIVER_BUCKET,'--key',DRIVER_KEY,str(driver))
    inputs.append({'role':'driver','target':'windows-x64','fileName':driver.name,'path':str(driver.resolve()),
                   'sha256':digest(driver),'origin':'s3://'+DRIVER_BUCKET+'/'+DRIVER_KEY,'protectedReference':True})
    # The official CLI bootstraps from the owned bucket on Windows; only the
    # coordinator reads this vendor URL. Authenticode is checked before execution.
    cli=directory/'AWSCLIV2.msi'
    with urllib.request.urlopen('https://awscli.amazonaws.com/AWSCLIV2.msi',timeout=60)as response,cli.open('wb')as stream:shutil.copyfileobj(response,stream,1024*1024)
    cli_info={'path':str(cli.resolve()),'sha256':digest(cli),'url':'https://awscli.amazonaws.com/AWSCLIV2.msi'}
    (directory/'frozen-tools.json').write_text(json.dumps({'inputs':inputs,'awsCli':cli_info},indent=2)+'\n')
    return inputs,cli_info
