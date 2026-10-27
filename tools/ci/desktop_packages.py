"""Desktop artifact contracts shared by packaging, promotion and public verification."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import stat
import zipfile
import desktop_validation
import desktop_update_gates

TARGETS = {
    'windows-x64': {'preset': 'Windows DirectX 12', 'os': 'Windows', 'driver': 'd3d12',
                    'executable': 'BlastCircuit.exe', 'runtime': 'coreclr.dll',
                    'webrtc': 'libwebrtc_native.windows.template_release.x86_64.dll',
                    'runner': 'windows-2025'},
    'linux-x64': {'preset': 'Linux Vulkan', 'os': 'Linux', 'driver': 'vulkan',
                  'executable': 'BlastCircuit.x86_64', 'runtime': 'libcoreclr.so',
                  'webrtc': 'libwebrtc_native.linux.template_release.x86_64.so',
                  'runner': 'ubuntu-24.04'},
}


def names(target):
    stem = 'Blast-Circuit-' + target
    return {'artifact': stem, 'archive': stem + '.zip', 'metadata': stem + '-metadata.json',
            'smoke_artifact': stem + '-smoke', 'smoke': stem + '-smoke.json', 'root': stem,
            'updater_test': stem + '-updater-tests.json', 'graphical': stem + '-graphical.json',
            'startup': stem + '-startup.json'}


def asset_names(target):
    item = names(target)
    from download_formats import names as download_names
    return (item['archive'], item['archive'] + '.sha256', item['metadata'], item['smoke'], item['graphical'],
            item['startup'], item['updater_test'], *download_names(target))


def artifacts(targets=None):
    values = [names(target)[key] for target in (TARGETS if targets is None else targets) for key in ('artifact', 'smoke_artifact')]
    if targets is None:
        import crossplay
        values += [desktop_validation.MAC_ARTIFACT, crossplay.ARTIFACT]
    return values


def jobs():
    return ({f'Trusted {target} package' for target in TARGETS} | {f'Exported {target} smoke' for target in TARGETS} |
            {'Exported macos-arm64 regression', 'Cross-platform Quick Play / Three-platform parity'})


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def inspect_archive(directory, target, revision, release):
    """Validate without extracting; a forged inventory cannot introduce arbitrary paths."""
    item, settings = names(target), TARGETS[target]
    archive = directory / item['archive']
    checksum = (directory / (item['archive'] + '.sha256')).read_text().strip()
    if checksum != digest(archive) + '  ' + item['archive']:
        raise RuntimeError('Desktop archive checksum mismatch: ' + target)
    metadata = json.loads((directory / item['metadata']).read_text())
    if (metadata.get('schemaVersion') != 1 or metadata.get('target') != target or
            metadata.get('revision') != revision or metadata.get('release') != release or
            metadata.get('driver') != settings['driver'] or metadata.get('automaticUpdates') is not True):
        raise RuntimeError('Desktop package identity mismatch: ' + target)
    files = metadata.get('files', {})
    with zipfile.ZipFile(archive) as bundle:
        entries = bundle.infolist()
        if len(entries) != len({entry.filename for entry in entries}):
            raise RuntimeError('Duplicate desktop archive entry')
        seen = set()
        for entry in entries:
            path = PurePosixPath(entry.filename)
            if (entry.is_dir() or '\\' in entry.filename or path.is_absolute() or
                    '..' in path.parts or ':' in entry.filename or path.parts[0] != item['root'] or
                    stat.S_ISLNK(entry.external_attr >> 16)):
                raise RuntimeError('Unsafe desktop archive entry: ' + entry.filename)
            name = '/'.join(path.parts[1:])
            if not name or name not in files or hashlib.sha256(bundle.read(entry)).hexdigest() != files[name]:
                raise RuntimeError('Desktop file inventory mismatch: ' + name)
            seen.add(name)
        if seen != set(files):
            raise RuntimeError('Desktop file inventory is incomplete')
        basenames = {PurePosixPath(name).name for name in seen}
        required = {settings['executable'], settings['runtime'], settings['webrtc'],
                    'Bomberman64Multiplayer.dll', 'Bomberman64Multiplayer.runtimeconfig.json',
                    'BlastCircuit.pck', 'release.json', 'Blast-Circuit-MIT.txt', 'WebRTC-SOURCE.txt', 'DotNet-LICENSE.TXT'}
        if not required.issubset(basenames):
            raise RuntimeError('Desktop runtime is incomplete: ' + ', '.join(sorted(required - basenames)))
        identity = json.loads(bundle.read(item['root'] + '/release.json'))
        if identity != {'revision': revision, 'release': release, 'target': target}:
            raise RuntimeError('Archived desktop identity mismatch')
        config = next(name for name in seen if name.endswith('/Bomberman64Multiplayer.runtimeconfig.json') or name == 'Bomberman64Multiplayer.runtimeconfig.json')
        if not json.loads(bundle.read(item['root'] + '/' + config)).get('runtimeOptions', {}).get('includedFrameworks'):
            raise RuntimeError('Desktop export must include the .NET runtime')
        if target.startswith('linux-'):
            executable = bundle.getinfo(item['root'] + '/' + settings['executable'])
            if not executable.external_attr >> 16 & stat.S_IXUSR:
                raise RuntimeError('Linux executable permission is missing')
    return metadata


def inspect(directory, revision, release, targets=None):
    """Require a native-OS exported smoke, bound to these exact archive bytes."""
    for target in (TARGETS if targets is None else targets):
        settings = TARGETS[target]
        inspect_archive(directory, target, revision, release)
        item = names(target)
        smoke = json.loads((directory / item['smoke']).read_text())
        if (smoke.get('passed') is not True or smoke.get('target') != target or
                smoke.get('revision') != revision or smoke.get('release') != release or
                smoke.get('archiveSha256') != digest(directory / item['archive']) or
                smoke.get('os') != settings['os']):
            raise RuntimeError('Desktop exported smoke is missing or mismatched: ' + target)
        desktop_validation.inspect_receipt(smoke, settings, revision, release)
        graphical = json.loads((directory / item['graphical']).read_text())
        if (graphical.get('passed') is not True or graphical.get('target') != target or
                graphical.get('revision') != revision or graphical.get('release') != release or
                graphical.get('archiveSha256') != digest(directory / item['archive']) or
                graphical.get('suite') != 'graphical-regression' or smoke.get('suite') != 'headless-regression'):
            raise RuntimeError('Full graphical desktop regression is missing or mismatched')
        desktop_validation.inspect_receipt(graphical, settings, revision, release)
        startup = json.loads((directory / item['startup']).read_text())
        desktop_validation.inspect_startup(startup, target, revision, release, digest(directory / item['archive']))
        desktop_update_gates.inspect(json.loads((directory / item['updater_test']).read_text()), target,
                                     revision, release, digest(directory / item['archive']))
    if targets is None:
        import crossplay
        desktop_validation.inspect_mac(json.loads((directory / desktop_validation.MAC_RECEIPT).read_text()), revision, release,
            digest(directory / 'Blast-Circuit-macos-arm64.zip'), digest(directory / 'Blast-Circuit-macos-arm64.dmg'))
        crossplay.inspect_summary(json.loads((directory / crossplay.RECEIPT).read_text()), revision, release, directory)


def download(repository, run_id, directory, gh):
    for artifact in artifacts():
        gh('run', 'download', str(run_id), '--repo', repository, '--name', artifact, '--dir', str(directory))


def present(directory):
    """Historical Mac-only releases remain verifiable; partial desktop releases fail."""
    return bool(present_targets(directory))


def present_targets(directory):
    return [target for target in TARGETS if any((directory / name).exists() for name in asset_names(target))]
