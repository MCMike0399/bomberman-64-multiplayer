# Verified iteration six — 2026-10-20

| Check | Result |
|---|---|
| Godot and independent server builds | Zero warnings/errors |
| Gameplay integration tests | 37 passed, including the newly reproduced kick-contact regression |
| Client/session/presentation tests | 86 passed |
| AI behavior, timing and allocation tests | 15 passed |
| Synthesized audio | 19 distinct clips validated for energy, bounded peaks, clean endpoints, decay and caching |
| Real network smoke | All 2–8 human matches and one human + seven server bots passed (42.8 seconds) |
| Final windowed graphical suite | 302 assertions, 62 captures, zero runtime errors or capture fallbacks |

Final run: `20261020T142843.186715Z-all` (32.432 seconds locally; not a performance guarantee). It plays through the actual menus, roster, controller paths, map navigation, pickups, bomb actions, explosions, water, combat, results and network sessions. The results screen additionally verifies that the celebrating 3D model uses the winning player's appearance. The refreshed README gallery comes from this run and contains only this project's original game visuals.

The focused presentation sequence compares small, medium and large real bombs in the same temple camera. It checks rendered radius against authority, distinct billow counts, once-only accepted pump/throw/blast cues, nonhazardous vapor, cleanup, ground-bomb lifting, a contact kick, practice elimination/respawn, falling into water, and the eight-player HUD. Networking verifies event metadata and one bomb cue across repeated snapshots.

Actual output-bus capture recorded 160,768 stereo frames, peak amplitude 0.2292 and RMS 0.0249, with no discarded frames. The mix was non-silent and below clipping. The public WAV demo is that generated game mix; no microphone or reference-game audio is included. These objective checks do not claim that the sounds were auditioned against the original game. The [reference study](iteration6-research.md) explicitly records the original-audio perception limitation.

The presentation test exposed a gameplay defect: a player could be blocked just beyond the old kick threshold. A real-input regression reproduced it; the fix tests contact along the intended movement segment while preserving direction checks and continuous-contact deduplication. Other event tests cover bounded history, repeated/older snapshots, coalesced updates, reconnect baselines and water/fall/explosion causes.

Native controller support, windowed debug defaults, external kernel ownership and macOS Game Mode/export limitations are unchanged. Production hosting, adverse-network latency, physical eight-controller play and subjective audio balance remain outside these local checks.

## Iteration five baseline — 2026-10-20

Environment: Macintosh, macOS ARM64, Godot 4.7.2 .NET / native Metal, .NET 10.0.401. The authoritative server remained a separate local .NET process.

| Check | Result |
|---|---|
| Client/server builds | Zero warnings/errors |
| Gameplay tests | 32 passed |
| Client/input policy tests | 67 passed |
| AI behavior/determinism/allocation tests | 15 passed |
| Real networking | All 2–8 human matches and one human + seven server bots passed (42.9 s) |
| Final windowed `--scenario all` | 261 assertions, 51 captures, zero runtime errors |
| Earlier fullscreen combined suite | 272 assertions, 54 captures, zero runtime errors |
| Native Switch 2 backend | Checksum-pinned source build, local signing/load, real USB enumeration and physical input observed |

The final windowed run is `20261020T132406.932535Z-all`, completed in 54.586 seconds. It includes production keyboard/canonical-controller input events, menus, roster, camera aspects, actual stair movement, powerups, carried bomb growth, flooding, combat, results/replay and real network movement. Six README gallery images were refreshed from that run. The default is now windowed, matching the user's latest preference; fullscreen requires an explicit option. Controller event trials wait for stable real window/application focus rather than assuming a few fast engine frames are enough.

## Physical versus simulated controller evidence

