"""Exact packaged gameplay coverage; never equate headless execution with GPU parity."""
import hashlib
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/playtest'))
from contracts import catalog, expanded

HEADLESS = ('core-headless', 'peer-ice-order', 'peer-rooms', 'peer-capacity', 'adaptive-rooms', 'greedy-trap-network', 'adaptive-slow-poll')
GRAPHICAL = ('controller-popups', 'all', 'menu-preview', 'water-surface')
# Windows/Linux CI renders in software (WARP/llvmpipe), where one serial `all`
# process needs about 5 h (llvmpipe) to 20 h (WARP). The catalog composes `all`
# from contiguous tours; each tour runs in its own fresh process on its own
# runner. Together they must cover exactly the same leaves in the same order.
# Mac still runs `all` in one process, which covers cross-leaf sequencing.
TOURS = tuple(catalog()['all']['steps'])
SHARDED_GRAPHICAL = ('controller-popups', *TOURS, 'menu-preview', 'water-surface')
# One fresh runner per part. WARP is ~5x slower than llvmpipe, so Windows
# gives each tour its own runner while Linux groups about an hour of tours.
SOFTWARE_PARTS = {
    'windows-x64': ('windows-2025', (('controller-popups', TOURS[0]), *((tour,) for tour in TOURS[1:3]),
                                     (TOURS[3], 'water-surface'), *((tour,) for tour in TOURS[4:]), ('menu-preview',))),
    'linux-x64': ('ubuntu-24.04', (('controller-popups', *TOURS[:4]), TOURS[4:9], TOURS[9:14],
                                   (*TOURS[14:18], 'water-surface'), (*TOURS[18:], 'menu-preview'))),
}
# Peer host lease for the owned server during software-rendered graphical parts.
SOFTWARE_PEER_HOST_LEASE = 120
STARTUP = tuple('-'.join((p, d, 'menu')) for p in ('low', 'medium', 'high') for d in ('fullscreen', 'windowed')) + tuple(p + '-fullscreen-gameplay' for p in ('low', 'medium', 'high'))
MAC_ARTIFACT = 'Blast-Circuit-macos-arm64-regression'
MAC_RECEIPT = MAC_ARTIFACT + '.json'


def catalog_digest():
    return hashlib.sha256((ROOT / 'assets/testing/scenarios.json').read_bytes()).hexdigest()


def software_renderer(report):
    # Godot D3D12 reports WARP as IntegratedGpu on the hosted Windows image.
    # Match the observed renderer exactly; an arbitrary integrated GPU does
    # not qualify for the software-only time allowance.
    return (report.get('adapterType') == 'Cpu' or
            report.get('platform') == 'Windows' and report.get('renderingDriver') == 'd3d12' and
            report.get('adapter') == 'Microsoft Basic Render Driver')


def inspect_report(report, scenario, settings, revision, release, graphical):
    if (report.get('passed') is not True or report.get('platform') != settings['os'] or
            report.get('release') != dict(release, revision=revision) or
            report.get('scenario') != scenario or report.get('catalogSha256') != catalog_digest() or
            report.get('completedScenarios') != expanded(scenario, catalog()) or
            type(report.get('assertions')) is not int or report['assertions'] <= 0 or
            not isinstance(report.get('checks'), list) or len(report['checks']) != report['assertions'] or
            any(check.get('passed') is not True for check in report['checks']) or
            report.get('runtimeErrors') != [] or report.get('frameStallMs', 0) != 0 or
            report.get('screenshotMode') != ('graphical' if graphical else 'disabled-headless')):
        raise RuntimeError('Incomplete or mismatched exported gameplay evidence: ' + scenario)
    if graphical and (report.get('renderingDriver') != settings['driver'] or
                       report.get('renderingMethod') != 'forward_plus' or
                       not report.get('adapter') or not report.get('screenshots')):
        raise RuntimeError('Missing intended renderer/adapter/captures: ' + scenario)
    if graphical and 'display-settings' in expanded(scenario, catalog()):
        from display_restart import inspect as inspect_display_restart
        inspect_display_restart(report.get('displayRestart', {}), report)


