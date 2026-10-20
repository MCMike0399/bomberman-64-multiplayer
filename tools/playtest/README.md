# Agent playtest harness

This drives the real Godot composition root and authoritative simulation through normal product sessions. It never teleports entities or fabricates gameplay screenshots. All assertions are explicit and failing scenarios return nonzero.

```sh
python3 tools/playtest/run.py --build --start-server --scenario all
python3 tools/playtest/run.py --scenario ramp --headless
python3 tools/playtest/run.py --scenario script --script tools/playtest/temple-walk.json
```

The default Godot path is the installed macOS .NET editor; override with `--godot` or `GODOT`. Existing healthy kernel servers are reused. `--start-server` starts and later stops a server only when the runner created it. Network scenarios require a real available server and fail if absent.

Each run gets a unique artifact directory under `artifacts/playtest/` with `runtime.log`, `runner-summary.json`, `summary.json`, `trace.jsonl` and graphical screenshots. Build/startup logs are included when those actions are requested. The runner records timeout, missing summary, nonzero exit and engine/script/unhandled errors in runtime.log as failures, even when in-engine assertions passed. Graphical runs must produce screenshots. Graphical runs capture actual viewport pixels after a bounded `RenderingServer.FramePostDraw` fence. If a background macOS window stops producing draw callbacks for 500 ms, capture explicitly redraws and synchronizes the real renderer on the main thread before readback; every fallback is recorded in the trace and summary. The network scenario asserts visible host/peer labels and countdown as well as server state. `--headless` intentionally disables screenshots and reports `screenshotMode: disabled-headless`, with every omitted capture recorded in the trace.

Built-in scenarios:

- `menu`: actual focused keyboard activation through local/online/practice buttons and back, plus transformed mouse clicks at 1280×800 and 1024×640, with screenshots.
- `local-actions`: one-human practice, place/lift/pump/throw, checking authoritative bomb/carrier states and leaving the session.
- `ramp`: actual walking from sunken-temple spawn through three stair flights to tier 4 and back to tier 1, checking arrival and elevation.
- `network`: Godot product NetworkSession hosts a real kernel lobby, second product client joins by code, and observes the injected engine player's movement remotely. It activates the actual online suit button and verifies that both chosen appearances reach authoritative snapshots.
- `combat`: seed64 temple with three CPU rivals, observes actual bomb/explosion/item IDs and a completed round or match, then captures the result. This continues long enough to exercise post-explosion item rendering.
- `results`: a real bomb eliminates the stationary human in a one-CPU duel, asserts the CPU winning point and actual Results screen, activates Play Again through keyboard input and leaves the replay.
- `powerup-collect`: walks through the visible practice cache, verifies all five upgrade types, HUD/state counters, model removal and remote detonation of an upgraded bomb.
- `pump-growth`: creates a carried bomb with lift+bomb, accepts three distinct presses, checks three rendered growth stages and body animations, then throws it.
- `hurry-water`: two real local humans, one walks to the crown while the other stays on a lower terrace; observes actual timed flooding, rising rendered water, lower player drowning and higher player survival.
- `roster`: real local-mode navigation, mixed human/CPU slots with gaps, actual role/appearance controls, moving shared-model previews and matching packed appearances in the authority.
- `framing`: projects all walkable cells plus character headroom into the camera at 1280×720 and 1280×800 (plus native fullscreen when explicitly requested), checks HUD-safe margins across all three stages and captures each result.
- `fullscreen`: a separate graphical-only run with the product fullscreen default; verifies F11 and Alt+Enter both switch and restore actual window mode. A headless fullscreen run fails explicitly because it cannot verify an OS window.
- `controllers`: production Godot keyboard and canonical pad events drive real sessions. Covers equal travel, compact/legacy keyboard aliases, D-pad, shoulder actions, sub-frame taps, held-button behavior, neutral arming, focus and stable hotplug seats. Xbox/Switch/DualSense profiles here are explicitly synthetic; no game-root input injection is used.
- `controller-inventory`: records detected engine/native controllers without serial numbers and captures the live tester. Detection alone does not verify physical buttons.
- `controller-observe`: a 60-second graphical session for human input. Records actual observed axes/buttons per device, checkpoints each newly observed action, and keeps physical evidence separate from synthetic tests. No injected events. An empty actions list means unverified, even if the observation session itself completes.
- `presentation`: real small/pumped blasts, size-specific geometry/radius checks, active versus vapor phases, effect cleanup, accepted kick/lift/throw/death/water cues and eight-player HUD. A graphical run attaches a temporary output-bus capture, validates non-silent unclipped mixing and writes `actions-mixed.wav`; headless runs explicitly do not measure mixer output.
- `all`: the full regression sequence, windowed by default. An explicit `--fullscreen` run also checks native fullscreen framing and restores fullscreen after aspect checks.

Local play uses fixed 30 Hz engine time and deterministic default seed 64. Network authority runs on real server time; kernel lobby countdown and message arrival remain asynchronous. The runner caps wall time and the in-engine driver bounds state waits and walking frames.

Custom scripts are JSON objects containing a `steps` array. Supported `op` values: `navigate` (screen), `click` (visible button label substring; focuses and injects Enter), `mouse-click` (label; documented canvas-to-screen conversion), `resize` (width,height), `back`, `start-local` (stage,bots,humans,practice), `frames` (count), `input` (seat,x,z,a,b,r), `move-to` (x,z,maxFrames), `capture` (label), `assert-screen` (screen), `assert-state` (dot-separated path plus equals or gte). A held input persists until another input replaces it. `move-to` uses observed positions and injected movement, bounded by maxFrames; it does not bypass collision. Avoid paths through blocking terrain. See temple-walk.json for a complete agent-editable example.

