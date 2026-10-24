# Native Metal and macOS Game Mode

Verified 2026-10-20. The game already selects Godot's Forward+ renderer and native Metal backend in `project.godot`; runtime captures identify the Metal renderer. Game Mode is a separate operating-system scheduling feature, not an extra rendering library.

Apple documents `LSSupportsGameMode=true` in the game's `Info.plist` as the opt-in. The new `macOS Metal` preset declares that key and categorizes the bundle as Games using Godot's `application/additional_plist_content` and `application/app_category` export options. No private Game Mode API, paid entitlement or modification of the installed Godot editor is required for this declaration. Sources: [Apple's plist key](https://developer.apple.com/documentation/bundleresources/information-property-list/lssupportsgamemode), [Godot macOS exporter](https://github.com/godotengine/godot/blob/4.7.2-stable/platform/macos/export/export_plugin.cpp).

Apple says Game Mode requires Apple Silicon, macOS 14 or newer and native fullscreen play. It reduces background activity and improves Bluetooth latency; the user can turn it off per game. The project does not override that preference or promise a measured performance increase. Windowed debug/harness runs are deliberately not Game Mode activation tests. Sources: [Apple Game Mode requirements](https://support.apple.com/en-mide/105118), [Apple WWDC25 explanation](https://developer.apple.com/videos/play/wwdc2025/209/).

Normal play still opens fullscreen. Run `./tools/dev/debug.sh` or the Python harness for windowed testing. `--fullscreen` opts a harness run into fullscreen; the dedicated `--scenario fullscreen` also explicitly requests it.

The installed editor is classified as a developer tool. Running this project through that editor executable does not establish that macOS recognizes the game as its own eligible app. The declaration applies to the game's exported `.app` bundle. Iteration seven now produces and tests that standalone app, including the native Switch 2 libraries and upstream notices. See [packaging](macos-app.md) and [Godot's macOS export guide](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html).

Configuration and bundle checks verify the Metal setting, native export platform, Games category and parseable plist containing the documented opt-in. The exported app also passes actual gameplay tests. These do not establish OS activation, increased FPS, lower latency or notarization.

Historical iteration-five preflight was blocked by missing templates. Iteration seven installed the verified matching .NET template, prepared an ARM64 variant, added the required C# solution and exported the self-contained app.
