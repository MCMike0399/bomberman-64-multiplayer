# Testing, diagnosis and delivery

Read this when selecting checks, reproducing a bug, extending gameplay tests,
recording evidence, or delivering an app/server. Commands run from the repo root.
The tools and their `--help` are authoritative for current flags; detailed
references are [playtests](../../tools/playtest/README.md), [CI](../ci.md),
[macOS packaging](../macos-app.md), and [verification](../verification.md).

## Select the branch before running work

| Change | Tight loop | Completion gate |
| --- | --- | --- |
| Docs only | Read changed instructions; verify links, paths and command flags | Diff/whitespace/link review; no packaging, GPU, install or service restart |
| Python QA/CI tooling | Relevant `unittest` module plus malformed/failure receipts | `public`; workflow edits also `actionlint .github/workflows/ci.yml` |
| Simulation/rules | Corresponding executable managed suite with a red regression | `managed`, relevant native journey, `headless` |
| Client/session/protocol | Client checks and real HTTP/WebSocket repro | `managed` plus `network`; restart only an owned stale server |
| AI | Focused deterministic regression and matched-seed metrics | Managed AI checks/36-scenario soak plus `ai-play` where visible behavior matters |
| Input/menu/focus | `controllers`, `aim-inputs`, `wasd-navigation`, or relevant journey | Real windowed graphical checks; physical observation if claiming device support |
| Rendering/rig/audio | Relevant native scenario and actual images/audio/video | Graphical regression; shipped-app checks for delivery |
| Packaging/runtime/native backend | Bundle and staged-runtime inspection | `package`, exported-app journey, exact installed-copy verification |

Run only the checks required by the active branch. Once those pass, repeat or
broaden them when a new change, failure or unresolved risk justifies it.
Executable managed suites use `dotnet run`, not test-discovery assumptions:

```sh
dotnet run --project tests/Bomber.Game.Tests
dotnet run --project tests/Bomber.Client.Tests
dotnet run --project tests/Bomber.Ai.Tests
python3 -m unittest discover -s tools/qa -p 'test_*.py'
python3 tools/qa/run.py --profile public
python3 tools/qa/run.py --profile managed
python3 tools/qa/run.py --profile headless
```

The `managed` profile includes audio, AI soak, Release server staging and a real
network smoke against that exact staged assembly. It emits JSON/JUnit receipts.
Exit zero alone is insufficient: profiles validate positive completion footers,
assertion counts and complete receipts. Use a fresh `--output` directory each run.

## Reproduce, locate the boundary, fix, verify

1. Write the concrete trigger and expected observable result. Preserve the
   failing receipt and revision before changing code. Prefer one failing journey
   over a broad run whose first failure hides the relevant behavior.
2. Reproduce through the appropriate boundary: managed authority, session/input,
   renderer, or OS/device. Establish a red assertion that fails for the actual
   defect; a test that already passes does not reproduce it.
3. Inspect `runner-summary.json`, then `summary.json`'s failed check, final state
   and matching `trace.jsonl` frame. Read `runtime.log` for engine/script errors;
   `triage.md` and `progress.json` locate timeouts and the last active scenario.
4. Compare authority state with rendered state at the same frame. Correct
   snapshots with a wrong mesh/reticle implicate presentation; missing accepted
   actions require tracing input/focus/session/authority before adjusting art.
5. Make the smallest fix at the owning boundary. Rerun the red case, then the
   branch's completion gate. Retain failures and explain material evidence gaps.

Before source native tests, build changed C# code with
`dotnet build Bomberman64Multiplayer.csproj --nologo -m:1`, or add `--build` to
the first playtest invocation. Otherwise a focused run can load stale DLLs.
Build managed suites once before repeated `--no-build` checks. Focused runs:

```sh
python3 tools/playtest/run.py --list
python3 tools/playtest/run.py --scenario ramp --headless
python3 tools/playtest/run.py --scenario reticle
python3 tools/playtest/run.py --scenario walk-feel
python3 tools/playtest/run.py --scenario script --script tools/playtest/temple-walk.json
python3 tools/qa/run.py --profile graphical
```

Headless mode intentionally omits screenshots, native window mode and GPU
claims. Local simulation is fixed 30 Hz; network countdown/message arrival uses
real server time. Bound asynchronous waits by wall time, not a guessed number
of fast headless frames. Preserve product focus suppression while diagnosing.

## Own the build, desktop and server

