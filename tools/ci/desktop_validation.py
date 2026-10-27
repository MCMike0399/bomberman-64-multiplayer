"""Exact packaged gameplay coverage; never equate headless execution with GPU parity."""
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/playtest'))
from contracts import catalog, expanded

HEADLESS = ('core-headless', 'peer-rooms', 'adaptive-rooms', 'greedy-trap-network', 'adaptive-slow-poll')
GRAPHICAL = ('all', 'menu-preview', 'water-surface')


def catalog_digest():
    return hashlib.sha256((ROOT / 'assets/testing/scenarios.json').read_bytes()).hexdigest()


def inspect_report(report, scenario, settings, revision, release, graphical):
    if (report.get('passed') is not True or report.get('platform') != settings['os'] or
            report.get('release') != dict(release, revision=revision) or
            report.get('scenario') != scenario or report.get('catalogSha256') != catalog_digest() or
            report.get('completedScenarios') != expanded(scenario, catalog()) or
            type(report.get('assertions')) is not int or report['assertions'] <= 0 or
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
