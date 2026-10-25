# Circuit Garden tutorial

Delivered with [v0.28](stages-release-028.md). The standalone source receipts below predate the combined stage review and release.

The main menu's **Tutorial** opens a replayable how-to section. Every lesson is
available immediately; clearing one saves a device-local star and offers the next
lesson. **Practice** remains available for existing battle arenas. **Free practice
in Circuit Garden** opens the tutorial arena without lesson objectives.

Circuit Garden is original procedural art informed by Green Garden's ruins and
Blue Resort's visible water channels. It has a marked throwing court, two low
terraces connected by a broad staircase, solid training pillars and a separate
landing dock across a canal. The [research](tutorial-research.md) distinguishes
primary-source observations from the project's design choices. No reference
assets or Smash branding ship.

## Lesson flow

The [course](../src/Bomber.Game/TutorialCourse.cs) owns the lesson order, copy and
objectives. It covers movement, blast safety, both pickup/throw flows, three-level
charging, precision aim, kick/stop, chain reactions, cover, all twelve pickups,
equipment swaps, expiration, terrain, impact/recovery, player holds/escape,
ghosts/shake-off, rising water and a final Easy CPU challenge.

Instructions use the current keyboard or controller family's labels. The header
shows the active objective, while a gold ground ring marks spatial goals. Clear
and failure states stop authority simulation until the player explicitly chooses
Next, Retry or Lessons. While a lesson is running, Esc/Start pauses; its menu
supports retry, settings and returning to lessons. On clear/failure, Start focuses
Next/Retry without activating it, and Esc/controller Back returns to the current
lesson in the browser. Selecting/replaying a lesson rebuilds its initial state.
Completed stars survive replay and restart; merely opening a lesson awards none.

Lessons deliberately author their starting conditions: required pickups and
cover, a stationary partner, an initial knockdown/hold/latch, or an Easy CPU.
Recovery and hold lessons therefore start in the state named in their description.
Supplied pickups last 60 seconds; expired required items and missed short recovery
windows offer a retry. Free-practice pickups retain ordinary item lifetimes.

## Owning boundaries

- [BlastStages.CircuitGarden](../src/Bomber.Game/Simulation/BlastStages.CircuitGarden.cs)
  owns cells, support, ramps, canal and practice cache. Battle layouts are unchanged.
- [BlastSession.Tutorial](../src/Bomber.Game/Simulation/BlastSession.Tutorial.cs)
  owns initial conditions, accepted-action progress, failure and completion.
  Bomb flight, fuse, collision, damage, inventory, recovery and AI use the existing
  simulation. The flood lesson applies the shared rise rate to the garden.
- [LocalSession](../src/Bomber.Client/LocalSession.cs) accepts an optional local
  lesson ID. `MatchOptions` checks stage, participant count and lesson identity.
  Online settings cannot select the training arena or request a lesson; ordinary
  server sessions fall back to Sunken Temple for an injected garden stage ID.
- [GameRoot.Tutorial](../scripts/app/GameRoot.Tutorial.cs) owns the local session
  lifetime, progress persistence and return destination. Progress is stored under
  `tutorial/completed` in the existing device preferences; QA uses its temporary file.
- [ArcadeMenu.Tutorial](../scripts/ui/menu/ArcadeMenu.Tutorial.cs) and
  [TutorialHud](../scripts/ui/hud/TutorialHud.cs) present the course and authoritative
  progress. Rendering does not decide whether a lesson passed.
- [TempleStageView.CircuitGarden](../scripts/arena/TempleStageView.CircuitGarden.cs)
  adds grass, range marks, banners and landing mosaics. All geometry is decorative;
  authority cells remain the sole source of collision and water boundaries.

The optional `tutorial` snapshot object carries lesson ID, objective index,
completion/failure, feedback and spatial target. Normal battles carry `null`.
Its progress comes from accepted effects/state, not raw key presses or elapsed
lesson-viewing time. Stable lesson IDs are persistence keys; changing copy or
ordering should preserve them.

## Concurrent map work

The user clarified on 2026-10-24 that all battle maps must remain. Preserve the
Up & Down rebuild ([PR #2](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/2)),
Top Rules ([PR #3](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/3)) and
Rock Garden rebuild ([PR #4](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/4)).
This tutorial is additive; the original removal request was withdrawn.

Those drafts independently change terrain/snapshots and some renderer entrypoints.
When integrating, retain their stage registrations, layered support and hazards;
keep the garden's separate partials and tutorial hooks. Recheck the stage-specific
hazard explanation against the merged behavior. This branch does not claim to
have integrated or verified those drafts.

## Verification

Run `dotnet run --project tests/Bomber.Game.Tests -- --tutorial` for ordinary-input
walkthroughs that clear every lesson, rejected invalid configurations, failure,
clean retry and the completion freeze. These are also part of the managed suite.
Run `python3 tools/playtest/run.py --scenario tutorial` for the actual menu,
progress persistence, retry/next, pause, framing and free-practice journey. It is
registered in both `all` and `core-headless`; headless success has no visual claim.

Inspect the captured browser, gameplay, aim, hold, flood, completion and pause
screens. Graphical controller injection verifies mapping and navigation, not
physical-device support. Source work is not an installed or published release.

### Source verification — 2026-10-24

Verified locally on `feature/circuit-garden-tutorial`, based on `0fa367a`:

- Public checks and documentation links passed.
- Managed profile passed game/client/AI/audio/cluster suites, all 36 AI soaks and
  real HTTP/WebSocket/peer-room smoke against its owned staged server. After the
  final progression regression, the game suite passed 2,855 checks (128 tutorial
  checks); the updated client suite passed 307 checks.
- Headless core passed 949 assertions and peer rooms passed 18. The final focused
  tutorial rerun passed 91 assertions, including the corrected Power bomb credit
  and return from training to ordinary battle.
- Full isolated macOS/Metal graphical regression passed 2,873 assertions across
  43 journeys with 280 captures and zero runtime errors/warnings. The final
  rebuilt-export tutorial rerun, after the last progression/local-render guards,
  passed 116 assertions with 19 captures and zero runtime errors/warnings.
- Inspected the menu/browser, garden, completion, pause, Power bomb destruction
  and controller carry views. Synthetic Xbox, Nintendo and PlayStation input
  readies/throws an actual bomb, updates family labels and confirms Next lesson.
  This is VM renderer and synthetic-input evidence, not physical controller or
  native-host GPU certification.

Receipts live under `artifacts/tutorial/`: `managed`, `public-authorized`,
`headless-final`, `headless-complete`, `graphical-final`, and
`final-export-tutorial/20261025T001641.413910Z-tutorial`. The retained failing
Power bomb credit witness is `regressions/red-cover-before-fix.log`. Earlier
sandbox-restricted persistence/process-inspection runs failed on permissions;
the authorized reruns passed. QA used temporary app bundles and preferences.
No release was published, no personal app was replaced and no external server
was deployed. The three map drafts were inspected for overlap, not merged here.
