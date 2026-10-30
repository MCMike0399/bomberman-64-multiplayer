"""Machine safeguards that replace the human approval click for paid GPU qualification.

A paid run is selected only when every guard passes:
- at most one automatic attempt per (release version, render fingerprint); a passing
  qualification for the fingerprint is never repeated and a failed or cancelled
  attempt is never retried automatically (an operator dispatch must name it as diagnosed);
- the most recent builder run's independent outcome report proves zero leftovers;
- every earlier paid allocation's cost is recorded in the reviewed ledger.
"""
import json
from pathlib import Path
import subprocess
import tempfile

REPOSITORY = 'MCMike0399/bomberman-64-multiplayer'
BASE = f'repos/{REPOSITORY}/actions/'
KEY_FILE = 'builder-qualification-key.json'
PLAN_ARTIFACT = 'builder-approval-plan'
BUILDER_ARTIFACTS = ('builder-intent-', 'builder-armed-', 'builder-stopped-', 'builder-state-',
                     'builder-evidence-', 'builder-rehearsal-', 'builder-outcome-')


def key(version, fingerprint):
    if not version or len(fingerprint or '') != 64:
        raise ValueError('Qualification key requires a release version and render fingerprint')
    return {'schemaVersion': 1, 'version': version, 'renderFingerprint': fingerprint}


def dedupe(attempts, wanted, automatic, diagnosed=''):
    """attempts: newest-first [{'id','status','conclusion','key'}] of earlier paid selections."""
    same = [run for run in attempts if run.get('key') == wanted]
    if any(run['status'] == 'completed' and run['conclusion'] == 'success' for run in same):
        return 'A passing qualification already exists for this version and render fingerprint'
    if any(run['status'] != 'completed' for run in same):
        return 'A paid qualification for this version and render fingerprint is still running'
    if same and automatic:
        return 'Automatic paid qualification already attempted for this version and render fingerprint; a failure needs diagnosis and an operator dispatch'
    if same and str(same[0]['id']) != str(diagnosed):
        return f"Previous attempt {same[0]['id']} ended {same[0]['conclusion']}; dispatch with diagnosed-failure-run={same[0]['id']} after diagnosis"
    return None


def outcome_refusal(runs, artifacts, read_outcome):
    """Fail closed unless the most recent builder run proves complete cleanup."""
    for run in runs:
        names = [item['name'] for item in artifacts(run['id']) if not item.get('expired')]
        if not any(name.startswith(BUILDER_ARTIFACTS) for name in names):
            continue
        if run['status'] != 'completed':
            return f"Builder run {run['id']} is still running; its outcome is not yet known"
        outcomes = sorted((name for name in names if name.startswith('builder-outcome-')),
                          key=lambda name: int(name.rsplit('-', 1)[1]))
        if not outcomes:
            return f"Builder run {run['id']} has no independent outcome report; run exact-ID cleanup/audit first"
        report = read_outcome(run['id'], outcomes[-1])
        if not (report.get('passed') is True and report.get('resourceCleanupVerified') is True and
                report.get('leftovers') == [] and report.get('errors') == [] and report.get('operatorCleanupRequired') is False):
            return f"Builder run {run['id']} outcome is not a verified zero-leftover cleanup; paid runs stay refused"
        return None
    return None


def spend_refusal(runs, ledger, current, artifacts):
    recorded = {str(item['workflowRunId']) for item in ledger.get('recordedRuns', [])}
    unknown = [str(run['id']) for run in runs if str(run['id']) != str(current) and str(run['id']) not in recorded and
               any(item['name'].startswith('builder-intent-') for item in artifacts(run['id']))]
    if unknown:
        return 'Earlier paid allocations lack a recorded cost receipt in docs/sancus-builder-spend.json: ' + ','.join(unknown)
    return None


def refusal(wanted, automatic, diagnosed, ledger, current, gh=None, download=None):
    gh = gh or (lambda path: json.loads(subprocess.check_output(['gh', 'api', path, '--paginate', '--slurp'], text=True, timeout=120)))
    download = download or _download
    runs = [run for page in gh(BASE + 'workflows/gpu-qualification.yml/runs?per_page=100') for run in page['workflow_runs']
            if str(run['id']) != str(current)]
    runs.sort(key=lambda run: run['created_at'], reverse=True)
    cache = {}
    def artifacts(identity):
        if identity not in cache:
            cache[identity] = [item for page in gh(BASE + f'runs/{identity}/artifacts?per_page=100') for item in page['artifacts']]
        return cache[identity]
    reason = spend_refusal(runs, ledger, current, artifacts) or outcome_refusal(
        runs, artifacts, lambda identity, name: download(identity, name, 'builder-outcome.json'))
    if reason:
        return reason
    attempts = []
    for run in runs:
        if any(item['name'] == PLAN_ARTIFACT and not item.get('expired') for item in artifacts(run['id'])):
            try:
                recorded = download(run['id'], PLAN_ARTIFACT, KEY_FILE)
            except FileNotFoundError:
                return f"Earlier paid selection {run['id']} has no qualification key; refuse until reviewed"
            attempts.append({'id': run['id'], 'status': run['status'], 'conclusion': run.get('conclusion'), 'key': recorded})
    return dedupe(attempts, wanted, automatic, diagnosed)


def _download(identity, artifact, filename):
    with tempfile.TemporaryDirectory() as directory:
        subprocess.check_call(['gh', 'run', 'download', str(identity), '--repo', REPOSITORY, '-n', artifact, '-D', directory], timeout=300)
        path = next(Path(directory).rglob(filename), None)
        if path is None:
            raise FileNotFoundError(f'{artifact} of run {identity} lacks {filename}')
        return json.loads(path.read_text())
