#!/usr/bin/env python3
"""Deploy verified CI artifacts through an operator-owned adapter; verify all nodes or roll back."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

from promote_release import ARTIFACT, SERVER_ARTIFACT, gh, inspect_package, inspect_run, inspect_server, require_current_main, inspect_existing_release
from release_identity import check_exports
from verify_deployment import targets_from_json, verify, verify_connections, build_probe


def adapter_call(adapter, phase, context):
    # A fixed executable/argv interface: never interpolate workflow inputs into a shell command.
    environment = {key: value for key, value in os.environ.items()
                   if key not in ('GH_TOKEN', 'GITHUB_TOKEN', 'BLAST_UPDATE_SIGNING_KEY')}
    result = subprocess.run([str(adapter), phase, str(context)], capture_output=True, timeout=600, env=environment)
    if result.returncode:
        raise RuntimeError('Deployment adapter failed during ' + phase + '; inspect private operator logs')
    return json.loads(result.stdout) if phase == 'drain' else None


def deploy(adapter, context, targets, release, revision):
    if not adapter.is_absolute() or not adapter.is_file() or not os.access(adapter, os.X_OK):
        raise RuntimeError('Deployment adapter must be an absolute executable outside the checkout')
    root = Path(__file__).resolve().parents[2]
    if adapter.resolve().is_relative_to(root):
        raise RuntimeError('Deployment adapter must be installed by the operator outside the checkout')
    drained = False
    changed = False
    try:
        drained = True  # A failed drain can leave a subset drained; resume must handle it.
        idle = adapter_call(adapter, 'drain', context)
        if (not isinstance(idle, dict) or set(idle) != set(targets) or
                any(not isinstance(value, dict) or value.get('drained') is not True or type(value.get('rooms')) is not int or value['rooms'] != 0 for value in idle.values())):
            raise RuntimeError('Deployment requires authenticated drained/zero-room evidence for every target')
        changed = True  # A failed apply may have changed only part of the fleet.
        adapter_call(adapter, 'apply', context)
        receipts = verify(targets, release, revision)
        adapter_call(adapter, 'resume', context)
        verify_connections(targets)
        for receipt in receipts:
            receipt['connectionVerified'] = True
        drained = False
        return receipts
    except Exception:
        if changed:
            # Adapter rollback must restore and verify the previous runtime/config/plugins on all targets.
            # If it fails, leave the fleet drained for operator recovery; never claim success.
            adapter_call(adapter, 'rollback', context)
        if drained:
            adapter_call(adapter, 'resume', context)
        raise


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--expected-sha', required=True)
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY', ''))
    parser.add_argument('--adapter', type=Path, required=True)
    parser.add_argument('--signing-key', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(argv)
    targets = targets_from_json(os.environ.get('BLAST_RELEASE_SERVER_URLS', '{}'))
    revision = inspect_run(args.repository, args.run_id)
    release = check_exports()
    if revision != args.expected_sha or release['version'] != args.version:
        raise RuntimeError('Deployment source/version mismatch')
    require_current_main(args.repository, revision)
    inspect_existing_release(args.repository, args.version, revision, publish=True)
    build_probe()  # Toolchain/configuration failure must happen before draining production.
    with tempfile.TemporaryDirectory(prefix='blast-deploy-') as temporary:
        directory = Path(temporary)
        for artifact in (ARTIFACT, SERVER_ARTIFACT):
            gh('run', 'download', args.run_id, '--repo', args.repository, '--name', artifact, '--dir', str(directory))
        archive = inspect_package(directory, revision, args.version)
        inspect_server(directory, revision, args.version)
        signer = Path(__file__).resolve().parents[1] / 'updates/sign_release.py'
        signed = subprocess.run([sys.executable, str(signer), '--archive', str(archive), '--key', str(args.signing_key),
                                 '--output', str(directory)], capture_output=True, timeout=60)
        if signed.returncode:
            raise RuntimeError('Release signing preflight failed; no servers have been drained or changed')
        inventory = json.loads((directory / 'server-build-metadata.json').read_text())['files']
        for plugin in ('Blast.Cluster', 'Realtime.Redis'):
            if f'plugins/{plugin}/plugin.json' not in inventory:
                raise RuntimeError('Deployment artifact is missing its fleet-tested plugin: ' + plugin)
        context = directory / 'deployment.json'
        context.write_text(json.dumps({'schemaVersion': 1, 'runId': args.run_id, 'revision': revision,
                                      'release': release, 'artifacts': str(directory), 'targets': targets}))
        require_current_main(args.repository, revision)
        receipts = deploy(args.adapter, context, targets, release, revision)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps({'revision': revision, 'release': release, 'nodes': receipts}, indent=2) + '\n')
    print('Verified deployment on every configured target; ready to publish')


if __name__ == '__main__':
    main()
