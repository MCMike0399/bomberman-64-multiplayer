# Agent playtest harness

This drives the real Godot composition root and authoritative simulation through normal product sessions. It never teleports entities or fabricates gameplay screenshots. All assertions are explicit and failing scenarios return nonzero.

On macOS, graphical runs default to a separate local macOS VM so you can keep
using your desktop. The game still has a real window, Metal renderer and native
focus inside the guest. `--headless` retains its existing simulation-only meaning.
See [isolated desktop setup](#isolated-macos-desktop) before the first graphical run.

```sh
python3 tools/playtest/run.py --build --start-server --scenario all
python3 tools/playtest/run.py --scenario ramp --headless
python3 tools/playtest/run.py --scenario script --script tools/playtest/temple-walk.json
```

The default Godot path is the installed macOS .NET editor; override with `--godot` or `GODOT`. Isolated network tests use their own guest server on a fresh loopback port. On an explicitly shared desktop, existing healthy kernel servers are reused; `--start-server` starts and later stops a server only when the runner created it. Network scenarios require a real available server and fail if absent.

## Isolated macOS desktop

Use an Apple Silicon Mac and a dedicated Tart macOS guest with automatic login,
the Tart guest agent and Python 3. The initial image download is large; it is
reused across runs. Install Tart using its [official instructions](https://tart.run/quick-start/),
then provision once (the implementation was developed against Tart 2.41.1):

```sh
tart clone ghcr.io/cirruslabs/macos-tahoe-base:latest blast-circuit-qa
tart set blast-circuit-qa --cpu 4 --memory 8192 --display 1600x1000
python3 tools/playtest/run.py --scenario menu
python3 tools/qa/run.py --profile graphical
```

Set `TART` to the executable if it is outside PATH; `BLAST_QA_VM` or `--vm`
selects a different **dedicated, stopped** guest. The runner refuses to take over
a running VM. It boots only while testing, stops its own VM process afterward,
and never falls back to the user's desktop when startup fails. One lock per VM
serializes guest ownership. Shared builds/exports still have one owner.
The runner also recognizes a task-local Tart bundle installed at
`~/.local/share/blast-circuit-qa/tart.app`; test-artifact cleanup can leave that
utility and the VM under `~/.tart` intact.

Without `--app`, the runner builds a current self-contained release export with
the existing package tool. With `--app`, it tests that exact signed bundle.
Temporary host bundles are removed after the run; file hashes remain in the
receipt. A read-only payload contains the app, guest launcher, optional script
and sanitized self-contained server binaries. Only the evidence directory is
writable from the guest. The checkout, personal home and credentials are not
mounted. The guest has host-only networking and uses an owned local server;
custom `--server` URLs require the shared mode.

Tart runs without a host window, clipboard sharing, host audio/microphone or USB
accessories. Its virtual speaker still lets Godot mix and capture game audio.
This is **not** Godot `--headless`: the unchanged scenarios exercise real guest
windows, native focus, ordinary menu hit testing, synthetic production input,
the renderer and authority. Recording still captures actual frames/audio and
is encoded and fully decoded on the host. Native raw traces retain guest paths;
`guest-summary.json` preserves the original summary before screenshot paths are
translated to local evidence files and checked by the existing validator.

Review `guest-environment.json`, `tested-app-files.json`, `runtime.log`, captures
and `host-observation.json` alongside the usual receipt. Host observation reads
foreground/pointer changes and VM resource use, without recording keys,
clipboard contents, window titles or pointer coordinates. It fails the run if
the VM itself becomes the host foreground application. Missing measurements are
reported as errors in that observation, rather than evidence of isolation.
The runner requires the actual Metal Forward+ startup banner in an isolated
graphical run. Runtime warnings and optional native-controller backend warnings
remain visible in the receipt.

Choose a guest OS compatible with the tested app's native libraries. The initial
macOS 26.6.2 guest rendered the full v0.24 suite successfully, but its loader
reported that the existing optional libusb binary uses `_pipe2` from macOS 27.
That guest cannot establish native USB-backend loading. Keep that verification
on a compatible native test machine or provision a matching guest; preserve the
warning instead of changing the app or treating synthetic pad checks as hardware
coverage. [Initial comparison and limits](../../docs/verification.md#isolated-agent-qa--2026-10-24).

| Evidence | What the isolated run establishes |
| --- | --- |
| Gameplay, network, menus, focus, synthetic pads | Same scenario catalog, assertions and real product paths inside the guest |
| Screenshots, movies, audio mix | Actual guest renderer and mixer output; inspect it normally |
| Native Mac GPU throughput/display behavior | Retain a separate bare-metal run; virtual GPU results do not certify host drivers, HDR or Game Mode |
| USB/Bluetooth buttons, rumble, physical audio | Retain an explicit physical test on a machine with that device; VM tests do not certify host hardware |

To deliberately use a test machine's current desktop, pass `--desktop shared`
to either runner. This still activates windows and moves that machine's pointer.
Use it only when that desktop is available for testing. Physical
`controller-observe` and `controller-inventory` require this explicit mode. The dedicated graphical CI
worker uses it as well; provisioning a local VM does not enable a CI runner.

Each run gets a unique artifact directory under `artifacts/playtest/` with `runtime.log`, `runner-summary.json`, `summary.json`, `trace.jsonl` and graphical screenshots. Build/startup logs are included when those actions are requested. The runner records timeout, missing summary, nonzero exit and engine/script/unhandled errors in runtime.log as failures, even when in-engine assertions passed. Graphical runs must produce screenshots. Graphical runs capture actual viewport pixels after a bounded `RenderingServer.FramePostDraw` fence. If a background macOS window stops producing draw callbacks for 500 ms, capture explicitly redraws and synchronizes the real renderer on the main thread before readback; every fallback is recorded in the trace and summary. The network scenario asserts visible host/peer labels and countdown as well as server state. `--headless` intentionally disables screenshots and reports `screenshotMode: disabled-headless`, with every omitted capture recorded in the trace.

## Scenario catalog and extension

### Native GPU performance

Use the development Mac's physical Apple Silicon GPU for GPU measurements when
needed; the isolated VM remains the default for ordinary correctness tests.
`graphics-benchmark` explicitly uses the shared desktop, so announce the short
windowed run first and keep the game foreground. Run one benchmark at a time,
with the QA VM and other owned GPU jobs stopped.

```sh
python3 tools/playtest/run.py --scenario graphics-benchmark --desktop shared --build
python3 tools/playtest/run.py --scenario graphics-benchmark --desktop shared --app /path/to/BlastCircuit.app
```

This path removes the ordinary `--fixed-fps 30` and 30 FPS cap, disables VSync,
and measures real elapsed time in a 1920×1200 window. It warms each scene for
three seconds, samples each menu for three seconds and an actual eight-player
Pyramid battle for ten seconds, for Low/Medium/High. Normal input selects presets;
real bots place bombs and produce explosions. It rejects focus interruptions,
missing combat evidence, virtual/software GPUs, `--headless` and `--record`.
It is deliberately excluded from `all` and never silently replaces a VM test.

`graphics-benchmark.json` records CPU/GPU/API/OS, actual window size,
sample counts, frame median/p95, FPS, root render CPU/GPU time and draw calls.
`runner-summary.json` adds read-only macOS power/thermal-state observations before
and after the run (`pmset -g batt/custom/therm`); it never changes power settings.
The harness validates complete scene/preset coverage and finite timing evidence.
Unsupported GPU counters are `null`, not zero-cost frames. Root GPU time covers
the arena/UI; it excludes independent menu render surfaces, so menu frame time
is the useful whole-frame measurement. Timed intervals exclude screenshots.
Diagnostics still add overhead. Use the same Release export, resolution, power
state and workload for comparisons; a short run is not a sustained thermal test.
With `--app`, `tested-app-files.json` identifies the exact tested bundle. The
engine frame cap is removed and VSync requested off, but platform presentation
may still pace frames near the display refresh rate (also recorded). GPU timing
availability is checked on native Metal too; it is not guaranteed by using a GPU.

On macOS, add `--metal-hud` to obtain Apple frame GPU timings even when Godot's
viewport timer is unavailable:

```sh
python3 tools/playtest/run.py --scenario graphics-benchmark --desktop shared --metal-hud --build
```

The game alone receives `MTL_HUD_ENABLED=1`, `MTL_HUD_LOG_ENABLED=1` and zero HUD
opacity. An owned `/usr/bin/log` reader filters by that game's PID. No global
defaults or privacy settings change. `metal-gpu-summary.json` contains positive
GPU median/p95/mean durations in milliseconds. These are command-buffer durations,
including possible gaps; they are not per-viewport or encoder-active timings.
See [Apple's metric definition](https://developer.apple.com/documentation/xcode/understanding-metal-performance-hud-metrics)
and [logging format](https://developer.apple.com/documentation/xcode/monitoring-your-metal-apps-graphics-performance).

Some macOS versions redact the dynamic timing array in unified logs while the
game's console contains it in full. The parser joins by owned PID, first-present
frame ID and matching public header, then deduplicates repeated system records.
It rejects conflicting/incomplete/nonfinite batches. It uses complete batches
inside UTC sample windows, inferring batch start from present intervals and
excluding 250 ms boundary guards. HUD IDs count drawable presents; they are not
Godot process frames. Raw console/unified logs remain available for inspection.

### First-use pipeline regression

```sh
python3 tools/playtest/run.py --scenario first-use-rendering
python3 tools/playtest/run.py --scenario first-use-rendering --cold-effects
```

The second command is an expected failure: it disables startup/live preparation
only in this QA scenario. The normal case verifies first/repeated real explosions
at all presets after garbage collection, with zero new synchronous Mesh/Surface/Draw
pipelines. It records background specialization separately and confirms scratch
viewports were removed. It belongs first in `all` so earlier effect tests cannot
accidentally warm the resources. Captures are outside timed sampling; these
fixed-step intervals diagnose hitches, not native GPU speed. Rebuild exported
apps after changing this test or its preparation behavior.

### Registering scenarios

`assets/testing/scenarios.json` is the shared source of truth for Python tooling and native C# dispatch. `python3 tools/playtest/run.py --list` prints the full catalog. `all` is the windowed graphical regression (1200-second limit, including the Up & Down traversal/flood journey); `core-headless` deliberately omits native UI/window journeys; `package-smoke` checks bundled assets, movement and charge/throw behavior. Manual controller observations remain separate.

For a new scenario:

1. Add a partial `PlaytestDriver` file in `scripts/testing/journeys/<area>/` with a private parameterless `async Task` method. Exercise ordinary inputs/product sessions and assert observable outcomes using `Check`; do not teleport actors or substitute fabricated state.
2. Register its ID, method name, domains, headless capability, manual flag and timeout in the catalog. Add the ID to relevant suite `steps`. Graphical-only children cannot belong to a headless suite.
3. Cover the actual failure first when fixing a regression. Keep rendering/physical-device assertions separate from simulation-only claims. For assets, extend [AssetAudit](../../docs/asset-audit.md) using actual production resources.
4. Run the focused scenario, then the relevant canonical QA profile. An exported app must be rebuilt after catalog changes: its bundled SHA-256 must match the runner's catalog.

Reports require the exact completed scenario list, positive assertion counts, matching catalog hash and valid capture files. Native captures inspect pixel diversity/contrast; the runner validates PNG integrity and confines paths to the run directory. These catch blank/missing evidence, not subjective artistic quality. Each run emits `junit.xml`, `triage.md` and periodic `progress.json`; crashes/timeouts retain the last active scenario. Graphical runs take an exclusive desktop lock. Dedicated process groups ensure cleanup stops only owned children. CI uses [the canonical QA profiles](../../docs/ci.md), including a fresh ephemeral server port rather than reusing a developer server.

New focused scenarios include:

- `asset-audit`: actual fonts, glyphs, model/material geometry, UI bounds and a graphical inspection gallery; headless mode explicitly omits GPU evidence.
- `walk-feel`: real courtyard travel, reversal and stopping with measured boot contacts and a temporary following inspection camera. Suitable for normal-speed recording.
- `charge-aim`: ordinary carry/pump/throw inputs, preview ownership, projected landing, actual hand-to-release continuity and water-endpoint behavior.

Earlier built-in scenarios:

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

Custom scripts are JSON objects containing a `steps` array. Supported `op` values: `navigate` (screen), `click` (visible button label substring; focuses and injects Enter), `mouse-click` (label; documented canvas-to-screen conversion), `resize` (width,height), `back`, `start-local` (stage,bots,humans,practice), `frames` (count), `input` (seat,x,z,a,b,r,aim), `move-to` (x,z,maxFrames), `capture` (label), `assert-screen` (screen), `assert-state` (dot-separated path plus equals or gte). A held input persists until another input replaces it. `move-to` uses observed positions and injected movement, bounded by maxFrames; it does not bypass collision. Avoid paths through blocking terrain. See temple-walk.json for a complete agent-editable example.

For failure triage, inspect runner-summary first, then summary's failed check, the final state and the corresponding trace frame. Check runtime.log for engine errors. A visual-only issue should be assessed in the paired screenshots; successful state assertions do not establish visual fidelity.

Direct engine invocation is also available:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . --windowed --max-fps 30 --fixed-fps 30 -- --display-windowed --playtest=ramp --playtest-output=/tmp/temple-evidence
```

Synthetic mouse coordinates follow [Godot’s official canvas/screen transform guide](https://docs.godotengine.org/en/4.7/tutorials/2d/2d_transforms.html): viewport screen transform × control global transform with canvas × local center. This matters when the 1440×900 logical UI is stretched into a smaller window.

QA and debug launches are **windowed by default**. Runner `--fullscreen` explicitly opts into fullscreen; `--scenario fullscreen` also requests it. Windowed runs pass engine `--windowed` and product `--display-windowed`, including embedded runtimes where consumed engine flags are not visible to C#. Explicit fullscreen menu/framing checks temporarily toggle into a window for aspect-ratio checks, then restore fullscreen. Actual window mode must remain stable for 500 ms. Headless checks never claim to verify an OS window.

Godot native popup menus have their own window ID. Synthetic role-selection keys must use `Input.ParseInputEvent` with `InputEventKey.WindowId = popup.GetWindowId()`. Sending keys to the main window or calling the popup viewport directly can leave the selected item unchanged. The roster scenario opens the actual role control, navigates with arrow keys in that popup window, then confirms with Enter.

Controller event trials explicitly focus the test window in the selected desktop and wait for a stable application-focus state. Do not launch another graphical process into that test desktop during those trials. Focus-loss suppression is a product feature; bypassing it would hide input-lifecycle regressions. The isolated guest owns its server for the entire run.

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

## Record and inspect gameplay

For documentation screenshots, `--scenario readme-gallery --timeout 360` captures
the current menus, tutorial, every arena and real explosions at High,
3840×2400 and Midday lighting. Run it with `--desktop shared --build` for
the native Mac Metal renderer. It is graphical-only, uses the
ordinary battle camera and leaves ordinary player preferences alone. See the
[gallery provenance and selection map](../../docs/screenshots.md). It is an
explicit capture journey rather than an addition to the full regression suite.

```sh
python3 tools/playtest/run.py --app "artifacts/exports/Blast Circuit.app" --scenario kick-physics --record --ffmpeg /path/to/ffmpeg
```

`--record` uses [Godot Movie Maker](https://docs.godotengine.org/en/stable/tutorials/animation/creating_movies.html) to capture actual rendered frames and game audio at 30 FPS. It saves the original AVI, encodes a 1× H.264/AAC MP4, decodes every frame to validate the export, and records frame count/duration/source in `recording.json`. FFmpeg is an optional external dependency; pass `--ffmpeg` or set `FFMPEG`. An export/decode failure fails the run. Headless recording is rejected. Fixed-step capture demonstrates simulation motion, not real-time GPU throughput.

The focused `kick-physics` scenario temporarily uses an inspection camera on the actual actors; `all` keeps the battle camera. Both assert planted anticipation, delayed strike sound, short measured victim displacement/height, corner fuses and a real courtyard drop followed by resumed rolling. No entity position is edited. `traversal` also covers dry shoreline grazing, falling above water, and elimination at the actual rendered water height. Inspect the MP4 and full-size captures alongside `kick-impact-measurements` in the trace; passing geometry assertions alone do not prove good animation.

## Iteration-twelve placement and sizing

`--scenario placement` verifies two separate concerns: an A press with zero active bombs at the previously rejected fractional flat-ground position, and near-foot placement/departure/deliberate second-A kick. It uses actual keyboard and synthetic Switch native-bridge events; positioning uses ordinary movement and releases the input override before button trials. It also records a stationary three-bomb trail, placement sound/hand motion and explained capacity rejection. `--record` produces actual footage with a temporary inspection camera in this focused scenario; `all` retains the normal battle camera.

`powerup-collect` verifies latest-pickup exclusive red/remote modes and captures an actual blue remote bomb. `pump-growth` checks distinct increases plus the restrained target cap. `framing` verifies full walkable terrain, 2.1-unit player headroom and actual pillar crowns at both test aspect ratios. Legacy kick fixtures now use a deliberate second A or a true leave-and-return approach, rather than relying on accidental kicks during placement departure.

## Iteration-thirteen AI and keyboard navigation

- `wasd-menu`: actual W/S main-menu focus, A/D arena and difficulty dials, W/S roster popup navigation, arrow compatibility, ordinary text entry, and A/D Results actions. It uses real window focus and native Godot action events.
- `ai-play`: three actual four-player temple matches, one for each difficulty, with a stationary human observer. It records all CPU motion, bomb/explosion activity and bounded eligible idle windows, and captures opening/midfight/outcome views. `--record` produces inspectable gameplay and game audio.

Both are included in `all`. For long deterministic AI sweeps outside Godot, use `dotnet run -c Release --project tests/Bomber.Ai.Tests -- --soak --output=artifacts/ai/soak.json`; see that suite’s README for safe-exit liveness and fairness criteria. Native popup tests first navigate to a known endpoint through real keys: after earlier mouse tests the popup may initially hover its last item, where another Down/S cannot change focus.

### Native focus interruptions

Synthetic controller coverage still uses the production focus guard. Switching foreground apps **in the test desktop** invalidates a held-input interval even if raw stick values remain set. Your host desktop is separate during isolated runs. The harness records actual window/application focus loss and restarts that complete controller or aiming-input scenario from released inputs and a fresh session, at most three times. Invalid attempt assertions are excluded from the passing receipt and retained in the trace; functional failures are never retried. Deliberate focus-loss/rearm tests remain in place; exhausted focus retries fail explicitly. [Historical shared-desktop diagnosis](../../docs/history/iteration15-controller-focus.md).

Mouse click witnesses use the documented screen transform and feed ordinary press/release events as one paired burst before the next rendered frame. Native control hit testing still handles the click. This avoids leaving a synthetic press held across unrelated native pointer-motion events after window resizing; actual screen-transition assertions remain required.

## Precision throw scenarios

`precision-aim` uses ordinary stock-temple movement and carry inputs to verify planted aiming, range/turn changes, charge-independent endpoints, retained intent while repositioning, and actual landing against the rendered guide. It runs headlessly for state validation; graphical mode adds real near/far/pumped/flight captures. `aim-inputs` separately verifies K/J/L (Shift remains an aim alias) and Xbox/Switch/PlayStation canonical carry/primary/right-trigger inputs through the production adapter. Native inputs require actual application focus and are explicitly synthetic, not physical-device certification. Both belong to `all`; only `precision-aim` belongs to headless/package suites. Custom scripts accept `"aim": true` on input steps, and traces retain that intent.


`walk-feel` records ordinary movement from side and three-quarter inspection cameras, followed by the normal battle camera. Its actor transforms verify weight transfer and independent shoulder follow-through in addition to fixed leg length, continuous displacement, turns and settling. `movement` samples the whole rig at 30/60/120 FPS; these bounds detect lost motion, not a human rating of charm.
