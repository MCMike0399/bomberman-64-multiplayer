"""Prepare a Mac-only reference payload from exact successful current-main artifacts."""
import argparse,hashlib,json,os,re,subprocess
from pathlib import Path
import builder_trigger
import download_formats

ROOT=Path(__file__).resolve().parents[2]
REPOSITORY=builder_trigger.REPOSITORY
ARTIFACT='Blast-Circuit-macos-arm64-ad-hoc'


def dispatch_allowed(event,ref,repository):
    return event=='workflow_dispatch' and ref=='refs/heads/main' and repository==REPOSITORY


def validate_run(run,checkout,current_main):
    if checkout!=current_main or not builder_trigger.eligible(run,checkout):
        raise ValueError('Mac reference requires successful exact current-main Verification')


def validate_payload(directory,source):
    data=json.loads((directory/'build-metadata.json').read_text())
    if data.get('revision')!=source:raise ValueError('Downloaded Mac artifact source differs from checkout')
    archive=directory/'Blast-Circuit-macos-arm64.zip'
    with archive.open('rb')as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
    if (directory/'Blast-Circuit-macos-arm64.zip.sha256').read_text().strip()!=digest+'  '+archive.name:
        raise ValueError('Downloaded Mac archive checksum differs')
    download=download_formats.inspect(directory,'macos-arm64',source,data['release'])
    return {'source':source,'target':'macos-arm64','archiveSha256':digest,'downloadSha256':download['sha256']}


def prepare_directory(directory):
    # artifacts/.gdignore is tracked, so a fresh checkout already has artifacts/.
    # Accept only that pristine state; any other existing content is refused.
    directory.mkdir(parents=True,exist_ok=True)
    if sorted(item.name for item in directory.iterdir())not in ([],['.gdignore']):
        raise ValueError('Mac reference directory must be empty apart from the tracked .gdignore')


def prepare(run_id,directory,checkout,*,event,ref,repository,gh,pin=None):
    if pin is None and not dispatch_allowed(event,ref,repository):raise ValueError('Mac reference dispatch is restricted to this repository on main')
    if not re.fullmatch(r'[0-9]+',str(run_id)):raise ValueError('Exact Verification run ID is required')
    run=json.loads(gh('api',f'repos/{REPOSITORY}/actions/runs/{run_id}'))
    if pin is None:
        current=json.loads(gh('api',f'repos/{REPOSITORY}/git/ref/heads/main'))['object']['sha'];validate_run(run,checkout,current)
    else:
        # Explicit pin: the payload source/run/artifact/archive digest are fixed in advance.
        if str(run['id'])!=str(run_id)or not builder_trigger.eligible(run,pin['source']):raise ValueError('Pinned Verification is not the successful main run for the pinned source')
        checkout=pin['source']
    pages=json.loads(gh('api',f'repos/{REPOSITORY}/actions/runs/{run_id}/artifacts?per_page=100','--paginate','--slurp'))
    artifacts=[entry for page in pages for entry in page['artifacts']if entry['name']==ARTIFACT]
    if len(artifacts)!=1 or artifacts[0].get('expired'):raise ValueError('Missing, expired or ambiguous trusted Mac artifact')
    if pin is not None and str(artifacts[0]['id'])!=str(pin['artifactId']):raise ValueError('Trusted Mac artifact ID differs from the pin')
    directory=Path(directory);prepare_directory(directory)
    gh('run','download',str(run_id),'--repo',REPOSITORY,'--name',ARTIFACT,'--dir',str(directory))
    report=validate_payload(directory,checkout)
    if pin is not None and report['archiveSha256']!=pin['archiveSha256']:raise ValueError('Trusted Mac archive digest differs from the pin')
    report.update(verificationRunId=str(run_id),artifactId=artifacts[0]['id'],
                  scope='Prepared exact-source Mac reference only; no capture, GPU or release qualification. Windows/Linux references come from the paid GPU campaign.')
    (directory/'mac-reference-prepared.json').write_text(json.dumps(report,indent=2)+'\n')
    return report


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--verification-run',required=True)
    parser.add_argument('--directory',type=Path,required=True)
    parser.add_argument('--pin-source');parser.add_argument('--pin-artifact-id');parser.add_argument('--pin-archive-sha256');args=parser.parse_args()
    pins=(args.pin_source,args.pin_artifact_id,args.pin_archive_sha256)
    if any(pins)and not all(pins):raise SystemExit('A pinned Mac reference needs source, artifact ID and archive digest')
    pin={'source':args.pin_source,'artifactId':args.pin_artifact_id,'archiveSha256':args.pin_archive_sha256}if all(pins)else None
    checkout=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    if subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=ROOT,text=True).strip():raise SystemExit('Mac reference checkout is dirty')
    def gh(*arguments):return subprocess.check_output(['gh',*arguments],cwd=ROOT,text=True,timeout=300)
    report=prepare(args.verification_run,args.directory,checkout,event=os.environ.get('GITHUB_EVENT_NAME'),
                   ref=os.environ.get('GITHUB_REF'),repository=os.environ.get('GITHUB_REPOSITORY'),gh=gh,pin=pin)
    with Path(os.environ['GITHUB_OUTPUT']).open('a')as output:output.write('source='+report['source']+'\n')
    print(json.dumps(report))
