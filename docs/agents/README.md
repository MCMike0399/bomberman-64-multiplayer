# Agent handoff

The user resumed gameplay work on 2026-10-23 for controller ergonomics, aiming
and knockdown/blast separation (iteration19). The first eighteen iterations
remain historical evidence. Iteration20 then rebuilt the walk. Current installed
runtime: **v0.23.0**; latest published client/server: **v0.23.1**
(see [verification](../verification.md)). Previous runtime baseline: **v0.18.0**, commit
`316a330d7d895a92f98c1a60be7d571ec445a8a3`. Later documentation commits may follow.
Verify the live branch, source, tools and installed version before relying on
that baseline. Start with [project instructions](../../AGENTS.md).

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
| [Architecture](../architecture.md) | Dependency direction, authority/session boundaries, state flow and deployment limits |
| [Latest verification](../verification.md) | Exact observed results and limitations, with earlier iterations below |

## Current decisions that supersede earlier experiments

- Shared online service (2026-10-23): the server is for all users. Routine
  releases follow [shared-server delivery](../online-server.md); former friends-party
  relay, Wi-Fi and ZIP-sharing checks are conditional historical procedures.
  Public hosting is deferred to a separate task; maintain the existing server.

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
  are superseded. [Research and alternatives](../iteration19-controls-research.md).
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
  approved by the user**. [Why and what changed](../iteration20-walking.md).
- A dimensional arcade menu, restrained shared palette, stamped controls and
  clear focus are preferred over webpage-like card grids or flat backgrounds.
- Sunken Temple uses the tiered pyramid/courtyard composition, readable sandstone,
  grounded foundations and animated water. Art geometry and collision are separate.
- AI difficulty changes decisions, not physics/resources. Measure pressure and
  reengagement, not only bombs placed; mixed benchmark outcomes must remain visible.
- The Mac has one installed v0.23.0 app. The previous bundle and export were
  removed after installation verification; a missing `artifacts/exports/*.app` is expected.
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

These are sources and explanations, not instructions to recreate older designs:

| History | Relevant lessons |
| --- | --- |
| [Initial research](../research.md), [gameplay research](../gameplay-research.md), [visual research](../visual-research.md) | Original manual/reference observations, 4-player original versus 8-player project, original assets rather than ripped media |
| [UI/design pass](../iteration7-design.md), [rendering](../iteration8-rendering.md) | Dimensional menus, lighting, stone and water; distinguish art inspiration from copied assets |
| [Input/gameplay](../iteration8-gameplay-input.md), [iteration nine](../iteration9-gameplay.md) | Continuous movement, impacts, grabbing, ghosts and terrain transitions |
| [Kick research](../iteration10-kick-research.md), [kick/shore refinement](../iteration11-kick-research.md) | Windup/force, finite bomb travel and restrained victim displacement |
| [Placement and bomb modes](../iteration12-bombs-research.md) | Foot-level departure, denied input, exclusive red/remote behavior |
| [AI recovery](../iteration13-ai.md), [AI test definitions](../../tests/Bomber.Ai.Tests/README.md) | Collision deadlocks, timed hazards, matched comparisons and pressure denominators |
| [Walk experiments](../iteration15-walk.md), [compact character](../iteration16-character.md) | Rejected robot/stretched-leg solutions; continuity alone did not preserve charm |
| [Charge and launch](../iteration15-charge.md), [deliberate aiming](../iteration16-aim.md) | Carry socket versus authority height, shared throw projection, fair fine adjustments |
| [Reticle diagnosis](../iteration17-reticle.md), [iteration seventeen](../iteration17-overview.md) | Obstruction visibility, Results lifecycle, loot openings and mixed AI evidence |
| [Walk and controls v0.18](../iteration18-walking.md) | Independent waist pivot, whole-body review, restored K carry, duplicate-app cleanup |
| [Current walk](../iteration20-walking.md) | Measured boot skating, cadence matched to leg reach, spring follow-through, antenna |

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
