#!/usr/bin/env python3
"""Exercise native controller startup and reject macOS-incompatible imports."""
import ctypes
from pathlib import Path
import re
import subprocess
import sys


def main():
    directory = Path(sys.argv[1]).resolve()
    for name in ('libusb-1.0.dylib', 'libSDL3.dylib'):
        library = directory / name
        imports = subprocess.check_output(['nm', '-u', str(library)], text=True)
        if re.search(r'\b_pipe2\b', imports):
            raise RuntimeError(f'{name} imports pipe2, unavailable on supported macOS versions')
        commands = subprocess.check_output(['otool', '-l', str(library)], text=True)
        versions = re.findall(r'\bminos\s+([\d.]+)', commands)
        if not versions or any(tuple(map(int, v.split('.'))) > (14, 0, 0)
                               for v in versions):
            raise RuntimeError(f'{name} does not support the app\'s macOS 14 minimum: {versions}')
    # Run the real library entrypoints. A native crash fails the parent QA step;
    # successfully resolving a dylib alone cannot establish startup compatibility.
    usb = ctypes.CDLL(str(directory / 'libusb-1.0.dylib'))
    context = ctypes.c_void_p()
    usb.libusb_init.argtypes = [ctypes.POINTER(ctypes.c_void_p)]
    usb.libusb_init.restype = ctypes.c_int
    usb.libusb_exit.argtypes = [ctypes.c_void_p]
    result = usb.libusb_init(ctypes.byref(context))
    if result != 0:
        raise RuntimeError(f'libusb initialization failed: {result}')
    usb.libusb_exit(context)
    sdl = ctypes.CDLL(str(directory / 'libSDL3.dylib'))
    sdl.SDL_Init.argtypes = [ctypes.c_uint32]
    sdl.SDL_Init.restype = ctypes.c_bool
    sdl.SDL_GetError.restype = ctypes.c_char_p
    if not sdl.SDL_Init(0x00002000):  # SDL_INIT_GAMEPAD includes joystick/HID startup.
        raise RuntimeError(f'SDL gamepad initialization failed: {sdl.SDL_GetError()!r}')
    sdl.SDL_Quit()
    print('PASS macOS 14-compatible native controller imports and libusb/SDL startup')


if __name__ == '__main__':
    main()
