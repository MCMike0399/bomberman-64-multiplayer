#!/usr/bin/env python3
"""Release-critical launch gates that deployment and publication require.

Trusted Verification runs both against the exact archived client on a real
Metal renderer: saved-settings startup for every preset/display mode, and an
actual signed update from the latest published release (including saved
High/fullscreen). Promotion downloads their receipts and re-validates them.
"""
import json
from pathlib import Path

STARTUP_ARTIFACT = 'release-gate-startup'
UPDATE_ARTIFACT = 'release-gate-update'
STARTUP_JOB = 'Release gate: saved-settings startup'
UPDATE_JOB = 'Release gate: update from published'
STARTUP_CASES = 9
UPDATE_CASES = ('successful-install', 'failed-launch-rollback', 'successful-install-high-fullscreen')


def version_tuple(value):
    return tuple(int(part) for part in str(value).split('.'))


def summarize(startup, update, version, build):
    """Validate both gate receipts for this exact client and return compact evidence."""
    startup = json.loads(Path(startup).read_text())
    update = json.loads(Path(update).read_text())
    app = startup.get('app', {})
    if (startup.get('schemaVersion') != 1 or startup.get('gate') != 'saved-settings-startup' or startup.get('passed') is not True
            or startup.get('legacyReport') is not False or app.get('version') != version or str(app.get('build')) != str(build)):
        raise RuntimeError('Saved-settings startup gate did not pass for this exact client')
    cases = startup.get('cases', [])
    if len(cases) != STARTUP_CASES or any(case.get('passed') is not True or 'graphics' not in case.get('report', {}) for case in cases):
        raise RuntimeError('Saved-settings startup gate is missing passing preset/display cases')
    candidate = update.get('candidate', {})
    if (update.get('schemaVersion') != 1 or update.get('gate') != 'update-from-published'
            or candidate.get('version') != version or str(candidate.get('build')) != str(build)):
        raise RuntimeError('Published-release update receipt does not describe this exact client')
    if update.get('baseline') == 'explicit':
        raise RuntimeError('Update gate must start from the latest published release, not an explicit baseline')
    # A skipped gate means the version was already live; such a run can never become a release.
    if update.get('passed') is not True or [case.get('name') for case in update.get('cases', [])] != list(UPDATE_CASES):
        raise RuntimeError('Actual update from the latest published release did not pass every transaction')
    failed = update.get('failedAttempts', [])
    if any(attempt.get('diagnostics', {}).get('crashReports') for attempt in failed):
        raise RuntimeError('The candidate crashed during an update attempt; a later pass does not clear a crash')
    previous = update.get('previous', {})
    if version_tuple(previous.get('version', '0')) >= version_tuple(version):
        raise RuntimeError('Update gate must start from an older published release')
    return {'startup': {'cases': [case['id'] for case in cases], 'adapter': cases[0]['report']['adapter'],
                        'host': startup.get('host', {}).get('model'), 'slowestSeconds': max(case['seconds'] for case in cases)},
            'update': {'from': previous['version'], 'fromSha256': previous.get('sha256'), 'cases': list(UPDATE_CASES),
                       'attempts': len(failed) + 1}}
