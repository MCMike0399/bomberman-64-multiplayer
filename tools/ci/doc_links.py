#!/usr/bin/env python3
"""Find broken relative Markdown links and heading anchors in tracked documentation."""
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[2]
FENCE = re.compile(r'^\s*(```|~~~)')
INLINE = re.compile(r'!?\[(?:[^\[\]]|\[[^\]]*\])*\]\(\s*<?([^)\s>]+)>?(?:\s+"[^"]*")?\s*\)')
REFERENCE = re.compile(r'^\s{0,3}\[[^\]]+\]:\s*<?(\S+?)>?(?:\s+"[^"]*")?\s*$')
HTML = re.compile(r'<(?:img|video|source|a)\b[^>]*?\b(?:src|href)="([^"]+)"', re.I)
EXTERNAL = re.compile(r'^(?:[a-z][a-z0-9+.-]*:|//)', re.I)
# Ignored local outputs are referenced as examples and need not exist in a clean checkout.
OPTIONAL_PREFIXES = ('artifacts/', 'burbuja-kernel/', 'native/macos/')


def slug(heading):
    """GitHub-style heading anchor."""
    text = re.sub(r'!?\[([^\]]*)\]\([^)]*\)', r'\1', heading)
    text = re.sub(r'<[^>]+>', '', text).strip().lower()
    return ''.join(ch for ch in text if ch.isalnum() or ch in ' -_').replace(' ', '-')


def anchors(path, cache={}):
    if path not in cache:
        found, counts, fenced = set(), {}, False
        for line in path.read_text(errors='replace').splitlines():
            if FENCE.match(line):
                fenced = not fenced
            elif not fenced and (match := re.match(r'^#{1,6}\s+(.*?)\s*#*\s*$', line)):
                base = slug(match.group(1))
                count = counts.get(base, 0)
                counts[base] = count + 1
                found.add(base if count == 0 else f'{base}-{count}')
        found.update(re.findall(r'<a\s+(?:name|id)="([^"]+)"', path.read_text(errors='replace')))
        cache[path] = found
    return cache[path]


def targets(text):
    fenced = False
    for number, line in enumerate(text.splitlines(), 1):
        if FENCE.match(line):
            fenced = not fenced
            continue
        if fenced:
            continue
        code_free = re.sub(r'`[^`]*`', '', line)
        for pattern in (INLINE, HTML):
            for match in pattern.finditer(code_free):
                yield number, match.group(1)
        if match := REFERENCE.match(code_free):
            yield number, match.group(1)


def broken_links(root, files):
    failures = []
    for name in files:
        source = root / name
        for number, target in targets(source.read_text(errors='replace')):
            if EXTERNAL.match(target):
                continue
            location, _, anchor = target.partition('#')
            resolved = (source.parent / unquote(location)).resolve() if location else source.resolve()
            try:
                relative = resolved.relative_to(root.resolve()).as_posix()
            except ValueError:
                failures.append(f'{name}:{number}: link leaves the repository: {target}')
                continue
            if not resolved.exists():
                if not relative.startswith(OPTIONAL_PREFIXES):
                    failures.append(f'{name}:{number}: missing target {target}')
                continue
            if anchor and resolved.suffix == '.md' and unquote(anchor).lower() not in anchors(resolved):
                failures.append(f'{name}:{number}: missing anchor #{anchor} in {relative}')
    return failures


def tracked_markdown(root):
    names = subprocess.check_output(['git', 'ls-files', '-z', '*.md'], cwd=root).decode().split('\0')
    return [name for name in names if name and not name.startswith('burbuja-kernel/')]


def main():
    failures = broken_links(ROOT, tracked_markdown(ROOT))
    for failure in failures:
        print(failure)
    print(f'{len(failures)} broken documentation links.' if failures else 'Documentation links and anchors resolve.')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
