# Standalone macOS app

The exported bundle targets Apple Silicon and macOS 14 or newer. It includes the exported Godot game pack, self-contained .NET runtime, game/client assemblies, and kernel contract assembly. Local play uses the in-process simulation; online play connects to the independent kernel server selected in the lobby.

The development build is installed at `~/Applications/Blast Circuit.app`. First launch is fullscreen. Settings saves display mode and resolution for future
launches; F11 or Alt+Enter also saves the mode. Debug and harness launches remain
windowed by default. Fullscreen resolution adjusts 3D rendering while menus stay
sharp; oversized windowed choices fit the current display.

## Rebuild

Requirements: the pinned kernel source dependency, .NET 10 SDK, Godot 4.7.2 .NET, matching .NET export templates, and macOS command-line tools. The optional Switch 2 backend can be built with `tools/controllers/bootstrap-sdl.sh` first.

```bash
python3 tools/packaging/package-macos.py
open "artifacts/exports/Blast Circuit.app"
```

For delivery, prefer the `package` QA profile: it also validates native backend
imports, minimum macOS version and actual libusb/SDL startup before exporting.
The controller bootstrap targets macOS 14 and disables libusb's unguarded `pipe2`
path, which newer SDKs can detect even on a host that cannot execute it.

Godot's official 4.7.2 .NET template archive contains Universal 2 binaries. The packaging script makes a project-local ARM64 template with Apple's `lipo`; it leaves the installed template untouched. `export_presets.cfg` selects that generated template. To export through the editor, run the packaging script once to prepare it.

For this build the official archive was downloaded from [Godot's release](https://github.com/godotengine/godot-builds/releases/tag/4.7.2-stable) and verified against its `SHA512-SUMS.txt` before extracting the macOS template. The engine's [macOS export documentation](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html) describes app bundles, signing and notarization.

The exporter requires `Bomberman64Multiplayer.sln`, even though ordinary `dotnet build` accepts the project file directly. Missing-solution errors originally produced an incomplete app despite an exporter exit code of zero. The packaging script now rejects logged exporter errors and requires the managed game assembly before signing.

## Native controller libraries and notices

When available locally, `libSDL3.dylib` and `libusb-1.0.dylib` are copied into `Contents/Frameworks/Controllers`, with their licenses and source/rebuild reference. The exported controller adapter resolves this bundle location instead of `res://native/macos`. The native libraries remain replaceable dynamic libraries; a modified bundle needs re-signing.

Godot, .NET, simulation and game notices accompany the bundle. The .NET notices come from the exact runtime package used by the self-contained publish. Original reference media, kernel source, SDK caches and test artifacts are excluded.

The script verifies the app with `codesign --verify --deep --strict` and validates `Info.plist`. This is a **local ad-hoc signature**, suitable for this Mac's local build; it does not claim Developer ID signing or Apple notarization for public distribution. Bundles and template archives are ignored by Git.

## Test the shipped app

```bash
python3 tools/playtest/run.py --app "artifacts/exports/Blast Circuit.app" --scenario all --start-server
python3 tools/playtest/run.py --app "artifacts/exports/Blast Circuit.app" --scenario model-profile
python3 tools/playtest/run.py --app "artifacts/exports/Blast Circuit.app" --scenario fullscreen
```

The runner launches the bundle executable without `--path` and with a working directory outside the source project. The suite exercises the bundled menus, gameplay, audio, controller adapter discovery, model profiles and real network sessions. Profile captures use a temporary inspection camera on the actual in-game player; facing still comes from authoritative movement inputs. It restores the battle camera afterward. Quick `--smoke` captures go to writable `user://captures` in the exported app, never into its read-only resource pack.

The bundle declares Games category and `LSSupportsGameMode=true`. Successful fullscreen launch does not prove OS Game Mode activation or a measured speed/latency improvement. See [Game Mode](macos-game-mode.md) and [verification](verification.md).
