# Ledge falling: references and tuning

2026-10-25. The reported symptom was ordinary movement off higher ground feeling
weightless: players could travel several cells and steer as freely as on land.
This changes the shared `Bomber.Game` authority used by local, peer-hosted and
server-hosted play. It does not change model proportions or bomb trajectories.

## Original-game references

- [Nintendo's 1997 manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_FR.pdf),
  printed pp. 6–7, describes analog movement and the bomb/carry controls. It does
  not publish gravity, air speed or acceleration. This is the 1997 3D game;
  similarly named 2001 Arcade Edition and Bomberman Hero are different games.
- [Oppaiman100's original Pyramid gameplay](https://www.youtube.com/watch?v=a-09Q57Xuew&t=13s),
  inspected at 12, 13, 13.2, 13.4, 13.6, 13.8 and 14 seconds. White exits the
  western terrace and reaches the lower outer path while continuing outward.
  This supports a short descent with some horizontal motion. Video does not
  reveal stick input, world-unit dimensions or hidden physics constants.
- [Original Up & Down gameplay](https://www.youtube.com/watch?v=8neP1yj-PFs&t=20s),
  inspected at 20 and 25–27 seconds, provides context for narrow elevated routes
  and lower piers. Those samples mostly show connected travel and combat, so
  they are not measurements of free-fall acceleration or air control.

The numerical settings below are an intentional response to the user's request,
informed by those references. They are not recovered original-game physics.
No reference footage, textures or game code is included in the repository.

## Reproduction and experiments

Base revision: `b0f7b8e`. A deterministic normal three-human match, seed 64, no
CPUs/cover/sudden death, starts beside a ledge. Ordinary movement steps off it;
accepted `player-fall` and `player-land` events measure travel and duration.
One command reproduced the complaint before the fix:

```sh
dotnet run --project tests/Bomber.Game.Tests -- --falling
```

The baseline failed the one-tier fall budget: 2.025 cells of travel and 14
authority ticks. Its existing traversal test explicitly required full air
steering; that expectation is now superseded.

Five configurations were run through the actual simulation, each with forward,
released, reversed and sideways input at heights 1, 2 and 4. Changes were applied
serially and the selected source restored afterward. No test-only runtime
physics override was added. Values below are **ticks / horizontal cells** for
continuous outward input; 30 authority ticks equal one second.

| Configuration | Gravity | Air speed | One tier | Two tiers | Four tiers |
| --- | --- | --- | --- | --- | --- |
| Baseline, instantaneous steering | 9.8 | 100% | 14 / 2.025 | 19 / 2.850 | 27 / 4.050 |
| Gravity only | 24 | 100% | 9 / 1.275 | 13 / 1.875 | 18 / 2.625 |
| Air control only, acceleration 8 | 9.8 | 45% | 14 / .945 | 19 / 1.283 | 27 / 1.823 |
| Selected combined, acceleration 8 | 24 | 45% | 9 / .608 | 12 / .810 | 17 / 1.148 |
| Heavier alternative, acceleration 8 | 30 | 35% | 8 / .420 | 11 / .577 | 16 / .840 |

Gravity alone leaves excessive travel. Air control alone keeps the long descent.
The combined setting reduces outward travel by about 70–72% while retaining more
useful correction than the heavier alternative. Releasing input coasts about
.291 cells, then settles; four-tier sideways correction is about .751 cells.
These are simulation measurements, not a claim of user approval or fun.

Rerun the selected configuration's measurements with:

```sh
dotnet run --project tests/Bomber.Game.Tests -- --measure-falls
```

Raw red/matrix receipts are local ignored files under `artifacts/falling/`.

## Behavior and boundaries

- Ledge gravity is 24 tier units/second². Ground movement remains 4.5 cells/second.
- Departure inherits 45% of the actual collision-resolved ground velocity, so a
  blocked movement axis cannot supply fictitious launch momentum.
- Stick intent targets 45% of status-adjusted ground speed. A vector acceleration
  limit of 8 cells/second² gives reversals and release a short transition without
  giving diagonal input a larger acceleration budget.
- `Falling` distinguishes ordinary ledge drops from thrown/knocked players.
  Their existing gravity, launch velocities and landing recovery remain separate.
- Ordinary landings immediately restore ground movement and do not add stun.
  Layered floors, cliffs, pillars, ramps, water/void contact and ghosts keep their
  existing authority paths. Bomb physics and aim projections are unchanged.
- This is source/PR work. An independently running old server needs the updated
  build to use these rules; this task does not deploy or reinstall a release.

## Verification

`FallingChecks` retains the red regression plus height/direction/status/carry,
coasting, reversal and immediate-ground-control checks. `TraversalAndGhostChecks`
now requires useful limited correction and still checks solid upper faces,
water/void contact and ghost recovery. The native `traversal` journey measures
accepted fall duration/travel on the stock temple, alongside actual rendered
landing, shore and ghost witnesses. Final gate and recording receipts are
recorded below.

- `public`: passed, including documentation links and tooling contracts.
- Game suite: **2,926** checks, including **16** new falling groups; client:
  **336** checks; audio: **19**; cluster: **23**.
- **48-case AI soak passed**, longest open stationary window 1.33 seconds
  against a 12-second limit. Real network smoke passed, including peer-hosted
  Top Rules descent/underpasses and Up & Down traversal. Adaptive relay:
  **9** checks; real fleet infrastructure: **36**.
- `headless`: passed the complete core journey (**1,229** assertions), peer
  rooms (**19**) and adaptive rooms (**9**). A first fresh-editor import exited with signal 11
  during font import; a fresh profile retry imported successfully and passed.
- Rebuilt-export graphical `traversal`: **88 assertions, 8 captures, zero runtime
  errors**, on an isolated macOS Metal desktop. Its ordinary stock-temple fall
  took **9 ticks / .330 cells** with partial outward input followed by release.
  The 27.03-second, 30 FPS recording decoded all **811** frames at normal speed.
  Full-clip sampled review, the fall-frame sequence and full-size captures show
  a brief descent, restrained horizontal displacement and a grounded landing;
  this is agent visual review, not user approval. No physical-controller or
  bare-metal performance coverage is claimed.

The final graphical receipt is
`artifacts/falling/graphical/20261025T170624.284163Z-traversal/runner-summary.json`;
the normal-speed recording is `gameplay.mp4` beside it. The selected run's
`ledge-fall-measurement` event retains the accepted authority measurements.
Other canonical receipts are `artifacts/falling/{public,managed,headless}/summary.json`.
The dedicated VM was stopped after testing and the temporary exported app was
removed by the harness. The installed app and external servers were untouched.

### Initial CPU pressure regression (resolved in integration review)

The initial **managed profile was not fully green**: its default AI suite stopped at the
stock Pyramid pressure check. An isolated checkout of exact base `b0f7b8e`
passes all four stock pressure cases. With the selected falling behavior,
medium/seed 9876 has five pressure bombs and a 244-tick longest gap; hard/seed
9876 has five and 270 ticks, against the existing 240-tick limit. Both remain
alive. Baseline gaps are 212 and 237 ticks respectively.

Matched authority/intent traces show changed lower-court routes and farming
after shorter ledge travel. Additional isolated gravity/speed/acceleration and
departure-momentum trials, and an experimental committed-fall AI policy, did
not satisfy every pressure case. Those experiments are discarded; no CPU
policy or test threshold is changed in this PR. This is a behavioral regression
to review alongside the movement improvement, not an unrelated baseline failure.
The early AI exception also means that default suite's later checks did not run;
the separate 48-case soak did complete. No deployment or release is claimed.

### Integration review correction — 2026-10-25

Combining directed CPU pursuit with the selected fall physics resolves the Hard
case, but retained Medium/9876's 244-tick failure. The trace showed a crate-farming
plan chosen just before its engagement deadline, then executed after the deadline
without reevaluating priority. Its non-threatening bomb forced another escape
and delayed pursuit.

Pursuit state is now refreshed before a pending placement, and the actual bomb
plan rechecks engagement priority. A real enemy threat remains eligible, and a
sealed route still permits demolition. Physics, resources, reaction intervals,
escape safety and the 240-tick pressure limit are unchanged. The four stock cases
now produce longest gaps of **176/160 ticks (Medium seeds 64/9876)** and
**201/204 ticks (Hard)**, with **7/6/7/6 threatening bombs and zero self-deaths**.

The combined source passes the complete **78-check AI suite**, **2,926 game
checks**, and **48-scenario soak**, whose longest open stationary window is 1.27s.
The AI total includes the independent remote-chain cover-safety regression from
the CPU PR review. Red/green receipts and the diagnostic trace are retained under
ignored `artifacts/review/cpu-falling-*`. This is integration evidence, separate
from the original graphical recording and the eventual combined release gates.
