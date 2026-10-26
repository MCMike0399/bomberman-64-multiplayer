#!/usr/bin/env python3
"""One release entrypoint: prepare identity, diagnose setup, or dispatch verified delivery."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import time

from promote_release import gh, inspect_run, require_current_main
from release_identity import ROOT, check_exports


def prepare(version, protocol=None, root=ROOT):
    previous = check_exports(root)
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', version):
        raise ValueError('Use a numeric semantic version, for example 0.31.0')
    if tuple(map(int, version.split('.'))) <= tuple(map(int, previous['version'].split('.'))):
        raise ValueError('New version must be greater than the current version')
    protocol = previous['protocolRevision'] if protocol is None else protocol
    if protocol < previous['protocolRevision']:
        raise ValueError('Protocol revision cannot decrease')
    path = root / 'Release.props'
    original = path.read_text()
    updated = original
    for name, value in (('BlastVersion', version), ('BlastBuild', int(previous['build']) + 1),
                        ('BlastProtocolRevision', protocol)):
        updated, count = re.subn(r'<' + name + r'>[^<]+</' + name + r'>',
                                f'<{name}>{value}</{name}>', updated)
        if count != 1:
            raise ValueError('Expected one release property: ' + name)
    path.write_text(updated)
    try:
        return check_exports(root, write=True)
    except Exception:
        path.write_text(original)
        raise


def doctor(repository, publishing=False):
    """Report names/verdicts only; never echo credentials or private node origins."""
    base = f'repos/{repository}'
    variables = {item['name']: item['value'] for page in json.loads(gh('api', base + '/actions/variables?per_page=100', '--paginate', '--slurp')) for item in page['variables']}
    secrets = {item['name'] for page in json.loads(gh('api', base + '/actions/secrets?per_page=100', '--paginate', '--slurp')) for item in page['secrets']}
    environments = json.loads(gh('api', base + '/environments?per_page=100'))['environments']
    production = next((item for item in environments if item['name'] == 'production'), None)
    if production:
        secrets.update(item['name'] for page in json.loads(gh('api', base + '/environments/production/secrets?per_page=100', '--paginate', '--slurp')) for item in page['secrets'])
        variables.update({item['name']: item['value'] for page in json.loads(gh('api', base + '/environments/production/variables?per_page=100', '--paginate', '--slurp')) for item in page['variables']})
    checks = {'canonical release/export identity': bool(check_exports()),
              'trusted builds enabled': variables.get('KERNEL_CI_ENABLED') == 'true',
              'private dependency credential configured': 'KERNEL_DEPLOY_KEY' in secrets or 'KERNEL_READ_TOKEN' in secrets,
              'private dependency repository configured': 'KERNEL_REPOSITORY' in secrets,
              'update signing secret configured': 'BLAST_UPDATE_SIGNING_KEY' in secrets,
              'production environment exists': production is not None}
    if publishing:
        runners = [item for page in json.loads(gh('api', base + '/actions/runners?per_page=100', '--paginate', '--slurp')) for item in page['runners']]
        online = any(item['status'] == 'online' and 'blast-deploy' in {label['name'] for label in item['labels']} for item in runners)
        try:
            age = time.time() - int(variables.get('BLAST_DEPLOY_AUTOSCALER_HEARTBEAT', '0'))
            autoscaler = -5 <= age <= 90
        except ValueError:
            autoscaler = False
        checks['deployment runner online or autoscaler healthy'] = online or autoscaler
        checks['absolute operator adapter configured'] = Path(variables.get('BLAST_DEPLOY_ADAPTER', '')).is_absolute()
        checks['persistent deployment state configured'] = Path(variables.get('BLAST_DEPLOY_STATE_DIR', '')).is_absolute()
        checks['deployment target secret configured'] = 'BLAST_RELEASE_SERVER_URLS' in secrets
        checks['worker placement secret configured'] = 'BLAST_RELEASE_PLACEMENTS' in secrets
    return checks


def dispatch(repository, run_id, publishing, retry_deployed=False):
    checks = doctor(repository, publishing)
    if not all(checks.values()):
        raise RuntimeError('Release prerequisites missing: ' + ', '.join(name for name, passed in checks.items() if not passed))
    revision = inspect_run(repository, run_id)
    if not retry_deployed:
        require_current_main(repository, revision)
    local = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    if local != revision or subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=normal'], cwd=ROOT, text=True).strip():
        raise RuntimeError('Dispatch from a clean checkout of the exact verified commit')
    release = check_exports()
    gh('workflow', 'run', 'promote.yml', '--repo', repository, '--ref', 'main',
       '-f', 'run_id=' + run_id, '-f', 'version=' + release['version'], '-f', 'publish=' + str(publishing).lower())
    print(f"Requested {'deployment/publication' if publishing else 'signed draft'} of v{release['version']} from Verification {run_id}.")
    print(f'https://github.com/{repository}/actions/workflows/promote.yml')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY', 'MCMike0399/bomberman-64-multiplayer'))
    commands = parser.add_subparsers(dest='command', required=True)
    bump = commands.add_parser('prepare', help='Update version/build and synchronize exports; does not commit or release')
    bump.add_argument('version')
    bump.add_argument('--protocol', type=int)
    diagnose = commands.add_parser('doctor', help='Read-only GitHub prerequisite audit; does not change settings')
    diagnose.add_argument('--publish', action='store_true', help='Also audit deployment prerequisites')
    deliver = commands.add_parser('dispatch', help='Dispatch the existing protected release workflow')
    deliver.add_argument('--run-id', required=True)
    deliver.add_argument('--publish', action='store_true', help='Explicitly deploy and publish; default creates a signed draft')
    deliver.add_argument('--retry-deployed', action='store_true', help='Retry a completed rollout after main advanced; workflow requires its durable success journal')
    args = parser.parse_args(argv)
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', args.repository):
        parser.error('Repository must be owner/name')
    if args.command == 'prepare':
        print(json.dumps(prepare(args.version, args.protocol), indent=2))
    elif args.command == 'doctor':
        checks = doctor(args.repository, args.publish)
        for name, passed in checks.items():
            print(('PASS ' if passed else 'MISSING ') + name)
        print('Configuration presence only; the workflow verifies key matching, host adapter and live targets. Runner isolation/protection rules need an operator audit.')
        return 0 if all(checks.values()) else 1
    else:
        if not re.fullmatch(r'[1-9][0-9]*', args.run_id):
            parser.error('Run ID must be a positive integer')
        if args.retry_deployed and not args.publish:
            parser.error('--retry-deployed requires --publish')
        dispatch(args.repository, args.run_id, args.publish, args.retry_deployed)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
