#!/usr/bin/env python3
"""Private, durable deployment state and explicit recovery after runner interruption."""
import argparse
from contextlib import contextmanager
import fcntl
import json
import os
from pathlib import Path
import re
import tempfile


def sync_directory(path):
    directory = os.open(path, os.O_RDONLY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def validate_adapter(adapter):
    root = Path(__file__).resolve().parents[2]
    if (not adapter.is_absolute() or not adapter.is_file() or not os.access(adapter, os.X_OK)
            or adapter.resolve().is_relative_to(root)):
        raise RuntimeError('Deployment adapter must be an operator-installed absolute executable outside the checkout')


def write_json(path, value):
    descriptor, temporary = tempfile.mkstemp(prefix=path.name + '.', dir=path.parent)
    try:
        with os.fdopen(descriptor, 'w') as stream:
            json.dump(value, stream, indent=2)
            stream.write('\n')
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        sync_directory(path.parent)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


@contextmanager
def locked(root):
    checkout = Path(__file__).resolve().parents[2]
    if not root.is_absolute() or root.resolve().is_relative_to(checkout):
        raise RuntimeError('Deployment state must be an absolute persistent directory outside the checkout')
    root.mkdir(mode=0o700, exist_ok=True)
    sync_directory(root.parent)
    if root.is_symlink() or root.stat().st_uid != os.getuid() or root.stat().st_mode & 0o077:
        raise RuntimeError('Deployment state must be owned by this user with mode 0700')
    with (root / 'deployment.lock').open('a') as lock:
        os.chmod(lock.name, 0o600)
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise RuntimeError('Another deployment or recovery owns this fleet') from error
        yield


class Journal:
    def __init__(self, path, adapter=None):
        self.path = path
        self.value = json.loads(path.read_text()) if path.exists() else {
            'schemaVersion': 1, 'adapter': str(adapter), 'status': 'prepared', 'events': []}

    def record(self, phase, status):
        self.value['events'].append({'phase': phase, 'status': status})
        self.value['status'] = status if phase == 'transaction' else 'incomplete'
        write_json(self.path, self.value)

    def call(self, phase, context):
        from deploy_release import adapter_call
        self.record(phase, 'started')
        result = adapter_call(Path(self.value['adapter']), phase, context)
        self.record(phase, 'completed')
        return result


def recover(root, run_id):
    with locked(root):
        directory = root / run_id
        journal = Journal(directory / 'journal.json')
        if journal.value['status'] in ('succeeded', 'recovered'):
            raise RuntimeError('Transaction is already complete; recovery would undo a completed release')
        context = directory / 'deployment.json'
        if not context.is_file() or journal.value['adapter'] == 'None':
            raise RuntimeError('Missing durable deployment context/adapter; inspect operator state')
        changed = any(item['phase'] == 'apply' for item in journal.value['events'])
        drained = any(item['phase'] == 'drain' for item in journal.value['events'])
        if changed or drained:
            validate_adapter(Path(journal.value['adapter']))
        if changed:
            # rollback must fence partially resumed admission before restoring old runtimes.
            journal.call('rollback', context)
        if drained or changed:
            journal.call('resume', context)
        journal.record('transaction', 'recovered')
    print('Recovered previous fleet state; this does not publish a client release.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--state-dir', type=Path, required=True)
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--recover', action='store_true', help='Restore prior fleet using the saved operator adapter and context')
    args = parser.parse_args()
    if not re.fullmatch(r'[1-9][0-9]*', args.run_id):
        parser.error('Run ID must be a positive integer')
    if args.recover:
        recover(args.state_dir, args.run_id)
    else:
        journal = Journal(args.state_dir / args.run_id / 'journal.json')
        print(json.dumps({'status': journal.value['status'], 'events': journal.value['events']}, indent=2))


if __name__ == '__main__':
    main()
