"""Read-only worker-pool observations for two checksum-pinned 4.7.2 templates only."""
import hashlib
from pathlib import Path
import struct

LAYOUTS = {
    'd776f83ae09d82cfa1295fb3d70ad7ab8a77fe66a4cc4f9d42244fae3665d8cc': {
        'platform': 'linux-x64', 'singleton': 0x4a43f48, 'indirections': 1,
        'count': 0x218, 'threads': 0x220, 'runlevel': 0x228, 'idle': 0x22c,
        'lowQueue': 0x1d0, 'queue': 0x1e0, 'stride': 104, 'flags': 24, 'current': 32, 'awaited': 40},
    '4a1015b8bce159aca903553f269356de1edcb2cbb51b19a416bb3cd5c42b2b56': {
        'platform': 'windows-x64', 'singleton': 0x5f011a0, 'indirections': 2,
        'count': 0x1d8, 'threads': 0x1e0, 'runlevel': 0x1e8, 'idle': 0x1ec,
        'lowQueue': 0x1b0, 'queue': 0x1c0, 'stride': 72, 'flags': 32, 'current': 40, 'awaited': 48},
}


def layout_for_image(path):
    digest = hashlib.sha256(Path(path).read_bytes()).hexdigest()
    if digest not in LAYOUTS: raise ValueError('Unknown template hash: refusing native field interpretation')
    return dict(LAYOUTS[digest], imageSha256=digest)


def snapshot(read, image_base, layout):
    def number(address, code):
        size = struct.calcsize(code); data = read(address, size)
        if len(data) != size: raise ValueError('Truncated native state read')
        return struct.unpack(code, data)[0]
    def pointer(address): return number(address, '<Q')
    pool = image_base + layout['singleton']
    for _ in range(layout['indirections']): pool = pointer(pool)
    if not 0 < pool < 2**47: raise ValueError('Invalid native pool pointer')
    count = number(pool + layout['count'], '<I')
    if not 0 < count <= 16: raise ValueError('Unexpected native thread count; no wider memory traversal')
    def task(address):
        if not address or address == 1: return {'pointer': hex(address)}
        try:
            description = pointer(address + 48); chars = []
            for index in range(32):
                if not description: break
                char = number(description + index * 4, '<I')
                if char == 0: break
                chars.append(chr(char) if char <= 0x10ffff else '\ufffd')
            return {'pointer': hex(address), 'id': pointer(address), 'nativeFunction': hex(pointer(address + 24)),
                    'descriptionPrefix': ''.join(chars)}
        except Exception as error: return {'pointer': hex(address), 'readError': str(error)}
    data = pointer(pool + layout['threads']); threads = []
    for index in range(count):
        thread = data + index * layout['stride']; flags = number(thread + layout['flags'], '<B')
        threads.append({'index': index, 'flags': flags, 'preExitedLanguages': bool(flags & 4),
                        'exitedLanguages': bool(flags & 8), 'current': task(pointer(thread + layout['current'])),
                        'awaited': task(pointer(thread + layout['awaited']))})
    queues = {}
    for name in ['lowQueue', 'queue']:
        head = pointer(pool + layout[name]); queues[name] = {'head': hex(head), 'firstTask': task(pointer(head + 8)) if head else None}
    return {'scope': 'Read-only fields of this exact template; no full hidden-state claim',
            'imageSha256': layout['imageSha256'], 'pool': hex(pool), 'runlevel': number(pool + layout['runlevel'], '<I'),
            'idleOrExitedCount': number(pool + layout['idle'], '<I'), 'threadCount': count, 'queues': queues, 'threads': threads}
