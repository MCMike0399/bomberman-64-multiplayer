# Verified iteration thirteen — 2026-10-21

| Check | Result |
|---|---|
| Targeted AI behavior, recovery and fairness policy | **36 checks passed**, including five navigation regressions, tactical difficulty choices, all-level open-arena pressure, and moving-remote safety |
| Long-match AI matrix | **36 scenarios + four starting-state/alias fairness checks passed** across three maps, three levels, two bot counts and two seeds |
| Gameplay integration | 104 checks passed |
| Client/session/input | 95 checks passed |
| Full standalone Mac regression | **1,575 assertions, 131 captures**, zero runtime errors or capture fallbacks |
| Recorded standalone AI matches | 13 assertions, nine captures, full MP4 decode passed |
| Independent HTTP/WebSocket networking | 2–8 humans and one human + seven server bots passed (43.3 seconds) |
| Installed app | v0.13.0; strict signature, export equality and Launch Services gameplay smoke passed |

The final full standalone run is `artifacts/iteration13/final/20261021T133414.618134Z-all`, 136.754 seconds including startup. It uses windowed QA, the self-contained app and the rebuilt project-owned localhost authority. A source integration run also passed. The first full run exposed a harness assumption: a popup could already hover its last item after earlier mouse tests, so Down/S could not move farther. The corrected test reaches the first item using actual W presses before checking S and W, preserving native routing rather than setting focus state directly.

The initial AI matrix reproduced long standstills in 30 of 36 scenarios. The final same matrix had none of its 12-second safe-exit liveness violations: longest eligible standstill with a safe exit was 1.87 seconds, compared with 85.03 seconds in the initial measurement; longest continuously blocked movement command was 0.10 seconds. It covers 3,172 simulated seconds, 54 completed rounds, 1,953 bombs, 734 visible opponent-pressure opportunities and 436 pickups. These are bounded seeded observations, not a guarantee for every layout or a human fun score. [Measurement definitions and test commands](../tests/Bomber.Ai.Tests/README.md).

All three difficulties now produce actual opponent-threatening bombs and explosions in a 30-second open-arena fixture. This initially failed for Easy, whose wide preferred spacing could otherwise persist forever. Same-fixture tests separately distinguish cautious spacing, contested dead-end avoidance and Medium-versus-Hard route choices; changing reaction timing alone would not satisfy those checks. Starting terrain, spawns, inventory and physics remain equal. The legacy `normal` setting is deterministic-equivalent to Medium.

The final recording is `artifacts/iteration13/video-final/20261021T133736.826927Z-ai-play`: 2,037 frames / 67.9 seconds, 1280×800, 30 FPS with game audio. It runs three real matches with a stationary human observer and captures ordinary CPU movement, bombing, blasts and outcomes. The public [AI difficulty comparison](videos/ai-difficulties.mp4) cuts six seconds from each match at 1× speed (source starts 7.567, 29.667 and 51.767 seconds), adds explicitly labeled QA difficulty captions, and includes three chapters. It is about 18 seconds long. Every clip was decoded and inspected across its opening, action and ending frames; no actor states were edited or gameplay frames fabricated. Fixed-step recording is not a real-time GPU performance claim.

A separate nine-run Release CPU benchmark observed per-run p95 active-tick times of 0.102–0.378 ms and a largest single tick of 4.666 ms on Apple M5 Pro. These simulation-only local measurements exclude networking and rendering and do not establish fleet capacity. The warmed-search regression retains zero allocations over 1,000 decisions.

WASD testing covers main-menu focus, setup navigation, difficulty selection, roster popups, Results actions and ordinary text entry. Arrow keys remain usable. No new physical-controller session is claimed. The installed app matches the tested export byte-for-byte, passes strict recursive signature validation and reached 118 gameplay snapshots through Launch Services with zero stderr. Local ad-hoc signing, production-deployment and Game Mode boundaries remain unchanged.

[AI architecture and navigation diagnosis](iteration13-ai.md), [harness recording](../tools/playtest/README.md).

## Iteration twelve baseline — 2026-10-20


| Check | Result |
|---|---|
| Gameplay integration | **104 checks passed**, including seven placement/departure and four exclusive-mode regressions |
| Client/session/input | 93 checks passed |
| AI | 21 checks passed, including six stock-map combat outcomes and near-foot placement proximity |
| Full standalone Mac regression | **1,545 assertions, 120 captures**, zero runtime errors or capture fallbacks |
| Final standalone placement recording | 19 assertions, four captures, full MP4 decode passed |
| Independent HTTP/WebSocket networking | 2–8 humans and one human + seven server bots passed (43.5 seconds) |
| Installed app | v0.12.0; strict signature verification, export comparison and Launch Services gameplay smoke passed |