Assign one owner to shared MSBuild/Godot import/export outputs. Managed build
commands use `-m:1`; simultaneous builds in the same checkout can invalidate
assemblies, imports and evidence. Parallel read-only inspection is safe; use
isolated checkouts/output paths for independent mutations and builds.

Assign one owner to the graphical desktop. Run one graphical Godot instance at
a time; the harness takes an exclusive desktop lock. Export/editor launches can
steal focus, so schedule them outside input trials. Keep the actual game window
foreground. Native focus loss can invalidate held input while raw axes remain
correct. The harness retries only proven focus interruptions, from released
inputs and a fresh session, at most three attempts; functional failures fail.

Native popups have distinct window IDs; use the existing popup helpers and
canvas-to-screen mouse transforms. Preserve ordinary input/hit testing rather
than calling menu callbacks or forcing focus flags to obtain a green receipt.

Canonical QA starts an owned server on a fresh Kestrel-assigned loopback port
and stops only its process group. Direct playtest `--start-server` can reuse a
healthy server and stops only one it created. Verify the assembly/revision of a
reused server after authority changes. Readiness is `/realtime/v1/games`, not
`/health`. A stale developer server may still respond successfully.

Before touching an existing listener, inspect its PID/command/owner and the
project launch scripts. Restart only a service owned by this task and needed
for the change; do not broadly kill Godot/dotnet or alter external hosts.
Host/network/provider changes require the applicable canonical operational
reference. Server artifact creation is separate from deploying a service.

For explicitly authorized remote party hosting, follow [the party guide](../online-party.md).
The Compose context comes only from a verified portable server ZIP. Keep deployment
and relay ownership separate from other host services. Verify real clients through
the actual relay address, then the installed app's `network` journey. A local probe
of the LAN address does not establish reachability from a friend's Mac. Use the
helper's launchd ownership for a relay that survives the tool/terminal launcher;
an orphaned `ssh -f` process may be cleaned up by the execution environment.

## Extend the harness with meaningful gameplay

`assets/testing/scenarios.json` is the common Python/C# catalog. Add a private
parameterless `async Task` on the `PlaytestDriver` partial class in a new
`scripts/Testing/*Playtest.cs` file,
then register method, domains, headless capability, timeout and suite membership.
Follow existing partial files under `scripts/Testing` for actual naming/layout.
Graphical-only children cannot enter a headless suite. Check `--list` afterward.

Drive ordinary product sessions and inputs. Use bounded walking helpers,
plant/retreat/fuse cycles, actual pickups, accepted actions and real round
transitions. Assert consequences: changed authority inventory, model removal,
observed impact/landing, Results entry and overlay cleanup. Initial screen
startup or a screenshot count alone does not establish gameplay correctness.

Keep targeted helper tests for interpolation/geometry/input transforms, paired
with an integrated journey through production behavior. Gameplay evidence must
not teleport actors, edit inventories/winners or fabricate visible states.
Inspection cameras may observe actual actors, then restore the normal camera.
Capture before/after the failure boundary with unique labels and trace data.

New receipts must retain exact completed leaf order, positive assertions,
catalog SHA-256, valid in-directory capture files and zero runtime errors.
Timeouts, missing summaries, engine errors and malformed evidence fail the run.
Rebuild the exported app after any scenario/catalog change: an old bundled
catalog SHA cannot validate a new runner, even if product code is unchanged.

Synthetic keyboard/pad/native-bridge scenarios prove those injected paths.
`controller-inventory` proves detection only. Use `controller-observe` for human
physical input; an empty observed-actions list leaves buttons unverified.
Bundling SDL/libusb does not by itself prove a fresh physical-controller test.

## Record evidence and state its limits

```sh
python3 tools/playtest/run.py --scenario walk-feel --record --ffmpeg /path/to/ffmpeg
python3 tools/playtest/run.py --app "$HOME/Applications/Blast Circuit.app" --scenario reticle
```

Recording uses actual Godot frames/audio, retains AVI and validates a 1× MP4 by
decoding every frame. Inspect the full clip and full-size captures; contrast
checks reject blank images but cannot establish artistic quality. Recordings
use fixed-step Movie Maker, so they do not measure real-time GPU throughput.
For published cuts, preserve source run, frame intervals, normal speed and
camera context. Distinguish inspection-camera footage from normal gameplay.
Measured animation bounds and passing checks do not imply user approval or fun.
Actual fullscreen launch likewise does not establish OS Game Mode activation.

## Deliver and keep one installed app

Use the package branch only when shipping changed runtime/assets, not for docs.
Check toolchain/backend requirements in [macOS packaging](../macos-app.md).

