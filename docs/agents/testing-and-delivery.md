# Testing, diagnosis and delivery

Read this when selecting checks, reproducing a bug, extending gameplay tests,
recording evidence, or delivering an app/server. Commands run from the repo root.
The tools and their `--help` are authoritative for current flags; detailed
references are [playtests](../../tools/playtest/README.md), [CI](../ci.md),
[macOS packaging](../macos-app.md), and [verification](../verification.md).

## Select the branch before running work

| Change | Tight loop | Completion gate |
| --- | --- | --- |
| Docs only | Read changed instructions; verify links, paths and command flags | `python3 tools/ci/doc_links.py`, diff/whitespace review; no packaging, GPU, install or service restart |
| Python QA/CI tooling | Relevant `unittest` module plus malformed/failure receipts | `public`; workflow edits also `actionlint .github/workflows/*.yml` |
| File moves/renames | `git grep` old paths; build; editor import for `.uid` files | `public` plus the moved area's gate; see [codebase map](codebase-map.md#moving-files-safely) |
| Simulation/rules | Corresponding executable managed suite with a red regression | `managed`, relevant native journey, `headless` |
| Client/session/protocol | Client checks and real HTTP/WebSocket repro | `managed` plus `network`; restart only an owned stale server |
| AI | Focused deterministic regression and matched-seed metrics | Managed AI checks/60-scenario soak plus `ai-play` where visible behavior matters |
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

