"""Player-facing downloads remain bound to the verified portable payload and signed inventory."""
import json
from pathlib import Path
import desktop_packages as desktop

FILES = {'windows-x64': 'Blast-Circuit-windows-x64-Setup.exe',
         'linux-x64': 'Blast-Circuit-linux-x64.AppImage',
         'macos-arm64': 'Blast-Circuit-macos-arm64.dmg'}


def names(target):
    name = FILES[target]
    return (name, name + '.sha256', 'Blast-Circuit-' + target + '-download.json')


def present(directory):
    return [target for target in FILES if any((directory / name).exists() for name in names(target))]


def record(directory, target, revision, release, source_archive):
    name, checksum, metadata = names(target)
    (directory / checksum).write_text(desktop.digest(directory / name) + '  ' + name + '\n')
    (directory / metadata).write_text(json.dumps({
        'schemaVersion': 1, 'target': target, 'revision': revision, 'release': release,
        'filename': name, 'sha256': desktop.digest(directory / name),
        'sourceArchiveSha256': desktop.digest(directory / source_archive),
    }, indent=2) + '\n')


def inspect(directory, target, revision, release):
    name, checksum, metadata = names(target)
    value = json.loads((directory / metadata).read_text())
    source = 'Blast-Circuit-' + target + '.zip'
    if ((directory / checksum).read_text().strip() != desktop.digest(directory / name) + '  ' + name or
            value != {'schemaVersion': 1, 'target': target, 'revision': revision, 'release': release,
                      'filename': name, 'sha256': desktop.digest(directory / name),
                      'sourceArchiveSha256': desktop.digest(directory / source)}):
        raise RuntimeError('Download format differs from verified source payload: ' + target)
    with (directory / name).open('rb') as stream:
        magic = stream.read(12)
    if target == 'windows-x64' and not magic.startswith(b'MZ'):
        raise RuntimeError('Windows download is not an executable')
    if target == 'linux-x64' and (not magic.startswith(b'\x7fELF') or magic[8:11] != b'AI\x02'):
        raise RuntimeError('Linux download is not a type-2 AppImage')
    return value
