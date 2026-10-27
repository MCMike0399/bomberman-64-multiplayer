# Desktop clients

Windows x64 uses Direct3D 12 and Linux x64 uses Vulkan, with Godot Forward+.
Apple Silicon retains Metal. Gameplay and online release identity share the
same source and `Release.props`.

## Downloads and signed updates

The requested player downloads are a per-user Windows installer
`Blast-Circuit-windows-x64-Setup.exe`, Linux `Blast-Circuit-linux-x64.AppImage`
and macOS `Blast-Circuit-macos-arm64.dmg`.
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

## Independent platform QA

Mac stable releases can continue on `main` while platform delivery is validated
on its separate branch and worktree. Keep candidate archives in private
`qa-desktop-<full-source-SHA>` drafts and run them against the harness-owned,
matching server. Do not deploy these candidates to maintained workers or install
them over the developer's stable Mac app.

The candidate workflow pins its signed Mac updater baseline to v0.43.0 instead
of following latest. Its receipt declares an explicit diagnostic baseline, which
cannot satisfy the stable publication gate. Candidate tests disable background
updates in their disposable guest; normal players retain the stable update feed.
Mac releases therefore do not change this QA input or its staged server.

The branch's v0.44.0/build54 identity is internal test data and reserves no public
version. Do not repeatedly merge or rebase onto advancing Mac releases during
qualification. When the platform work is ready, integrate current `main` once,
choose the next available public version and build, and rebuild and verify the
combined source. Final trusted gates must use those exact bytes and test updating
from the actual latest stable release before matching-server deployment and
publication. Earlier candidate evidence remains attached to its original SHA.

## Build and verify

Build with .NET 10, the pinned private dependency and official matching Godot
.NET export templates. Portable clients can cross-export; installers and tests
run on their actual target systems.

```sh
python3 tools/packaging/package-desktop.py --target windows-x64 --godot /path/to/godot
python3 tools/packaging/package-download.py --target windows-x64
python3 tools/packaging/verify-desktop.py --target windows-x64 --disposable-desktop
python3 tools/packaging/verify-desktop.py --target windows-x64 --graphical --disposable-desktop
```

The regression tool requires the matching archived server and metadata beside
the client archive. It installs the real Windows EXE into a path containing
spaces and compares every installed payload hash before testing. Linux runs the actual AppImage. Only the harness's own server starts, on a fresh
Kestrel-selected loopback port.

Headless regression covers the complete core suite, direct/relay peer rooms,
adaptive rooms, Greedy Trap server gameplay and slow polling. Graphical mode
runs `all` plus additional menu-preview and water journeys. Every case retains
exact leaf order, catalog hash, compiled release/platform, assertions, captures
and runtime errors. Promotion rejects package-smoke-only receipts. CI retains
failure summaries, JUnit, runtime logs and failure captures.
Windows regression refuses a personal desktop by default because the production
AppId owns per-user installer registration and shortcuts. Use a disposable VM or
profile (`--disposable-desktop` outside CI); the runner uninstalls its fixture in
cleanup before removing the temporary payload.

The opt-in `desktop-validation` PR label runs native hosted tests of separately
prepared exact-SHA draft candidate assets, without private build credentials.
Private builds still run only on trusted main. The independent
`tests/Bomber.DesktopUpdate.Tests` process fixtures exercise helper handoff,
replacement, uninstall preservation and launch rollback. Actual exported-game
transactions are also mandatory through `verify-desktop-update.py`. They use a
synthetic newer fixture identity, verify healthy renderer relaunch (including
saved High/fullscreen settings) and deliberate
pre-health rollback, compare complete inventories, and uninstall after Windows
updates. These tests require disposable Windows/Linux desktops or profiles.
The Windows payload carries an exact `installed-files.txt` inventory. Its
preserved uninstaller reads the current inventory so files and directories added
by later updates are removed too. It deletes listed files and empty directories
inside the installation, rejecting outside paths and reparse parents, while
preserving unrelated files. Newer update fixtures add both a root file and a new
directory; actual game-update tests require complete removal after uninstall.
Windows transactions allow up to ten seconds per atomic rename for native
access/sharing/lock violations (5/32/33), including rollback after process termination.
Persistent permission denial still fails at that bound; no permissions change.
Other I/O failures are immediate. The native helper regression first proves a
real directory handle blocks renaming, then holds it across replacement and
rollback; the diagnostic lane compares the retained old transaction with the fix.
Updater parent/child markers use an exact kernel birth identity. Linux compares
the boot ID and `/proc/PID/stat` field22 start counter, avoiding wall-clock
`Process.StartTime` estimates that can differ across runtimes. Windows retains
its kernel creation timestamp, and the existing Mac shell marker is unchanged.
Both handoff and cleanup reject a reused PID without controlling that process.
`startup-desktop.py` covers all nine saved preset/display launches using separate
preference files and captures. Ordinary player preferences are not rewritten.
Promotion compares complete regression receipts with the exact player-download
and archived-server hashes, including Mac and mixed-platform evidence.

