#!/usr/bin/env python3
"""Fail closed unless every configured node serves the exact release and rejects obsolete admission."""
import json
import argparse
from pathlib import Path
import subprocess
import re
import tempfile
import urllib.error
import urllib.parse
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        raise RuntimeError('Deployment checks do not follow redirects')


def request(origin, path, token=None, body=None):
    headers = {'Cache-Control': 'no-cache'}
    if token is not None:
        headers['X-Blast-Protocol'] = token
    if body is not None:
        headers['Content-Type'] = 'application/json'
    req = urllib.request.Request(origin.rstrip('/') + path, headers=headers,
                                 data=None if body is None else json.dumps(body).encode())
    try:
        response = urllib.request.build_opener(NoRedirect).open(req, timeout=15)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        data = response.read(1024 * 1024 + 1)
        if len(data) > 1024 * 1024:
            raise RuntimeError('Deployment response exceeds size limit')
        return response.status, response.headers.get_all('X-Blast-Protocol'), json.loads(data)


def targets_from_json(value):
    targets = json.loads(value)
    if not isinstance(targets, dict) or not targets or any(not isinstance(k, str) or not k for k in targets):
        raise RuntimeError('deployment requires a nonempty JSON object of node names to origins')
    for origin in targets.values():
        if not isinstance(origin, str):
            raise RuntimeError('deployment origins must be URLs')
        url = urllib.parse.urlsplit(origin)
        if (url.scheme not in ('http', 'https') or not url.hostname or url.username or url.password or url.query or url.fragment
                or (url.scheme == 'http' and url.hostname not in ('localhost', '127.0.0.1', '::1'))):
            raise RuntimeError('deployment origins require HTTPS, or HTTP on loopback, without credentials')
    if len(set(targets.values())) != len(targets):
        raise RuntimeError('deployment targets must have distinct origins')
    return targets


def placements_from_json(value, targets):
    placements = json.loads(value)
    if not isinstance(placements, dict) or not placements or set(placements) != set(targets):
        raise RuntimeError('deployment needs explicit expected serverId/region placement for every target')
    for item in placements.values():
        if (not isinstance(item, dict) or set(item) != {'serverId', 'region'} or
                any(not isinstance(item[key], str) or not re.fullmatch(r'[A-Za-z0-9_.-]+', item[key]) for key in item)):
            raise RuntimeError('deployment placements need nonempty serverId and region identifiers')
    return placements


def verify(targets, release, revision):
    expected = dict(release, revision=revision)
    receipts = []
    for name, origin in targets.items():
        status, headers, body = request(origin, '/game')
        if status != 200 or headers != [release['admissionToken']] or body.get('release') != expected:
            raise RuntimeError('deployment release identity mismatch: ' + name)
        status, headers, games = request(origin, '/realtime/v1/games', release['admissionToken'])
        if status != 200 or headers != [release['admissionToken']] or not games.get('value'):
            raise RuntimeError('deployment realtime readiness failed: ' + name)
        # Same schema, different release: this catches gates which accidentally compare only revision.
        obsolete = f"{release['protocolRevision']}/0.0.0/1"
        for path, body in (('/realtime/v1/guests', {}), ('/realtime/v1/connect', None)):
            status, headers, error = request(origin, path, obsolete, body)
            if status != 409 or headers != [release['admissionToken']] or error.get('error', {}).get('code') != 'client_update_required':
                raise RuntimeError('deployment accepted obsolete admission: ' + name)
        # A valid client must actually pass admission, rather than every client receiving 409.
        status, headers, guest = request(origin, '/realtime/v1/guests', release['admissionToken'], {'displayName': 'Release readiness'})
        if status != 201 or headers != [release['admissionToken']] or not guest.get('playerId') or not guest.get('token'):
            raise RuntimeError('deployment rejected current client: ' + name)
        receipts.append({'node': name, 'revision': revision, 'admissionToken': release['admissionToken'], 'passed': True})
    return receipts


def private_failure_log(output):
    directory = Path(tempfile.mkdtemp(prefix='blast-release-probe-'))
    log = directory / 'failure.log'
    log.write_text(output)
    log.chmod(0o600)
    return str(log)


def build_probe():
    root = Path(__file__).resolve().parents[2]
    project = root / 'tests/Bomber.Release.Probe'
    build = subprocess.run(['dotnet', 'build', str(project), '--nologo', '-m:1'], cwd=root, capture_output=True, timeout=120)
    if build.returncode:
        log = private_failure_log((build.stdout + build.stderr).decode(errors='replace'))
        raise RuntimeError('Deployment connection probe build failed; .NET SDK 10 is required. Private log: ' + log)
    return root, project


def verify_connections(targets, placements=None):
    root, project = build_probe()
    for name, origin in targets.items():
        placement = placements.get(name) if placements else None
        arguments = ['dotnet', 'run', '--no-build', '--project', str(project), '--', origin]
        if placement:
            arguments.extend([placement['serverId'], placement['region']])
        try:
            probe = subprocess.run(arguments, cwd=root, capture_output=True, text=True, timeout=125)
        except (subprocess.TimeoutExpired, OSError) as error:
            log = private_failure_log(str(error))
            raise RuntimeError('Deployment connection probe timed out or could not start: ' + name + '. Private log: ' + log) from None
        if probe.returncode or not any(re.fullmatch(r'PASS [1-9][0-9]* release connection and cleanup checks', line) for line in probe.stdout.splitlines()):
            log = private_failure_log(probe.stdout + probe.stderr)
            raise RuntimeError('Deployment real lobby/WebSocket connection failed: ' + name + '. Private log: ' + log)


def main():
    from release_identity import check_exports
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--targets', required=True, help='JSON object of node names to origins')
    parser.add_argument('--expected-sha', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    release = check_exports()
    receipts = verify(targets_from_json(args.targets), release, args.expected_sha)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({'revision': args.expected_sha, 'release': release, 'nodes': receipts}, indent=2) + '\n')
    print(f'PASS {len(receipts)} deployed release identity/admission checks')


if __name__ == '__main__':
    main()
