# Controller compatibility research

> Historical evidence: this document records its iteration. Read the [current maintenance guide](../agents/input-and-ui.md) before reusing controls, animation tuning or compatibility claims.

Verified 2026-10-20 against the installed engine, physical device enumeration and upstream source. Device detection, mapped controls, simulated events and observed human input are distinct evidence levels.

## Godot and Switch 2

Godot's desktop controller support uses SDL3. The installed Godot 4.7.2 dependency manifest pins SDL 3.2.28, before the dedicated Switch 2 driver. A mapping string cannot initialize a device that the backend never exposes. Sources: [Godot controller guide](https://docs.godotengine.org/en/4.7/tutorials/inputs/controllers_gamepads_joysticks.html), [pinned dependencies](https://raw.githubusercontent.com/godotengine/godot/4.7.2-stable/thirdparty/README.md).

The Switch 2 Pro USB device is VID/PID `057e:2069`. SDL added wired support using libusb in 3.4.0. The implemented optional macOS adapter pins **SDL 3.4.18**, including subsequent calibration and USB fixes, plus libusb 1.0.29. It opens only that controller model; Xbox and DualSense remain on Godot's backend. Sources: [SDL releases](https://github.com/libsdl-org/SDL/releases/tag/release-3.4.18), [Switch 2 driver](https://github.com/libsdl-org/SDL/blob/release-3.4.18/src/joystick/hidapi/SDL_hidapi_switch2.c).

On the tested Mac, opening the controller and claiming USB interface 1 succeeded, but claiming HID interface 0 returned `LIBUSB_ERROR_ACCESS`. Upstream's HID-open path therefore failed before its dedicated bulk driver could initialize. The bootstrap applies a narrow, reviewable [macOS patch](../../tools/controllers/patch-sdl-macos.py): for this VID/PID only, it supplies the open USB handle without claiming interface 0 or starting a HID read transfer. The existing Switch 2 driver owns bulk interface 1 for initialization. Live input still arrives through HID interface 0, so a second [reader patch](../../tools/controllers/patch-sdl-hid-reader.py) attaches a nonexclusive macOS HID reader and forwards input reads to it. A narrow enumeration exception permits that reader without changing which device the merged list prefers. The reader must be USB and uniquely identified; multiple Switch 2 USB controllers are refused rather than assigned the wrong input stream. Closing a handle without a HID transfer is guarded, and SDL property cleanup closes the independent reader. No system driver is installed or detached by this adapter's HID-open patch.

The optional SDL build disables its Apple MFI driver to avoid duplicate Objective-C classes alongside Godot's SDL. It retains a framework link needed by remaining upstream helper functions. These build settings and the patch are specific to this tested desktop integration, not a claim that unmodified SDL works on every Mac.

## Positional actions

SDL's canonical buttons describe physical positions. [SDL gamepad definitions](https://github.com/libsdl-org/SDL/blob/release-3.4.18/include/SDL3/SDL_gamepad.h).

| Action / position | Xbox | Switch 2 | DualSense |
|---|---|---|---|
| Bomb / south | A | B | Cross |
| Carry / west | X | Y | Square |
| Remote / east | B | A | Circle |
| Alternate carry | LB | L | L1 |
| Alternate remote | RB | R | R1 |

The product keeps positions consistent and changes prompts by family. It does not apply a Switch 1 raw mapping to Switch 2. Keyboard, D-pad and analog stick directions are normalized to the same running speed after a radial stick deadzone; analog direction is retained.

## Wireless distinction

The SDL driver used here does not implement Switch 2 Bluetooth. That does **not** mean wireless is impossible: projects such as [switch2mac](https://github.com/Peterksharma/switch2mac) document decoding the controller's proprietary BLE protocol and exposing a virtual gamepad or local input feed. Its author reports a signed virtual-HID release and a separate local UDP path. Those are third-party implementation claims, not hardware tests performed by this project. No Bluetooth helper, Accessibility permission, virtual-HID extension or wireless pairing was installed in this iteration.

## Reproducible dependencies

The bootstrap verifies the archives against the official GitHub release-asset SHA-256 values before extracting them:

- SDL3-3.4.18.tar.gz: `9c75cf16330322c217dedd2e0609f1124f1b54b8633e763467b4684d0f4334a3`.
- libusb-1.0.29.tar.bz2: `5977fc950f8d1395ccea9bd48c06b3f808fd3c2c961b44b0c2e6e29fc3a70a85`.

Sources: [SDL assets](https://github.com/libsdl-org/SDL/releases/expanded_assets/release-3.4.18), [libusb assets](https://github.com/libusb/libusb/releases/expanded_assets/v1.0.29). Libraries, build tools and downloaded source stay ignored. The bootstrap copies each library's upstream license beside the generated binaries; those dependencies are not relicensed under the game's MIT license.


## Iteration-eight input corrections

The native Switch USB report path remained live and full-range. The reproducible failures were above it: gradual analog deflection did not reliably navigate the GUI, changed-only reports provided no held repeat, and retained match bindings disabled analog navigation in Lobby/Results. The explicit menu state machine and visible-screen input role fix these without changing positional button mappings, unit-speed gameplay or the hybrid native reader. See [input/recovery details](iteration8-gameplay-input.md) and [current evidence](../verification.md).


## Printed Nintendo labels (iteration nine)

The user's latest preference supersedes the earlier positional mapping: Switch printed A is primary/confirm, printed B is carry/back, and X or R detonates remote bombs. Device-specific InputMap bindings preserve normal Godot menu and embedded popup behavior. The native hybrid reader is unchanged. Synthetic gradual-circle and unchanged-held-axis tests passed before the remap; the reported stepped feel was reproduced in visual snapshot chasing and corrected with interpolation. Current physical and synthetic evidence is recorded in [verification](../verification.md).
