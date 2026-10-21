#!/usr/bin/env python3
"""Fetch the pinned private build dependency without persisting credentials."""
import os
from pathlib import Path
import re
import shlex
import subprocess
import tempfile
import json
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
REVISION = 'e3c46ab795a8095d55b75790d76ea2786559be7f'


def failure_category(stderr):
    """Expose useful diagnostics without reflecting Git output or private origins."""
    message = stderr.decode(errors='replace').lower() if isinstance(stderr, bytes) else stderr.lower()
    categories = [
        (('invalid format', 'error in libcrypto'), 'SSH private-key format was rejected'),
        (('permission denied (publickey)',), 'SSH public-key authentication was rejected'),
        (('host key verification failed', 'host identification has changed', 'host key is known'), 'SSH host-key verification failed'),
        (('could not resolve hostname', 'could not resolve host'), 'GitHub DNS resolution failed'),
        (('connection refused', 'connection timed out', 'network is unreachable'), 'SSH network connection failed'),
        (('not our ref', "couldn't find remote ref", 'reference is not a tree'), 'Pinned revision is unavailable'),
        (('repository not found', 'could not read from remote repository'), 'Repository access was rejected'),
    ]
    for markers, label in categories:
        if any(marker in message for marker in markers):
            return label
    return 'Git operation failed without a recognized safe diagnostic'


def main():
    repository = os.environ.get('KERNEL_REPOSITORY', '')
    token = os.environ.get('KERNEL_READ_TOKEN', '')
    deploy_key = os.environ.get('KERNEL_DEPLOY_KEY', '')
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository) or not (token or deploy_key):
        raise SystemExit('Configure KERNEL_REPOSITORY (owner/name) and KERNEL_DEPLOY_KEY or KERNEL_READ_TOKEN secrets before trusted CI.')
    destination = ROOT / 'burbuja-kernel'
    if destination.exists():
        raise SystemExit('CI requires a clean workspace: burbuja-kernel already exists; refusing to overwrite it.')
    with tempfile.TemporaryDirectory(prefix='blast-git-auth-') as temporary:
        environment = dict(os.environ, GIT_TERMINAL_PROMPT='0', GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL=os.devnull)
        if deploy_key:
            key = Path(temporary) / 'deploy-key'
            key.write_text(deploy_key.rstrip() + '\n')
            key.chmod(0o600)
            # Obtain GitHub's published host keys through authenticated HTTPS,
            # rather than trusting unauthenticated ssh-keyscan output.
            request = urllib.request.Request('https://api.github.com/meta', headers={'User-Agent': 'blast-circuit-ci'})
            with urllib.request.urlopen(request, timeout=30) as response:
                host_keys = json.load(response)['ssh_keys']
            if not host_keys or any('\n' in item for item in host_keys):
                raise SystemExit('GitHub host-key metadata is invalid.')
            hosts = Path(temporary) / 'known_hosts'
            hosts.write_text(''.join('github.com ' + item + '\n' for item in host_keys))
            environment['GIT_SSH_COMMAND'] = shlex.join([
                'ssh', '-F', os.devnull, '-i', str(key), '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes',
                '-o', 'StrictHostKeyChecking=yes', '-o', 'UserKnownHostsFile=' + str(hosts),
            ])
            origin = 'git@github.com:' + repository + '.git'
        else:
            askpass = Path(temporary) / 'askpass.py'
            askpass.write_text('#!/usr/bin/env python3\nimport os, sys\nprint("x-access-token" if "Username" in sys.argv[1] else os.environ["KERNEL_READ_TOKEN"])\n')
            askpass.chmod(0o700)
            environment['GIT_ASKPASS'] = str(askpass)
            origin = 'https://github.com/' + repository + '.git'
        commands = [
            ['git', 'init', '--quiet', str(destination)],
            ['git', '-C', str(destination), 'fetch', '--quiet', '--depth=1',
             origin, REVISION],
            ['git', '-C', str(destination), 'checkout', '--quiet', '--detach', REVISION],
        ]
        for command in commands:
            try:
                result = subprocess.run(command, env=environment, capture_output=True, timeout=120)
                if (result.returncode and command[3:4] == ['fetch']
                        and failure_category(result.stderr) == 'Pinned revision is unavailable'):
                    # Some servers deny direct requests for old/unadvertised
                    # commit IDs. Fetch advertised history, then still require
                    # the exact pinned object: never substitute a branch tip.
                    advertised = ['git', '-C', str(destination), 'fetch', '--quiet', origin,
                                  '+refs/heads/*:refs/remotes/bootstrap/*', '+refs/tags/*:refs/tags/*']
                    result = subprocess.run(advertised, env=environment, capture_output=True, timeout=120)
            except subprocess.TimeoutExpired:
                raise SystemExit('Pinned kernel fetch timed out; verify network access to GitHub.') from None
            if result.returncode:
                # Git errors can contain the private origin. Never echo captured output.
                raise SystemExit('Pinned kernel fetch failed: ' + failure_category(result.stderr)
                                 + f' (exit {result.returncode}). Verify the scoped deploy key and pinned revision.')
    actual = subprocess.check_output(['git', '-C', str(destination), 'rev-parse', 'HEAD'], text=True).strip()
    if actual != REVISION:
        raise SystemExit('Kernel checkout revision mismatch.')
    (destination / '.gdignore').touch()
    print('Pinned kernel checkout ready; credentials were not stored in Git configuration.')


if __name__ == '__main__':
    main()
