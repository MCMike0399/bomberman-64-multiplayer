# Agent handoff

The user resumed gameplay work on 2026-10-23 for controller ergonomics, aiming
and knockdown/blast separation (iteration19). The first eighteen iterations
remain historical evidence. Iteration20 then rebuilt the walk. Recorded
release baseline: **v0.34.0/build 40**, published with independently verified Mac
and server archives (see [verification](../verification.md)). Previous runtime baseline: **v0.18.0**, commit
`316a330d7d895a92f98c1a60be7d571ec445a8a3`. Later documentation commits may follow.
Verify the live branch, source, tools and installed version before relying on
that baseline. Start with [project instructions](../../AGENTS.md).

The v0.30 review merged diorama polish first, then the five reviewed gameplay/UI
PRs. Protocol 4 accompanies Fast-bomb removal; both maintained workers are updated.
The subsequent v0.31 release includes the reviewed explosion, Blizzard Battle,
Greedy Trap and Up & Down work together with the activated release pipeline. [Review fixes, complete gates and delivery](../verification.md).

## What this project is

Blast Circuit is an original Godot C# arena game inspired by Bomberman 64 battle
mode: 2–8 total participants, local humans and CPU players, online lobbies and
mixed human/server-bot matches. Practice is intentionally different from normal
battle. Godot owns the client; Burbuja kernel hosts the independent realtime
server. The same product-owned simulation runs locally and online.

Source files and `--help` own exact implementation/flags. These guides own the
reasoning, user preferences, pitfalls and completion criteria that source alone
cannot convey. Historical iteration documents retain research and failed
experiments. When they disagree, verify code and the newest user correction;
update the current guide rather than silently reviving old behavior.

## Read only the branch you need

| Guide | What it answers |
| --- | --- |
| [Gameplay and AI](gameplay-and-ai.md) | Which layer owns the bug? How do bomb actions, terrain, collision and fair bot decisions work? |
| [Visuals and animation](visuals-and-animation.md) | How is the procedural character built? How do we tune it without losing the silhouette? What counts as visual evidence? |
| [Input and UI](input-and-ui.md) | What are the current controls? Where do focus, hotplug, native Switch USB and reticle failures occur? |
| [Testing and delivery](testing-and-delivery.md) | Which tests to run, how to add a meaningful journey, how to record, and how to ship one verified app? |
| [Codebase map](codebase-map.md) | Where does each kind of code live, which naming rules does Godot enforce, and how do I add or move files safely? |
| [Architecture](../architecture.md) | Dependency direction, authority/session boundaries, state flow and deployment limits |
| [Latest verification](../verification.md) | Exact observed results and limitations, with earlier iterations below |

## Current decisions that supersede earlier experiments

