#!/usr/bin/env python3
"""Allowlist QA receipts and rendered evidence for public Actions artifacts."""
import argparse
from pathlib import Path
import shutil


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path('artifacts/qa'))
    parser.add_argument('--destination', type=Path, default=Path('artifacts/ci-reports'))
    parser.add_argument('--include-runtime-logs',action='store_true',help='Retain only named game runtime.log files; arbitrary build/bootstrap logs remain excluded')
    args = parser.parse_args()
    source = args.source.resolve()
    destination = args.destination.resolve()
    if destination == source or destination.is_relative_to(source) or source.is_relative_to(destination):
        parser.error('Staging source and destination must not contain one another')
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    count = 0
    # Never upload arbitrary logs, build outputs, workspaces or the private checkout.
    for path in sorted(source.rglob('*')):
        if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(source):
            continue
        if (path.suffix.lower() not in ('.json', '.jsonl', '.png', '.mp4', '.wav') and path.name != 'junit.xml'
                and not(args.include_runtime_logs and path.name=='runtime.log')):
            continue
        relative = path.relative_to(source)
        if any(part.startswith('.') or part == 'burbuja-kernel' for part in relative.parts):
            continue
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        count += 1
    print(f'Staged {count} QA receipts/media files; source, binaries and arbitrary logs excluded.')


if __name__ == '__main__':
    main()
