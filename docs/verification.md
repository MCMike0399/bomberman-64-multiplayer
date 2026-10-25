# Peer hosting verification — v0.27.0 delivered

2026-10-24: **0.27.0/build 32** adds persistent random lobbies and player-hosted
rooms. The [connection contract](peer-hosting.md) distinguishes cluster authority,
direct peer traffic, relay fallback and fresh-match host takeover. [Trusted CI](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40145403090)
and [signed publication](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40145957975)
passed for `0fa367a334c70b73cdf28e2c597ed7b5178f9a13`.
[v0.27.0 is published stable](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.27.0).

| Gate | Observed result |
| --- | --- |
| Public contracts | Pass, including pinned WebRTC archive integrity/path checks and independent QA receipt directories |
| Managed | 2,727 game, 304 client, 53 AI, 19 audio and 19 cluster checks; all 36 AI soaks pass |
| Real HTTP/WebSocket | Random waiting-room admission, two-human start gate, manual leader start, peer relay gameplay/results, stalled-host election, socket interruption and former-host reconnect pass; legacy 2–8-player coverage remains passing |
| Headless | 868 core assertions and 18 real peer assertions pass |
| Exported package | 252 package assertions plus 18 direct/relay/election/eight-player assertions pass; native controller compatibility and signature checks pass |
| Full graphical regression | 42 journeys, 2,768 assertions, 262 captures and zero runtime errors in the isolated Metal desktop |
| Final focused peer UI | 19 assertions, 5 captures, zero errors/warnings on the frozen export, including eight direct players |
| Cross-machine peer play | Three independent processes on Macintosh and DiveMac use the public primary route, exchange direct gameplay, retain the room after host departure and exchange new direct packets in replacement match 2 |

The graphical review included the updated online choices, the disabled one-player
start state, room labels and actual peer battles. Physical controller behavior,
new native GPU throughput and arbitrary Internet NAT/firewall combinations are
not established by these checks. Intermittent SCTP stream-reset warnings during
intentional connection teardown remain visible in receipts; no runtime error was
accepted as a pass. Later host-recovery refinements retain focused exported-peer
verification, rather than claiming the earlier full render run covered new code.

Two failures were retained and fixed: an eight-player initial roster/snapshot
burst evicted 12 ICE messages from a shared outbound queue, leaving two peers on
relay; and a fixed render clock could accelerate player-hosted authority in a
headless run. Separate bounded signalling traffic and a monotonic 30 Hz authority
clock pass the retained eight-peer regression. Reconnect and lobby-stall checks
also guard a restored roster and replacement-host eligibility.

The public random-lobby probe, forced-relay gameplay/results and real retained-seat
reconnect probe passed. Both cluster nodes received the exact published
runtime serially after authenticated drain/empty checks. Each mounted runtime
matched all 42 published file hashes; public random-lobby, relay gameplay/results
and retained-seat reconnect checks were repeated against that installation. Private configuration, kernel/plugin
pins, limits and other services were preserved; rollback directories remain.
**Limit:** the full-capacity cluster load probe was refused by the existing guard
because the fallback had less than 10% idle CPU (and its monitor still referenced
an intentionally stopped service). Its pressure admission also rejected a bounded
new-room probe with `no_capacity`. No fallback-load success, higher capacity or
live cluster-node migration is claimed. Functional direct play through the public
primary and eight-peer transport tests are separate passing evidence.

The public Mac download independently passed the P-256 update signature,
size/SHA-256, source/version/build, native WebRTC/notices and safe archive checks.
All **228 app files** were inventoried in place; no public app bundle was extracted
or installed over the user's app. Archive SHA-256:
`2618601594f03dd0738cb2d9396a296a4bee2b0178d61d7cc315648fe2e1e4b1`.
Task-created remote test bundles were identity-checked and removed. The shared
checkout moved to another active task, so its work and export were preserved.

The initial Linux CI import failed twice. A fresh minimal extension project
reproduced the crash; pre-registering the verified extension in Godot's ignored
registry passed the same import. The bootstrap preserves other entries and keeps
strict failures. The full isolated Linux reproduction then passed 868 core and 18
peer assertions, followed by passing official Linux CI. Engine/dependency pins
were unchanged; no failed import was accepted as a successful retry.

Local evidence is retained under ignored `artifacts/p2p/`, including failed
reproductions, frozen gates, captures and cross-machine receipts. Published-asset,
installation and final live receipts are in `artifacts/p2p/release-027/`.

# PR #1 regression verification — v0.26.2 delivered