The final full run is `artifacts/iteration12-final/20261021T043748.523851Z-all`, 62.230 seconds including startup, windowed, using the self-contained app and rebuilt localhost authority. It preserves all earlier gameplay, controller, menus, camera, blast, flood, ghost and network coverage. The test durations are local observations, not performance guarantees.

Two separate placement failures were reproduced and fixed. A valid fractional position on empty flat ground was rejected by the old far-forward footprint/connectivity check. Separately, the full controller suite exposed a moving A hold retrieving the bomb from behind; a regression then reproduced that behavior before movement was made to cancel the fresh-bomb auto-lift candidate. Stationary hold/lift, deliberate second-A kick, normal leave-and-return contact kicks, fuse timing and finite capacity remain verified. Genuine capacity/space rejection now has a visible cue.

The camera's measured temple width at 1280×800 grows from 55.94% in the final v0.11.0 run to 63.51%, while the new framing checks cover full 2.1-unit player headroom and actual pillar crowns at two window aspects on all three maps. Default bombs are 22% larger and pickups 20% larger. Following the user's oversized-maximum feedback, pump growth is reduced: measured target scales 1.5398, 1.8598, and 2.1798 for three stages at base fire, with a 2.3 target cap. Blast strength is unchanged. Real captures confirm the blue remote shell and green indicators after walking clear, exclusive modes, and the smaller fully pumped silhouette.

The final placement movie is `artifacts/iteration12-video-final/20261021T043939.632698Z-placement`: 298 frames / 9.933 seconds, 1280×800 at 30 FPS with original game audio. The public [near-foot placement clip](videos/near-foot-placement.mp4) starts at 4.933 seconds and lasts 1.567 seconds at 1× speed. It is actual rendered gameplay with a temporary inspection camera and ordinary positioning inputs, followed by real engine keyboard and synthetic Switch bridge events. No actor state was rewritten and no frames were generated. The first drop, departure and stationary trail were inspected across extracted frames; the complete MP4 and short clip decoded successfully. Original reference footage remains ignored. Fixed-step recording is not a real-time frame-rate or physical-controller test.

The installed app matches the export byte-for-byte. Launch Services smoke reached 117 gameplay snapshots with zero stderr output.

An additional final-app scripted run, `20261021T044207.082914Z-script`, passed four checks and captured an unobscured blue remote after the player walked away. The source visual runs verified all maps/aspects, item collection and capped pumping before final export. The earlier full-run controller failure and red test artifacts are retained locally rather than overwritten as success.

[Original-game placement, remote color and the unresolved equal-power red-radius question](iteration12-bombs-research.md). Mode exclusivity is the user's requested rule. No extra red radius multiplier was inferred from uncontrolled footage. No new physical-controller session, external deployment, notarization or Game Mode gain is claimed.

## Iteration eleven baseline — 2026-10-20


| Check | Result |
|---|---|
| Gameplay integration | **93 checks passed**: 11 new terrain-travel, 5 kick timing/short-impact and 5 shoreline cases |
| Client/session/input | 93 checks passed |
| AI | 21 checks passed, including actual combat outcomes |
| Full standalone Mac regression | **1,524 assertions, 115 captures**, zero runtime errors or capture fallbacks |
| Recorded standalone kick/impact/terrain scenario | 79 assertions, 11 captures, zero runtime errors; complete MP4 decode passed |
| Independent HTTP/WebSocket networking | 2–8 humans and one human + seven server bots passed (43.4 seconds) |
| Installed app | v0.11.0; strict signature check, export equality and Launch Services gameplay smoke passed |

The final full run is `artifacts/iteration11-final/20261021T001659.140575Z-all`, 97.855 seconds including startup. The app runs independently of the source project, windowed, against the rebuilt project-owned localhost server. The earlier run failed when the old grapple witness started its carrier too far from the now-nearby victim to reach them before recovery. The final test starts the carrier closer and still requires ordinary movement, body clearance, accepted grab, escape and throw. No actor state is teleported or rewritten.

