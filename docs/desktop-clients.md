# Desktop clients

Windows x64 uses Direct3D 12; Linux x64 uses Vulkan. Both use Godot 4.7.2 .NET
and Forward+. The Apple Silicon client retains Metal. Shared gameplay, graphics presets and the
exact online admission token come from the same source and `Release.props`.

## Downloads and updates

Windows users extract the entire `Blast-Circuit-windows-x64.zip` and run
`BlastCircuit.exe`. Keep the PCK, data directory and native libraries beside it.
The .NET runtime and WebRTC extension are included; Godot and a .NET SDK are not
required on the player's computer. A Direct3D 12-capable GPU and current drivers
are required for the intended renderer. The Windows executable is not Authenticode
signed; the release's signed delivery manifest authenticates the archive.

Linux users extract `Blast-Circuit-linux-x64.zip` with executable permissions
preserved, then run `./BlastCircuit.x86_64`. If the extractor drops the executable
bit, run `chmod +x BlastCircuit.x86_64`. The runtime is self-contained but OS
libraries and a Vulkan driver are still required. Ubuntu 24.04 x64 is the initial
CI baseline; other distributions and X11/Wayland need their own validation.
ARM64 Linux, 32-bit systems and universal-distribution packages are outside this
initial port. A headless Docker test does not establish Vulkan/GPU coverage.

These initial desktop ports use manual updates. Settings → Download latest opens
the official releases page. Close the game and extract the new archive to a fresh
folder. Device preferences live outside the installation and are preserved.
Automatic checks/installation are unavailable on these platforms. macOS retains
its existing signed automatic updater and rollback behavior.

Every online client must match the server's exact version/build; installing only
an older platform archive will not join a newer server. Publication includes all
enabled desktop targets from the same trusted Verification run, and fails if an
archive or native-OS exported smoke is missing.

## Build and verify

Use the .NET 10 SDK, the pinned private build dependency and the matching official
Godot .NET export templates. Builds may cross-export from macOS/Linux; the target
OS must run the archived executable for its smoke check.

```sh
python3 tools/packaging/package-desktop.py --target windows-x64 --godot /path/to/godot
python3 tools/packaging/smoke-desktop.py --target windows-x64
python3 tools/packaging/package-desktop.py --target linux-x64 --godot /path/to/godot
python3 tools/packaging/smoke-desktop.py --target linux-x64
```

Run each smoke command on its matching OS and x64 CPU. It extracts the inspected archive into a
temporary location and runs `package-smoke` without the editor, SDK or private
checkout. The game reports its compiled release identity and OS. Receipts bind
that result to the archive checksum. The temporary client is removed; detailed
logs and captures remain under `artifacts/Blast-Circuit-windows-x64-smoke-details`.

On an owned Windows graphical desktop, add `--graphical` to require Direct3D 12,
Forward+, a hardware adapter and actual captures. This is a windowed QA launch.
Inspect the captures. Also exercise saved Low/Medium/High startup, fullscreen,
water/reflections, explosion warmup, controller hotplug/rumble/focus and mixed-OS
public/direct/relay/host-takeover journeys before claiming hardware coverage.
On a Linux graphical desktop the same flag requires Vulkan and Forward+.

CI cross-exports on Ubuntu and runs the exact archived Windows client headless on
`windows-2025`; Linux packaging and archived smoke run on `ubuntu-24.04`. All
desktop jobs are trusted-main-only; public PR checks never receive
private build credentials. Headless smoke establishes packaged runtime/assets
and local gameplay, not native graphics, performance, physical controllers or
Internet cross-play. Those remain explicit hardware validation requirements.

Archives include a per-file inventory, notices, release identity and .NET runtime.
Promotion, deployment preflight and anonymous post-publication verification check
their identity, inventory and smoke receipts. The existing Mac startup/update and
fleet deployment gates remain required. Historical Mac-only releases remain
verifiable with their original signed asset sets.

The special SDL/libusb Switch 2 USB backend remains macOS-only. Windows and Linux use
Godot's controller backend; support for individual physical pads must be tested.
