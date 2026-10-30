"""Require actual archived-game update/relaunch and rollback, independently of helper fixtures."""
import math
import hashlib
import json
import zipfile
from desktop_rendering import report_matches


def inspect(receipt, target, revision, release, archive_sha):
    candidate, fixture = receipt.get('candidate', {}), receipt.get('fixture', {})
    if (receipt.get('schemaVersion') != 1 or receipt.get('passed') is not True or receipt.get('platform') != target or
            candidate != {'release': release, 'revision': revision} or fixture.get('revision') != revision or
            receipt.get('candidateArchiveSha256') != archive_sha or receipt.get('runtimeErrors') != []):
        raise RuntimeError('Actual desktop updater receipt differs from the candidate')
    future = fixture.get('release', {})
    if tuple(map(int, future.get('version', '0').split('.'))) <= tuple(map(int, release['version'].split('.'))):
        raise RuntimeError('Actual desktop updater fixture must be newer than the candidate')
    cases = receipt.get('cases', [])
    if [case.get('name') for case in cases] != ['successful-install', 'failed-launch-rollback', 'saved-high-fullscreen']:
        raise RuntimeError('Actual desktop updater is missing install/rollback transactions')
    for case in cases:
        result = ('failed ' + future['version'] + ' launch-rollback') if case['name'] == 'failed-launch-rollback' else ('installed ' + future['version'])
        seconds = case.get('seconds')
        if (case.get('result') != result or case.get('fromVersion') != release['version'] or
                case.get('toVersion') != future['version'] or case.get('oldGameplay') is not True or
                case.get('inventoryMatched') is not True or
                not isinstance(seconds, (int, float)) or not math.isfinite(seconds) or seconds <= 0 or
                target == 'windows-x64' and case.get('uninstallVerified') is not True):
            raise RuntimeError('Incomplete actual desktop updater transaction evidence')
    health = cases[0].get('health', {})
    if (health.get('version') != future['version'] or not report_matches(target,health,'driver') or
            health.get('method') != 'forward_plus' or not health.get('adapter') or cases[1].get('restoredGameplay') is not True):
        raise RuntimeError('Actual desktop updater is missing renderer/rollback gameplay evidence')
    high=cases[2].get('health',{})
    if (high.get('version')!=future['version'] or not report_matches(target,high,'driver') or high.get('driver')!=health.get('driver') or
            high.get('method')!='forward_plus' or not high.get('adapter') or high.get('graphics')!='high' or high.get('window')!='fullscreen'):
        raise RuntimeError('Actual desktop updater is missing saved High/fullscreen relaunch evidence')
    return receipt


def unpack(directory, target):
    import desktop_packages as desktop
    item = desktop.names(target)
    archive = directory / (item['root'] + '-update-fixture.zip')
    if archive.with_suffix('.zip.sha256').read_text().strip() != desktop.digest(archive) + '  ' + archive.name:
        raise RuntimeError('Update fixture bundle checksum mismatch')
    destination = directory / 'update-fixture'
    if destination.exists():
        raise RuntimeError('Use a fresh update-fixture destination')
    expected = {item['archive'], item['archive'] + '.sha256', item['metadata'], 'fixture.json'}
    with zipfile.ZipFile(archive) as bundle:
        if set(bundle.namelist()) != expected or len(bundle.infolist()) != len(expected):
            raise RuntimeError('Unsafe or incomplete update fixture bundle')
        for entry in bundle.infolist():
            if entry.file_size > 512 * 1024 * 1024 or (entry.external_attr >> 16) & 0xf000 == 0xa000:
                raise RuntimeError('Unsafe or oversized update fixture bundle')
        bundle.extractall(destination)
    baseline = json.loads((directory / item['metadata']).read_text())
    metadata = json.loads((destination / item['metadata']).read_text())
    fixture = json.loads((destination / 'fixture.json').read_text())
    if (fixture.get('testOnly') is not True or fixture.get('baselineRelease') != baseline['release'] or
            fixture.get('fixtureRelease') != metadata['release'] or metadata['revision'] != baseline['revision']):
        raise RuntimeError('Update fixture does not match this candidate source')
    desktop.inspect_archive(destination, target, metadata['revision'], metadata['release'])
    return destination