The final recorded run is `artifacts/iteration11-video-final/20261021T001910.741104Z-kick-physics`: 754 frames, 25.133 seconds, 1280×800, 30 FPS, original game audio. It uses a temporary inspection camera on the same actual stock-temple actors for the first hit and restores the battle camera afterward. The 1× [four-second kick/stumble clip](videos/kick-and-stumble.mp4) is cut from 10.633 seconds, with no timing changes, generated frames or reference-game assets. Its H.264/AAC output was fully decoded and its beginning, wind-up, strike, low hop, nearby landing and recovery inspected across extracted frames. Fixed-step Movie Maker capture verifies simulation/presentation, not real-time rendering throughput or input latency.

The actual hit measures a 0.301-unit rise and 1.10-unit horizontal displacement. The old impulse fails the bounded-stumble regression at 0.995 units of rise. A sustained kick now crosses the actual lower courtyard lip and resumes rolling on landing; unit-level ordinary-input cases also cover more than two seconds of unobstructed travel, repeated drops and full-radius corner collision. [Original-video observations versus project tuning](iteration11-kick-research.md).

The water regression reproduced death at height 0 while the visible plane was −1. The final stock-temple traversal checks dry radius grazing, a live falling actor between floor and water, and a death event exactly at the water plane. The full suite retains flooding, ordinary ledge descent, ghost interactions, normal/remote fuse semantics and controller mappings. This iteration did not require another physical-controller input session; existing physical observations remain distinct from synthetic input coverage.

The installed `~/Applications/Blast Circuit.app` matches the tested export byte-for-byte and passes strict recursive signature verification. Launch Services smoke reached 117 gameplay snapshots with zero stderr. The app remains locally ad-hoc signed; no notarization, external deployment or Game Mode performance claim is made. Menus and approved head geometry are unchanged.

## Iteration ten baseline — 2026-10-20


| Check | Result |
|---|---|
| Server and self-contained Mac export | Successful; server build zero warnings/errors |
| Gameplay integration | 72 checks passed, including eight bomb-impact/fuse and seven body-contact cases |
| Client/session/input | 93 checks passed |
| AI | 21 checks passed, including combat outcomes across all stages |
| Original synthesized audio | 19 cues validated for energy, bounded peaks, clean edges and caching |
| Full exported-app regression | **1,470 assertions, 109 captures**, zero runtime errors, zero capture fallbacks |
| Independent HTTP/WebSocket networking | 2–8 humans and one human + seven server bots passed (42.9 seconds) |
| Installed app | Version 0.10.0; strict signature check, byte-for-byte export comparison and macOS Launch Services gameplay smoke passed |

The final windowed standalone run is `artifacts/iteration10-final/20261020T225239.321415Z-all`, 65.325 seconds including startup. It runs the bundled executable without a source-project path against the rebuilt project-owned localhost server. Kick launch/impact/landing, normal corner fuse, remote command, body contact/yield/side escape, grabs, all earlier menu/controller/presentation flows and actual network gameplay passed. Selected real screenshots refresh the README gallery. Local test durations are not performance benchmarks.

The normal and pumped finite-fuse checks already passed before the fix. The reproduced indefinite-bomb defect was an orphaned remote after owner departure in a three-player match. New coverage separates that lifecycle from active remote command behavior, moving remote detonation, billiard transfer and full-radius wall sweeps. [Primary manual and inspected footage, diagnosis and tuning](iteration10-kick-research.md).

Controlled uneven snapshot arrival tests exercise the production interpolation helper at 30/60/120 FPS, plus stale arrival, starvation and discontinuity handling. Actual scene running/stop behavior also passes. The 100 ms visual buffer trades roughly 33 ms more delay than the earlier one-snapshot interpolation for smoother cadence. It is not client prediction and does not establish internet latency/loss tolerance.

Height-aware body tests cover head-on fairness, glancing contact, idle yielding, wall pressure/escape, eight-body crowds, separate heights and overlap repair. The visible scene checks also require real model separation. Grapple tests now wait for landing and walk to the displaced victim using ordinary input; they do not teleport it back to the previous fixture location.

The installed `~/Applications/Blast Circuit.app` matches the tested export. A separate macOS Launch Services smoke reached 118 gameplay snapshots with no stderr output, using Metal on Apple M5 Pro. The local ad-hoc signature is verified; no notarization, OS Game Mode boost, new physical-controller observation or production deployment is claimed. The approved head geometry and menus are unchanged in this iteration.

## Iteration nine baseline — 2026-10-20


