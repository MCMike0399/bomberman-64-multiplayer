#!/usr/bin/env python3
"""Own a disposable X11 desktop and null audio sink for Linux functional rendering checks."""
import argparse
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inside-display', action='store_true')
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not command: parser.error('A test command is required')
    if args.inside_display:
        with tempfile.TemporaryFile() as log:
            manager = subprocess.Popen(['openbox', '--sm-disable'], stdout=log, stderr=subprocess.STDOUT)
            try:
                time.sleep(.5)
                if manager.poll() is not None: raise RuntimeError('Owned window manager exited')
                return subprocess.run(command, check=False).returncode
            finally:
                manager.terminate()
                try: manager.wait(timeout=5)
                except subprocess.TimeoutExpired: manager.kill(); manager.wait()
    with tempfile.TemporaryDirectory(prefix='blast-linux-desktop-') as temporary:
        folder = Path(temporary)
        socket = folder / 'pulse.sock'
        with (folder / 'pulse.log').open('w') as log:
            pulse = subprocess.Popen(['pulseaudio', '-n', '--daemonize=no', '--use-pid-file=no', '--exit-idle-time=-1',
                '--load=module-native-protocol-unix socket=' + str(socket) + ' auth-anonymous=1',
                '--load=module-null-sink sink_name=blast_qa'], stdout=log, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 10
                while not socket.exists():
                    if pulse.poll() is not None or time.monotonic() > deadline:
                        raise RuntimeError('Owned PulseAudio null sink did not start')
                    time.sleep(.1)
                environment = dict(os.environ, PULSE_SERVER='unix:' + str(socket))
                environment.pop('WAYLAND_DISPLAY', None)
                return subprocess.run(['xvfb-run', '-a', '-s', '-screen 0 1920x1200x24', sys.executable,
                    str(Path(__file__).resolve()), '--inside-display', '--', *command], env=environment, check=False).returncode
            finally:
                pulse.terminate()
                try: pulse.wait(timeout=5)
                except subprocess.TimeoutExpired: pulse.kill(); pulse.wait()


if __name__ == '__main__':
    raise SystemExit(main())
