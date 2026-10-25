#!/usr/bin/env python3
"""Read the canonical release identity and synchronize Godot's export version."""
import argparse
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def identity(root=ROOT):
    properties = ET.parse(root / 'Release.props').getroot().find('PropertyGroup')
    version = properties.findtext('BlastVersion', '')
    build = properties.findtext('BlastBuild', '')
    protocol = properties.findtext('BlastProtocolRevision', '')
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', version):
        raise ValueError('Release.props needs a numeric semantic version')
    if any(not re.fullmatch(r'[1-9][0-9]*', value) for value in (build, protocol)):
        raise ValueError('Release build and protocol revision must be positive integers')
    return {'version': version, 'build': build, 'protocolRevision': int(protocol),
            'admissionToken': f'{protocol}/{version}/{build}'}


def check_exports(root=ROOT, write=False):
    release = identity(root)
    path = root / 'export_presets.cfg'
    original = path.read_text()
    expected = original
    for key, value in (('short_version', release['version']), ('version', release['build'])):
        expected, count = re.subn(r'^application/' + key + r'="[^"]*"$', f'application/{key}="{value}"', expected, flags=re.MULTILINE)
        if count != 1:
            raise ValueError('Expected one Godot export identity: ' + key)
    if write:
        path.write_text(expected)
    elif original != expected:
        raise ValueError('Release identity differs from exports; run python3 tools/ci/release_identity.py --sync')
    return release


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sync', action='store_true')
    args = parser.parse_args()
    release = check_exports(write=args.sync)
    print(f"Release v{release['version']} build {release['build']}; online token {release['admissionToken']}")


if __name__ == '__main__':
    main()
