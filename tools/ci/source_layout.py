#!/usr/bin/env python3
"""Check the C# source layout rules in docs/agents/codebase-map.md.

* Product code (`src/`, `scripts/`) uses file-scoped namespaces that mirror folders:
  `src/Bomber.Game/Simulation/Stages` -> `Bomber.Game.Simulation.Stages`,
  `scripts/input_devices` -> `BlastCircuit.InputDevices`.
* A partial file whose class is declared in another file (`BlastSession/Bombs/BlastSession.Kicks.cs`,
  a playtest journey) keeps that main file's namespace: C# requires one namespace per class.
* A file is named after a type it declares, or is a partial of a class whose main file exists.
* `scripts/` folders are lowercase snake_case (Godot's PCK is case-sensitive); `src/` folders
  are PascalCase like their namespaces. `src/` never references Godot.
* A source folder holds at most MAX_FILES C# files; group by concern before it grows past that.
"""
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
MAX_FILES = 20
ACRONYMS = {'ui': 'UI'}
DECL = re.compile(r'^(?:public |internal |file )?(?:sealed |static |abstract |readonly |partial )*'
                  r'(?:record struct|record|class|struct|enum|interface) (\w+)', re.M)
PARTIAL = re.compile(r'^(?:public |internal )?(?:sealed |static |abstract )*partial class (\w+)', re.M)
NAMESPACE = re.compile(r'^namespace ([\w.]+);', re.M)
SNAKE = re.compile(r'^[a-z][a-z0-9_]*$')
PASCAL = re.compile(r'^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)*$')


def pascal(segment):
    return ACRONYMS.get(segment) or ''.join(part[:1].upper() + part[1:] for part in segment.split('_'))


def folder_namespace(path):
    parts = path.parts
    if parts[0] == 'src':
        return '.'.join(parts[1:-1])
    if parts[0] == 'scripts':
        return '.'.join(['BlastCircuit'] + [pascal(part) for part in parts[1:-1]])
    return None


def tracked_sources(root=ROOT):
    names = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard',
                                     '--', 'src', 'scripts', 'tests'], cwd=root).decode().split('\0')
    return sorted(Path(name) for name in names if name.endswith('.cs') and (root / name).exists())


def check(root=ROOT, files=None):
    files = tracked_sources(root) if files is None else files
    texts = {path: (root / path).read_text() for path in files}
    mains = {}
    for path, text in texts.items():
        if path.stem in DECL.findall(text):
            mains[path.stem] = path
    failures = []
    folders = {}
    for path, text in texts.items():
        folders.setdefault(path.parent, []).append(path)
        if path.parts[0] == 'tests':
            continue
        declared = DECL.findall(text)
        partials = PARTIAL.findall(text)
        owner = partials[0] if partials and path.stem not in declared else None
        if path.stem not in declared and not (owner and owner in mains) and path.name != 'Program.cs':
            failures.append(f'{path}: file name must match a declared type or be a partial of an existing class')
        match = NAMESPACE.search(text)
        if match is None:
            if not (path.name == 'Program.cs' and path.parts[0] == 'src'):
                failures.append(f'{path}: missing file-scoped namespace')
            continue
        expected = folder_namespace(path)
        if owner and owner in mains:
            main = NAMESPACE.search(texts[mains[owner]])
            expected = main.group(1) if main else expected
        if match.group(1) != expected:
            failures.append(f'{path}: namespace {match.group(1)} should be {expected}')
        if path.parts[0] == 'src' and re.search(r'^using Godot\b', text, re.M):
            failures.append(f'{path}: src/ assemblies must not depend on Godot')
    for folder, members in sorted(folders.items()):
        if len(members) > MAX_FILES:
            failures.append(f'{folder}: {len(members)} C# files exceed {MAX_FILES}; group them by concern')
        segments = folder.parts[1:]
        if folder.parts[0] == 'scripts' and any(not SNAKE.match(part) for part in segments):
            failures.append(f'{folder}: scripts/ folders must be lowercase snake_case')
        if folder.parts[0] == 'src' and any(not PASCAL.match(part) for part in segments):
            failures.append(f'{folder}: src/ folders must be PascalCase')
    return failures


def main():
    failures = check()
    for failure in failures:
        print(failure)
    if not failures:
        print('C# namespaces, file names and folder sizes follow the codebase map.')
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
