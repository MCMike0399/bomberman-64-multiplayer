#!/usr/bin/env python3
"""Skip trusted builds only for a proven documentation-only main push."""
import argparse
import os
from pathlib import Path, PurePosixPath
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def runtime_required(base, root=ROOT):
    if not re.fullmatch(r'[0-9a-f]{40}', base) or base == '0' * 40:
        return True
    try:
        subprocess.run(['git', 'merge-base', '--is-ancestor', base, 'HEAD'], cwd=root,
                       check=True, capture_output=True, timeout=10)
        changed = subprocess.check_output(['git', 'diff', '--no-renames', '--name-only', '-z', base, 'HEAD'],
                                          cwd=root, timeout=10).decode().split('\0')
    except (subprocess.SubprocessError, OSError, UnicodeError):
        return True
    paths = [PurePosixPath(name) for name in changed if name]
    return not paths or any(not (str(path) == 'README.md' or
                                path.parts[0] == 'docs' and path.suffix == '.md') for path in paths)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', default='')
    args = parser.parse_args()
    # Dispatch is the explicit way to request every lane, regardless of the diff.
    required = os.environ.get('GITHUB_EVENT_NAME') != 'push' or runtime_required(args.base)
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'], 'a') as stream:
            stream.write('runtime=' + str(required).lower() + '\n')
    print('Trusted runtime verification: ' + ('required' if required else 'skipped (documentation-only push; public contracts still run)'))


if __name__ == '__main__':
    main()
