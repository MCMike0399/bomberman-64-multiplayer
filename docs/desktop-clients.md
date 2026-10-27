# Desktop clients

Windows x64 uses Direct3D 12 and Linux x64 uses Vulkan, with Godot Forward+.
Apple Silicon retains Metal. Gameplay and online release identity share the
same source and `Release.props`.

## Downloads and signed updates

The requested player downloads are a per-user Windows installer
`Blast-Circuit-windows-x64-Setup.exe`, Linux `Blast-Circuit-linux-x64.AppImage`
(from the stacked Linux PR), and macOS `Blast-Circuit-macos-arm64.dmg`.
Windows installs without administrator privileges. Each client includes its
.NET runtime, PCK, native networking library and notices. Windows executables
are not Authenticode signed; the signed delivery inventory authenticates the
installer. The DMG retains the existing ad-hoc Mac signature.

Settings enables automatic signed update checks on installed releases. Windows
replaces the complete installed folder after the old game exits. Linux replaces
the original AppImage outside its temporary mount. A detached helper launches
the replacement and requires a matching health token; launch failure or a
missing response restores and relaunches the previous payload. Desktop cold
rendering gets a bounded 180-second health window. The Mac transaction stays
unchanged. P-256 signatures, bounded downloads and SHA-256 authenticate update
payloads. Preferences remain outside the installation.

ZIPs remain available as portable runtimes and updater/archive inputs. Online
clients must match the server's exact version/build. Enabled packages, native
regression receipts, player downloads and signed platform update manifests are
published together. These paths remain source work until the exact candidate
passes target-OS validation.

## Build and verify

Build with .NET 10, the pinned private dependency and official matching Godot
.NET export templates. Portable clients can cross-export; installers and tests
run on their actual target systems.

```sh
python3 tools/packaging/package-desktop.py --target windows-x64 --godot /path/to/godot
python3 tools/packaging/package-download.py --target windows-x64
python3 tools/packaging/verify-desktop.py --target windows-x64
python3 tools/packaging/verify-desktop.py --target windows-x64 --graphical
```

The regression tool requires the matching archived server and metadata beside
the client archive. It installs the real Windows EXE into a path containing
spaces and compares every installed payload hash before testing. Linux's PR
runs the actual AppImage. Only the harness's own server starts, on a fresh
Kestrel-selected loopback port.

Headless regression covers the complete core suite, direct/relay peer rooms,
adaptive rooms, Greedy Trap server gameplay and slow polling. Graphical mode
runs `all` plus additional menu-preview and water journeys. Every case retains
exact leaf order, catalog hash, compiled release/platform, assertions, captures
and runtime errors. Promotion rejects package-smoke-only receipts. CI retains
failure summaries, JUnit, runtime logs and failure captures.

The opt-in `desktop-validation` PR label runs native hosted tests of separately
prepared exact-SHA draft candidate assets, without private build credentials.
Private builds still run only on trusted main. The independent
`tests/Bomber.DesktopUpdate.Tests` process fixtures exercise helper handoff,
replacement, uninstall preservation and launch rollback. Actual exported-game
transactions are also mandatory through `verify-desktop-update.py`. They use a
synthetic newer fixture identity, verify healthy renderer relaunch and deliberate
pre-health rollback, compare complete inventories, and uninstall after Windows
updates. These tests require disposable Windows/Linux desktops or profiles.
`startup-desktop.py` covers all nine saved preset/display launches using separate
preference files and captures. Ordinary player preferences are not rewritten.

## Evidence limits

Headless execution establishes packaged gameplay and loopback multiplayer.
D3D12/Vulkan visuals, native performance, physical pad hotplug/rumble,
X11/Wayland behavior and mixed-OS Internet play require their own evidence.
Run the full native renderer suite and inspect captures before claiming visual
parity. The SDL/libusb Switch 2 USB backend remains Mac-only; observe individual
Windows/Linux physical pads before claiming their support. Linux x64 initially
targets Ubuntu 24.04; ARM64 and other distributions need separate validation.