On macOS, use the harness's default isolated desktop so the developer can keep
working. Read [VM setup, execution and evidence limits](../../tools/playtest/README.md#isolated-macos-desktop)
before provisioning or diagnosing the guest. The real graphical suite runs in
its own macOS desktop with unchanged focus/input checks. VM startup failure
fails the run; it never takes over the developer's desktop as a fallback.
Use a 1920×1200 guest display for the complete suite: `graphics-settings` needs
room for its actual 1600×1000 native test window. The v0.35 full Metal run used
this size without weakening window, focus or startup checks.
Each QA process uses its own GUID-named `qa-preferences-*.cfg`. Never key it by
PID: a rebooted guest reuses PIDs, and a killed run leaves its file behind, so a
new process silently inherits old display/volume choices (observed 2026-10-26).

Assign one owner per test desktop/VM and one owner for shared builds. Explicit
`--desktop shared` is for a desktop available for testing or physical-device
observation; it activates the game and moves that machine's pointer. In either
mode keep the game foreground **inside its test desktop**. The harness retries
only proven focus interruptions, from released inputs and a fresh session, at
most three attempts; functional failures fail. Record guest GPU evidence
separately from bare-metal performance and physical device evidence.

For GPU performance work, use the development Mac's physical Apple Silicon GPU
when needed. The native `graphics-benchmark` scenario is an explicit shared-desktop
path; it is not part of `all`, and routine graphical regression stays isolated.
Announce its brief windowed run before launch, keep the game foreground, and stop
the QA VM/other owned rendering jobs first so they do not compete for the GPU.
Do not reroute a failed isolated correctness run onto the host automatically.

The native benchmark acquires an exclusive host GPU lock plus the QA VM's lock and verifies that Tart has
no running local VMs before launching. A running VM or another QA owner fails
the benchmark rather than producing contested GPU timings. Stop the VM before
native Metal measurements; keep the lock through completion. This also prevents
another checkout's isolated runner from starting that VM during the sample.
Updated isolated runners hold a shared host GPU lock as well, so choosing another
`--vm` cannot bypass a native benchmark. Individual VM ownership remains separate.

Use `--benchmark-resolution WIDTHxHEIGHT`, `--benchmark-preset high`,
`--benchmark-stage all` (or a single arena), and `--benchmark-seconds 30` for
matched workloads. Native-panel-sized windows become borderless while remaining
windowed; receipts require the actual pixel dimensions and full High/Medium
render scale. The benchmark reports p99/max frame time, allocation/frame and the
fraction above the 8.33 ms process-frame budget. That fraction is not a count of
dropped display presents. `--benchmark-disable` is explicitly diagnostic and
cannot count as full-quality evidence. `--gpu-profile` prints engine pass timings
when supported; Godot 4.7.2's unavailable Metal timestamps remain unavailable.

```sh
python3 tools/playtest/run.py --scenario graphics-benchmark --desktop shared --build
python3 tools/playtest/run.py --scenario graphics-benchmark --desktop shared --app /path/to/BlastCircuit.app
```

The benchmark rejects VM/software GPUs, headless mode and recording. It uses
uncapped real time with VSync off, a 1920×1200 window, warmup and wall-clock
sampling across all presets in menus and real eight-player battles. Keep native
hardware/OS/power state, frame percentiles, actual bomb/explosion witnesses and
the GPU-counter availability in its receipt. Root GPU timings exclude separate
menu surfaces; zero/unsupported counters must be reported as unavailable. Prefer
a rebuilt Release export when comparing CPU results; source builds have different
runtime overhead. A brief native run does not certify thermal endurance or other
M-series models, Windows/Linux drivers, or physical controller behavior.

For before/after performance comparisons, match the build toolchain and inspect
the bundled `*.runtimeconfig.json` servicing version. A published archive and a
local build can carry different .NET patches despite targeting the same major
version. PR #1's controlled local baseline removed an apparent High-preset gap
seen when comparing unlike artifacts; retain all measurements and their limits.

Add `--metal-hud` on macOS to collect Apple GPU durations independently of Godot's
viewport counters. The harness enables HUD/logging only in the owned game process,
keeps its overlay transparent, and joins its console batches to PID-filtered
unified-log timestamps. It does not change global HUD/privacy settings. Read the
[parser, alignment and metric scope](../../tools/playtest/README.md#native-gpu-performance)
before interpreting the GPU report. Godot 4.7.2 returns unavailable internal Metal
GPU times; the evaluated 4.8 beta does support them. [Engine decision](../engine-upgrade-research.md).

For first-use stutter, run `first-use-rendering` in a fresh process. Its optional
`--cold-effects` disables warmup and should fail on deferred synchronous pipeline
creation. Preserve both receipts. Fixed-step frame intervals are diagnostic stall
witnesses, not real-time GPU throughput. The default case tests every quality
preset and retained shader resources after garbage collection.

For map-picker freezes, run `map-preview-loading` in a fresh graphical process.
It cycles all maps forward/backward through Setup, Practice and room settings
at every preset, verifies retained scenes and paused inactive previews, and
rejects synchronous pipeline creation during selection. Inspect its actual
miniature captures alongside the timing/pipeline trace. `setup` also checks
miniature bounds and starts ordinary gameplay after preview selection.

Arena geometry assertions must inspect the active `ArenaView`, rather than
counting across the composition root: cached menu miniatures retain hidden
copies of stage geometry. The complete headless sequence retains the Rock
Garden regression, where root-wide column counts included both scenes.

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

For authorized server updates, follow the [shared-server guide](../online-server.md).
It owns deployment and completion checks for the service used by all players.
When a test host also runs a maintained worker, coordinate heavy QA/build work
through protected publication: pressure admission must qualify naturally for
the real fleet probes. Preserve admission policy and measured resource caps.
The [historical party guide](../online-party.md) applies only to an explicit
same-Wi-Fi party or relay task.

## Extend the harness with meaningful gameplay

The `tutorial` journey covers the lesson browser, accepted-action completion/failure, saved progress, retry/next, pause and free practice. [Tutorial verification](../tutorial.md#verification) pairs it with managed ordinary-input completion of every lesson.

`assets/testing/scenarios.json` is the common Python/C# catalog. Add a private
parameterless `async Task` on the `PlaytestDriver` partial class in a new
`scripts/testing/journeys/<area>/*Playtest.cs` file (areas: combat, input, matches,
menus, online, rendering, stages), then register method, domains, headless
capability, timeout and suite membership. Journey files keep the
`BlastCircuit.Testing` namespace; follow neighbouring journeys for naming/layout.
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

For README/gallery refreshes, use the explicit `readme-gallery` journey with
`--desktop shared --build --timeout 360` on the native Mac Metal renderer when
high-resolution hardware captures are requested. It selects High through the UI
and renders 3840×2400 in a borderless window, avoiding Cocoa's decorated-window
height limit. Review actual PNG dimensions and every selected image; retain old
media for historical links. [Capture recipe and provenance](../screenshots.md).
Routine graphical correctness checks still default to the isolated desktop.

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

Windows Vulkan (with recorded Direct3D 12 fallback) and Linux Vulkan exports use the shared [desktop packaging and verification
contract](../desktop-clients.md). Their trusted package and target-OS exported
smoke are required for publication. Headless receipts do not establish GPU or
physical-controller coverage; installed desktop releases use signed automatic updates.

Use the package branch only when shipping changed runtime/assets, not for docs.
Check toolchain/backend requirements in [macOS packaging](../macos-app.md).

The package gate checks native controller startup even when reusing local libraries.
Both libraries must target macOS 14 and omit `pipe2`: newer SDKs can detect that
symbol at link time even when the supported/running OS lacks it. The bootstrap
selects libusb's `pipe`/`fcntl` fallback, and `check-macos-backend.py` exercises
actual libusb and SDL gamepad initialization before export. Headless game smoke
does not initialize that backend and cannot substitute for this check.

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
python3 tools/packaging/cleanup-macos-builds.py --dry-run --include-export
python3 tools/packaging/cleanup-macos-builds.py --apply --include-export --expected-installed-version VERSION
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

**Release fast path (2026-10-26).** Trusted CI now owns the release-critical
launch checks, so a routine release no longer needs a local full Metal suite or
hand-run download/signature checks:

1. Run the changed branch's tight loop and completion gate from the table above.
   Add a local graphical journey only for rendering/menu/input changes, and run
   the affected suite rather than `all` unless the change is broad.
2. Merge. Trusted Verification builds once and runs, in parallel, managed/network,
   headless, package and then two **release gates** on the archived app with a
   hosted macOS VM's real Metal renderer: saved-settings startup for all three
   presets × fullscreen/windowed plus gameplay, and an actual signed update from
   the latest published release (install, rollback, saved High/fullscreen).
   Deployment and publication re-validate both receipts and refuse without them.
3. Follow progress with `python3 tools/ci/release.py status` instead of polling
   several `gh` commands. After publication, the **Verify published release**
   job downloads every asset anonymously and checks signatures, signed inventory,
   archives, tag, latest channel and byte equality with CI. Its receipt is the
   post-publish evidence; `release.py verify-published` reruns it locally.

A failed gate, an unexplained startup crash or a missing receipt blocks the
release; do not retry until green or relax a check. The gates prove launch and
update on paravirtual Metal, not art approval, physical controllers or native
GPU performance. Gameplay, rendering and input changes still need their own
journeys. [Gate details](../releases.md#release-gates).

Read [release identity and deployment gates](../releases.md) before changing
release configuration or preparing, retrying or recovering a release. Start with
`python3 tools/ci/release.py doctor --publish`; the release guide owns the
prepare/dispatch/recovery sequence and completion receipt. Keep the exact tested
run/artifacts across retries instead of rebuilding unchanged content. Signed
updates and ad-hoc app signing remain the chosen delivery policy.
`Release.props` is canonical; export drift fails public
QA. Run actionlint over all three workflow files. Server delivery now includes
the exact plugins used by the fleet drill. Both manual and automatic publication
require the protected deployment gate and matching live release identities.

The [release-latency policy](../releases.md#release-latency) owns the fast path:
documentation-only pushes stop after public contracts, public controller libraries
can be cached, and publication prepares the signed draft before server cutover.
Deployment and publication share one protected runner and one fresh fleet match
cycle. Retry a failed publication with the same run; the durable successful
deployment is rechecked without reapplying it. Preserve the archived startup/update
gates and record actual hosted timings before claiming delivery-time gains.

Read [CI](../ci.md) for trusted-job configuration and artifact validation. Public
Fork PR checks use no private dependency credentials. Private functional builds
run on trusted main and user-authorized same-repository PRs; self-hosted graphics
remains main-only. Keep both bootstrap tools aligned with the checked-in dependency
pin; a historical unavailable SHA is not a reason to restore upstream history
or rotate credentials. Keep private source/origins and secrets out of tracked
files, logs, caches and uploaded artifacts. Upload only allowlisted reports and
named delivery ZIPs/identity receipts. Inspect downloaded app ZIPs in place to
avoid recreating Spotlight-visible duplicates after installation cleanup.

Before claiming CI delivery, verify required jobs for the exact source SHA and
version, checksums, app/controller manifest and server per-file inventory. A
skipped optional Metal job is not rendering evidence. A downloadable artifact
is not an installed app, notarization, physical-device validation or a deployed
server. Promotion verifies app/server artifacts from the same successful run
and creates a signed draft by default. Explicit publication requires the
protected matching-fleet deployment gate; host operations still require their
own authorization and configured operator adapter.

For v0.27 connection changes, include the `peer-rooms` journey and the
[peer transport checks](../peer-hosting.md#verification-entrypoints). The official
WebRTC extension is a hash-pinned build dependency, installed before import/export.

## Battle polish integration regression (2026-10-27)

Post-match network checks await the guest menu's accepted arena separately from
`NetworkSession.Party`: the socket worker publishes that transport snapshot
before the engine frame drains it into presentation. An immediate menu assertion
after the transport acknowledgment races the normal UI update. Preserve the
same expected arena and bounded wait; do not force menu callbacks or state.

The native walking robot uses bounded ordinary movement pulses for a remote
server, releases each pulse through the thread-safe input queue independently
of an engine-frame stall, then observes fresh snapshots after release before steering
again. Local/peer authority still runs on its owning polling thread. Greedy Trap's
server tour deliberately delays feedback by 180 ms; the retained old controller
overshot a landing while the pulsed controller keeps the original half-cell
settle bound and real stair/basin checks. This tests controller lag, not changed
physics or measured Internet tolerance.

`battle-polish-regression` combines first-use rendering, all randomized-cover stage
journeys, camera framing, active-blast hits, local/online post-match map selection,
Quick Play permissions/countdown, the asset audit, pickup personality, the Greedy
Trap canal and mishap poses. Run it on the final Mac export when integrating these
features. Quick Play's map-selection controls stay disabled even for a lone public
room owner after its countdown cancels; the private-host editor remains available.

## Automatic updater checks

Shutdown owns three separate boundaries: saved state, managed resource finalizers
and the pinned native low-priority worker queue. `ApplicationShutdown` stops new
warmup/render work, flushes saves, runs the bounded dedicated CLR finalizer waiter
while native Godot is alive, syncs cleanup and then runs the FIFO worker fences.
No watchdog turns a failed shutdown into success. Schema-2 shutdown receipts require
both drains; schema-1 historical receipts remain readable. A save-failure control
must exit 1, not merely any nonzero code that could hide a native crash.

`tools/qa/shutdown_probe.py --resources-only` retains a deliberate pending-resource
native failure and a fenced SDK control. This is distinct from the original
low-priority pending/fenced pair. Neither SDK pair substitutes for actual game,
Low/High, updater/rollback or Mac quit-path evidence. A headless gameplay pass
followed by a native abort remains a failed gate. Named game `runtime.log` files
can be staged explicitly for diagnosis; arbitrary build/bootstrap logs stay out.

Read [automatic updates](../automatic-updates.md) before changing release signing,
downloads or installation. The client signature key is public; the signing secret
stays outside Git. Release fixtures must never be published. Test both successful
relaunch and failed-launch rollback with the production installer and an isolated
app location. Run the existing public contracts and actionlint for workflow edits.
A local updater fixture does not prove hosted CI or a public release; the trusted
`Release gate: update from published` job runs the same transactions from the
latest published app to every candidate. Preserve the user's update preference
and avoid replacement while a match/session is active.

## Overhead explosion verification

`dotnet run --project tests/Bomber.Game.Tests -- --blast-hitbox` reproduces
zero- and three-pump overhead remote explosions whose spheres intersect a
player's body while excluding its feet. Ordinary throws and remote inputs cover
normal battle and practice, with and without a heart. Geometry checks retain
out-of-range, elevated-player, hard-terrain, soft-cover, red-piercing and solid-deck
boundaries. The AI suite checks the corresponding overhead danger interval.

The focused suite also walks players into already-active small, three-pump and
maximum-level blasts, with expired-blast controls, hearts, owner attribution and
original-cover/red-piercing regressions. The missed-hit red witness was a player
3.30 units from a radius-4 center at age 0.33 seconds who survived because only
ignition applied damage. Retain this temporal case separately from spatial tests.

The `blast-hitbox` native journey walks three humans on the stock Temple, collects
Remote, pumps/aims/throws and detonates over the victim. It checks the actual blast
event's position and death, and captures the held bomb, overhead flight and
explosion. It also enters small and three-pump blooms after surviving ignition,
checks the actual death position/age and captures ignition and contact. It belongs
to `all` and `core-headless`; graphical evidence uses the isolated desktop.

## Ledge fall verification

For ledge movement, `dotnet run --project tests/Bomber.Game.Tests -- --falling`
checks short drops, limited steering, momentum, four directions, real status
pickups, carrying and immediate landing controls. `--measure-falls` reports
accepted event-to-event duration/travel for forward, released, reversed and
sideways input at three heights. The native `traversal` journey asserts those
budgets on the stock temple and retains shore/ghost checks. [Reference research,
comparisons and known validation limits](../falling-physics.md).

`dotnet run --project tests/Bomber.Game.Tests -- --ledge-drops` retains the
ghost snap-down and Pyramid lower-court crate regressions. It checks four
directions, landing on cover, falling off it, solid crate sides, ghost drops
onto cover and an upper-deck crate above a clear lower passage. These fixtures
use ordinary input and actual explosion-created ghosts. The native `traversal`
journey additionally walks a reachable stock Pyramid ledge above real seeded
cover, captures its fall/landing, then records an unlatched ghost crown drop
before verifying the existing latch lifecycle.

## Up & Down verification

`dotnet run --project tests/Bomber.Game.Tests -- --up-and-down` checks six-flight
round trips, underpass navigation nodes, bridge slab/blast collision, layered
stage messages, underside throw projection and low-to-middle flood elimination.
The 2026-10-25 regression adds eight ordinary side-rail drops (both piers), authored
cover/density checks, native falling/landing and real stone-block destruction.
Native geometry checks reject coplanar paving/slab tops; setup checks the circular
miniature sea and trimmed supports. Actual original and corrected renderer
captures remain necessary. Server-hosted and peer-hosted network smoke also
observes the side drop from another client. The CPU checks reject stair recovery
that is safe for one tick but reaches water before the next steering reaction;
`Bomber.Ai.Tests -- --front-placement` reruns the ordinary stock combat witness.
The initial topology failure is retained in ignored task artifacts.
`up-and-down` is a real native journey in both `all` and `core-headless`: ordinary
inputs cross all flights/underpasses, plant beneath the ring, fall into the sea,
and observe normal three-human play flooding the low and middle levels in order.
Graphical mode adds battle, oblique and underside inspection captures. The
combined graphical suite has a bounded catalog budget (1500 seconds since stage scenery) for the longer journey;
the QA profile reads that deadline from the catalog, retaining outer cleanup
headroom. The former hardcoded deadline could interrupt the expanded suite after
positive assertions; the catalog owns the bounded runtime.
`framing`, `stage-lighting` and `graphics-settings` retain all-stage coverage.
These fixtures do not establish original-game numerical physics or physical-pad
input. See the [stage contract](../up-and-down.md) for the accepted adaptation.

## Advanced rules verification (v0.25)

`AdvancedRulesChecks` covers validation, empty/filtered pools, custom caps and real
status pickup/movement/expiry; `ItemExpirationChecks` retains the formerly
unlimited map/training item regression. Actual crate-destruction sampling in
`LootAvailabilityChecks` checks the independent 40% default and category priority across 32 seeds per
map. `advanced-settings` is the graphical menu/status/expiration journey;
`network` also edits the advanced page in a live room and checks peer settings.

The existing `loot-opening` and `cover-layout` collection journeys explicitly
select 100% drops and a basic-upgrade-only pool through ordinary match settings
to keep their pickup/rendering evidence deterministic. They do not certify the
default drop probability; managed actual-destruction samples do. `sandbox`
selects a six-item restricted pool and a 60-second lifetime for its long equipment
tour. Bomb-handling, kick and grapple journeys request a restricted upgrade/Remote
pool so seeded sandbox selection cannot remove the equipment under test.
`powerup-collect` first checks and captures the default six-item 3/2/1 cache, then
uses a separate six-item explicit pool for its equipment-swap walkthrough.
`ItemDensityChecks` samples 100,000 weighted draws and 64 practice seeds per
training map, including replay, every type, filtered/empty pools and expiration.
Its `--loot-density-sweep` compares five rates over four maps and 128 seeds via
actual authority block destruction. See the [tuning evidence](gameplay-and-ai.md#item-density-tuning-2026-10-25).
Default and short
lifetimes have separate authority and graphical expiration witnesses.

The 2026-10-25 bomb loadout keeps black starting bombs and only Remote, Red and
Bounce equipment. `AdvancedRulesChecks` rejects the removed Fast pickup/catalog
entry; `SandboxEquipmentChecks` compares actual aimed throws with/without Haste
for all four bomb modes and commands a remote planted before collecting Haste.
The native `sandbox` journey checks Red/Bounce swaps and Haste movement/expiry
without replacing Red. Pair it with `advanced-settings` and `tutorial` for the
eleven-item menu, timed status rendering and the updated lesson browser.

### Item-density source verification — 2026-10-25

Receipts under ignored `artifacts/item-density/` retain baseline/tuned sweeps,
the red priority witness, and successful `managed-final`, `headless`, `public`
and focused `graphical` runs. The first managed run exposed an old per-map
all-types assertion: a 1%-weight type can be absent from a small sample. Full
pool coverage now belongs to the 100,000-draw regression; actual map samples
retain probability and category-order checks. The final managed profile passed
2,914 game checks, 336 client checks, 65 AI checks, the 48-case soak and real
network/relay/fleet gates. Headless core passed 1,233 assertions, peer rooms 19
and adaptive rooms nine.

Rebuilt exports passed isolated macOS/Metal `powerup-collect`, `advanced-settings`
and `sandbox`: 100 assertions, 22 captures, zero runtime errors/warnings. Actual
captures reviewed the six-item default cache, collected inventory/equipment,
settings, before/after expiration and bomb swaps/blasts. The VM stopped after
its owned runs; this is renderer/synthetic-input evidence, not a fresh physical
controller or native-host GPU performance certification. This source work does
not establish installation, release publication or external server delivery.

## Stage scenery verification

`stage-scenery` (in `all`, `core-headless` and `stage-fidelity`) walks all seven
arenas. It checks signature props, the absence of collision, asset-audit geometry
rules, and that mesh bounds stay out of every passable cell's floor-to-2.1
standing column, before and after animation. It also checks that animated nodes
move, that a real timed flood lifts the sailboat with the rendered water, and that
every setup miniature skips the scenery. Graphical runs add Midday/Night battle
captures and a labelled establishing view per arena. `scenery-regression` pairs
it with startup, asset, graphics, lighting, setup, menu-preview, framing and the
five stage journeys. [Stage scenery](../stage-scenery.md).

## Greedy Trap verification

Native resize QA allows up to twenty seconds for a slow first Metal frame, while
still requiring the actual requested size and an unchanged input transform for
200 ms. Resize receipts record sample count, actual size and elapsed time; this
does not bypass native focus or stability checks.

Greedy Trap's managed `--greedy-trap` checks, focused 12-scenario AI soak and
native `greedy-trap` journey cover its three stories, six flights, actual bottom
loot, quality/lighting variants and crushing animation. The
`greedy-trap-regression` suite adds startup preparation, setup, graphics,
lighting, framing and real network regression. See [stage verification](../greedy-trap.md).
The `greedy-canal` leaf, included in `greedy-trap-regression` and
`scenery-regression`, checks the actual rotating wheel mesh bounds against the
quay/fortress clearance through a full turn, checks nearby rowboat/oar clearance,
and captures ordinary battle and labelled canal inspection views at all presets.
Headless runs check geometry at the current preset; graphical runs select each
preset through Settings. See the [capture provenance](../media/greedy-waterwheel/manifest.json).


## Top Rules verification

`top-rules` is a standard leaf in `all` and `core-headless`. The graphical
`all` suite keeps `first-use-rendering` first; a Python contract guards its
fresh-process precondition. It drives normal
menu keys and gameplay inputs through every staircase, the raised crosswalk,
lower underpasses/throws, real crate → equipment → lower death-drop presentation,
night lighting, eight-player CPU battle and inward
sudden-death walls. Headless results establish state assertions only; inspect
the graphical captures separately. Managed `--top-rules` checks preserve the
layered support, falling, kick/throw/slab and CPU path witnesses. The standard
60-case AI soak includes the new arena. `PartyNetworkChecks` edits a real room
to Top Rules and observes a host's lower-story traversal from its peer.

For focused castle changes, `--scenario top-rules-regression` runs the eight
affected graphical journeys: first-use rendering, graphics settings, lighting,
setup, the castle journey, framing, precision aiming and real online play. It
uses a rebuilt export and keeps the startup check first. The complete `all`
suite remains available for broader regressions.

## Rock Garden reconstruction

`--rock-garden` in the managed game suite covers enclosed topology, center
traversal, four wall contacts, shielding, block percentages and fireball
warning/escape/damage. The `rock-garden` native journey belongs to `all` and
`core-headless`: four/eight-seat starts, real boulder destruction and pickup,
fireball warning/pause/escape/impact, CPU combat and graphical detail captures. The
shared walking helper reads the active stage rather than assuming Temple.
See the [stage contract](../rock-garden.md) and [research](../rock-garden-research.md).

The combined graphical suite uses a 1500-second catalog budget for all journeys (raised from 1200 for `stage-scenery`, 2026-10-26);
QA forwards that deadline and retains outer cleanup/VM startup headroom. Online
settings checks compare rendering with the latest accepted server lighting,
since a real round can advance while graphics pipelines prepare.

The eight-peer native capacity fixture must poll live sessions while later
guests connect, wait for eight connected members in each accepted roster, and
yield engine frames between guest polls. All eight clients share one engine
thread in QA; batching their setup can otherwise starve the real host's
heartbeat and trigger the production six-second takeover policy. Keep that
policy intact rather than weakening it to accommodate the fixture.
Production and every hardware or headless gate keep six seconds. By user
decision on 2026-10-28, the owned QA server for verified software-renderer
graphical parts only uses `--Blast:PeerHostLeaseSeconds=120`. On WARP, frames
of about 1 s, plus arena loads, made a 6-second peer host fail over 95 times.
`PeerHostLease.From` rejects values below six. Receipts record the lease, and
validation rejects a lengthened lease on any non-software receipt.
Online test robots that press a button for N frames turn a tap into a
multi-second hold on software renderers, and a held A charges a throw. Use
`NetworkTap`: it sends the press edge immediately and releases it 100 ms
later in wall time.
For crossplay movement use `NetworkNudge`: one owner controls the injected
provider and socket intent until two matching socket input samples have actually
been sent. `LatestInputSend` publishes immutable progress; capture/allocation
alone never counts as a successful send. A dedicated QA observer releases input
without engine calls or dependence on a rendered frame. The safety cutoff begins
at the first captured movement sample and is derived from the verified 1.25-cell
corridor; a final spatial check rejects overruns. An unsampled intent has a
separate bounded wait and cannot claim delivery. The provider cannot resurrect
released input on a late frame. Observe an authority acknowledgement beyond the
release sequence before judging movement. This instrumentation does not change
normal input queues, wire messages or send cadence. `network-input-pulse`
is a diagnostic leaf outside release suites. The old frame controller moved
4.23 cells for a short tap under a one-second stall. An earlier bounded-wall-time
diagnostic moved 0.45 cells with a two-second stall; the observed-send controller
moves 0.30 cells with a one-second stall and retains carry beyond a full fuse.
Crossplay selects a short supported inward path from actual stage/cover/actor
state, creates a carried bomb first, and only counts fresh healthy movement
along that intent. It holds carry through the cohort barrier. Failure retains
`crossplay-progress.json`; deliberate frame-stall receipts remain unqualified.
Personal Quick Play Settings is opened while alone; the second client joins
while it is open, so the unchanged real minute starts immediately before the
return check instead of being consumed by screenshots. Starting a match while
Settings is open is a separate phase transition, not evidence of a reset. On llvmpipe, `post-match` played six drawn rounds before
this fix and now ends in 6 frames, as on Mac.

For the combined v0.28 release, `stage-fidelity` is the focused fresh-process suite for all reconstructed arenas, explosion presentation and tutorial. The complete `all` suite and package gate remain the release regression. [Combined receipts](../stages-release-028.md).

## Adaptive multiplayer verification (v0.29)

For Quick Play changes, run the deterministic timer/permissions/lifecycle regressions:

```sh
dotnet run --project tests/Bomber.Game.Tests -- --public-matchmaking
```

The managed smoke's `--quick-only` uses real queue admission and the complete
one-minute wait without a production timer override. The graphical `quick-play`
leaf (also included in `network`) checks actual lobby text, countdown updates,
hidden public owner actions, personal Settings return and departure cancellation.
Keep source verification separate from a released client/server deployment.

The managed gate includes real adaptive sessions, a separately configured owned
server with zero simulation permits to force relay, and a two-process Valkey
fleet drill. The native `adaptive-rooms` journey joins eight clients, measures
host candidates, validates the actual disposable snapshot channel, injects
snapshot loss and retains a match after guest departure. It is included in
headless/package gates and graphical `all`. Retain the distinction between
loopback/process proofs and public/physical cluster measurements.

Keep the macOS test host awake during bounded timing tests (`caffeinate -i` around the
owned command). A clamshell sleep invalidated a realtime lease test while its
monotonic test timer barely advanced; do not lengthen production leases to hide
that environmental interruption.

## Blizzard Battle verification

[Stage contract and commands](../blizzard-battle.md#verification-entrypoints). The `blizzard-battle` native leaf belongs to `all`, `core-headless` and `stage-fidelity`; `blizzard-regression` adds all-stage graphics, lighting, setup, framing, assets and network checks. Managed fixtures cover actual drift/fuses, future-wind flight prediction, cone budgets and avalanche reset; peer validation rejects malformed weather, and a real server room verifies advancing weather over WebSocket. The AI soak expands to sixty cases for five battle arenas. Keep actual image inspection separate from headless assertions. Full `graphics-settings` needs a guest display larger than its 1600×1000 test window; the Blizzard regression passed on a 1920×1200 guest. `SetupMenus` configures its own initial default settings so earlier arena tours cannot change its fixture precondition.

## Saved-High startup regression

`render-surface-startup` forces an unlaid-out real menu viewport while High is
active, then renders its previews before layout, at the minimum valid target
and at ordinary output size. The unfixed code reproduces the native invalid
mipmap error and process crash; settled-preset sweeps do not cover this ordering.
It belongs to `all` and `menu-ux`. Pair it with the actual updater fixture's
VM-only `--high-fullscreen` option for release-to-release launch verification.
Both now run automatically on every trusted candidate: `tools/qa/startup_gate.py`
launches the archived app with each saved preset/display mode, and
`tools/ci/update_from_published.py` updates the latest published app to it. On
hosted CI the unfixed v0.35.0 crashes (SIGSEGV) in all three High cases while
v0.35.1/v0.36.0 pass all nine.

## Display settings verification

`display-settings` is graphical-only and belongs to `all` and `menu-ux`. It checks
native window sizes, actual fullscreen transitions, persistent shortcuts, 3D
resolution scaling and paused local-session continuity. It retains a unique
QA-only preference fixture; after the parent game exits, the runner launches the
same executable and requires a separate `restart/` graphical receipt within
90 seconds. The child restores the saved choices through real controls and
captures; it does not inherit a QA display override. Promotion requires this
child's matching catalog, release, renderer, positive assertions and zero errors.
The VM transport envelope includes the additional sequential leg while each
game keeps its own deadline. Concurrent parent/child Metal startup stalled in
native pipeline creation on hosted Mac; the identical child passed after parent
exit. This is process-startup evidence beyond in-process config
reloads; personal preferences remain untouched. Native host display checks use
an explicitly announced `--desktop shared` run, separate from VM Metal evidence.

## Menu navigation regression (2026-10-25 source)

`menu-navigation` drives native keyboard, Xbox/DualSense events and the Switch
USB publication bridge through every menu. It checks Start focus without
activation, row-preserving grid movement, remembered shoulder column switches,
text editing, popup Back, focus restoration, nested Practice/room setup returns,
Continue/Save Back-loop prevention, pause session continuity and full Tab cycles
through enabled controls. `--headless` is the fast logic loop; it cannot prove
native focus or appearance.

`python3 tools/playtest/run.py --scenario menu-ux` runs the windowed graphical
completion gate in the isolated desktop: that regression plus WASD, popups,
controllers, menus/resizes, setup, dense layouts, player settings, tutorial and
the real two-client network journey. Inspect the captures and keep physical-pad
claims separate. `all` includes the new regression; no install is implied.

### Socket worker and slow native polling regression

The full native suite exposed socket I/O inheriting Godot's synchronization
context: cumulative native polling stalls prevented queued heartbeats from being
sent, while console-only tests passed. The worker now starts on the thread pool;
peer simulation and Godot link objects stay on their owning polling thread.
`UiContextNetworkChecks` in the real managed network smoke blocks a UI context
and requires transport pings to keep advancing.

`adaptive-slow-poll` adds controlled 950 ms guest-poll stalls to the eight-client
fixture; the elected authority is polled between guests. This is deliberate test
load, not observed Internet latency. Headless and package gates retain it, while
`peer-adaptive-regression` reproduces the ordinary journey sequence. Production
host leases and qualification budgets remain unchanged.

## Small visual glitch regression

The reticle Results lifecycle must observe the finish announcement before
expecting navigation. Authority phase `Over` keeps the game on Match for the
ordinary three-second hold; it does not mean Results has opened. The reticle
journey checks the survivor's carried bomb during that hold, waits for actual
Results, then retains the carried-snapshot and hidden-overlay assertions.
Use `run.py --scenario reticle` for the focused reproduction and `tour-13` for
its native delivery context. This fixture correction changes no game timing,
authority, input or rendering behavior.

`visual-glitches` uses ordinary practice movement, placement and charged throws
for Up & Down rear-ring effects and Temple stairs/shoreline. It is included in
`all` and `stage-fidelity`. Pixel probes normalize logical camera coordinates to
actual capture pixels; headless mode only establishes gameplay/geometry checks.
`shore-blast` is the tight standalone shoreline case. `visual-regression` runs
fourteen affected rendering/stage journeys, keeping first-use preparation first.
Inspect the actual before/after images separately from numeric checks. The
complete graphical catalog defaults to a 1500-second budget. Archived Mac
verification allows 2400 seconds for the complete tour: the hosted Metal run on
2026-10-28 reached journey 56/61 with 459 captures and no engine errors before
the old overall cutoff. Its last capture was still advancing seconds before
termination. This changes only the suite allowance; per-case focus, readiness
and render-preparation deadlines are unchanged. Timeout remains a failure, and
promotion still requires every leaf in order, positive assertions and zero
engine errors. Candidate and trusted Mac jobs retain `progress.json` for diagnosis.

Source verification on 2026-10-26: the rebuilt export passed the fourteen-journey
visual regression with 1,758 assertions, 202 captures and zero runtime errors or
warnings on isolated Metal. Public QA passed all eleven steps; the same export
passed 48 headless checks. Reviewed images cover the original/corrected rear-ring
blast, Temple treads, shoreline occlusion, snow/ice and day/night flooding.
[Image provenance and before/after files](../images/visual-glitches/manifest.json).
The complete 55-journey attempt stopped at the existing fresh-display-process
timeout after 474 checks, before the new journey; it is not a full-suite pass.

## Windows/Linux archived regression

The desktop delivery PRs add `tools/packaging/verify-desktop.py`: run complete
packaged core/online journeys against an owned matching archived server, and
use `--graphical` for the full renderer suite. Windows installs the actual EXE;
Linux launches its AppImage. Exact catalog coverage and runtime errors survive
in receipts; the four-leaf package smoke is insufficient for promotion.
`tests/Bomber.DesktopUpdate.Tests` verifies helper handoff and rollback with
real fixture processes. Keep that separate from actual game updates and
physical/GPU evidence. See [desktop clients](../desktop-clients.md).

Current three-platform strategy (user decision 2026-10-29): hosted Windows WARP
and Linux llvmpipe are functional/headless, installer/updater, startup/exit and
network test surfaces only. Graphics and complete graphical tours use real
GPUs. Preserve production quality and use the measured Mac-to-Mac residual
envelope and user visual sign-off in [GPU qualification](../gpu-qualification.md).

The historical software-runner split is retained as an ordering/partition
contract; it is not graphics qualification. `all` is 21 contiguous `tour-NN`
catalog suites. Mac runs `all` in one process; Windows/Linux run each tour in a
fresh process via `verify-desktop.py --cases … --part …`. The `--merge`
step must see every part on identical archive, server and download bytes.
Change tour boundaries only in `assets/testing/scenarios.json` and runner
grouping only in `desktop_validation.SOFTWARE_PARTS`; contracts reject gaps,
repeats or reordering. A leaf that needs a predecessor's state belongs inside
the same tour. To reproduce a failed part locally, run its tour with
`run.py --scenario tour-NN`. A part runs all of its cases even after one
fails, so a single run reports every failure. Use `PlaytestDriver.Waiting`
for new "wait until" loops; it gives verified software renderers
frame-equivalent time. Fixed-duration measurements must stay wall-clock.
Fixture `NetworkSession`s have no game loop. Give each long-lived fixture
exactly one owner, `KeepFixtureLive`, which advances and drains it like a
30 FPS client on the engine thread (Godot peer polling must stay there). It
never runs faster in uncapped headless frames, and it is scope-bound
(`using`). Hand messages a test needs to its callback rather than calling
`DrainMessages` alongside it. The owner records its cadence for diagnostics. Otherwise a slow software-rendered
journey overflows its 128-message control queue or misses the 6-second peer
host lease. Both failures appeared on WARP/llvmpipe on 2026-10-28. On verified
software renderers only, `peer-rooms` first warms its Top Rules arena, because
a cold first load in a fresh process stalled past that lease. Every leaf
appends Godot's video/static memory to `leaf-memory.json` for cross-platform
comparison.

Diagnose slow-frame failures locally before spending a native run:
`run.py --frame-stall-ms N` blocks the engine thread N ms per frame. Headless,
with an owned server, it reproduces software-renderer frame times in
minutes. Summaries record the stall, and release validation rejects any
receipt with one. The server writes `Bomber.PeerRoom` transitions (membership
changes, elections, lease expiry with measured silence, handshake kicks,
byte-budget drops). `NetworkSession.ConnectionEvents` keeps each client's
last 64 transitions with generation and the server's close code/reason. Peer
and network failure diagnostics, and the owned server logs, are uploaded with
CI artifacts. Candidate crossplay starts once exact downloads and the Mac
archive exist; trusted main still waits for every smoke job. It records
per-phase timings for budget decisions.
Crossplay commits one immutable witness (authority/match/round/actors/actions,
server-time bounds, renderer and capture references) BEFORE publishing its action
readiness artifact. The finish barrier permits cleanup once all observers committed
their evidence; it does not promise simultaneous exits. Final serialization uses
that frozen witness. A lobby transition during teardown cannot rewrite recorded
cluster hosting; an actual auto-hosted witness is still rejected. The diagnostic
`crossplay-witness-lifetime` regression mutates the source after commit.

The `visual-parity` fixture uses the full production ArenaView at original
High/native scale/2×MSAA/Circle-jitter-on/SSIL. It records fresh render-buffer
history, explicit zero-step draws/readbacks, actual resource inventories and
package-bound manifests. Same-draw/fresh-target/fresh-process equality and
DOF/mirror negative controls are separate from gameplay and hardware claims.
[Recipe, provenance and limits](../full-high-capture.md). A source image with
matching pixels but no package provenance remains diagnostic, not qualification.

The diagnostic `authority-replay` leaf executes the shipped `LocalMatch` at
exact ticks and writes complete ordered observable messages plus separate math/RNG
canaries. It does not serialize all private authority state. Native replay
installs actual downloads and checks two fresh-process repetitions and then
cross-target outputs, binding loaded MVID/file hashes to the archived modules.
[Corpus, schema, limits and commands](../packaged-authority-replay.md).

Research proposals for parity and delivery (not qualification receipts) are in
[rendering parity research](../desktop-rendering-parity-research.md) and
[delivery research](../desktop-parity-delivery-research.md). Current decisions:

- Crossplay lifetimes are one nested contract (`crossplay.PEER_START_SECONDS`,
  `SERVER_LIFETIME_SECONDS`, `ARTIFACT_CAP_SECONDS`), enforced by a test that
  parses `crossplay.yml`. Artifact waits track only producers of still-missing
  artifacts. A missing artifact whose producer ended fails promptly.
- `NetworkSession.DeadlineObserved` is the first socket receipt of the current
  room deadline, published as one immutable record. The exact 60 s reset and
  no-early-start rules are proven on the server clock in
  `PublicMatchmakingChecks`. The native journey proves client propagation.
- `peer-link-recovery` (diagnostic leaf, in no release suite) terminally closes
  one native WebRTC connection mid-match. It checks relay continuity and direct
  recovery as separate predicates. `PeerRoomClient` lets the pair's original
  offerer replace a terminally failed link with a fresh link. Recovery uses
  bounded backoff (5 attempts per epoch) and a link-attempt generation, kept
  separate from the authority epoch, that rejects stale SDP/ICE. New-attempt
  ICE reaches a fresh answerer even before SDP; replaced-link callbacks are
  ignored. Attempts reset when the coordinator reports a new member connection.
  Healthy,
  transiently disconnected and relay paths are left alone. Pinned
  webrtc-native cannot re-offer a closed peer. The forced-close diagnostic emits
  UTC/frame/injection markers into both runtime and trace logs and captures the
  recovered real scene. Its deliberate fault remains outside release suites;
  functional assertions and generic zero-error/graphical verdicts are separate.
  A native SCTP error without timing correlation is not a clean qualification.
  Focused Windows diagnostics select a JSON list through
  `BLAST_DIAGNOSTIC_SCENARIOS` (default: `network`, `peer-rooms`, and the close
  leaf); select only an unresolved case once earlier predicates have native proof.
  The post-capture check records both endpoints before judging them, requires
  unchanged open native channels/attempts and the same match, then permits normal
  polling to refresh the existing two-second direct-health measurement. It never
  increases that policy. A headless-only `BLAST_QA_RECOVERY_CAPTURE_STALL_MS`
  control adds a declared stall (at most five seconds) to distinguish stale health
  from another terminal close; it is diagnostic evidence, not graphical qualification. The runner retains every case and exits unsuccessfully if any
  child fails, lacks positive completion or reports no assertions; a later pass
  cannot hide an earlier failure. Diagnostic runner and frozen payload revisions
  are recorded separately.
  A merge-conflicted isolated PR cannot trigger `pull_request` workflows.
  Focused diagnostics also accept a push on `qa/three-platform-release` only
  when `BLAST_DIAGNOSTIC_RUNNER_SHA` equals that exact pushed SHA. Set frozen
  payload variables separately, arm the runner SHA before pushing, and retain
  both identities. Ordinary later pushes and main releases do not opt in.
  This keeps Mac stable independent until planned final integration.

Keep long-running platform QA independent of Mac stable releases: use the
separate worktree, private exact-SHA drafts, owned matching server and pinned
diagnostic updater baseline described in [independent platform QA](../desktop-clients.md#independent-platform-qa).
Integrate current main and select the final public identity when ready; stable
publication still tests the actual latest release and rejects diagnostic receipts.

### Native artifact and DiskImages lifetimes

Replay/input artifacts retain receipts, replay streams, inventories and captures,
while excluding their disposable `replay/installed` and `inputs/installed`
payloads. Windows uninstall can remove its own executable after the parent
process returns; uploading that changing directory races file enumeration. The
original package artifacts already preserve those bytes. Offline aggregation
also skips legacy installed directories so it cannot recreate redundant Mac app
bundles. Keep the QA evidence, including raw binary inventories.

Mac DMG creation retries once only for the observed DiskImages `Resource busy`
error, using a fresh owned temporary output on the destination filesystem. Other
errors and timeouts remain fatal. A successful image still requires `hdiutil
verify` and the existing mounted payload comparison against the source ZIP.
Never detach unrelated images or restart system services to make packaging pass.

### Greedy peer movement feedback

The Greedy network journey keeps its deliberate delayed server-observer case.
Its peer-hosted leg uses damped synthetic steering near the one-cell middle
landing: high-gain feedback from an older snapshot can overshoot during the
peer's existing five-tick catch-up and fall into the basin. The deterministic
client check first establishes the real upper landing, then compares old and
bounded steering under delayed observation through the unchanged `LocalMatch`.
The original controller misses the middle floor; bounded steering reaches it.
Normal player input, authority timing/collision, journey destinations, tolerances
and deadlines are unchanged. The QA flag is scoped to the peer leg and restored
in `finally`; source headless evidence does not replace exported native checks.

### Native early-ICE proof on hosts with multiple interfaces

`peer-ice-order` waits for host-only gathering to complete and drains the queued
native callbacks before reordering the batch. The first candidate is insufficient
to describe every interface. Every real gathered candidate precedes remote SDP;
the fixture checks that the SDP contains no embedded candidates and that no later
signal substitutes for the early batch. Connection and actual data delivery keep
their existing five-second predicates. The adapter exposes only a read-only
internal gathering-state observation; production signalling behavior is unchanged.
Signal counts, gathering/connection flags and the failing stage are written before
link disposal and retained in native CI. Use those observations to distinguish
incomplete fixture signalling from an actual native connection failure.

### Eight-peer native signalling observations

Playtest-only link observations retain SDP fingerprints, candidate endpoints,
produced/received/applied/error counts, gathering state and monotonic timing
from link creation. These are API observations; they do not expose the selected
ICE pair or the exact instant of a native transition. No raw SDP is retained.
Normal launches do not enable this collection. Capacity receipts freeze each
checkpoint and preserve the first failure before teardown.

The original all-eight-direct predicate and twenty-second bound remain required.
After a failure, the fixture may collect authoritative snapshots and bomb-owner
witnesses through relay, but it rethrows the original failure. A successful
reverse-order observation cannot clear it. The optional Windows second process
is armed only when `BLAST_QA_PEER_ROTATION_SHA` matches the exact CI source SHA.
It reverses logical guest labels, creating new identities and ephemeral ports;
it is not an identity-preserving port experiment. Clear that one-shot variable
after the run. If complete signalling still leaves a native link Connecting,
retain the evidence and request a release decision rather than growing timeouts.

### Seven-stage native input measurement

The free native package lane runs `visual_parity.py --inputs-only --stage ID`
for each of the seven controlled stages, in a separate process and output folder.
It retains the original High recipe without drawing or claiming GPU qualification.
Inventories include actual packed mesh surface buffers, texture bytes, stored resource and
node properties, shader/material parameters, camera, preset and authority inputs.
Primitive parameters alone cannot establish equality of generated vertex data.

The aggregate requires all three native stage collections, bound to the same
source/archive/download identities as that target's replay. Every stage must
match the Mac reference exactly. Missing files, failed execution, mismatched
scene identities or differing inputs remain red; other stages still receive
reports. Fresh unused builder diagnostics keep their own results. Raw data is
never rounded or masked. A green input gate does not qualify images, effects or
hardware. Run one measurement pass before choosing any math/geometry correction.

Recipes bind external inventory JSON/binary files by hash instead of embedding
multiple full copies of each resource graph in memory. The raw files remain
complete; all five trial bindings are validated and compared. This avoids a
combined-report allocation spike without increasing the fixture deadline.