| Check | Result |
|---|---|
| Game and exported app builds | Zero warnings/errors |
| Gameplay integration tests | 57 passed |
| Client/session tests | 93 passed |
| AI tests | 21 passed, including six real production matches requiring explosions, destroyed cover and drops |
| Full exported-app regression | **1,303 assertions, 95 captures**, zero runtime errors; one bounded redraw fallback |
| Final spherical-head export | 13 profile checks / 5 captures and 10 Results checks / 1 capture, zero runtime errors |
| Independent networking | 2–8 humans and one human + seven server bots passed (42.7 seconds) |

The full app run is `20261020T191040.273345Z-all`, 66.656 seconds locally. It covers all earlier flows plus primary-only charging, normal/remote detonation, continuous kick directions, rendered movement cadence, directed ledge falls, ghost riding/control/expiry, new Nintendo mapping, and the 3D Results theatre. Timing is a local test duration, not a performance guarantee.

After that full run, the user requested a more spherical head. Only model geometry was further refined: the final self-contained export passed `20261020T192454.987991Z-model-profile` and `20261020T192458.903369Z-results`. All five head views were visually inspected. Actual shell vertices satisfy the continuous ellipsoid shape, normals are finite/unit/outward, and the faceplate has real volume with nondegenerate outward triangles. The screenshot gallery uses the full gameplay run, with final head/Results closeups and the two main-menu views updated afterward. The final menu gallery run `20261020T193259.542675Z-script` passed three checks with two captures.

The movement regression demonstrated alternating .102/.198-cell rendered steps before interpolation (1.95 ratio), versus approximately .150/.150 afterward (1.000003 ratio) at the same authoritative speed and fixed 30-FPS sampling. Native bridge tests already preserved 360-degree axes; no unsupported native SDK diagnosis is claimed.

Physical Switch 2 USB capture `20261020T184756.285569Z-controller-observe` contains 20,737 reports, full-axis range and all 36 ten-degree direction bins, all four face buttons and both shoulders. The first capture contained only idle drift and is not claimed as physical movement coverage. A three-second held diagonal was not established by the physical trace; unchanged held-state coverage is synthetic. Printed Nintendo A/B/X actions and menu confirm/back are tested through the production per-device path.

Front placement uncovered a real bot regression: 265 placements but no explosions/destruction/drops in the fixed-seed temple witness. Front-aware placement and retreat now produce 31 placements, 30 explosions, 15 destroyed cover objects and seven drops. All three stock stages with three and seven bots pass outcome checks. The full combat harness also requires actual pickups and round completion; its assertion was retained.

The new role-popup default focus was verified in the full suite. Failed exploratory/early runs remain in ignored artifacts, including a later native mouse-resize interruption during a cosmetic screenshot pass. Final head-only validation does not claim a second full gameplay run. Normal windowed QA, transparent battle HUD, private kernel exclusion and local ad-hoc signing remain. Production deployment, adverse-network latency, physical DualSense and Game Mode performance gains remain unverified.

[Movement, bomb, ghost and AI implementation](iteration9-gameplay.md) · [Harness](../tools/playtest/README.md) · [Mac app](macos-app.md)

## Iteration eight baseline — 2026-10-20


| Check | Result |
|---|---|
| Game and server builds/export | Zero warnings/errors |
| Gameplay integration tests | 43 passed, including outgoing-kick, recovery, hold, throw and cleanup regressions |
| Client/input/session tests | 93 passed |
| AI behavior/determinism/allocation checks | 15 passed |
| Real independent server smoke | 2–8 humans and one human + seven bots passed (42.8 seconds) |
| Final exported-app full suite | **623 assertions, 81 captures**, zero runtime errors and zero capture fallbacks |
| Physical Switch 2 Pro USB | Both left-stick axes reached −1/+1, gradual intermediate values observed, then centered below deadzone |
| Installed app | Strict signature check; Launch Services gameplay smoke passed with 223 snapshots and zero stderr |

Final app run: `20261020T165457.490551Z-all`, completed in 43.72 seconds on this Mac. It launches the self-contained ARM64 app without the source-project path. This includes actual menu navigation, gradual/held/reversed controller input, retained-session Lobby/Results navigation, configured role popups, the new setup dials, five character profile views, terrain traversal, powerups, recovery/grabs/throws, rendering/audio, flooding, results and real network sessions.

The new gameplay regression reproduced a player catching the back of their outgoing bomb and stunning themselves. Other regressions verify visible hit causes, victim-owned recovery, no repeated effort from held input, finite passive holds, no latent bomb, paired carry following/escape, continuous throw flight and safe carrier disconnect/departure. The graphical grapple sequence uses ordinary inputs and the real temple training items, not state mutation.

