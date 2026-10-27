#!/usr/bin/env python3
"""Deploy verified CI artifacts through an operator-owned adapter; verify all nodes or roll back."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import re
import time

from deployment_state import Journal, locked, write_json, sync_directory, validate_adapter

from promote_release import ARTIFACT, SERVER_ARTIFACT, gh, inspect_gates, inspect_package, inspect_run, inspect_server, require_current_main, inspect_existing_release, stage_draft, publish_draft
from release_identity import check_exports
from verify_deployment import targets_from_json, verify, verify_connections, build_probe, placements_from_json


def adapter_call(adapter, phase, context):
    # A fixed executable/argv interface: never interpolate workflow inputs into a shell command.
    environment = {key: value for key, value in os.environ.items()
                   if key not in ('GH_TOKEN', 'GITHUB_TOKEN', 'BLAST_UPDATE_SIGNING_KEY')}
    result = subprocess.run([str(adapter), phase, str(context)], capture_output=True, timeout=600, env=environment)
    if result.returncode:
        raise RuntimeError('Deployment adapter failed during ' + phase + '; inspect private operator logs')
    return json.loads(result.stdout) if phase == 'drain' else None


def deploy(adapter, context, targets, release, revision, journal=None, placements=None):
    validate_adapter(adapter)
    def call(phase):
        return journal.call(phase, context) if journal else adapter_call(adapter, phase, context)

    drained = False
    changed = False
    try:
        drained = True  # A failed drain can leave a subset drained; resume must handle it.
        idle = call('drain')
        if (not isinstance(idle, dict) or set(idle) != set(targets) or
                any(not isinstance(value, dict) or value.get('drained') is not True or type(value.get('rooms')) is not int or value['rooms'] != 0 for value in idle.values())):
            raise RuntimeError('Deployment requires authenticated drained/zero-room evidence for every target')
        changed = True  # A failed apply may have changed only part of the fleet.
        call('apply')
        receipts = verify(targets, release, revision)
        call('resume')
        deferred = verify_connections(targets, placements) or []
        for receipt in receipts:
            receipt['connectionVerified'] = True
            receipt['roomPlacementDeferred'] = receipt.get('node') in deferred
        drained = False
        if journal:
            journal.record('transaction', 'succeeded')
        return receipts
    except Exception:
        if changed:
            # Adapter rollback must restore and verify the previous runtime/config/plugins on all targets.
            # If it fails, leave the fleet drained for operator recovery; never claim success.
            call('rollback')
        if drained:
            call('resume')
        if journal:
            journal.record('transaction', 'recovered')
        raise


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--expected-sha', required=True)
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY', ''))
    parser.add_argument('--adapter', type=Path, required=True)
    parser.add_argument('--signing-key', type=Path, required=True)
    parser.add_argument('--publish', action='store_true', help='Stage the signed draft before rollout, then publish after live verification in this transaction')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--state-dir', type=Path, required=True, help='Persistent private operator directory outside checkout')
    args = parser.parse_args(argv)
    if not re.fullmatch(r'[1-9][0-9]*', args.run_id):
        parser.error('Run ID must be a positive integer')
    targets = targets_from_json(os.environ.get('BLAST_RELEASE_SERVER_URLS', '{}'))
    placements = placements_from_json(os.environ.get('BLAST_RELEASE_PLACEMENTS', '{}'), targets)
    revision = inspect_run(args.repository, args.run_id)
    release = check_exports()
    if revision != args.expected_sha or release['version'] != args.version:
        raise RuntimeError('Deployment source/version mismatch')
    existing_draft = inspect_existing_release(args.repository, args.version, revision, publish=True)
    build_probe()  # Toolchain/configuration failure must happen before draining production.
    validate_adapter(args.adapter)
    with locked(args.state_dir):
        for pending in args.state_dir.glob('*/journal.json'):
            if Journal(pending).value['status'] not in ('succeeded', 'recovered'):
                raise RuntimeError('An interrupted deployment requires recovery before another rollout')
        directory = args.state_dir / args.run_id
        if directory.exists():
            saved = directory / 'journal.json'
            context = directory / 'deployment.json'
            if saved.is_file() and Journal(saved).value['status'] == 'succeeded':
                previous = json.loads(context.read_text())
                if any(previous.get(key) != value for key, value in
                       (('revision', revision), ('release', release), ('targets', targets), ('placements', placements))):
                    raise RuntimeError('Completed deployment context differs; do not reuse this run')
                # Retry an interrupted publication using inspected durable artifacts.
                # No host phases are repeated and main may have advanced meanwhile.
                if args.publish:
                    inspect_package(directory, revision, args.version)
                    inspect_server(directory, revision, args.version)
                    inspect_gates(args.repository, args.run_id, args.version)
                    notes = stage_draft(args.repository, args.run_id, args.version, revision, directory, existing_draft)
                receipts = verify(targets, release, revision)
                deferred = verify_connections(targets, placements) or []
                for receipt in receipts:
                    receipt['connectionVerified'] = True
                    receipt['roomPlacementDeferred'] = receipt.get('node') in deferred
                args.output.parent.mkdir(parents=True, exist_ok=True)
                write_json(args.output, {'revision': revision, 'release': release, 'nodes': receipts})
                if args.publish:
                    verify(targets, release, revision)
                    publish_draft(args.repository, args.version, notes)
                print('Previously deployed candidate verified; retry publication without redeployment')
                return
            # No host phase started, or recovery restored the old fleet. Preserve the
            # failed attempt privately and allow the same tested artifacts to retry.
            history = args.state_dir / 'history'
            history.mkdir(mode=0o700, exist_ok=True)
            sync_directory(args.state_dir)
            directory.rename(history / f'{args.run_id}-{time.time_ns()}')
            sync_directory(history)
            sync_directory(args.state_dir)
        require_current_main(args.repository, revision)
        directory.mkdir(mode=0o700)
        sync_directory(args.state_dir)
        for artifact in (ARTIFACT, SERVER_ARTIFACT):
            gh('run', 'download', args.run_id, '--repo', args.repository, '--name', artifact, '--dir', str(directory))
        archive = inspect_package(directory, revision, args.version)
        inspect_gates(args.repository, args.run_id, args.version)
        server = inspect_server(directory, revision, args.version)
        signer = Path(__file__).resolve().parents[1] / 'updates/sign_release.py'
        signed = subprocess.run([sys.executable, str(signer), '--archive', str(archive), '--key', str(args.signing_key),
                                 '--output', str(directory), '--server', str(server)], capture_output=True, timeout=60)
        if signed.returncode:
            raise RuntimeError('Release signing preflight failed; no servers have been drained or changed')
        inventory = json.loads((directory / 'server-build-metadata.json').read_text())['files']
        for plugin in ('Blast.Cluster', 'Realtime.Redis'):
            if f'plugins/{plugin}/plugin.json' not in inventory:
                raise RuntimeError('Deployment artifact is missing its fleet-tested plugin: ' + plugin)
        if args.publish:
            notes = stage_draft(args.repository, args.run_id, args.version, revision, directory, existing_draft)
        context = directory / 'deployment.json'
        write_json(context, {'schemaVersion': 1, 'runId': args.run_id, 'revision': revision,
                             'release': release, 'artifacts': str(directory), 'targets': targets, 'placements': placements})
        require_current_main(args.repository, revision)
        journal = Journal(directory / 'journal.json', args.adapter)
        journal.record('transaction', 'prepared')
        receipts = deploy(args.adapter, context, targets, release, revision, journal, placements)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps({'revision': revision, 'release': release, 'nodes': receipts}, indent=2) + '\n')
        if args.publish:
            # deploy() just proved placement/gameplay/retirement on every target.
            # Keep the fleet lock through publication; repeat cheap identities only.
            verify(targets, release, revision)
            publish_draft(args.repository, args.version, notes)
    print('Verified deployment on every configured target; ' + ('published signed update' if args.publish else 'ready to publish'))


if __name__ == '__main__':
    main()
