"""Read-only host measurements; never activates apps, captures keys or reads clipboard text."""
import ctypes
import json
import os
from pathlib import Path
import re
import subprocess
import threading
import time


class HostObserver:
    def __init__(self, pid, output):
        self.pid, self.output = pid, Path(output)
        self.done = threading.Event()
        self.thread = threading.Thread(target=self.observe, daemon=True)
        self.result = {'samples': 0, 'foregroundChanges': 0, 'pointerChanges': 0,
                       'vmBecameForeground': False, 'peakLauncherRssMiB': 0, 'peakLauncherCpuPercent': 0,
                       'errors': []}

    def start(self):
        self.thread.start()

    def finish(self):
        self.done.set()
        self.thread.join(timeout=10)
        if self.thread.is_alive():
            self.result['errors'].append('Host observer did not finish')
        (self.output / 'host-observation.json').write_text(json.dumps(self.result, indent=2) + '\n')
        return self.result

    def observe(self):
        try:
            ctypes.CDLL('/System/Library/Frameworks/AppKit.framework/AppKit')
            objc = ctypes.CDLL('/usr/lib/libobjc.A.dylib')
            objc.objc_getClass.argtypes = [ctypes.c_char_p]
            objc.objc_getClass.restype = ctypes.c_void_p
            objc.sel_registerName.argtypes = [ctypes.c_char_p]
            objc.sel_registerName.restype = ctypes.c_void_p
            send = ctypes.CFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)(('objc_msgSend', objc))
            send_int = ctypes.CFUNCTYPE(ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p)(('objc_msgSend', objc))
            selector = lambda name: objc.sel_registerName(name.encode())
            workspace = send(objc.objc_getClass(b'NSWorkspace'), selector('sharedWorkspace'))
            cg = ctypes.CDLL('/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics')
            cf = ctypes.CDLL('/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation')
            class Point(ctypes.Structure):
                _fields_ = [('x', ctypes.c_double), ('y', ctypes.c_double)]
            cg.CGEventCreate.argtypes = [ctypes.c_void_p]
            cg.CGEventCreate.restype = ctypes.c_void_p
            cg.CGEventGetLocation.argtypes = [ctypes.c_void_p]
            cg.CGEventGetLocation.restype = Point
            cf.CFRelease.argtypes = [ctypes.c_void_p]
            previous = None
            started = time.monotonic()
            while not self.done.is_set():
                pool = send(objc.objc_getClass(b'NSAutoreleasePool'), selector('new'))
                try:
                    app = send(workspace, selector('frontmostApplication'))
                    foreground = send_int(app, selector('processIdentifier')) if app else None
                    event = cg.CGEventCreate(None)
                    if not event:
                        raise RuntimeError('Cannot observe host pointer')
                    point = cg.CGEventGetLocation(event)
                    cf.CFRelease(event)
                    current = (foreground, point.x, point.y)
                    self.result['samples'] += 1
                    self.result['vmBecameForeground'] |= foreground == self.pid
                    if previous:
                        self.result['foregroundChanges'] += current[0] != previous[0]
                        self.result['pointerChanges'] += current[1:] != previous[1:]
                    previous = current
                    usage = subprocess.run(['ps', '-o', 'rss=,%cpu=', '-p', str(self.pid)],
                                           text=True, capture_output=True, timeout=2)
                    if usage.returncode == 0 and usage.stdout.strip():
                        rss, cpu = map(float, usage.stdout.split())
                        self.result['peakLauncherRssMiB'] = max(self.result['peakLauncherRssMiB'], round(rss / 1024, 1))
                        self.result['peakLauncherCpuPercent'] = max(self.result['peakLauncherCpuPercent'], cpu)
                    if self.result['samples'] % 5 == 1:
                        pressure = subprocess.run(['memory_pressure', '-Q'], text=True,
                                                  capture_output=True, timeout=3)
                        match = re.search(r'System-wide memory free percentage:\s*(\d+)%', pressure.stdout)
                        if match:
                            free = int(match.group(1))
                            self.result.setdefault('initialHostFreeMemoryPercent', free)
                            self.result['minimumHostFreeMemoryPercent'] = min(
                                self.result.get('minimumHostFreeMemoryPercent', free), free)
                finally:
                    send(pool, selector('drain'))
                self.done.wait(1)
            self.result['seconds'] = round(time.monotonic() - started, 2)
            self.result['hostLoadAverage'] = os.getloadavg()
        except Exception as error:
            self.result['errors'].append(str(error))