For failure triage, inspect runner-summary first, then summary's failed check, the final state and the corresponding trace frame. Check runtime.log for engine errors. A visual-only issue should be assessed in the paired screenshots; successful state assertions do not establish visual fidelity.

Direct engine invocation is also available:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . --windowed --max-fps 30 --fixed-fps 30 -- --display-windowed --playtest=ramp --playtest-output=/tmp/temple-evidence
```

Synthetic mouse coordinates follow [Godot’s official canvas/screen transform guide](https://docs.godotengine.org/en/4.7/tutorials/2d/2d_transforms.html): viewport screen transform × control global transform with canvas × local center. This matters when the 1440×900 logical UI is stretched into a smaller window.

QA and debug launches are **windowed by default**. Runner `--fullscreen` explicitly opts into fullscreen; `--scenario fullscreen` also requests it. Windowed runs pass engine `--windowed` and product `--display-windowed`, including embedded runtimes where consumed engine flags are not visible to C#. Explicit fullscreen menu/framing checks temporarily toggle into a window for aspect-ratio checks, then restore fullscreen. Actual window mode must remain stable for 500 ms. Headless checks never claim to verify an OS window.

Godot native popup menus have their own window ID. Synthetic role-selection keys must use `Input.ParseInputEvent` with `InputEventKey.WindowId = popup.GetWindowId()`. Sending keys to the main window or calling the popup viewport directly can leave the selected item unchanged. The roster scenario opens the actual role control, navigates with arrow keys in that popup window, then confirms with Enter.

Controller event trials explicitly focus the test window and wait for a stable application-focus state. Do not launch a second Godot editor/export process during those trials. Focus-loss suppression is a product feature; bypassing it would hide input-lifecycle regressions. Keep a persistent server running when overlapping network checks, since an independently owned harness server is stopped when that harness exits.

## Test the standalone macOS app

```bash
python3 tools/playtest/run.py --app "artifacts/exports/Blast Circuit.app" --scenario all --start-server
python3 tools/playtest/run.py --app "artifacts/exports/Blast Circuit.app" --scenario fullscreen
```

`--app` reads the executable name from the bundle plist, omits the source-project `--path`, and launches with its working directory in the evidence folder. This exercises the shipped pack, managed runtime and bundled controller libraries. Windowed remains the default. The roster assertions inspect the actual visible shared 3D models, including their appearance and changing walk pose.

`--scenario model-profile` turns the actual practice player through front/right/back/left using normal movement inputs, checks helmet/torso mesh depth, and captures a temporary inspection camera before restoring the arena view. Native resize tests wait for both window size and input transform to settle; controller trials verify actual window/application focus rather than forcing focus flags.

Mouse automation moves the OS pointer to the target before injecting press/release events. `Input.ParseInputEvent` alone does not move it, so native motion arriving between those events can otherwise cancel a click. Focus recovery uses the real platform activation API, and still fails if the window/application cannot acquire focus.

## Iteration-eight scenarios

- `setup`: actual stage/rule dials, native keyboard navigation, private-room settings persistence and practice launch.
- `controller-popups`: gradual Switch bridge input selects roles in the configured embedded Godot popup and adjusts setup rules.
- `grapple`: ordinary three-player training sessions collect a remote pickup, kick into a victim, recover, grab/follow/escape and throw/land. No fixture state mutation.
- `model-profile`: now includes a three-quarter view and actual cream face mesh volume/winding checks.

`controller-observe` also saves `physical-axis-trace.jsonl` with wall-clock timestamps and all axis samples, including center/release. It records no serial number. Synthetic controller tests use the same native publication helper for Switch events, and separately exercise retained match bindings in Results/Lobby.

## Iteration-nine scenarios

- `movement`: actual rendered per-frame displacement must remain steady across authority snapshots, then settle on release.
- `bomb-handling`: tap/hold primary, persistent held state, pumping, carry-button throw, normal fuse and remote-command behavior through stock-map inputs.
- `traversal`: gravity drops, air steering, blocked upward cliffs, real ghost creation/latch, retained victim control and visible riding/expiry.

`controller-observe` now allows 120 seconds and can end early with Menu / + / Options. Physical reports remain separate from injected tests. The Nintendo controller cases use printed A/B/X instead of assuming the same physical action positions as Xbox.

## Iteration-ten scenarios

- `kick-physics`: real stock-temple contact kick, launch hop/travel, victim flight/landing, active remote waiting/command, and a normal diagonal corner stop followed by a timed explosion and safe retreat.
- `body-contacts`: ordinary three-human paths test head-on authority/rendered separation, capped idle yielding and tangential escape.
- `movement`: also feeds the production interpolation helper controlled uneven arrival times at 30/60/120 FPS, including starvation, stale packets and discontinuities; then verifies real arena run/stop cadence.
- `grapple`: waits for actual kick flight to land and walks to the displaced victim before grabbing; impact captures are uniquely numbered.

These are included in `all`. Run only one graphical Godot instance at a time. For changed authority code, rebuild/restart your project-owned local server before networking tests; an already reachable server may otherwise run an older assembly.
