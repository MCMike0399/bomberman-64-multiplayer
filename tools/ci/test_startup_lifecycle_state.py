"""Pinned native field decoding must preserve queue/counter evidence and fail closed."""
from pathlib import Path
import struct
import tempfile
import unittest

import startup_lifecycle_state as native


class NativePoolStateTests(unittest.TestCase):
    def fixture(self, digest):
        layout = dict(native.LAYOUTS[digest], imageSha256=digest)
        memory = {}; pool, threads, node, task, text = 0x10000, 0x20000, 0x30000, 0x40000, 0x50000
        def write(address, data): memory.update({address + i: b for i, b in enumerate(data)})
        def put(address, value, kind='<Q'): write(address, struct.pack(kind, value))
        write(pool, bytes(0x300)); write(threads, bytes(layout['stride'] * 4)); write(node, bytes(32)); write(task, bytes(64))
        base = 0 if layout['platform'] == 'linux-x64' else 0x140000000
        location = base + layout['singleton']
        if layout['indirections'] == 2: put(location, 0x8000); location = 0x8000
        put(location, pool); put(pool + layout['count'], 4, '<I'); put(pool + layout['threads'], threads)
        put(pool + layout['runlevel'], 1, '<I'); put(pool + layout['idle'], 3, '<I')
        put(pool + layout['lowQueue'], node); put(node + 8, task); put(task, 99); put(task + 24, 0x70000)
        put(task + 48, text); write(text, ('Shader cache\0').encode('utf-32le'))
        for i in range(3): put(threads + i * layout['stride'] + layout['flags'], 4, '<B')
        def read(address, size): return bytes(memory[address + i] for i in range(size))
        return layout, base, read, put

    def test_both_exact_layouts_retain_missing_idle_worker_and_queued_task(self):
        for digest in native.LAYOUTS:
            layout, base, read, _ = self.fixture(digest)
            with self.subTest(platform=layout['platform']):
                result = native.snapshot(read, base, layout)
                self.assertEqual((1, 3, 4), (result['runlevel'], result['idleOrExitedCount'], result['threadCount']))
                self.assertEqual([True, True, True, False], [x['preExitedLanguages'] for x in result['threads']])
                self.assertEqual('Shader cache', result['queues']['lowQueue']['firstTask']['descriptionPrefix'])
                self.assertEqual(99, result['queues']['lowQueue']['firstTask']['id'])
                self.assertIsNone(result['queues']['queue']['firstTask'])

    def test_unknown_template_never_authorizes_native_field_interpretation(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'unknown.exe'; path.write_bytes(b'unknown binary')
            with self.assertRaisesRegex(ValueError, 'Unknown template hash'): native.layout_for_image(path)

    def test_unexpected_count_and_truncated_reads_fail_instead_of_widening_walk(self):
        digest = next(iter(native.LAYOUTS)); layout, base, read, put = self.fixture(digest)
        put(0x10000 + layout['count'], 1000, '<I')
        with self.assertRaisesRegex(ValueError, 'thread count'): native.snapshot(read, base, layout)
        with self.assertRaisesRegex(ValueError, 'Truncated'): native.snapshot(lambda address, size: b'', base, layout)


if __name__ == '__main__': unittest.main()