## Evidence limits

Headless execution establishes packaged gameplay and loopback multiplayer.
D3D12/Vulkan visuals, native performance, physical pad hotplug/rumble,
X11/Wayland behavior and mixed-OS Internet play require their own evidence.
Run the full native renderer suite and inspect captures before claiming visual
parity. The SDL/libusb Switch 2 USB backend remains Mac-only; observe individual
Windows/Linux physical pads before claiming their support. Linux x64 initially
targets Ubuntu 24.04; ARM64 and other distributions need separate validation.


## One three-platform release witness

The release also requires complete DMG-installed Mac regression and
`cross-platform-quick-play`. The latter launches one actual installed Windows
client, Linux AppImage and DMG-installed Mac client against the same isolated
packaged server. An ephemeral HTTPS test tunnel exposes only that disposable
server; maintained workers and player rooms are not used for this pre-release
journey. The tunnel executable is hash-pinned and stops with its runner.

An Actions artifact barrier waits for all three real renderers before joining.
Windows leaves and rejoins; every client must observe its replacement identity
and the same fresh one-minute deadline. All three then observe the same server
match, accepted movement and carried bombs from every participant. A late
stranger must get another waiting room. A second barrier keeps original players
connected until every witness finishes. Complete source/release, driver, capture,
shared roster and player-download hash evidence is signed into
`Blast-Circuit-crossplay.json`. Independent per-OS passes cannot replace it.

The full headless suite also exercises an eight-player direct-peer room. Native
rendering receipts identify the adapter, including software/paravirtual adapters;
those runs establish functional rendering rather than physical GPU throughput.
CPU-device graphics preparation has a bounded 180-second QA allowance; hardware
devices retain 20 seconds. The explicit software-renderer full-suite runner
allows two hours for `all` and rejects receipts whose actual adapter type is
not CPU. The retained Linux failure drew only 14 preparation frames in the
ordinary 20-second budget. These allowances change test scheduling, not game
graphics or loading behavior, and cannot certify native GPU responsiveness.
The disposable Linux desktop supplies X11 and a PulseAudio null sink. Physical
speakers, controllers, native performance and Wayland remain separate evidence.
The disposable Windows fixture selects a real supported 32-bit display mode and
requires enough actual desktop work area for the unchanged 1600×1000 window
test, including the game's existing 48×80 decoration allowance. Its taskbar is
hidden within the disposable desktop when needed; no pixels beyond the selected
OS mode are invented. It also requires a usable WASAPI endpoint.
When no endpoint exists, it downloads the hash-pinned basic
[VB-CABLE 45](https://vb-audio.com/Cable/) driver from its vendor and requires
ordinary Windows signature validation of its installer and Microsoft hardware
catalog. No trust-store changes, test-signing mode or dummy backend override
are used. This donationware is a CI prerequisite only, subject to its
[vendor terms](https://vb-audio.com/Services/licensing.htm); it is not shipped in
the game downloads. The fixture fails if its real endpoint or display is absent.
Saved-display process restoration runs sequentially after the main graphical
process exits. Its unchanged 90-second child deadline, real persisted settings,
independent capture/check receipt and zero-error requirement remain release gates.
This avoids resident parent/child renderer contention without changing game graphics.