- v0.34 delivers the four reviewed metrics, cached-preview, overhead-hit and
  ledge-fall PRs through #23. Required trusted CI, protected publication and both
  workers passed. The user explicitly accepted an unfinished full graphical
  gate after VM stalls and skipped native checks; this is a release-specific
  verification exception. [Delivery and GPU gap](../verification.md#v0340-delivered).

- v0.33 delivers PR #18's feature-folder/namespace restructure while retaining
  v0.32 display behavior. The complete exported Metal suite passed 51 journeys;
  native M5 Pro checks, trusted CI and both production workers passed. Native
  frame-time comparisons retain desktop-load/focus limitations.
  [Layout](codebase-map.md) and [delivery receipt](../verification.md#v0330-delivered).

- v0.32 delivers saved resolution and Windowed/Fullscreen controls. Ordinary
  launch restores both choices; QA stays explicitly windowed. Native M5 Pro Metal,
  the complete exported graphical suite, trusted CI and protected publication
  passed. [Display behavior](input-and-ui.md) and [delivery receipt](../verification.md#v0320-delivered).

- v0.31 delivered: PR #15 merged the five reviewed PRs, both new arenas and
  exact schema-6 admission. The signed update and both matching game workers
  are live. [Delivery receipt](../verification.md#v0310-delivered) and
  [release commands/recovery](../releases.md) own future delivery.

- Release pipeline source (2026-10-25): `Release.props` owns version/build/schema;
  every release has an exact admission token. Publication requires deployment
  verification across all configured workers. [Release procedure and adapter
  setup](../releases.md) distinguish implemented gates from activation/host evidence.

- Blizzard Battle source work (2026-10-25) adds the enclosed snowfield, sixteen ice cones, changing diagonal wind and bilateral avalanches. [Stage contract](../blizzard-battle.md) and [inspected references](../blizzard-battle-research.md) separate original evidence from eight-seat/timing tuning. Delivered in v0.31 with exact schema-6 release admission; standalone numeric protocol 5 is superseded.
- Stage scenery (2026-10-26 source work): every arena gains original,
  collision-free surroundings and small ambient animations outside the playable
  volume, themed on Bomberman 64's worlds; menu miniatures skip them.
  [References, composition and rules](../stage-scenery.md). Agent review is not
  user approval of the art.

- Greedy Trap adds the enclosed upper court, narrow middle ring and lower reward
  chamber, with four inner flights, two upper exits and closing side walls.
  [Stage contract](../greedy-trap.md) and [inspected references](../greedy-trap-research.md)
  distinguish original observations from eight-player/physics tuning. Its
  reservoir/town exterior is a [Blue Resort-informed extension](../greedy-trap-background-research.md)
  with shared outdoor lighting and animated water.

- v0.29.1 delivers adaptive public/private rooms, independent coordination and execution limits, measured player-host selection and the Rock Garden miniature bounds fix. [Behavior](../smart-multiplayer.md) and [delivery evidence](../smart-multiplayer-release-029.md) define measured capacity and remaining limits.

- v0.28 integrates all stage PRs, the tutorial and reference-informed explosion effects. [Combined verification and release status](../stages-release-028.md) supersedes independent draft limitations only where its evidence explicitly covers them.

- Tutorial work adds an original local-only Circuit Garden and replayable lessons. The user withdrew map removal: preserve all battle arenas and the concurrent map PRs. Read [tutorial ownership and integration](../tutorial.md) before changing lessons, progress or training terrain.

- Top Rules (2026-10-24 source work) adds the original castle composition with
  three raised lanes, a central crosswalk, four stairs and real lower passages.
  Terrain, CPU routing, bombs and aim previews distinguish both stories.
  [Research and explicit adaptations](../top-rules-research.md); the gameplay,
  visual and testing guides own maintenance. This does not change the installed
  or published release by itself.


- Rock Garden reconstruction (2026-10-24): an enclosed square lawn, five round
  columns, central cross-shaped paving and sixteen destructible boulders replace
  the wide open stone platform and thirteen pillars. Eight seats use separate
  quadrant/corner spawns; falling fireballs replace closing-column Hurry. [Stage contract](../rock-garden.md) and
  [original references](../rock-garden-research.md) distinguish evidence from tuning.

- v0.27 connection work adds persistent random lobbies and player-hosted peer
  rooms. [Peer hosting](../peer-hosting.md) owns the two-human minimum, direct/relay
  routes and return-to-lobby host takeover. See verification for delivery status.


- Up & Down rebuild (2026-10-24): a suspended upper ring and green crown,
  two middle piers, a low reward deck, six purple flights, staggered links and
  real underpasses replace the filled terraces. Twelve cover blocks sit below
  the ring; sudden death floods upward. [Stage contract](../up-and-down.md),
  [source research](../up-and-down-research.md). Dimensions, extra spawns and
  physics remain project tuning.


- Repository layout (2026-10-24, extended 2026-10-26): Godot code lives in
  snake_case feature folders under `scripts/` and pure .NET code in PascalCase
  feature folders under `src/`; namespaces mirror folders. `Bomber.Game` is split
  into `Matches`, `Online`, `Tutorial` and `Simulation` (`State`, `Stages`, `Rules`,
  `Ai`, and `BlastSession/` partials by concern). The scene is `scenes/main.tscn`;
  shell launchers are in `tools/dev/`; dated iteration notes are in `docs/history/`.
  Large classes are split into `Class.Concern.cs` partials. The public profile
  enforces namespaces, file names and a 20-file folder limit. The
  [codebase map](codebase-map.md) owns layout and naming rules.

- Diorama polish (2026-10-25): clear refracting shallows with caustics, tilt-shift
  depth of field and haze outside the arena, lacquered toy materials and painterly
  terrain, inspired by BDSP, Tunic and Link's Awakening. Low keeps opaque water and
  no blur. See the [rendering guide](visuals-and-animation.md#diorama-polish-2026-10-25).

- Graphics settings now provide Low/Medium/High, with Medium as default. Menus
  render at output pixel density and use the same AA policy as the arena;
  presets remain local even online. [Research and budgets](../graphics-research.md)
  and the [rendering guide](visuals-and-animation.md) own the details and limits.
  Source work also prepares first-use effects at startup/preset changes. The
  engine remains 4.7.2 after an isolated [4.8 evaluation](../engine-upgrade-research.md).
  This source work is separate from the published v0.25.0 release; see verification
  for the subsequently requested local preview installation.

- Item-density tuning (2026-10-25 source work): 40% independent crate drops,
  60/30/10 upgrade/effect/bomb-type weights, and six-item default practice caches.
  The [gameplay guide](gameplay-and-ai.md#item-density-tuning-2026-10-25) records
  measured alternatives and scope. This supersedes the v0.25 density defaults.

- v0.25 delivered: Advanced match settings, independent 55% crate drops without
  guaranteed openings, expiring practice/map items, default five-stack blast/bomb
  caps and temporary Tiny/Big/Haste/Slow effects. The gameplay and input guides
  own the current rules. See verification for delivery status.

- v0.24: Morning, Midday, Afternoon and Night on all stages. Settings → Local
  lighting controls local play; online rounds use the server's snapshot choice.
  Coordinated sky reflections, water and wet stone shading; the 35% item drop
  chance from v0.23.1 is preserved. See [rendering](visuals-and-animation.md).

- Graphical QA uses an isolated local macOS desktop by default so the developer
  can keep working. Preserve the complete scenario suite and real guest focus;
  retain explicit shared-desktop runs for native hardware checks. See
  [testing](testing-and-delivery.md#own-the-build-desktop-and-server).

- Shared online service (2026-10-23): the server is for all users. Routine
  releases follow [shared-server delivery](../online-server.md); former friends-party
  relay, Wi-Fi and ZIP-sharing checks are conditional historical procedures.
  Public hosting is deferred to a separate task; maintain the existing server.

- Bomb loadout (2026-10-25): black starting bombs plus three collectible types:
  Remote, Red and Bouncing. Fast bomb equipment is removed; player speed uses the
  existing temporary Haste effect. This supersedes iteration23’s Fast bomb mode.

- Iteration23: wider five-tier Pyramid with rear pitfalls; enlarged other arenas;
  replaceable bomb/ability slots, bouncing/fast bombs, Focus charging/assist, heavier
  charged throws, blue red-bomb blasts and no ghost/bomb interaction. Leaders can
  edit waiting-room rules through the existing match editor. See the gameplay guide.

- v0.22 party polish (delivered 2026-10-23): Copy code, saved online name/server,
  named character/HUD labels, first-to-two and 1:00 + 1:00 defaults, immediate flood,
  water ghost returns, rear temple barrier (superseded by the v0.23 open pitfall) and diagonal stair kick fix.
  Right shoulder now stops kicked bombs; remote remains on the face button.
  See [input/UI](input-and-ui.md), [gameplay](gameplay-and-ai.md) and [verification](../verification.md).

- v0.21 private rooms wait for their leader, including rooms with CPUs; results return to the same room. Esc/Start opens a game menu. Settings saves volume, vibration (default off) and automatic update checks (default on). [Online server](../online-server.md), [signed updates](../automatic-updates.md).

- Iteration19 recommends shoulder carry (L/LB/L1); Switch Y replaces B as its
  face alternative. v0.19.1 also lets Switch B ready/pump while Y is held;
  A still works and X remains remote. Hold the aim trigger and steer the target with either stick.
  Keyboard L + WASD steers the same map-plane cursor. Relative turn/range controls
  are superseded. [Research and alternatives](../history/iteration19-controls-research.md).
- Knockdowns have a brief impact-only cooldown and allow subsequent hit chains.
  Knockdown, hold escape and thrown-player protection do not stop explosions;
  hearts and respawns retain separate damage protection.

- Keyboard is **J bomb, K hold carry, L hold aim, I remote**; WASD moves/adjusts
  aim, Shift/R remain aliases. Earlier Space-carry and K-aim instructions are
  historical. The input guide explains both primary-hold and carry-held flows.
- The reticle is shape-only. It remains visible for obstructed/unsafe outcomes
  and clears on Results; diagnostic metadata is not visible text.
- Keep the compact chibi body and short fixed white legs. Iteration20 replaces the
  2.4-unit glide with a 1.5-unit gripping scamper (heel/toe roll, passing tuck,
  squash, spring-driven gloves/head/lean and a wobbling antenna). Tested, **not yet
  approved by the user**. [Why and what changed](../history/iteration20-walking.md).
- A dimensional arcade menu, restrained shared palette, stamped controls and
  clear focus are preferred over webpage-like card grids or flat backgrounds.
- Sunken Temple uses the tiered pyramid/courtyard composition, readable sandstone,
  grounded foundations and animated water. Art geometry and collision are separate.
- AI difficulty changes decisions, not physics/resources. Measure pressure and
  reengagement, not only bombs placed; mixed benchmark outcomes must remain visible.
- Keep one installed app and verify its live version before relying on a dated
  receipt. A missing `artifacts/exports/*.app` is expected after verified cleanup.
  Rebuild only when needed, then clean duplicates after the next verified install.

## A reliable small-agent work packet

Use this structure when taking or delegating a bounded task:

```text
Symptom and expected outcome:
Session/stage/seed/players/input sequence:
Known facts versus hypotheses:
Owning code boundary and files:
Small failing check / actual visual witness:
Files this worker may edit:
Build/desktop owner (or serial execution):
Change and preserved invariants:
Passing evidence and remaining limits:
Documentation updated:
```

A worker should return file paths and evidence, not an unsupported “fixed” claim.
When using multiple agents, divide by source ownership and serialize shared
builds/desktop tests. The same checklist works serially. Resolve uncertain scope
through the latest user request; model names and an older concurrency preference
are not reasons to skip review or create new infrastructure.

## Research and history map

These live in [`docs/history/`](../history/README.md). They are sources and explanations, not instructions to recreate older designs:

| History | Relevant lessons |
| --- | --- |
| [Initial research](../history/research.md), [gameplay research](../history/gameplay-research.md), [visual research](../history/visual-research.md) | Original manual/reference observations, 4-player original versus 8-player project, original assets rather than ripped media |
| [UI/design pass](../history/iteration7-design.md), [rendering](../history/iteration8-rendering.md) | Dimensional menus, lighting, stone and water; distinguish art inspiration from copied assets |
| [Input/gameplay](../history/iteration8-gameplay-input.md), [iteration nine](../history/iteration9-gameplay.md) | Continuous movement, impacts, grabbing, ghosts and terrain transitions |
| [Kick research](../history/iteration10-kick-research.md), [kick/shore refinement](../history/iteration11-kick-research.md) | Windup/force, finite bomb travel and restrained victim displacement |
| [Placement and bomb modes](../history/iteration12-bombs-research.md) | Foot-level departure, denied input, exclusive red/remote behavior |
| [AI recovery](../history/iteration13-ai.md), [AI test definitions](../../tests/Bomber.Ai.Tests/README.md) | Collision deadlocks, timed hazards, matched comparisons and pressure denominators |
| [Walk experiments](../history/iteration15-walk.md), [compact character](../history/iteration16-character.md) | Rejected robot/stretched-leg solutions; continuity alone did not preserve charm |
| [Charge and launch](../history/iteration15-charge.md), [deliberate aiming](../history/iteration16-aim.md) | Carry socket versus authority height, shared throw projection, fair fine adjustments |
| [Reticle diagnosis](../history/iteration17-reticle.md), [iteration seventeen](../history/iteration17-overview.md) | Obstruction visibility, Results lifecycle, loot openings and mixed AI evidence |
| [Walk and controls v0.18](../history/iteration18-walking.md) | Independent waist pivot, whole-body review, restored K carry, duplicate-app cleanup |
| [Current walk](../history/iteration20-walking.md) | Measured boot skating, cadence matched to leg reach, spring follow-through, antenna |

Reference files under ignored artifacts and old temporary clipboard paths may
not exist on a new machine. Follow the durable source URLs in research docs;
state what you actually inspected. A still image cannot prove gait timing or
button sequence, and a video cannot establish hidden simulation constants.

## What has and has not been established

The v0.18 local exported suite passed 2,064 assertions with 172 captures; the
subsequent inspection-camera-only correction passed a fresh package smoke and
52-check walking recording. Headless core passed 840, bundled smoke 247. The
installed app matched 217 export files and passed Launch Services smoke. The
[baseline CI run](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/39712262559)
passed public, managed/network, headless and clean Mac packaging. These are dated
receipts, not a substitute for testing new changes.

The project does not claim exact undocumented original-game physics, universal
human-rated fun, new physical-controller coverage from synthetic tests, Switch 2
Bluetooth support, notarization, measured Internet tolerance, production fleet
capacity or validated multi-node failover. Fullscreen/Metal configuration does
not by itself prove macOS Game Mode activation. Consult the relevant guide before
turning a configuration option or passing fixture into a product claim.
