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
- `network`: Godot product NetworkSession hosts a real kernel lobby, second product client joins by code, and observes the injected engine player's movement remotely.
- `combat`: seed64 temple with three CPU rivals, observes actual bomb/explosion/item IDs and a completed round or match, then captures the result. This continues long enough to exercise post-explosion item rendering.
- `results`: a real bomb eliminates the stationary human in a one-CPU duel, asserts the CPU winning point and actual Results screen, activates Play Again through keyboard input and leaves the replay.
- `powerup-collect`: walks through the visible practice cache, verifies all five upgrade types, HUD/state counters, model removal and remote detonation of an upgraded bomb.
- `pump-growth`: creates a carried bomb with lift+bomb, accepts three distinct presses, checks three rendered growth stages and body animations, then throws it.
- `hurry-water`: two real local humans, one walks to the crown while the other stays on a lower terrace; observes actual timed flooding, rising rendered water, lower player drowning and higher player survival.
- `all`: all nine, in order.

Local play uses fixed 30 Hz engine time and deterministic default seed 64. Network authority runs on real server time; kernel lobby countdown and message arrival remain asynchronous. The runner caps wall time and the in-engine driver bounds state waits and walking frames.

Custom scripts are JSON objects containing a `steps` array. Supported `op` values: `navigate` (screen), `click` (visible button label substring; focuses and injects Enter), `mouse-click` (label; documented canvas-to-screen conversion), `resize` (width,height), `back`, `start-local` (stage,bots,humans,practice), `frames` (count), `input` (seat,x,z,a,b,r), `move-to` (x,z,maxFrames), `capture` (label), `assert-screen` (screen), `assert-state` (dot-separated path plus equals or gte). A held input persists until another input replaces it. `move-to` uses observed positions and injected movement, bounded by maxFrames; it does not bypass collision. Avoid paths through blocking terrain. See temple-walk.json for a complete agent-editable example.

For failure triage, inspect runner-summary first, then summary's failed check, the final state and the corresponding trace frame. Check runtime.log for engine errors. A visual-only issue should be assessed in the paired screenshots; successful state assertions do not establish visual fidelity.

Direct engine invocation is also available:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . --max-fps 30 --fixed-fps 30 -- --playtest=ramp --playtest-output=/tmp/temple-evidence
```

Synthetic mouse coordinates follow [Godot’s official canvas/screen transform guide](https://docs.godotengine.org/en/4.7/tutorials/2d/2d_transforms.html): viewport screen transform × control global transform with canvas × local center. This matters when the 1440×900 logical UI is stretched into a smaller window.