2026-10-24: functional verification of the codebase refactor, prepared as
**0.26.2/build 31**. The tested local export is from `90d71c2`; subsequent
verification-document edits do not change its runtime. [PR #1](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/1)
merged at `081754b9ee580ded19f5ac3372bd7fbdb6a0ecd3`.
[Trusted verification](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40138710624)
and [signed publication](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40139306645)
passed; [v0.26.2 is published stable](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.26.2).

The public downloads independently passed source/version/build identity, archive
checksums, the update manifest's P-256 signature, native-library compatibility
and startup, and the complete 217-file Mac / 42-file server inventories. Mac ZIP
SHA-256: `9f8bc281fcaa863f3d667aef4d4ac72d4784e50f05a99b5172aae584ceee11da`.
The exact public Mac archive was temporarily extracted, matched against all 217
archive files and strictly code-signature verified, then passed a graphical
`package-smoke` in the isolated Metal desktop: **271 assertions, 16 captures,
zero errors/warnings**. Its temporary bundle was removed afterward.

Both shared game nodes were drained and checked empty before serial installation
of the public server inventory. Each passed a public one-human/one-bot match with
authoritative movement and human bomb placement, and each test room retired.
The primary produced 255 snapshots and the fallback 256 in their bounded probes.
Resource limits and private configuration were preserved; other sampled container,
user-unit and HTTP states were unchanged. Rollback copies remain on their hosts.
The primary still matched all 42 running release files at the final audit. A
separate, user-confirmed P2P task subsequently replaced the fallback with a
`0.27.0-candidate`; that concurrent deployment was preserved and is outside this
release's certification. No user app installation was replaced. Task-created
comparison worktrees and launchable Mac bundles were cleaned up after retaining
archives and receipts.

On Apple M5/macOS 26.6.2, Godot 4.7.2 mono, SDK 10.0.400 and kernel `64ec8db`:

| Gate | Observed result |
| --- | --- |
| Public contracts and audio | Local suite and GitHub PR check pass, including the new native-startup failure gate |
| Managed and network | 2,705 game, 145 client, 53 AI, 19 audio and 19 cluster checks; all 36 AI soaks; real 2–8-client HTTP/WebSocket, matchmaking, reconnect and private-party lifecycle checks pass |
| Headless gameplay | All 21 journeys, 868 assertions pass |
| Release package | 217-file app, strict signature, native controller startup and 252 exported-app assertions pass |
| Complete native Metal gameplay | All 41 journeys in catalog order, 2,750 assertions, 257 captures, zero runtime errors/warnings and zero focus retries; approximately 408 seconds including launch |
| Display | Fullscreen launch, F11 and Alt+Enter transitions pass: 7 assertions, 2 captures |
| Existing client compatibility | Published v0.26.1 client against the refactored server: 70 assertions, 4 captures, zero runtime errors |
| Actual updater | Signed fixture feed/download, running-app handoff, successful new launch and failed-launch rollback to v0.26.1 pass |

The complete graphical run covers local/practice sessions, every stage and lighting
variant, graphics and advanced settings, keyboard and synthetic pad mappings,
focus/hotplug, terrain, ghosts, all bomb/aim/carry/kick/grapple paths, pickups and
temporary effects, AI difficulties, flooding, actual results/rematches, and a real
two-client online journey. Capture review included menus, eight-seat standings,
character views, aiming overlays, lighting/flooding and combat. The actual audio
mix was non-silent and unclipped, with zero discarded capture frames. Physical
inventory found no gamepads; synthetic tests and native backend loading do not
establish new physical button/rumble coverage or artistic approval.

**Release blocker found and fixed:** a fresh controller build with the macOS 27
SDK detected `pipe2`, then crashed on macOS 26.6.2 inside libusb initialization.
The failure reproduced without Godot by calling libusb directly. Both libraries
now target the app's macOS 14 minimum, and libusb uses its existing `pipe`/`fcntl`
fallback. A new package gate checks imports/minimum OS and calls actual libusb/SDL
initialization, including reused libraries. It rejects the original binary;
the rebuilt backend and full native gameplay pass. No gameplay rules changed.

Native real-time benchmarks at 1920×1200 passed all 47 checks for Low/Medium/High
menus and actual eight-player combat. An initial comparison against the public
v0.26.1 archive showed a High-preset difference, but that archive bundles .NET
10.0.13 while these local builds use 10.0.11. A clean pre-refactor `0a4dba4`
worktree rebuilt with the same local toolchain and byte-identical controller
libraries measured 73.5 menu / 42.8 battle FPS on High, versus 73.3 / 44.9 on the
repeated candidate run. This did not reproduce a refactor-specific slowdown.
All samples are retained; short-run variation, unavailable Godot GPU counters
and the different published toolchain prevent stronger performance claims.

Evidence is under ignored `artifacts/pr1-release/`, particularly
`graphical-native-complete/playtest/20261024T220648.422430Z-all/`,
`package-native-fix/`, `old-client-new-server/`, `update-integration/`, and the
four benchmark directories. Earlier failures remain: native startup before the
fix, unavailable focus on the locked desktop, and an isolated VM run that reached
AI testing but exhausted its 600-second process budget. The next VM attempt was
intentionally cancelled when the native desktop became available. The final
native suite used a 1,200-second budget with unchanged scenarios/assertions and
an owned ephemeral server; no tests were skipped to obtain the pass.

# Codebase restructure — source verification (no release)

2026-10-24, branch `refactor/codebase-structure`: a pure reorganization (feature
folders, partial splits, tool/doc moves; see the [codebase map](agents/codebase-map.md)).
No version bump: the release baseline remains **v0.26.1/build 30** below.

Fresh worktrees of this branch and of `main` (`0a4dba4`), kernel `64ec8db`, Godot
4.7.2 mono and .NET 10.0.400 on Apple Silicon: `public`, `managed`, `headless` and
`package` passed. Compared with `main`, the game, client, AI, audio and cluster
suite logs and the 36-scenario AI soak receipt were identical (excluding timings);
network smoke passed; the headless playtest had the same 868 checks; and the packaged
app had the same 217-file list, identity `io.github.mcmike0399.blastcircuit`
0.26.1/30, strict codesign and the same 252 `package-smoke` checks. Line-multiset
comparisons confirmed that the split files moved members verbatim.

Not run: graphical/windowed Metal QA (no isolated VM on the verifying Mac),
physical controllers and installation. A checkout with an older `.godot/` cache
logs old-path autoload errors on its first import after pulling; a second import
or deleting `.godot/` clears them.

# UI layout polish and smoother menu characters — v0.26.1 delivered

2026-10-24: Online, Advanced settings, Settings, Lobby and Results text/fields now
stay inside their boxes (vector `ArcadeToggle`, fitted arena preview, suit dial,
standings fitted to the projected 3D board, taller lobby seats, period ellipses).
Menu character previews and the backdrop render every frame on Medium/High (30 Hz
on Low) with a shadowed key/fill/rim studio rig. Version **0.26.1/build 30**.

Isolated-VM receipts under `artifacts/playtest/`: the new `ui-layout` scenario plus
`results`, `menu`, `advanced-settings`, `wasd-navigation`, `setup`, `player-settings`,
`party-polish`, `network` and `graphics-settings` passed with zero runtime errors
(pre-bump export); `ui-layout` and `results` passed again on the installed export.
That build (labelled 0.26.0/29) was installed locally at the user's request: 217
files matched, strict codesign and LaunchServices smoke (59 snapshots) passed.
Receipt: `artifacts/ui-polish-install-20261024/` (the historical pre-release installation).

**Published and installed 2026-10-24:** [v0.26.1](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.26.1)
ships **0.26.1/build 30** from `46cf598ff8b105c0e141ef1c22149f5cd11cda5e`, including
UI commit `7769b54` and the optional pressure-aware cluster plugin. The exact
[Verification run](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40129951585)
passed public, managed/network, headless and Apple Silicon package gates;
[publishing](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40130620187)
completed successfully. The release is stable, neither draft nor prerelease.

Both archive checksums, source/identity receipts and the server's per-file
inventory passed. The update manifest's P-256 signature verified against the
public key shipped in the client. Mac ZIP SHA-256:
`e34e64fb449bb7308c44e6799363476d209646469ff8f7942a828c66762431f2`.
The installed public archive matched all **217 files**, passed strict recursive
code-signature checks, and passed a fresh windowed LaunchServices smoke with
**59 snapshots** and an inspected viewport capture. Identity-checked cleanup
left only the canonical installed app. Receipts: `artifacts/release-0261/`,
including `verification.json` and `install/{receipt.json,gameplay.png,cleanup-after.json}`.

The public service's read-only game catalog still advertises Blast at 30 Hz and
2–8 players. Client protocol, game authority and server source are unchanged
from v0.26.0; the shared runtime was not redeployed for this UI release. The
optional placement-plugin update has separate [cluster evidence](cluster-hosting.md).
No bare-metal frame-rate measurement of the full-rate menu previews, Apple
notarization, new physical-controller validation or user approval of the look
is claimed.

# Graphics presets, native profiling and first-use preparation — source preview

2026-10-24: Low/Medium/High graphics preferences, output-resolution menu worlds,
consistent MSAA, filtered stone/water detail, preset shadow/SSAO budgets and
cached idle plaques are implemented. The compact models, movement, simulation
rules and server-owned lighting remain unchanged. Effects render once during
startup preparation and after preset changes, before control returns.
[Research and budgets](graphics-research.md), [engine evaluation](engine-upgrade-research.md).

The final staged client is **v0.26.0/build 29**, including the concurrently authored
public-host default/compression negotiation. It remains on Godot **4.7.2**. This
preview was subsequently installed on the user's Mac at their explicit request.
All 217 installed files match the verified archive, strict signature checks pass,
and LaunchServices smoke passed on the M5 Pro with 59 gameplay snapshots and an
inspected viewport capture. The previous installed bundle and extraction copy
were removed after verification. Receipt: `artifacts/graphics/install-20261024/`.
This graphics work has not published that client. Shared-server delivery,
the new kernel pin and compressed-server load validation belong to the separate
hosting work; a local graphical pass does not certify those operations.

Receipts under `artifacts/graphics/`:

- `public-final`: public boundaries, CI/playtest/QA/cluster tooling contracts and
  audio passed.
- `combined-final/playtest/20261024T185954.677983Z-all`: the combined exported
  client passed **40 scenarios, 2,739 assertions and 251 captures**, with zero
  runtime errors. This includes first-use checks, native settings/navigation,
  live preset changes, all stages/lighting, effects, controllers and real local
  WebSocket sessions against the owned guest server.
- Visual review then found diagonal self-shadow marks at grazing sun angles.
  Only the Morning/Afternoon bias multiplier changed afterward. The rebuilt
  final export passed `final-lighting-export/20261024T191607.639726Z-stage-lighting`:
  **146 assertions, 24 captures, zero runtime errors**. The afternoon marks were
  visibly reduced; all twelve stage/time views and flooded shoreline views were
  retained. This focused check covers the final lighting-only adjustment.
- `final-app-files.json` matches all **217 files** of that tested final export;
  strict deep codesign verification passed. The preview ZIP also matches that
  inventory: `Blast-Circuit-graphics-preview-20261024.zip`, SHA-256
  `0a7fb6f89851ee074ee6ee32a964ec9be7a80682c3234c301eab0d23c1497ad9`.
  The two temporary app bundles were archived and identity-checked cleanup removed
  them, preserving the installed v0.25.0 app. Build ownership is released in
  `artifacts/graphics/coordination.md`.

The first-use regression is red-capable. With `--cold-effects`, the latest
negative control created **five Surface and one Draw pipeline** and recorded a
**293.1 ms** frame interval. Normal preparation produced zero new synchronous
pipelines in all six first/repeated trials across Low/Medium/High, including
garbage collection. The focused warm run's worst intervals were 6.9–14.1 ms;
the full combined suite also recorded zero synchronous pipelines, with one
unrelated 25.8 ms interval. These fixed-step VM intervals identify initialization
stalls; they are not native GPU benchmarks. Preparation itself took 3.32–4.20
seconds in the guest runs and is shown as a loading/settings step.

Native M5 Pro checks separately passed. The stable-engine eight-player benchmark
at 1920×1200 recorded approximately 120 process FPS for all presets. First-use
explosions on the M5 also created zero synchronous pipelines. The evaluated 4.8
beta produced valid internal GPU times and a successful independently collected
Apple HUD report; it is not adopted because a sufficient gameplay benefit has
not been established. Instrumentation/preparation differed between engine runs,
so their frame rates are not a matched engine speedup comparison.

The harness now supports explicit native hardware benchmarks and process-local
`--metal-hud` capture without global privacy/HUD changes. Interrupted foreground
trials remain failed receipts. The isolated guest retains its known optional
Switch USB `_pipe2` warning; this work makes no new physical-controller,
Windows/Linux, HDR, sustained-thermal or macOS Game Mode claim. Image review is
agent evidence, not user artistic approval.

# Advanced match rules and temporary items — v0.25.0

The 2026-10-24 change adds a dedicated Advanced settings page for local/practice
and waiting online rooms. Each destroyed block independently rolls the configured
chance (55% by default), without the former guaranteed opening reward. Item
lifetime, authored block amount, blast/bomb caps, effect duration and twelve
item exclusions share local/server validation. Default caps are five; normal
starting inventory remains one fire / up to three bombs.

Initial map/training items previously used an unlimited lifetime. The retained
`--item-expiration` regression failed before the fix and now passes for both
battle and practice. Every item source uses the configured lifetime, with a
three-second visual blink. Tiny/Big combine size and speed; Haste/Slow are
speed-only project extensions. The [Nintendo manual research](status-items-research.md)
distinguishes those choices from the original game's documented behavior.
Collision dimensions remain unchanged; temporary states expire and reset.

Local receipts under `artifacts/iteration25/`:

- `public-retry`: public/CI/harness contracts and original audio pass. The first
  sandboxed attempt could not inspect its own child processes.
- `managed-final`: 2,705 game, 143 client, 53 AI and 19 audio checks, all 36 AI
  soaks, verified portable server packaging and real HTTP/WebSocket smoke.
  The network test edits advanced rules, observes the peer's accepted settings,
  and starts a match whose cover and inventory follow those rules.
- A newly exposed Rock Garden Easy / seven bots / seed 64 stall reproduced with
  `--soak --idle-regression`. Collision recovery had ignored small steering
  commands that movement accepts. Its longest open stationary window fell from
  16 seconds to 1.93 seconds; the full 36-case soak also passes at 1.93 seconds.
- `headless`: 868 checks across 21 scenario leaves. `package-final2`:
  v0.25.0/build 28 exported, signed and passed 252 bundled smoke assertions.
- `graphical-final2/playtest/20261024T171024.662672Z-all`: the final v0.25.0
  export passed all 38 leaves, 2,477 assertions and 233 captures with zero runtime
  errors in the isolated Metal VM. Advanced settings, Tiny/Big models, the
  before/after expiration arena and live room settings captures were inspected.
  The new journey also verifies Haste/Slow pickup, expiry and restored scale.
  The final game suite was rerun after moving the practice pickups and retained
  all 2,705 passing checks (`game-after-pickup-placement.log`).

Delivery completed on 2026-10-24: runtime source
`daa5f95d55f0c81256e9bd5199ec21a15d74e250` is on main.
[Verification 40115267087](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40115267087)
and [signed publication 40115771346](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40115771346)
succeeded; [v0.25.0 is the stable public release](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.25.0).
The downloaded update signature, checksums, source/version metadata, 217 Mac
runtime files and 42 server runtime files were verified. The established shared
service was updated only after complete lifecycle logs showed no active/waiting
public or private rooms. It is healthy, with all 42 running files matching the
public artifact, unchanged network exposure and the previous runtime retained.
Live production-client checks passed for advanced edits, eight-player rooms,
manual start, real results/rematch, leadership transfer, movement, bomb actions,
aiming and reconnect; the temporary operator tunnel was closed afterward.
Receipts: `public-release/verified-release.json`, `ci-result.json`,
`publication-result.json`, `server-deploy.log` and `deployed-network.log` beneath
`artifacts/iteration25/`. The local tested app was archived with all 217 hashes
verified; identity-checked cleanup removed its temporary launchable export and
preserved the existing installed app. No manual local installation was requested.

Earlier graphical attempts retained useful failures: the new practice items
were moved off ramps onto flat ground; the new witness was scoped to the arena
rather than menu preview models, and waits for accepted pickup snapshots.
Native hardware coverage and Apple notarization are not claimed by VM evidence.

# Isolated agent QA — 2026-10-24

Graphical macOS QA now runs the unmodified exported game in a dedicated local
macOS VM. Native focus, mouse hit testing, production input, actual Metal
Forward+ rendering and the existing assertions remain active inside that desktop.
The host has no VM window, input capture, clipboard sharing or audio pass-through.
The guest uses 4 CPU cores / 8 GiB RAM and stops after each run. A separate
explicit `--desktop shared` mode retains native hardware verification on a test
machine. No production service or installed game was changed.

The v0.24 exported app completed the same **37 scenario leaves**, **2,425
assertions**, **225 captures** and **zero runtime errors**, including actual
HTTP/WebSocket play against an owned, self-contained guest server and real audio
mix capture. Source authority, input and rendering code and the scenario catalog
were unchanged. The recent native baseline had 2,426 assertions: its bounded
network loop received one extra peer snapshot and repeated the same lighting
assertion. Both runs asserted that comparison and peer movement; the alternate
lighting preference varied with the server's selected lighting. No scenario or
assertion path was removed.

The guest runner took **235.169 seconds**, versus **232.049 seconds** for the
recent native baseline (different sessions, not a controlled GPU benchmark).
The host observer recorded 229 samples, 26 pointer changes, no VM foreground
activation and a minimum 34% system-wide free-memory reading. Launcher process
RSS/CPU values are not total guest resource usage. The user reported normal work
with no interruption or noticeable slowdown. Menu, night-stage and network
captures were visually inspected; this is not new artistic approval.

Receipts are under `artifacts/isolated-qa/`: `graphical/summary.json`,
`graphical/playtest/20261024T124419.329218Z-all/`,
`native-baseline.json` and `coverage-comparison.json`. The separate menu trial
passed 30 checks / 7 captures; real fullscreen toggles passed 7 checks / 2 captures.
The walk recording passed 52 checks / 5 captures and exported a 19.4-second
30 FPS MP4 whose 582 frames were fully decoded. Sampled video frames were
visually inspected. The default source-export path also passed 30 menu checks /
7 captures and cleaned up its temporary bundle. Public tooling/audio checks and
workflow lint also passed. Failure receipts retain the initial signature
copy issue; using `ditto` preserves the macOS signing metadata. The actual image
was independently verified against OCI manifest
`sha256:4aaa751fa7538902d73c767f27a066fc87e83f183069da4e4b5022863abd4c77`;
the standard downloader failed, and a checksum-verified HTTP/1.1 transfer
completed it. This was a one-time setup operation, not a new runtime dependency.

Limits: the guest is macOS **26.6.2**, using **Apple Paravirtual device (Apple5)**.
The existing optional USB library needs `_pipe2` from macOS 27 and reports a
loader warning in this guest. Synthetic controller tests still pass; USB backend
loading, physical buttons/rumble, native GPU/display performance and physical
speaker output require compatible native hardware checks. Those paths remain
available explicitly; VM receipts do not certify them. The default fails if the
VM is unavailable rather than taking over the developer's desktop.

# v0.24.0 stage lighting — delivered, 2026-10-23

All three stages now support **Morning, Midday, Afternoon and Night**. Settings →
Local lighting saves a personal choice for local play. Online snapshots carry
the server's choice: the match seed selects the first variant and successive
rounds cycle all four without consuming gameplay randomness. Late joins and
reconnects use the same snapshot, and local preference edits cannot override it.
The v0.23.1 **35% ordinary item drop probability** is preserved.

Lighting profiles coordinate sunlight/moonlight, fill, ambient, sky, fog and
water. Procedural sky reflections replace the fixed daytime water tint; filtered
fine ripples, depth-limited shoreline foam and wet stone relief improve surface
response. A close flood capture exposed foam along submerged tile seams; vertical
depth gating removes that grid while retaining exposed contact edges. No terrain,
collision, character proportions or authority water rules changed.

Source gates passed **1,475 game / 143 client / 53 AI / 19 audio checks**, **36 AI
soaks**, a real staged-server network smoke, and **868 headless assertions**.
The final windowed graphical run completed **37 scenarios, 2,426 assertions,
225 captures and zero runtime errors**, including all twelve stage/time views,
night aiming, real flooding, settings persistence and server lighting precedence.
Normal arena views and four close shoreline views were visually inspected.
This does not imply user artistic approval or new physical-controller testing.

Source receipts: `artifacts/iteration24/{baseline,first-look,managed,headless,graphical}`;
final native run `graphical/playtest/20261023T232734.727626Z-all/`.
The rebuilt **v0.24.0/build27** Mac export passed **252 package-smoke assertions**
and the complete **37-scenario graphical suite: 2,426 assertions, 225 captures,
zero runtime errors**. The four exported lighting looks were reviewed, and the
strict recursive signature check passed. Its 217-file/symlink inventory and a
local archive are retained under `artifacts/iteration24/`.

Runtime source `4f774e8ce9858e2f414c41694e8c947b82cd4d3a` is pushed to main.
[Verification 40040566256](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40040566256)
and [signed publication 40041286969](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40041286969)
passed. [v0.24.0](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.24.0)
is published with matching Mac/server artifacts. The public manifest signature,
archive checksums, source/version metadata, bundle identity and server inventory
were independently verified. The Mac ZIP is 64,744,057 bytes, SHA-256
`9bf50e059b8d08f3061a170454142be9b2e6d35303488f482a9fedf2e7650a89`.
A partial CLI download timed out; an HTTP/1.1 resume completed it and the final
hash matched. The three AppleDouble license metadata entries are outside the
217 runtime app files and were checked against their corresponding ZIP members.

The existing shared server runs the exact published runtime: **42 running file
hashes matched**, the game registration endpoint is healthy, and network exposure
is unchanged. Complete process lifecycle logs confirmed no active or waiting
public/private rooms before the restart; the previous deployment is retained for
rollback. One bounded release-matched exported-client journey through a temporary
operator SSH tunnel passed **50 assertions / 3 captures / zero runtime errors**.
The actual two-player match was visually inspected and the tunnel was closed.

Unrelated QA-launcher work began during publication. Its new isolated default
rejected a custom server before launching; the successful final check used the
runner and catalog frozen from the release commit. Those ongoing edits were
preserved and excluded from this release. No local app replacement, party sharing
or public hosting configuration was performed.

Delivery receipts: `artifacts/iteration24/{release-public,release-package,release-graphical,public-release,ci-result.json,publication-result.json,server-deploy.log,server-runtime-verified.json,deployed-network}`.

# v0.23.1 item drop tuning — delivered, 2026-10-23

Ordinary destroyed blocks now drop an item with **35% probability**, down from
70%, to reduce clutter. Each spawn area's guaranteed opening fire/bomb reward,
regional cover layout and weighted item mix remain unchanged. Local and online
matches use the same authoritative rule.

The existing real-destruction regression first failed at a 70.6% ordinary drop
rate. After tuning, 32 seeds per stock map measured 34.1% on Sunken Temple,
34.6% on Rock Garden and 33.9% on Up & Down, with opening rewards and all eight
item types preserved. Managed verification passed **1,473 game / 143 client /
53 AI / 19 audio** checks, **36 AI soak scenarios**, and complete HTTP/WebSocket
smoke against the staged Release server. Public checks, **868** headless core
assertions, the **25**-assertion opening-loot journey and **252** rebuilt Mac
package assertions also passed. The first sandboxed managed run could not start
its temporary server; the outside-sandbox retry passed the full profile.

The v0.23.1/build26 export passed all **36 windowed scenarios: 2,268 assertions,
201 captures, zero runtime errors**. The actual court reward capture was reviewed;
strict recursive code-signature verification also passed. Controller coverage
remains synthetic, with no new physical-device claim.

Receipts: `artifacts/loot-rate-tuning/{red.log,managed-retry,public,headless,loot-opening,package,graphical}`.

Source `845cc4179c35233cc7ccc958b7b4b8123278a645` is pushed to main.
[Verification 40038592623](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40038592623)
and [signed publication 40039336510](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40039336510)
passed. [v0.23.1](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.23.1)
was published successfully. The public update signature, Mac archive hash,
bundle identity/version and server inventory were independently verified.

The existing server now runs the exact published v0.23.1 runtime, with all **42**
file hashes verified and a healthy game registration endpoint. There were no
active or waiting rooms at cutover; the previous deployment remains available
for rollback. One real client lobby/match journey through a temporary operator
SSH tunnel passed **37 assertions / 3 captures / zero runtime errors**. It used
the v0.23.1 exported app and release-matched runner/catalog, preserving unrelated
source work that began during CI. The full multi-client suite already passed
against CI's staged artifact, so it was not repeated on the live host.

The user clarified that the service is intended for all users and the former
same-Wi-Fi friends-party workflow is no longer active. The
[shared-server guide](online-server.md) now owns routine deployment scope;
public hosting is deferred to a separate task. No LAN party setup, sharing ZIPs,
local app replacement or public endpoint changes were part of this release.

Public Mac ZIP: 64,733,322 bytes, SHA-256
`c93ce00b97e76b8c14551d3be9b40b5b16a47e5eb3f457eea184af8502609eed`.
Delivery receipts: `artifacts/loot-rate-tuning/{public-release,ci-result.json,publication-result.json,server-deploy.log,server-runtime-verified.json,deployed-network}`.

# v0.23.0 sandbox and lobby update — delivered, 2026-10-23

Implemented the wider five-tier Pyramid with rear water pitfalls, larger legacy
arenas and more cover, leader-only room-rule editing, two replaceable equipment
slots, bouncing/fast bombs and Focus, timed charging, heavier charged throws,
blue amplified red-bomb explosions, fuse-free remote bomb art, and ghost/bomb
interaction removal. The latest bounce correction is explicit: ordinary rolls
ricochet from walls; only charged throws rebound from ground. Stop still works.

Final local gates passed before the release commit/push: **1,473 game / 143 client /
53 AI / 19 audio** checks, **36 AI soak scenarios**, and real HTTP/WebSocket smoke
against the staged Release server. Public contracts passed. Headless core passed
**868** assertions; rebuilt Apple Silicon package smoke passed **252**. The final
export completed **all 36 windowed scenarios in one run: 2,268 assertions,
201 captures, zero runtime errors**. Reviewed captures include the wider Pyramid,
rear fall, both lobby settings buttons, live room-rule editor, ordinary rolling
bounce bomb and blue blast. Controller input coverage is synthetic, not a new
physical-device claim.

Receipts: `artifacts/iteration23/{cover-public,cover-managed,cover-headless,cover-package}`
and `cover-graphical-retry/playtest/20261023T201421.112911Z-all`. Focus interruptions and
superseded geometry/test assumptions remain in earlier receipts. The final run
includes ordinary input/real pickups, shared throw projection, expiry and equipment
sharing, host settings reaching the guest, and a real two-client match.

New regressions exposed and fixed: slight stick input must override Focus; ghosts
must not kick or grab bombs; AI chain prediction must include the authority's
half-cell margin; timed escape movement must permit crossing threatened tiles
before the fuse; failed orient/place attempts must resume navigation. Final soak
has no stationary-with-open-exit failure. Stock Pyramid's larger-map first-pressure
budget is eight seconds (old smaller map: six), retaining five threats per 30 seconds,
a maximum eight-second gap, and zero self-deaths in its four seeded fixtures.

The final follow-up adds map-specific regional quotas and clustered cover, rather
than uniform density: 90 Pyramid blocks emphasizing the lower court, 110 balanced
Rock Garden blocks, and 88 Up & Down blocks on wider platforms/outer pockets.
Fire/Bomb each receive 35% of ordinary drops. Layout and opening-reward checks
cover 32 seeds per map; a native journey reaches the lower court, destroys cover,
retreats, and collects its actual reward. The richer loot layout permits farming
detours; CPU goals now distinguish useful upgrades and rival threats from crate
farming. Its fixed stock pressure cases produce five to seven threats in 30 seconds
with no self-deaths. The previous six-threat minimum is superseded explicitly.

[Verification 40018017728](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40018017728)
and [automatic publication 40018980790](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40018980790)
succeeded for source `62d5378ba735ec133f01495aa9045a0dd939e3d7`.
[v0.23.0](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.23.0)
is published with signed update metadata and matching Mac/server artifacts.
All required CI jobs passed; the optional hosted Metal lane was skipped, with
actual rendering evidence supplied by the local exported run above.

The public manifest signature, archive hashes, version/source metadata and all
42 server runtime files were independently verified. The chosen Docker host now
runs that exact runtime; the previous deployment was retained for rollback.
The complete remote HTTP/WebSocket smoke passed through the existing LAN relay
in **81.3 seconds**, including 2–8 humans, reconnect, party lifecycle and seven CPUs.

The production updater from the actual installed v0.22 app downloaded the public
release and completed a healthy **v0.22 → v0.23** in-place replacement. The installed
v0.23.0/build25 app matches all **217** public archive files/symlinks and passes strict
recursive code-signature verification. Its normal-map reward journey passed
**43 assertions / 6 captures**; its actual remote lobby/settings/game journey passed
**37 assertions / 3 captures**, both with zero errors. The Downloads ZIP and existing
party share now contain the exact public ZIP; identity-checked cleanup leaves only
`~/Applications/Blast Circuit.app` launchable.

Public Mac ZIP: 64,733,282 bytes,
SHA-256 `957a1d54e3b1541f0d1cfdea5e61f3d1b76ac4087c1b720d48e52e72fcd8e4ac`.
Delivery receipts are under `artifacts/iteration23/public-release`,
`installed-public-update`, `installed-cover`, `installed-network`, and the
`divemac-*`/cleanup receipts. The v0.22 history below remains dated evidence.

# v0.22.0 combined release — 2026-10-23

The party-polish work below is now integrated with the server performance patch:
room-code copying, saved/chosen names, first-to-two defaults and explicit timing,
kick-stop input, pyramid downhill traversal/rear closure, water-ghost recovery,
four-second snapshot history and server performance diagnostics.

The merged Release server passed **950 gameplay cases** (including the 789-case
pyramid sweep), **143 client / 52 AI / 19 audio** checks, **36 AI soak scenarios**,
and the complete real HTTP/WebSocket smoke. Headless core passed **895** assertions;
package smoke passed **258**. The combined exported app completed **all 34 native
scenarios in one passing run: 2,288 assertions, 184 captures, zero runtime errors**.
Receipts: `artifacts/iteration22-combined/managed-fixed`, `headless`, `package`,
and `graphical/playtest/20261023T182011.666245Z-all`. Actual lobby/copy confirmation,
ghost return and setup captures were reviewed.

Integration exposed a real short-input problem: a press and release delivered in
one server step overwrote each other before simulation. A deterministic authority
regression first failed on a batched bomb tap, then passed for bomb and kick-stop.
Humans now retain up to 16 action transitions, consume one per tick with the newest
movement axes, and clear the queue on disconnect/round/respawn resets. The network
stop test also uses the temple's clear row, avoiding random nearby cover that can
stop a bomb before a round trip completes. Its brief Stop tap is observed by a
second client, including stationary X/Z afterward. The failed first merged network
receipt is retained under `artifacts/iteration22-combined/managed`.

The performance-only change was measured separately against the v0.21 baseline;
see [server performance](server-performance.md). Those same-state comparisons must
not be confused with the intentional gameplay changes in this combined version.
[Verification 40003769404](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40003769404)
and [automatic publication 40004810095](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40004810095)
succeeded for merge `89d90a8082e7a091fad1f5318eff94e543336bf5`.
The [v0.22.0 release](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.22.0)
was downloaded and independently checked against its signed manifest and app/server
inventories. DiveMac runs that exact verified CI server; its complete real network
smoke passed in **81.9 seconds**, including queued Stop and chosen/blank names.

The actual installed v0.21 updater then fetched the public v0.22 release, verified
it, replaced the app and confirmed a healthy relaunch. All **217 installed files**
match the published archive and strict signature verification passed. The published
app passed the feature journey (**36 checks / 5 captures**) and real DiveMac network
journey (**32 checks / 2 captures**), with zero runtime errors. Receipts are under
`artifacts/iteration22-combined/published` and `real-update`. The current ZIP is also
in Downloads. SHA-256: `a583fff7dc1752c4bf571f419c7ea8bea5d14e2acae2a6fa702af6014e571bc7`.

A fresh production 3-human/3-CPU sample averaged **7.5 KB snapshots / 0.322 MB/s
aggregate snapshot traffic**, versus roughly **26 KB / 1.07 MB/s** in the clean
baseline probe. These different-seed live samples are descriptive; the controlled
performance-only comparison established the byte/allocation reduction separately.
Rare delivery jitter remains possible; it is not represented as eliminated.

# Party-polish source verification before integration — 2026-10-23

Implemented lobby Copy code with clipboard confirmation; explicit name editing and
saved name/server URL; chosen names on the character and HUD with seat fallback;
first-to-two wins and an explicit 1:00 normal + 1:00 sudden-death default; immediate
water rise; water/void ghost returns to dry ground; a sealed rear temple passage;
and a separate kick-stop command (O / period / right shoulder).

The original [Nintendo manual](https://manuals.plus/m/a8e88edc86a9ce47e982bda2351d4c7d9fe8c079505b31257b5768b4c5e9907e)
confirms N64 R stops a kicked bomb. The product keeps keyboard R as a remote alias,
uses controller R/RB/R1 for stop, retains timed fuses and continues gravity when
stopping an airborne kick. This is a project mapping, not an exact physics claim.

The bomb report reproduced at four mirrored diagonal courtyard stair entries.
The center had descended slightly while its trailing radius still overlapped the
upper floor; the next rolling sweep treated that existing overlap as an uphill
wall. Allowing only shrinking existing overlap fixes it without weakening pillars,
new uphill contacts or placement. The exhaustive fixture covers **789** downhill
cardinal/diagonal edge and alignment cases on exact pyramid terrain, with cover
removed to isolate geometry. A native journey also kicks across a real stair corner.
Water casualties previously became ghosts at the water plane and were eliminated
again on the next movement tick. They now return to clear dry land and stay on it.
Ghosts remain disabled during sudden death and when the room disables comeback.

Managed validation passed **947 gameplay cases, 143 client checks, 52 AI checks,
19 audio checks, 36 AI soak scenarios and real HTTP/WebSocket smoke**, against the
staged Release server: `artifacts/qa/party-polish-managed-final/summary.json`.
The focused native feature journey passed **36 assertions / 5 captures**, and the
controller journey passed **767 assertions / 13 captures**, both with zero runtime
errors. Receipts: `artifacts/playtest/20261023T171231.458679Z-party-polish` and
`artifacts/playtest/20261023T173434.663953Z-controllers`.

Final headless core passed **895 assertions**, zero runtime errors
(`artifacts/qa/party-polish-headless`). All **34** windowed catalog scenarios have
passing completed-leaf coverage: the full run in `artifacts/qa/party-polish-graphical-complete`
completed 33 scenarios before the new network-name assertion mistakenly used a
local-player lookup. After correcting that helper, the separate native network
journey passed **32 assertions / 2 captures**, including actual clipboard content,
chosen-name meshes and HUD text. The same owned server then passed the expanded
2–8-client smoke, including a queued brief Stop tap observed by the other client
and an empty-name guest becoming P1. Receipt: `artifacts/qa/party-polish-network`.
The full-run receipt remains failed; composed completed-leaf coverage is recorded
explicitly in `artifacts/qa/party-polish-windowed-coverage.json`, with no missing
catalog leaves. Reviewed actual online, setup, ghost, diagonal-kick, lobby and
network gameplay captures. Before/fixed ledge receipts are retained in
`artifacts/qa/party-polish-repro`.

The first UI test incorrectly assigned server text programmatically (Godot does
not emit TextChanged for that); the corrected test types through native input.
Visual review corrected clipped timing copy and badges overlapping names.
The full suite caught and corrected a disabled Copy code button taking controller
focus, plus old win-target expectations. Earlier failed receipts are retained.
Performance-task changes were moved into an independent checkout before final
verification; they are not included here. No installed app, release or external
server is changed by this source verification. Controller evidence is synthetic,
not a fresh physical-device observation.

# v0.21.0: persistent parties, player settings and signed updates — 2026-10-23

Private rooms now wait for an authenticated leader's Start game command, including
solo-host rooms with CPUs. A completed match returns all connected players to the
same room/code after results; the next match also needs the leader to start it.
Disconnect transfers leadership. Esc/Start opens a game menu; local authority
pauses, while online authority continues with neutral local inputs. Settings saves
volume, vibration (off by default) and automatic update checks (on by default).

| Check | Result |
|---|---|
| Gameplay / client / AI / audio | **152 / 139 / 52 / 19** checks passed |
| AI soak | **36 scenarios**, longest eligible safe idle 1.63 seconds |
| Staged server HTTP/WebSocket | Full smoke passed, including eight production party clients, manual start, same-room results/rematch and leader transfer |
| Headless core / bundled smoke | **895 / 258 assertions**, zero runtime errors |
| Full rebuilt export | **2,245 assertions, 179 captures**, zero runtime errors |
| Actual DiveMac Docker route | Full network smoke passed in **75.0 seconds**; installed native journey **27 assertions, 2 captures**, zero runtime errors |
| macOS updater integration | Real app handoff and successful launch passed; a deliberately broken signed executable restored and relaunched the previous app |
| Publication tooling | Public contracts, signature/download boundaries, promotion guards and actionlint passed |

Final receipts: `artifacts/iteration21-delivery/`; updater installation/rollback:
`artifacts/iteration21-party/update-integration`. The installed v0.21.0/build22
matches all 217 validated export files, passes strict signature verification,
and Launch Services smoke produced 58 snapshots with zero stderr. A fresh ZIP is
also in Downloads for the one-time upgrade from pre-updater builds.

The initial authority regression failed because a solo room with bots generated
state without a leader command. The first full GUI run exposed an obsolete test
expecting the lobby's sole Leave button to retain focus; the lobby now also has
Settings. The updated check verifies real analog navigation to Settings, and the
complete exported run above passed. Earlier receipts remain under
`artifacts/iteration21-party/`.

Vibration tests cover disabled-by-default behavior, saved configuration, backend
routing and one pulse per accepted event, not fresh physical motor observation.
The updater fixture verifies signatures, archive checks and the production
installer in isolated locations; it is not evidence of a published GitHub release.
The user authorized publishing v0.21 and enabling signed releases after successful
trusted main verification. The public release workflow is gated by those checks;
its hosted run and published assets are separate from the local receipts above.

The public release is now [v0.21.0](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.21.0), built from `c9febec6df5a396b2ca923d4a060c102a0c80744`.
[Verification 39982931556](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/39982931556)
passed public, managed/network, headless and clean Apple Silicon packaging; the
optional self-hosted Metal job stayed disabled. [Automatic publication 39983991742](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/39983991742)
signed and published the checked artifacts. The downloaded release's signature,
app hash, identity/source metadata and server inventory were independently verified.
The production updater discovered v0.21 from v0.20 through the real public feed,
verified/downloaded it, and correctly offered nothing to an already-current v0.21.

The canonical installed app and Downloads ZIP were then replaced with the exact
published CI artifact (all 217 files matched; strict signature passed). That build
passed Launch Services smoke with **57 snapshots**, the real DiveMac network
journey with **27 assertions**, and settings/menu/haptic routing with **35
assertions**, all without runtime errors. The published ZIP SHA-256 is
`e96fba1ff14652dda02841d336d4046a127ed9d2c88459ca301343b416135058`.
Receipts: `artifacts/iteration21-delivery/published`. Future new-version main builds
publish automatically only after the required verification gates pass.

# v0.20.0: gripping cartoon scamper — 2026-10-23

The walk was rebuilt after the user again found it stiff and cheap-looking; see [iteration twenty](history/iteration20-walking.md). The new movement check reproduced planted-boot skating at **.948** of body speed before the change (`artifacts/iteration20-walk-red`); the rebuilt gait measures **.04–.08** across 30/60/120 FPS. Gameplay rules, speed, collision, networking and controls are unchanged.

| Check | Result |
|---|---|
| Movement (source) | **69 assertions**, including skating, hidden tuck/slide, antenna follow-through |
| Recorded `walk-feel` (source) | **52 assertions**, side/three-quarter/battle camera, frames inspected |
| Full source graphical journey | **2,203 assertions, 177 captures**, zero runtime errors (`artifacts/iteration20-graphical`) |
| Bundled package smoke | **258 assertions**, zero runtime errors (`artifacts/iteration20-package`) |
| Full exported Mac journey | **2,202 assertions, 177 captures**, 32 scenarios, zero runtime errors (`artifacts/iteration20-exported-graphical/playtest/20261023T144212.754142Z-all`) |

The installed **v0.20.0** (build 21) app matches all **217** tested export files, passes strict signature verification, and produced **57 gameplay snapshots** with zero stderr in Launch Services smoke. Identity-checked cleanup removed the v0.19.1 backup and the export, leaving only `~/Applications/Blast Circuit.app`. Receipts: `artifacts/iteration20-install`. Before/after clip: `artifacts/iteration20-walk-compare.mp4`. The new walk is mechanically verified, not yet approved by the user.

# v0.19.1: adjacent Switch B charging — 2026-10-23

The user's physical Switch 2 follow-up found Y+A still uncomfortable. Printed **B** is now an additional primary/bomb input: hold **Y** and tap **B** to ready/pump, then release Y to throw. A remains available, X remains remote detonation, and menu B remains Back. Both held-button sampling and short-tap buffering recognize the alias. HUD hints, Controls, and the README teach the new combination.

The rebuilt exported app passed **764 controller assertions with 13 captures and zero runtime errors**, including Y+B creation, held B pumping once, a sub-frame B pump, A compatibility, X remaining remote-only, release-Y flight, and neutral arming. Receipt: `artifacts/iteration19-b-charge-controls/20261023T141935.379905Z-controllers`. Package/headless smoke passed **248 assertions** in `artifacts/iteration19-b-charge-package`. The Y+B gameplay capture and Controls screen were visually inspected. This is synthetic native-path verification, not a new physical-controller observation. Simulation, networking and other controller-family mappings are unchanged by this follow-up.

The installed **v0.19.1** app matches all **217** tested export files, passes strict signature verification, and produced **58 gameplay snapshots** with zero stderr in Launch Services smoke. Identity-checked cleanup leaves only the canonical installed app. Installation receipts: `artifacts/iteration19-b-charge-install`.

## Iteration nineteen: controller carry, cursor aiming and blast-vulnerable knockdowns — 2026-10-23

| Check | Result |
|---|---|
| Gameplay / client / AI / audio | **139 / 123 / 52 / 19** checks passed |
| AI soak | **36 scenarios**, longest eligible safe idle 1.63 seconds |
| Real HTTP/WebSocket smoke | Passed against the exact newly staged Release server, including retained cursor aim |
| Source headless core | **885 assertions**, zero runtime errors |
| Full exported Mac journey | **2,184 assertions, 176 captures**, zero runtime errors, 169.696 seconds |
| Final exported controller follow-up | **756 assertions, 12 captures**, zero runtime errors |
| Final bundled package smoke | **248 assertions**, zero runtime errors |
| Public contracts | Passed, including process cleanup and bundle identity checks |

The complete exported run is `artifacts/iteration19-delivery-graphical/playtest/20261023T134026.118677Z-all`. Its controller, aim-input and reticle trials all completed on their first attempts. Visual review then caught one stale B-carry label in the Controls panel. Only that panel's static/live label copy changed afterward. The rebuilt final export passed `artifacts/iteration19-final-package` and the full controller scenario at `artifacts/iteration19-final-controls/20261023T134407.112609Z-controllers`, also with no focus retries. The complete gameplay suite is explicitly the run before that narrow panel-copy correction.

The first graphical attempt exhausted its real-focus retries; the next completed input checks but exposed a stale reticle test that still pressed S to shorten a throw. The test now uses W to move a south-facing cursor inward and stops on accepted range, preserving its obstructed-warning assertion. A separate 40-check reticle run and the complete run above pass. Earlier failures remain under `iteration19-exported-graphical*`; they are not counted as successful runs. A public-profile process test initially encountered sandbox-denied `ps`; the final unsandboxed profile passed. Managed/source/package/public receipts use the corresponding `iteration19-*` directories.

The new controller recipe promotes **L/LB/L1 carry**, with **Y/X/Square** face alternatives for Switch/Xbox/PlayStation. Hold **ZR/RT/R2** and steer the target with either stick or D-pad. Keyboard retains **J bomb, K carry, L aim, I remote**; L + WASD moves the target in arena directions. Both input families share a 6-cell/second cap and fine-start ramp. The 0–9-cell range lets the cursor cross through the carrier; close self-lobs retain their actual hit risk. There is no target lock. The [research note](history/iteration19-controls-research.md) compares three designs against primary sources and records the selected policy and limits.

A retained failing authority test reproduced a remote bomb knocking down a player, exploding beside them, and failing to kill despite overlap. `KnockDown` wrote the same timer checked by blast damage. Contact now uses an independent 0.35-second impact cooldown; blast damage still respects real heart/respawn protection. Grounded stunned players can be hit again after the cooldown. Tests cover normal and practice snipes, heart consumption without repeated lingering-blast damage, and chained falling-bomb knockdowns. The native `grapple` journey collects a real remote pickup, kicks its bomb into another human seat, and detonates during impact flight; the death event and practice respawn are observed. Red/green logs remain in `artifacts/iteration19-regressions`.

The installed **v0.19.0** app matches all **217** files in the validated export, passes strict recursive signature verification, and completes Launch Services gameplay smoke with **58 snapshots** and zero stderr. `artifacts/iteration19-install` retains manifests, logs and cleanup receipts. Identity-checked cleanup removed the previous backup and the export; only `~/Applications/Blast Circuit.app` remains launchable in the scanned locations.

Updated Controls, keyboard aim, blocked-reticle and remote-snipe captures were visually reviewed. Controller events are synthetic, including Switch values through the production six-axis sampler; this does not certify fresh physical hardware behavior or human-rated comfort. The portable server was staged and tested on an owned ephemeral loopback listener. Existing external servers require the matching new build; this pass does not deploy them. Hosted CI evidence is separate from these local verification receipts.

## Iteration eighteen: coordinated walking and keyboard correction — 2026-10-21

| Check | Result |
|---|---|
| Whole-rig movement | **59 checks**, including body/shoulder/head motion at 30/60/120 FPS |
| Full exported Mac journey | **2,064 assertions, 172 captures, zero runtime errors** |
| Headless core / bundled package smoke | **840 / 247 assertions** passed |
| Final walking recording | **52 checks, 5 captures**, 608 decoded frames / 20.2667 seconds |
| Native reticle recording with new keys | **40 checks, 15 captures**, 413 decoded frames / 13.7667 seconds |
| Tooling | Public contracts pass, including six duplicate-app cleanup safety tests |

The full exported run is `artifacts/iteration18-final-graphical/playtest/20261022T022345.367662Z-all` (162.332 seconds including build/import). Reticle inputs lost real application focus twice; the existing bounded retry restarted whole trials and the third completed. Functional assertions were not selectively retried. A later visual review found that the new three-quarter *inspection camera* hid the actor behind the court wall. Only that harness camera was corrected; a fresh export/package smoke and focused 52-check walking recording verify the final view. Gameplay rendering, controls and motion are identical to the full passing run.

The body was moving, but too subtly to carry the gait. A new whole-body regression failed on .024-unit lateral travel before the change. A waist pivot now separates shoulder follow-through from hip motion; opposite arms share the foot phrasing, the head counterbalances, and the stride cadence slows while player speed remains unchanged. Head proportions and fixed short legs remain intact. [Measured bounds, rig details and limits](history/iteration18-walking.md).

Keyboard controls are **J bomb, K carry, L aim, I remote**, with Shift/R aliases. Native checks verify K+J pickup, K+L simultaneous hold/aim, release-L retention, and release-K throw. Space has no gameplay action. The reticle has no new labels. Controller mappings and second-keyboard controls are unchanged; synthetic tests do not certify fresh physical-controller behavior.

The final [walking clip](videos/walk-personality.mp4) contains 397 frames / 13.2333 seconds at 1×, 1280×800/30 FPS. Source `iteration18-final-walk-clear/20261022T022833.287351Z-walk-feel` segments are [199,340) side, [344,472) three-quarter, and [476,604) normal battle camera. Every section was decoded and visually inspected; actor movement comes from ordinary inputs. The full [reticle clip](videos/throw-reticle.mp4) comes from `iteration18-reticle-video/20261022T022900.857695Z-reticle` and distinguishes normal match actions, separate renderer contracts and actual Results cleanup. Screenshots and controls in README were refreshed.

The installed v0.18.0 app matches all 217 export files and passes strict signature verification. Duplicate app bundles are removed only after installation and launch verification, using exact identity and payload checks; ZIPs, receipts and unrelated applications are preserved. Eight duplicate bundles were removed; the final filesystem inventory and Spotlight both report only `~/Applications/Blast Circuit.app`. Its Launch Services gameplay smoke produced 58 snapshots and zero stderr. Two renamed backups had no Launch Services registration; that explicit missing-entry case is now handled and tested, while unrelated errors stop deletion. Final cleanup evidence is retained under `artifacts/iteration18-install`. Hosted checks are verified after push; local Metal evidence remains separate from the intentionally disabled hosted GPU lane.

## Iteration seventeen baseline — 2026-10-21
| Check | Result |
|---|---|
| Gameplay / client / AI / audio | **136 / 118 / 52 / 19** checks passed |
| AI long-match matrix | **36 scenarios**, longest eligible safe idle 1.63 seconds |
| Exact staged server and real HTTP/WebSocket smoke | Passed, **51.8 seconds**, including two-client aim; portable server packaged |
| Full exported Mac regression | **2,028 assertions, 171 captures, zero runtime errors**, 105.096 seconds including build/import |
| Headless core / bundled package smoke | **828 / 235 assertions** passed |
| Exported text-free reticle recording | **40 checks, 15 captures**, 406 decoded frames at 30 FPS |
| Exported walking recording | **36 checks, 4 captures**, 473 decoded frames at 30 FPS |
| Installed app | **v0.17.0**, all 217 files match export, strict signature and Launch Services smoke passed |

The final full run is `artifacts/iteration17-final-graphical/playtest/20261022T001243.090587Z-all`. It exercises the exported app with an owned ephemeral local server, not an old running developer server. Managed, public, headless and package receipts are under their corresponding `artifacts/iteration17-final-*` folders. No new physical-controller certification is inferred from synthetic input events. The installed app produced 57 gameplay snapshots and zero stderr. The matching project-owned localhost server was refreshed; external servers were not changed.

The missing reticle was reproduced in a normal local Sunken Temple match with a bomb overhead. Cover produced an `Obstructed` endpoint, but the old renderer only showed a landing marker. Safe endpoints now have an outlined ring; blocked/unsafe endpoints an X; offscreen endpoints an edge arrow. There are **no reticle text labels**. A second regression ends a real duel while its winner still carries a bomb and verifies that Results clears the overlay. [Diagnosis and checks](history/iteration17-reticle.md).

Keyboard play uses WASD + Space carry, J bomb/pump, K aim and L remote; Shift/R remain aliases. Native input checks confirm that K cannot also carry/release a bomb. [Observed Persona 5 Royal PC UI and design limits](history/iteration17-keyboard-research.md).

Walking retains short fixed legs and approved head proportions. The old return curve exceeded intended reach by 40% and compressed almost an entire body drop into one 30 FPS frame. Bounded eased reaches and a symmetric bounce reduce actual maximum leg angle from .671 to .480 radians and maximum per-frame body-Y change from .0240 to .0139, with smaller glove/shoulder swings. Measurements establish continuity, not subjective charm. The [new walking clip](videos/walk-personality.mp4) is 269 frames / 8.9667 seconds at 1×: 4.7 seconds with an inspection camera, then 4.2667 seconds with the normal battle camera. Source is `iteration17-final-walk-video/20261022T001628.599125Z-walk-feel`; selected source frames are [196,337) and [341,469). Both segments were decoded and visually inspected.

The [text-free reticle recording](videos/throw-reticle.mp4) is the full 406-frame / 13.5333-second export from `iteration17-final-reticle-video/20261022T001520.532290Z-reticle`. It contains normal local keyboard play, explicitly separate renderer contracts, and an ordinary duel ending while carrying. It is not a performance benchmark. README screenshots were refreshed from the final exported run, including model angles, current controls, temple, enlarged pickup icon and clean reticle.

Loot now has balanced nearby temple cover, an opening fire/bomb reward per spawn area and a 60% later drop chance. Pickup symbols scale 1.30× while plates stay unchanged. The native `loot-opening` journey uses ordinary movement, placement, fuse, blast survival and collection; it verifies inventory and model removal without editing actor state.

AI tests reproduced both reached-goal loitering and refusal to take a necessary ramp detour. In matched stock-Pyramid Medium seed64, pressure rose 4→7 over 30 seconds and longest gap fell 12.27→6.93 seconds with no bot death. Other seeds/difficulties vary; normalized soak pressure rose 1.83% overall, 1.20% Medium, 26.47% Hard, while Easy fell 14.51%. No speed, inventory, cooldown or escape-safety advantage was added. [Fixture definitions, measured tables and limits](../tests/Bomber.Ai.Tests/README.md).

Local Mac signing remains ad-hoc. Hosted graphical CI remains intentionally disabled; local Metal captures supply that evidence. Hosted verification for this commit is checked after push, separately from these local receipts.

## Iteration sixteen baseline — 2026-10-21
| Check | Result |
|---|---|
| Gameplay / client / AI / audio | **134 / 118 / 36 / 19** checks passed |
| AI long-match matrix | **36 scenarios** passed |
| Published server and real network | Existing 2–8-player/reconnect/bot smoke plus two-client precision aim passed (51.7 seconds); exact staged server packaged |
| Final exported Mac regression | **1,928 assertions, 153 captures**, zero runtime errors (134.078 seconds) |
| Exported package smoke | **218 assertions**, headless bundled-asset/motion/throw checks |
| Focused native aim input | **103 assertions, four captures**, actual keyboard and canonical controller paths |
| Compact-leg motion | **45 assertions**, including fixed limb length, hip attachment and continuity at multiple sampling rates |
| Normal-speed aim recording | **32 assertions, seven captures**, 335 decoded frames at 30 FPS |

The full exported run is `artifacts/iteration16-verified-all/playtest/20261021T222206.448549Z-all`. A subsequent direction review found right/left aiming mirrored on the stage; a new all-cardinal regression reproduced it, and right now turns clockwise. The correction was verified with 134 game checks, 103 native input checks, real network aim/server tests and a rebuilt exported-app smoke/aim recording. The earlier full UI suite was not represented as a new run after that narrow correction. Its complete controller attempt passed first time with **zero focus retries**. Package evidence is `artifacts/iteration16-direction-package`; managed/server delivery evidence is `artifacts/iteration16-direction-managed`. The preceding core-headless run (`artifacts/iteration16-headless3`) passed 783 assertions; later test-only snapshot waits add explicit accepted-state checks, and the final exported suite above exercises them. No fresh physical-controller certification is inferred from synthetic events.

The user rejected the extending white legs. A regression measured 2.49× stem extension and a .27-unit hip gap in the prior animation. The final legs retain fixed length and hip attachment; boot pitch/reach/recovery are continuous. The suit is about 8% slimmer while helmet, gloves and boot sizes preserve the chibi silhouette. [Inspected official renders, original references and actual measurements](history/iteration16-character.md).

Space now holds a bomb, J places/pumps and Shift enters precision aim; controller right triggers provide the same mode. Native Switch 2 polling was found to omit trigger axes despite synthetic events passing. It now samples all six canonical SDL axes, with a regression through the same sampling/publication code. P2 prompt detection and small-stick movement with ignored Aim were also corrected. [Original control evidence](history/iteration16-controls-research.md), [authority and fairness rules](history/iteration16-aim.md).

Static throw prediction and authority now share swept terrain/cover collision, actual water height, perched landings and support-loss gravity. A native regression caught a trailing-shore collision that incorrectly stopped a bomb leaving land; it now descends into water while forward walls still block. Another earlier test assumed two render frames guaranteed two fresh authoritative updates. The recorded nudge was a valid one-degree input; the final test waits for accepted state changes instead of asserting that timing assumption. Failed runs remain in ignored artifacts rather than being relabeled as success.

Native popup readiness now waits for actual visibility/item focus without setting either. Earlier combined runs observed an initially unfocused popup or application focus interruption; focused popup/navigation checks and the final full suite pass. The Mac app remains locally ad-hoc signed; native Metal evidence here is local, with the optional hosted graphical lane still disabled. No external server deployment or notarization is claimed.

The installed v0.16.0 app matches the tested export byte-for-byte, passes strict recursive signature verification, and completed a windowed Launch Services gameplay smoke. The project-owned localhost server was rebuilt/restarted with the matching aim protocol. Remote servers were not modified.

## Iteration fifteen baseline — 2026-10-21

| Check | Result |
|---|---|
| Gameplay / client / AI / audio | **113 / 95 / 36 / 19** checks passed |
| AI long-match matrix | **36 scenarios**, positive activity and bounded idle validated |
| Portable Release server | Real HTTP/WebSocket smoke against the exact staged delivery passed; ZIP and file manifest verified |
| Full exported Mac regression | **1,787 assertions, 142 captures**, zero runtime errors; complete controller attempt passed after two logged focus interruptions |
| Headless core | **746 assertions**, no graphical claim |
| Final softly oval model | **13 assertions, five actual captures**, zero runtime errors |
| Final exported package smoke | **178 assertions**, bundled assets, movement and carry/throw checked headlessly |
| Tooling contracts | **40 tests** across runner, QA, CI and release validation; both workflows lint clean |

The final full exported run is `artifacts/iteration15-trot-final-all/playtest/20261021T163253.260940Z-all` (121.502 seconds), including the cartoon trot and the user's slightly shorter, softly oval helmet. Model screenshots in the README were refreshed from this final app. Headless core evidence is `artifacts/iteration15-trot-headless`; package evidence is `artifacts/iteration15-trot-package2`; managed delivery evidence is `artifacts/iteration15-managed-rewritten-pin`. Focused final exported checks also passed: kick physics 79 assertions/11 captures, charge/aim 105/seven and presentation 39/11.

The user rejected the earlier careful walk as robotic. The final cartoon trot uses short planted pushes, gathered boot arcs, brief flight, coordinated body spring and glove follow-through. Geometry and motion retain separate controls. [Original-video observations, tuning and actual movement witness](history/iteration15-walk.md).

Two carry failures were reproduced: low neutral launch speed dropped a thrown charged bomb onto its owner, and mismatched launch height made the rendered bomb dip toward the helmet immediately after release. Shared authority/preview math now gives a decisive neutral throw, charge-dependent reach and a hand-height launch. No self-hit immunity was added. Static local aiming distinguishes safe landings from water/void removal; dynamic actors can interrupt the prediction. Final focused native evidence has **105 assertions and seven captures**. [Red/green evidence and boundaries](history/iteration15-charge.md).

The harness now shares a catalog between Python and C#, requires exact completed scenario coverage and catalog hashes, validates real captures, records periodic progress, and emits JSON/JUnit failure receipts. [Asset audit](asset-audit.md) covers fonts, original model/material geometry and visible UI bounds. The canonical [QA/CI profiles](ci.md) own isolated server processes and publish separate tested client/server artifacts; public PR checks never receive kernel access. Native Metal CI remains disabled pending an isolated graphical runner; the local Metal run above is the graphical evidence.

Earlier failed runs remain retained. One roster assertion sampled two near-identical points in an active stride; it now measures a complete pose excursion. Other runs lost native focus to foreground applications while measuring held controller input. The harness now restarts only an explicitly interrupted controller scenario, with a three-attempt cap, fresh inputs/session, and logged discarded attempts. Functional failures are not retried; production focus safety and its intentional focus-loss tests remain intact. The final full run recovered from **two logged focus interruptions**, then completed an entire valid controller attempt. Invalid partial attempts do not count toward the passing assertion receipt. [Controller diagnosis](history/iteration15-controller-focus.md).

The final v0.15.0 app is installed at `~/Applications/Blast Circuit.app`, matches the tested export byte-for-byte, and passes strict recursive signature verification. Launch Services gameplay smoke passed. This remains an ad-hoc signed development app; no notarization, fresh physical-controller session, external server deployment or measured Game Mode gain is claimed.

## Prior interface style pass — 2026-10-21


This pass replaces the 2D interface layer: the Godot fallback font, glyph arrows and ad hoc panels give way to bundled Dela Gothic One and M PLUS Rounded 1c (OFL, Latin subsets) and a shared stamped-key style. It also adds drawn icons, keycap input prompts, a fuse-clock round timer and participant tokens. Simulation, networking protocol and input routing are unchanged; the HUD additionally reads the existing `roundSeconds` and `winsTarget` fields from stage snapshots.

| Check | Result |
|---|---|
| Game build | Zero warnings/errors |
| Gameplay integration | 104 checks passed |
| Client/session/input | 95 checks passed |
| AI behavior | 36 checks passed |
| Audio | 19 checks passed |
| Full windowed source regression, `--start-server --scenario all --timeout 600` | **1,575 assertions, 131 captures**, zero runtime errors (112.9 seconds) |

The passing run is `artifacts/playtest/20261021T143630.905825Z-all`. Harness label checks were updated for the new copy (Title Case buttons, "Start Battle", "Battle begins in", "Medium"); they still locate the same native controls and assert the same behavior. Two earlier full runs did not pass. One missed a single synthetic mouse release on the roster Back button at 1280×800 immediately after a fresh build; the same menu scenario then passed five consecutive standalone runs and the later full run. The other hit the runner's default 120-second wall clock while the network step was still passing; the suite now takes about 113 seconds, so a longer `--timeout` is advisable. README gallery images for menus, roster, setup, room settings, controls, results, difficulty selection, AI matches, flooding and the eight-player HUD were refreshed from the passing run.

## Iteration thirteen baseline — 2026-10-21

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

[AI architecture and navigation diagnosis](history/iteration13-ai.md), [harness recording](../tools/playtest/README.md).

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

[Original-game placement, remote color and the unresolved equal-power red-radius question](history/iteration12-bombs-research.md). Mode exclusivity is the user's requested rule. No extra red radius multiplier was inferred from uncontrolled footage. No new physical-controller session, external deployment, notarization or Game Mode gain is claimed.

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

The actual hit measures a 0.301-unit rise and 1.10-unit horizontal displacement. The old impulse fails the bounded-stumble regression at 0.995 units of rise. A sustained kick now crosses the actual lower courtyard lip and resumes rolling on landing; unit-level ordinary-input cases also cover more than two seconds of unobstructed travel, repeated drops and full-radius corner collision. [Original-video observations versus project tuning](history/iteration11-kick-research.md).

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

The normal and pumped finite-fuse checks already passed before the fix. The reproduced indefinite-bomb defect was an orphaned remote after owner departure in a three-player match. New coverage separates that lifecycle from active remote command behavior, moving remote detonation, billiard transfer and full-radius wall sweeps. [Primary manual and inspected footage, diagnosis and tuning](history/iteration10-kick-research.md).

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

[Movement, bomb, ghost and AI implementation](history/iteration9-gameplay.md) · [Harness](../tools/playtest/README.md) · [Mac app](macos-app.md)

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

The face mesh is checked for real depth, outward clockwise triangles and nondegeneracy, then captured front/back/both sides/three-quarter. The final model keeps the separate plate at moderate convexity, with a rounded return, slightly reduced forehead and rounder lower helmet. The setup preview renders actual authority terrain. The courtyard shader is preserved; upper stone, lower lighting, foundation and water were inspected at normal and flood levels. [Rendering sources and boundaries](history/iteration8-rendering.md).

A separate 15-second installed-app smoke, capped at 60 FPS, reported 59–61 FPS intervals on Apple M5 Pro with native Metal and normal gameplay. This is a short local observation, not a benchmark, improvement claim, worst-case guarantee or Game Mode measurement. The app remains locally ad-hoc signed, not notarized. Production hardware deployment, internet latency/loss, Bluetooth Switch support and physical eight-controller play remain outside this verification.

The README screenshots come from the final exported-app run. [Gameplay/input design](history/iteration8-gameplay-input.md), [harness](../tools/playtest/README.md), [Mac app packaging](macos-app.md).

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

The README gallery was refreshed from that full app run. Exported quick-smoke screenshots go to writable `user://captures`, and capture errors fail the smoke run. A separate macOS Launch Services smoke verified gameplay (115 snapshots) and a saved screenshot with zero stderr output. The installed bundle is checked against the final export. Source and documentation are public; app/template binaries, private kernel source, downloaded references and raw test traces remain ignored. The app uses a local ad-hoc signature and is not notarized. Game Mode's plist opt-in is present, but OS activation and performance benefits remain unmeasured. [Packaging and reproduction](macos-app.md), [design research](history/iteration7-design.md).

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

Actual output-bus capture recorded 160,768 stereo frames, peak amplitude 0.2292 and RMS 0.0249, with no discarded frames. The mix was non-silent and below clipping. The public WAV demo is that generated game mix; no microphone or reference-game audio is included. These objective checks do not claim that the sounds were auditioned against the original game. The [reference study](history/iteration6-research.md) explicitly records the original-audio perception limitation.

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

Physical eight-controller coverage, internet latency/loss, production hosting and multi-node capacity remain unverified. The current work does not change that boundary. See [harness instructions](../tools/playtest/README.md) and [reference research](history/iteration4-research.md).

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