```sh
python3 tools/qa/run.py --profile package
python3 tools/qa/run.py --profile graphical --app "artifacts/exports/Blast Circuit.app"
codesign --verify --deep --strict --verbose=2 "artifacts/exports/Blast Circuit.app"
```

Finish graphical evidence/recordings on the final export before deleting it.
If any runtime/catalog/resource changes afterward, rebuild and repeat affected
checks; attach evidence to the actual delivered content rather than an older app.
Inspect `Contents/Info.plist`: identity must be
`io.github.mcmike0399.blastcircuit`; version/build must match the intended
delivery. Record source revision and per-relative-file SHA-256 inventory
(plus symlink targets) for the validated export.

Install by copying the whole bundle with `ditto` into
`~/Applications/Blast Circuit.app`. Close the existing app first. If replacing
an existing destination, move it to a uniquely named temporary backup before
copying; merging a new bundle into an old directory can retain obsolete files.
Compare the installed relative-file/hash/symlink inventory to the validated
export, then repeat strict signature/version checks. Keep the backup until this
verification and the following LaunchServices smoke pass.

```sh
ditto "artifacts/exports/Blast Circuit.app" "$HOME/Applications/Blast Circuit.app"
/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$HOME/Applications/Blast Circuit.app/Contents/Info.plist"
codesign --verify --deep --strict --verbose=2 "$HOME/Applications/Blast Circuit.app"
open -n -W --stdout /ABSOLUTE/FRESH/RUN/smoke.stdout.log --stderr /ABSOLUTE/FRESH/RUN/smoke.stderr.log "$HOME/Applications/Blast Circuit.app" --args --windowed -- --display-windowed --smoke --smoke-seconds=4
```

Replace `/ABSOLUTE/FRESH/RUN` with a fresh existing evidence directory. Retain
both logs; the Godot `--` separates engine flags from product arguments. Require
`GODOT_SMOKE_OK`, positive snapshots, a real captured viewport and no runtime
errors; a successful `open` exit is insufficient. This complements the harness
launch, which invokes the bundle executable directly outside the source tree.

After verified installation, remove other project app copies as the user
requested, including the export. ZIPs and raw QA receipts remain available.

```sh
python3 tools/cleanup-macos-builds.py --dry-run --include-export
python3 tools/cleanup-macos-builds.py --apply --include-export --expected-installed-version VERSION
mdfind 'kMDItemCFBundleIdentifier == "io.github.mcmike0399.blastcircuit"'
```

Replace `VERSION` with the verified installed version. The tool rechecks exact
identity/APPL/executable payload, keeps the canonical installed bundle, removes
matching backups (including renamed roots containing `Contents/Info.plist`),
unregisters old LaunchServices entries and registers the installed app. Its
dry-run JSON and a filesystem scan complement Spotlight: a renamed backup can
remain launchable without appearing in `mdfind`. Verify only the installed app
remains. LaunchServices may report `-10814 from spotlight` for an unregistered
renamed backup; the tool accepts only that exact normalized missing-entry
diagnostic and fails on other unregister errors. Never broadly delete artifacts.

## CI and publication boundaries

Read [CI](../ci.md) for trusted-job configuration and artifact validation. Public
PR checks use no private dependency credentials; private builds run only on
trusted main. Keep both bootstrap tools aligned with the checked-in dependency
pin; a historical unavailable SHA is not a reason to restore upstream history
or rotate credentials. Keep private source/origins and secrets out of tracked
files, logs, caches and uploaded artifacts. Upload only allowlisted reports and
named delivery ZIPs/identity receipts. Inspect downloaded app ZIPs in place to
avoid recreating Spotlight-visible duplicates after installation cleanup.

Before claiming CI delivery, verify required jobs for the exact source SHA and
version, checksums, app/controller manifest and server per-file inventory. A
skipped optional Metal job is not rendering evidence. A downloadable artifact
is not an installed app, notarization, physical-device validation or a deployed
server. Draft promotion verifies app/server artifacts from the same successful
run and creates a draft only; publication/host operations need their own scope.

## Automatic updater checks

Read [automatic updates](../automatic-updates.md) before changing release signing,
downloads or installation. The client signature key is public; the signing secret
stays outside Git. Release fixtures must never be published. Test both successful
relaunch and failed-launch rollback with the production installer and an isolated
app location. Run the existing public contracts and actionlint for workflow edits.
A local updater fixture does not prove hosted CI or a public release; verify those
separately when publication is authorized. Preserve the user's update preference
and avoid replacement while a match/session is active.