- **Xbox Series controller:** actual left/right stick and face-button input was observed through Godot in `20261020T044301.802650Z-controller-observe`. Godot calls this device “Xbox One”; the product does not infer an exact model solely from that name.
- **Switch 2 Pro over USB:** actual left X/Y movement, all four canonical face buttons and both shoulders were observed in `20261020T130215.347536Z-controller-observe`. That successful capture followed the USB initialization plus nonexclusive HID-reader fixes. Earlier detection-only runs and the user's report of no movement were genuine failures, not evidence of working input.
- **PS5 DualSense:** canonical mapping, prompts and gameplay are covered with synthetic Godot events. No physical DualSense was connected for this session, so physical-device compatibility is not claimed as observed.

Synthetic controller tests replace only device discovery; movement and buttons travel through Godot's input event system, `PlayerControls`, the normal session and real authority. They cover keyboard/cardinal/diagonal/partial-stick travel equality, 20% radial drift rejection, independent keyboard seats, compact and legacy aliases, D-pad navigation, shoulder actions, sub-frame button taps, no held-bomb auto-repeat, neutral arming, focus loss/restoration, stable hotplug reservations and native-range device IDs. They do not use the game-root input-injection shortcut.

The live tester records no serial numbers and checkpoints newly observed input categories. An observation session completing without input is explicitly **unverified**, not a passing physical-button test. Raw observations, native build logs and failed witnesses stay in ignored artifacts.

## macOS and packaging boundaries

Native Metal is active in the tested renderer. The macOS export preset declares Games category and Apple's `LSSupportsGameMode` opt-in, with ARM64 ASTC import enabled. Godot parsed the preset; standalone export preflight is blocked by the missing matching .NET macOS export template. No exported bundle, actual OS Game Mode activation, performance improvement or notarized release was established. See [Game Mode research](macos-game-mode.md).

The optional native adapter currently handles one uniquely identified Switch 2 Pro USB controller. Switch 2 Bluetooth was researched but not implemented or installed. Xbox/DualSense remain on the engine backend. Upstream native libraries, copied licenses, kernel source, private logs and downloaded media are excluded from Git. Physical eight-controller coverage, adverse internet latency and production hosting remain unverified.

## Iteration four baseline — 2026-10-19

Environment: macOS ARM64, Godot 4.7.2 .NET / Forward+ Metal, .NET 10.0.401. The kernel-backed server runs as an independent local .NET process. Deployment to the user's server hardware remains separate work.

| Check | Result |
|---|---|
| Godot client and server builds | Zero warnings/errors |
| Gameplay executable tests | 32 checks passed |
| Client executable tests | 37 checks passed |
| AI behavior/determinism/allocation tests | 15 checks passed |
| Real HTTP/WebSocket network smoke | All 2–8 human matches, seven server bots, round reset and reconnect passed |
| Pre-match appearance catch-up | Late join and reconnect received existing choices before the match began |
| Fullscreen graphical `--scenario all` | 171 assertions, 42 viewport captures, zero runtime errors |
| Dedicated `--scenario fullscreen` | 7 assertions, 2 captures, F11 and Alt+Enter both ways |

The full suite launched fullscreen and entered actual matches. It exercised real role and suit controls, verified changing leg poses in the shared 3D previews, packed gapped human/CPU selections correctly, walked all temple stairs up and down, collected powerups, carried/pumped/threw bombs, observed flooding and bot combat, reached results, replayed, and verified remote movement and selected colors against the real server.

Camera projection checks covered all three stages in native fullscreen plus 1280×720 and 1280×800. Only explicit resize checks temporarily used a window; the harness restored fullscreen before gameplay. Window-mode assertions require 500 ms of stability. Native roster popup key events target the popup's own window ID; routing every key to the main window had produced an automation failure that is now fixed.

