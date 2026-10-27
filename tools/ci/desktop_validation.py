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
GRAPHICAL = ('all', 'menu-preview', 'water-surface')
STARTUP = tuple('-'.join((p, d, 'menu')) for p in ('low', 'medium', 'high') for d in ('fullscreen', 'windowed')) + tuple(p + '-fullscreen-gameplay' for p in ('low', 'medium', 'high'))
MAC_ARTIFACT = 'Blast-Circuit-macos-arm64-regression'
MAC_RECEIPT = MAC_ARTIFACT + '.json'


def catalog_digest():
    return hashlib.sha256((ROOT / 'assets/testing/scenarios.json').read_bytes()).hexdigest()


def inspect_report(report, scenario, settings, revision, release, graphical):
    if (report.get('passed') is not True or report.get('platform') != settings['os'] or
            report.get('release') != dict(release, revision=revision) or
            report.get('scenario') != scenario or report.get('catalogSha256') != catalog_digest() or
            report.get('completedScenarios') != expanded(scenario, catalog()) or
            type(report.get('assertions')) is not int or report['assertions'] <= 0 or
            not isinstance(report.get('checks'), list) or len(report['checks']) != report['assertions'] or
            any(check.get('passed') is not True for check in report['checks']) or
            report.get('runtimeErrors') != [] or
            report.get('screenshotMode') != ('graphical' if graphical else 'disabled-headless')):
        raise RuntimeError('Incomplete or mismatched exported gameplay evidence: ' + scenario)
    if graphical and (report.get('renderingDriver') != settings['driver'] or
                       report.get('renderingMethod') != 'forward_plus' or
                       not report.get('adapter') or not report.get('screenshots')):
        raise RuntimeError('Missing intended renderer/adapter/captures: ' + scenario)


def inspect_receipt(receipt, settings, revision, release):
    if receipt.get('schemaVersion') != 2 or receipt.get('suite') not in ('headless-regression', 'graphical-regression'):
        raise RuntimeError('Full desktop regression receipt is required')
    graphical = receipt['suite'] == 'graphical-regression'
    cases = receipt.get('cases', [])
    expected = GRAPHICAL if graphical else HEADLESS
    if [case.get('scenario') for case in cases] != list(expected):
        raise RuntimeError('Desktop regression is missing required gameplay/multiplayer cases')
    for case in cases:
        inspect_report(case, case['scenario'], settings, revision, release, graphical)
    return receipt


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


def inspect_mac(receipt, revision, release, archive_sha, download_sha):
    cases = receipt.get('cases', [])
    if (receipt.get('schemaVersion') != 1 or receipt.get('passed') is not True or receipt.get('revision') != revision or
            receipt.get('release') != release or receipt.get('archiveSha256') != archive_sha or receipt.get('downloadSha256') != download_sha or
            [case.get('scenario') for case in cases] != list((*HEADLESS, *GRAPHICAL))):
        raise RuntimeError('Complete Mac archive regression is missing or mismatched')
    for case in cases:
        inspect_report(case, case['scenario'], {'os': 'macOS', 'driver': 'metal'}, revision, release, case['scenario'] in GRAPHICAL)
    return receipt
