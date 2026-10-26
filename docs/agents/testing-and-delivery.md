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
| Python QA/CI tooling | Relevant `unittest` module plus malformed/failure receipts | `public`; workflow edits also `actionlint .github/workflows/ci.yml` |
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
server. Promotion verifies app/server artifacts from the same successful run
and creates a signed draft by default. Explicit publication requires the
protected matching-fleet deployment gate; host operations still require their
own authorization and configured operator adapter.

For v0.27 connection changes, include the `peer-rooms` journey and the
[peer transport checks](../peer-hosting.md#verification-entrypoints). The official
WebRTC extension is a hash-pinned build dependency, installed before import/export.

## Automatic updater checks

Read [automatic updates](../automatic-updates.md) before changing release signing,
downloads or installation. The client signature key is public; the signing secret
stays outside Git. Release fixtures must never be published. Test both successful
relaunch and failed-launch rollback with the production installer and an isolated
app location. Run the existing public contracts and actionlint for workflow edits.
A local updater fixture does not prove hosted CI or a public release; verify those
separately when publication is authorized. Preserve the user's update preference
and avoid replacement while a match/session is active.

## Overhead explosion verification

`dotnet run --project tests/Bomber.Game.Tests -- --blast-hitbox` reproduces
zero- and three-pump overhead remote explosions whose spheres intersect a
player's body while excluding its feet. Ordinary throws and remote inputs cover
normal battle and practice, with and without a heart. Geometry checks retain
out-of-range, elevated-player, hard-terrain, soft-cover, red-piercing and solid-deck
boundaries. The AI suite checks the corresponding overhead danger interval.
The `blast-hitbox` native journey walks three humans on the stock Temple, collects
Remote, pumps/aims/throws and detonates over the victim. It checks the actual blast
event's position and death, and captures the held bomb, overhead flight and
explosion. It belongs to `all` and `core-headless`; graphical evidence uses the
isolated desktop.

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

For the combined v0.28 release, `stage-fidelity` is the focused fresh-process suite for all reconstructed arenas, explosion presentation and tutorial. The complete `all` suite and package gate remain the release regression. [Combined receipts](../stages-release-028.md).

## Adaptive multiplayer verification (v0.29)

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

## Display settings verification

`display-settings` is graphical-only and belongs to `all` and `menu-ux`. It checks
native window sizes, actual fullscreen transitions, persistent shortcuts, 3D
resolution scaling and paused local-session continuity. Its fresh-executable child
uses a unique QA-only preference fixture and retains a separate `restart/`
graphical receipt. This is process-startup evidence beyond in-process config
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

`visual-glitches` uses ordinary practice movement, placement and charged throws
for Up & Down rear-ring effects and Temple stairs/shoreline. It is included in
`all` and `stage-fidelity`. Pixel probes normalize logical camera coordinates to
actual capture pixels; headless mode only establishes gameplay/geometry checks.
`shore-blast` is the tight standalone shoreline case. `visual-regression` runs
fourteen affected rendering/stage journeys, keeping first-use preparation first.
Inspect the actual before/after images separately from numeric checks. The
complete graphical catalog has a 1500-second budget with the additional journey;
per-case focus, readiness and render-preparation deadlines are unchanged.

Source verification on 2026-10-26: the rebuilt export passed the fourteen-journey
visual regression with 1,758 assertions, 202 captures and zero runtime errors or
warnings on isolated Metal. Public QA passed all eleven steps; the same export
passed 48 headless checks. Reviewed images cover the original/corrected rear-ring
blast, Temple treads, shoreline occlusion, snow/ice and day/night flooding.
[Image provenance and before/after files](../images/visual-glitches/manifest.json).
The complete 55-journey attempt stopped at the existing fresh-display-process
timeout after 474 checks, before the new journey; it is not a full-suite pass.