The passing complete run was `20261020T032443.975972Z-all` (49.765 s); dedicated display verification was `20261020T032539.382344Z-fullscreen` (4.609 s). The full run needed no capture fallback. These are local measurements, not performance guarantees. Logs and traces remain in ignored artifacts. The [README gallery](../README.md#screenshots) contains five unmodified captures from this final full run, showing the new models, roster, camera, materials, bomb growth and water.

Read-only mesh review checked all 1,728 rounded-cuboid triangles for representative helmet, face and sole dimensions: no degenerate triangles or incorrect winding. Actual screenshots were inspected as well as state assertions. Publication checks found no credentials, private kernel origin, local user paths or ignored source/media in the publishable file set.

Physical eight-controller coverage, internet latency/loss, production hosting and multi-node capacity remain unverified. The current work does not change that boundary. See [harness instructions](../tools/playtest/README.md) and [reference research](iteration4-research.md).

## Iteration three baseline — 2026-10-19

Environment: macOS ARM64, Godot 4.7.2 .NET / Forward+ Metal, .NET 10.0.401. The authoritative kernel-backed server was a separate local .NET process. This verifies the client/server separation; no deployment to the user's server hardware was performed.

| Check | Result |
|---|---|
| Game and server builds | Zero warnings/errors |
| `dotnet run --project tests/Bomber.Game.Tests` | 27 checks passed |
| `dotnet run --project tests/Bomber.Client.Tests` | 28 checks passed |
| `dotnet run --project tests/Bomber.Smoke -- http://127.0.0.1:18496` | Passed 2–8 human lobbies, mixed seven-bot match, actual movement/bombs, quick play/cancellation, acknowledgements and reconnect |
| Graphical `tools/playtest/run.py --scenario all` | 103 assertions, 29 actual viewport captures, zero runtime errors |
| `dotnet run -c Release --project tests/Bomber.Ai.Tests` | 15 behavior, determinism and allocation checks passed |

The gameplay checks include actual stair ascent/descent, continuous height, forbidden cliff crossings, altitude-aware blast/cover, reachable bot paths, own-bomb escape, trapped-bot refusal, practice respawn after self-explosion/water falls, and retained final bot-win scores. Client checks include real input-edge preservation, roster schemas, controller seat assignment and connection cleanup.

The graphical scenarios activate actual UI controls with keyboard and mouse at 1280×800 and 1024×640; place, lift, pump and throw bombs; walk the temple stair flights; sustain combat until items and a round outcome; reach CPU-victory results and replay; and connect two real kernel clients. The lobby capture verifies visible connected-player names and countdown in addition to authoritative state.

Selected local-game captures are included in [the screenshot gallery](../README.md#screenshots). Full traces, runtime logs, and machine-readable reports are generated by the harness under ignored `artifacts/playtest/` directories; they are deliberately not published.

The harness caught a named-pickup rendering exception that a startup-only smoke missed. It also caught window-coordinate errors in synthetic mouse input. Captures wait for rendering completion; earlier stale-image artifacts remain separate from passing evidence. Runtime errors fail the wrapper even if Godot itself exits 0.

Physical eight-controller coverage, internet latency/loss, multi-node capacity and production hosting were not established by these local checks. See [playtest instructions](../tools/playtest/README.md) for repeatable runs and JSON scenario authoring.


Iteration-three checks additionally cover weighted destruction drops and all five upgrade types, capacity/fire limits, round resets, red/remote chain behavior, one-hit hearts, held creation and fuse pausing, per-press pumping, real model growth and body poses, local training reset, Pyramid water timing and altitude-based drowning. New AI checks cover useful pickup pursuit, timed chain danger, flood refuge, repeated opponent pressure, fair response profiles and reusable search storage.

The full graphical run executed nine scenarios and inspected actual model scale, active pump pose, inventory, flood height and connected lobby UI. It completed in 41.388 seconds on this machine with zero runtime errors. Captures use a bounded post-draw fence with a main-thread redraw fallback, rather than waiting forever if background drawing pauses. The final passing run required no fallback. These timings are a record of this run, not performance guarantees.

The [AI benchmark report](../tests/Bomber.Ai.Tests/README.md) records approximately 58–62% lower allocations per active tick across the sampled maps, improved movement/pickup counters, and increased CPU cost on some maps. It explicitly distinguishes the combined iteration comparison from isolated AI causality and does not claim that numerical counters prove fun.
