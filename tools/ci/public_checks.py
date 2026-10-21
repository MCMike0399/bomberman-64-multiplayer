#!/usr/bin/env python3
"""Dependency-free public-repository checks, safe to execute on fork PRs."""
import ast
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def main():
    tracked = subprocess.check_output(['git', 'ls-files', '-z'], cwd=ROOT).decode().split('\0')
    failures = []
    for name in filter(None, tracked):
        path = Path(name)
        if name.startswith('burbuja-kernel/') or path.suffix in ('.pem', '.key', '.p12', '.pfx'):
            failures.append(f'Forbidden tracked private material: {name}')
        if path.suffix == '.py':
            ast.parse((ROOT / path).read_text(), filename=name)
    # Include newly created tooling when running locally before the first commit.
    for path in (ROOT / 'tools').rglob('*.py'):
        ast.parse(path.read_text(), filename=str(path.relative_to(ROOT)))
    for failure in failures:
        print(failure)
    if failures:
        return 1
    print('Python syntax and tracked private-material boundary checks passed.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