def inspect_receipt(receipt, settings, revision, release):
    if receipt.get('schemaVersion') != 2 or receipt.get('suite') not in ('headless-regression', 'graphical-regression'):
        raise RuntimeError('Full desktop regression receipt is required')
    graphical = receipt['suite'] == 'graphical-regression'
    cases = receipt.get('cases', [])
    expected = SHARDED_GRAPHICAL if graphical else HEADLESS
    if [case.get('scenario') for case in cases] != list(expected):
        raise RuntimeError('Desktop regression is missing required gameplay/multiplayer cases')
    if graphical and [leaf for case in cases[1:-2] for leaf in case.get('completedScenarios', [])] != expanded('all', catalog()):
        raise RuntimeError('Desktop regression tours do not cover the complete all-suite in order')
    for case in cases:
        inspect_report(case, case['scenario'], settings, revision, release, graphical)
    if receipt.get('softwareRendererBudget') and (not graphical or any(not software_renderer(case) for case in cases)):
        raise RuntimeError('Software-renderer time allowance requires actual CPU-device or Windows WARP receipts')
    if (receipt.get('peerHostLeaseSeconds') or 6) not in (6, SOFTWARE_PEER_HOST_LEASE if receipt.get('softwareRendererBudget') else 6):
        raise RuntimeError('A lengthened peer host lease is allowed only for verified software-renderer receipts')
    return receipt


def software_plan(targets=None):
    """Actions matrix for the software-rendered graphical parts."""
    return [{'target': target, 'runner': runner, 'part': 'p%02d' % index, 'cases': ','.join(cases)}
            for target, (runner, parts) in SOFTWARE_PARTS.items() if targets is None or target in targets
            for index, cases in enumerate(parts, 1)]


def merge_parts(parts, settings, revision, release):
    """Join per-runner graphical parts only when every part tested identical bytes."""
    identity = ('target', 'revision', 'release', 'archiveSha256', 'serverSha256', 'downloadSha256', 'os', 'softwareRendererBudget', 'peerHostLeaseSeconds')
    if not parts:
        raise RuntimeError('No graphical regression parts to merge')
    cases = {}
    for part in parts:
        if (part.get('schemaVersion') != 2 or part.get('suite') != 'graphical-part' or part.get('passed') is not True or
                not isinstance(part.get('part'), str) or not part['part'] or
                any(part.get(key) != parts[0].get(key) for key in identity)):
            raise RuntimeError('Graphical regression parts are incomplete or tested different bytes')
        for case in part.get('cases', []):
            if case.get('scenario') in cases:
                raise RuntimeError('Graphical regression parts repeat a case: ' + str(case.get('scenario')))
            cases[case.get('scenario')] = case
    if set(cases) != set(SHARDED_GRAPHICAL):
        raise RuntimeError('Graphical regression parts are missing required cases')
    receipt = {key: parts[0].get(key) for key in identity}
    receipt.update(schemaVersion=2, passed=True, suite='graphical-regression',
                   cases=[cases[name] for name in SHARDED_GRAPHICAL], parts=sorted(part.get('part') for part in parts),
                   limits=parts[0].get('limits'))
    return inspect_receipt(receipt, settings, revision, release)


def inspect_startup(receipt, target, revision, release, archive_sha):
    cases = receipt.get('cases', [])
    if (receipt.get('schemaVersion') != 1 or receipt.get('passed') is not True or receipt.get('target') != target or
            receipt.get('revision') != revision or receipt.get('release') != release or receipt.get('archiveSha256') != archive_sha or
            [case.get('id') for case in cases] != list(STARTUP)):
        raise RuntimeError('Desktop startup matrix is missing or mismatched')
    for case in cases:
        seconds = case.get('seconds')
        if (case.get('passed') is not True or not case.get('adapter') or not isinstance(seconds, (int, float)) or
                not math.isfinite(seconds) or seconds <= 0 or case.get('kind') == 'gameplay' and case.get('snapshots', 0) <= 0):
            raise RuntimeError('Desktop startup case is incomplete')


def inspect_mac(receipt, revision, release, archive_sha, download_sha, server_sha):
    cases = receipt.get('cases', [])
    if (receipt.get('schemaVersion') != 1 or receipt.get('passed') is not True or receipt.get('revision') != revision or
            receipt.get('release') != release or receipt.get('archiveSha256') != archive_sha or receipt.get('downloadSha256') != download_sha or
            [case.get('scenario') for case in cases] != list((*HEADLESS, *GRAPHICAL))):
        raise RuntimeError('Complete Mac archive regression is missing or mismatched')
    if receipt.get('serverSha256') != server_sha:
        raise RuntimeError('Complete Mac archive regression used different server bytes')
    for case in cases:
        inspect_report(case, case['scenario'], {'os': 'macOS', 'driver': 'metal'}, revision, release, case['scenario'] in GRAPHICAL)
    return receipt


if __name__ == '__main__':
    print(json.dumps({'include': software_plan(sys.argv[1:] or None)}))