Physical evidence is `20261020T161913.603699Z-controller-observe`: 8,998 timestamped USB axis events, left X/Y full-range movement, and final centered values approximately −.0048/−.0289. No physical button press was observed in that capture. Synthetic Switch tests use the production native-publication helper; Xbox/DualSense canonical coverage remains synthetic in this iteration. Earlier physical-button evidence is retained below.

The face mesh is checked for real depth, outward clockwise triangles and nondegeneracy, then captured front/back/both sides/three-quarter. The final model keeps the separate plate at moderate convexity, with a rounded return, slightly reduced forehead and rounder lower helmet. The setup preview renders actual authority terrain. The courtyard shader is preserved; upper stone, lower lighting, foundation and water were inspected at normal and flood levels. [Rendering sources and boundaries](iteration8-rendering.md).

A separate 15-second installed-app smoke, capped at 60 FPS, reported 59–61 FPS intervals on Apple M5 Pro with native Metal and normal gameplay. This is a short local observation, not a benchmark, improvement claim, worst-case guarantee or Game Mode measurement. The app remains locally ad-hoc signed, not notarized. Production hardware deployment, internet latency/loss, Bluetooth Switch support and physical eight-controller play remain outside this verification.

The README screenshots come from the final exported-app run. [Gameplay/input design](iteration8-gameplay-input.md), [harness](../tools/playtest/README.md), [Mac app packaging](macos-app.md).

## Iteration seven baseline — 2026-10-20


| Check | Result |
|---|---|
| Managed build/export | Zero warnings/errors; self-contained ARM64 app |
| Exported app, windowed full suite | **330 assertions, 66 captures**, zero runtime errors; 3 capture fallbacks |
| Exported app, fullscreen controls | 7 assertions, 2 captures; normal fullscreen launch and F11/Alt+Enter verified |
| Client contracts/session checks | 86 passed |
| Independent real network smoke | All 2–8 human matches and one human + seven server bots passed (42.8 seconds) |
| Bundle validation | Strict recursive signature check and plist validation passed |

The final full run is `20261020T153937.985698Z-all`, 37.031 seconds locally. Fullscreen is `20261020T152500.279221Z-fullscreen`, 4.989 seconds. These are local run durations, not frame-rate or performance guarantees. Both launch the exported executable without a source-project path, from an independent evidence directory. Graphical output identifies native Metal on Apple M5 Pro.

The full suite exercises the live 3D menu, actual shared-stage roster models, mouse/controller navigation, multiple window aspects, appearance transfer, traversal, powerups, bomb actions, blast/audio effects, flooding, combat, results and real networking. The new profile scenario captures the actual in-game character from four directions using authoritative movement inputs, with a temporary close-up camera. Mesh checks guard against a flattened helmet/torso; visual inspection checked the curved crown/chin and preserved front face.

Early packaged runs exposed export and automation issues that are fixed: Godot returned success while reporting missing C# solution errors, and fast harness frames could race native window resize/focus. Packaging now rejects logged export errors and requires the game assembly. The harness waits for stable native window size/input transforms, re-requests actual OS activation when needed, and rechecks real focus before input trials. Synthetic mouse events do not move the OS pointer; it now warps the real pointer onto the control before clicking, preventing pending native motion from cancelling the test press. It does not bypass production focus gates or weaken gameplay assertions. Earlier failing evidence remains in ignored artifacts.

The bundle includes its .NET runtime and optional SDL/libusb controller libraries with notices. The controller tester detected the Switch 2 adapter from the bundle; this is not a new claim of observed physical buttons. The existing physical USB evidence remains in the iteration-five section. Native Bluetooth support is unchanged.

The README gallery was refreshed from that full app run. Exported quick-smoke screenshots go to writable `user://captures`, and capture errors fail the smoke run. A separate macOS Launch Services smoke verified gameplay (115 snapshots) and a saved screenshot with zero stderr output. The installed bundle is checked against the final export. Source and documentation are public; app/template binaries, private kernel source, downloaded references and raw test traces remain ignored. The app uses a local ad-hoc signature and is not notarized. Game Mode's plist opt-in is present, but OS activation and performance benefits remain unmeasured. [Packaging and reproduction](macos-app.md), [design research](iteration7-design.md).

## Iteration six baseline — 2026-10-20


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
