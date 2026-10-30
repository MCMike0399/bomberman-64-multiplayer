# Public authority replay candidates — source-only packet

2026-10-28. Two candidates, neither executed here. No gameplay divergence or all-mechanics
identity established. Integration waits for full-quality capture review; strict combined
comparison stays unchanged. Supplied native results are context, not receipts reviewed here.

References use implementation revision `a0e70c8ceb39ef561bbc6d8551e1588b8bbc3302`.
Read-only diff against payload `19c078f2dc39d2a94788bb9943f4efea3546d327` found no changes in `src/Bomber.Game`,
the replay JSON or its C# journey. This note's isolated checkout is older
(`1006972cf9e5ff97a81e13f7b68a52c1665b691a`); resolve the newer paths against that
implementation revision, not the older local layout. Only this document was written.

Source keys (suffixes supply file:line):

- `G/` = `src/Bomber.Game/`; `S/` = `src/Bomber.Game/Simulation/`.
- `B/` = `src/Bomber.Game/Simulation/BlastSession/Bombs/`; `AI/` = `src/Bomber.Game/Simulation/BlastSession/Bots/`.
- `SS/` = `src/Bomber.Game/Simulation/BlastSession/Snapshots/`.
- `T/` = `tests/Bomber.Game.Tests/`; `J/` = `scripts/testing/journeys/`.

**Correction to the premise:** `S/BlastSession.cs:44–48` is the cached, fixed
`BotPlacementDirections` table, not a random-direction draw. Its operand is
`direction * MathF.Tau / 32`, for integer direction 0…31. Bot decisions explicitly
avoid gameplay RNG (`AI/BlastSession.Bots.cs:93`). The other direction pair is
blocked-steering recovery, `AI/BlastSession.BotNavigation.cs:142–161`.
Search of Game/Client source found no authority `Atan2` callsite.
Time convention (`docs/packaged-authority-replay.md:19–38`):

- `G/Matches/LocalMatch.cs:28` is public; the StageLayout overload at :30 is internal.
  Construction executes ticks 1 and 2 (:38–40); first externally driven tick is 3.
- At tick k, deliver every `SetInput` in recorded order **before** the one `Tick`.
  The replay sends listed changes first, then held inputs for unchanged slots in slot
  order (`J/matches/AuthorityReplayPlaytest.cs:70–94`). Preserve repeated same-tick edges.
- Snapshot `k` is only refreshed every second tick (`S/BlastSession.cs:238–264`).
  Feedback reads `StateMessage.d`; identify freshness by envelope `k`, not loop count.
  Materialize feedback into a fixed input list once, then replay that same list on all OSes.
- Defaults: Seed=64, Wins=2, RoundSeconds=120, SuddenDeath/Ghosts=true,
  BotDifficulty="normal" (Medium), Practice=false, TutorialLesson=null, appearances=[].
  Advanced defaults: DropPercent=40, BlockPercent=100, ItemSeconds=20, MaxFire=5,
  MaxBombs=5, EffectSeconds=10, DisabledItems="" (`G/Matches/MatchOptions.cs:8–20`,
  `G/Matches/AdvancedRules.cs:7–15`; difficulty mapping `S/Ai/BotDifficultyProfile.cs:13–19`).

## Candidate 1: public Top Rules lower-bomb route, then two bounded kick timings

**Public base; proposed adaptation, no existing fixed-tick threshold witness.**
`T/Stages/TopRulesChecks.cs:44–54`, “Top Rules lower bombs stay below bridges
and preserve their fuse”, calls `Match()` through its genuinely public branch (:15–17).
It walks stock geometry and places a real bomb; add a fresh primary edge instead of retreating.

Options: Stage="top-rules", HumanPlayers=1, BotPlayers=0, Practice=true; other values
are defaults above. No StageLayout, reflection, AI hooks or actor edits. Practice removes cover
through the product rule (`S/BlastSession/Items/BlastSession.Loot.cs:152–161`).
Top Rules defines no training items (`S/Stages/BlastStages.TopRules.cs:44–59`).
Proposed feedback recipe through `SetInput/Tick/StateMessage` only:

1. Send neutral through absolute tick 64 (the existing `Fight` adds 62 ticks).
   Require phase=1. Walk to (5.5,1.5), (2.5,1.5), (2.5,6.5), (5.5,6.5), in that order.
   Reproduce `GameFixtures.Walk`: each tick use x/z=`Clamp(delta/.3,-1,1)` from the
   latest snapshot; when both absolute errors <.12, neutral for two ticks, then next
   waypoint (`T/Fixtures/GameFixtures.cs:12–30`). Require final player y=0.
2. Early shot: face +X for one tick; A=true with zero axes for two ticks; neutral
   for two; fresh A=true for two; then neutral. Require windup and launch within
   15 ticks of the second press. Observe the same bomb's positive hop then y=0
   while still rolling; retain its eventual blast and wait until hazards clear.
3. At the unchanged lower-floor player position, late shot: A=true for two ticks,
   neutral for two. Keep neutral until a **fresh** snapshot first reports that bomb
   resting with .20 < fuse <= .30. Fresh A=true for two ticks, then neutral.
   Require launch followed by that bomb's blast with positive event y before hop
   expiry. Reject if the fuse window is missed, placement fails, or the player respawns.
   The .30 trigger is proposed, not a tested successful tick.

Budget: 600 walking ticks **total** (existing helper cap, not four times that), then
100 ticks per shot from first A.
Ready/orientation fits absolute lastTick<=900 (`J/matches/AuthorityReplayPlaytest.cs:60`).
Stop at completion and freeze that tick (>=120); timeout leaves it unmaterialized, without seed search.

Positive evidence to accumulate by event seq/entityId, rather than final snapshot:

- `bomb-drop`, `kick-windup`, `kick` for each bomb; `players[].kickWindup>0` with
  bomb state=0 before state=1 launch (`B/BlastSession.Kicks.cs:15–40`; drop: `B/BlastSession.BombActions.cs:146–155`).
- Early shot: state=1, y>0, increasing x, then state=1/y=0 after the .28-second hop.
  This is hop completion, **not** a Flying→Rolling terrain landing. No dedicated
  hop-land event exists; falling-kick landing also directly assigns state without an
  event (`B/BlastSession.BombTravel.cs:89–100`). Do not infer a ledge fall from this case.
- Late shot: matched `blast.entityId`, event y>0, bomb disappearance, fresh hazard
  y>0. Require instrumentation below to establish its actual Sin result/branch use.
  Mere `requireBomb`, visual elevation, or a normal blast after hop expiry is insufficient.

Source reachability and threshold distinction:
- Fresh A reaches contextual kick (`B/BlastSession.BombActions.cs:77–86`);
  `AdvanceKick` launches after 4/30 s, with 5/30 s recovery (`B/BlastSession.Kicks.cs:10–40`).
  `LaunchKickedBomb` assigns Rolling and .28 s hop for a non-bouncy bomb
  (`B/BlastSession.BombActions.cs:60–65`; `S/State/BlastBomb.cs:47–50`).
- `BombY` uses `operand = MathF.PI * KickHopRemaining / KickHopSeconds` and
  `support + .18f * MathF.Sin(operand)` while Rolling/hop>0
  (`B/BlastSession.BombTravel.cs:191–198`). Preserve that operation order.
- This is not display-only: `StepRollingBomb` selects cells with `BombY` (:217);
  `BombTerrainClear` passes it to AtHeight/CellsConnected/BombCellsDescend
  (`B/BlastSession.BombActions.cs:104–117`). `StageLayout.AtHeight` chooses Below
  iff y < cell.Tier-.25 (`S/Stages/StageLayout.cs:39–48`). For these tier-two bridges,
  a <=.18 lower hop is far below 1.75: source supports evaluation, not near-ULP sensitivity.
- Rolling player/billiard impacts use **support height**, not the moving bomb's
  hop (`B/BlastSession.BombTravel.cs:247,265`); the >.05 departure test uses support and starts
  FlyY=previousY, clearing hop (:233–240). Do not claim Sin perturbs those thresholds.
- Fuse expiry precedes hop decrement (`B/BlastSession.BombTravel.cs:150–172`). `ExplodeBomb` reads BombY
  into hazard Y, HitsPlayer, chain and cover queries (`B/BlastSession.Explosions.cs:20–38`).
  `BlastGeometry` clamps to occupied player height, checks distance²<=radius² and
  samples terrain/cover occlusion (`S/Rules/BlastGeometry.cs:13–47`). Same-floor player
  height clamping can remove vertical distance entirely; a nonzero hop does not prove
  a damage decision changes. Top Rules has DecksBlockBlasts=false, not solid-deck shielding.
- Record terrain comparisons and blast margins. No inspected public fixed recipe guarantees
  proximity to a discontinuity or a cross-OS branch flip; this exercises consumers only.
- Other BombY authority readers include Blizzard drift and flood removal
  (`S/BlastSession/Rounds/BlastSession.Blizzard.cs:24–34`, `S/BlastSession/Rounds/BlastSession.Rounds.cs:204`).

## Candidate 2: instrument the existing Blizzard case; add no duplicate match

**Existing public fixed recipe.** `assets/testing/authority-replay.json:131–144`,
`blizzard-ai-rng-math`: Stage="blizzard-battle", Seed=17017, humans=1, bots=3,
Wins=5, RoundSeconds=60, Ghosts=true; remaining defaults above, including Practice=false,
SuddenDeath=true, normal difficulty and unmodified AdvancedRules. No input changes:
slot 0 receives neutral before every tick 3…480 (478 driven ticks). Keep that budget.

- A bot-owned `bomb-drop` with a matching bombs[].ownerId and players[].bot=true
  proves proactive placement, since the human never presses A. `BotThink` evaluates
  the fixed table (`AI/BlastSession.Bots.cs:127–147`), orients, then revalidates/presses A
  (:103–110). Require selected direction index and accepted plan in a proposed trace.
- Table Sin/Cos operands are 32 fixed `direction*MathF.Tau/32` floats, evaluated in a
  static initializer (`S/BlastSession.cs:44–48`), possibly before any replay observer.
  Instrument actual initialization or retain its actual stored result bits; later
  recomputation is another canary, not proof of the values used. Record table index
  at `AI/BlastSession.Bots.cs:135`, utility/escape acceptance and resulting intent.
- Path choice flows `BotSearch` → `BotSteer` → `BotUnblockedInput`
  (`AI/BlastSession.Bots.cs:300–345,443–464`). Only a blocked nonzero desired direction
  not handled by retained recovery reaches the eight Sin/Cos pairs:
  `angle=(d+bot.Slot)*MathF.PI/4`, d=0…7. Collision/safety probes filter them; strict
  score>best selects the accepted vector (`AI/BlastSession.BotNavigation.cs:117–161`).
  Record loop-entry count, slot/d, desired vector, candidate acceptance/score,
  winner and RecoveryUntil. No public event identifies this fallback or chosen waypoint;
  movement/bomb counts cannot certify it. If absent by 480, report the gap.
- Graph edge order is `(d+bot.Slot)%4`, not RNG (`AI/BlastSession.Bots.cs:327`); angles are
  also slot-ordered. Weather hashes seed/round/cycle into diagonals without RNG or
  Sin/Cos (`S/Rules/BlizzardRules.cs:15–24`). Seeded does not mean random-angle math.
- Actual gameplay RNG is `new Random(options.Seed)` (`G/Matches/LocalMatch.cs:99`).
  Blizzard's regional cover uses Shuffle and weighted Priority/NextDouble before
  external ticks (`S/BlastSession/Rounds/BlastSession.Rounds.cs:77`,
  `S/BlastSession/Items/BlastSession.Loot.cs:15–47,152–179`, `S/Stages/CoverArea.cs:12–17`).
  Later draws: `S/BlastSession/Items/BlastSession.Loot.cs:79–82,103–107` (chance/type).
  Require actual draw-site observations, not independent replay Random equality.
  No public draw counter/checkpoint exists.

Keep the run through 480; evaluate predicates independently. `T/Snapshots/SnapshotPerformance.cs:12–26`
is another public authoring precedent; its 2700-tick multi-seed benchmark is unnecessary here.

## Evidence boundary and minimum first-divergence record

Retain case/options/seed, absolute tick, snapshot k, ordered input calls/edges, event
seq/entityId and first differing field. Proposed additional authority trace hooks must
record callsite plus per-tick call/substep ordinal, actual operand/result IEEE float32
bits, and raw pre/post accepted position, state, support, velocity, fuse/hop timer.
For terrain/blasts retain selected cells/layers, threshold operands/branch result,
distance²/radius² and first occluding sample; for bots retain start/waypoint, direction
index, legality/utility/escape result, selected input and ensuing accepted movement/bomb.
For RNG record actual callsite/order/API arguments and returned bits/value if instrumented;
label a new call-site counter's scope, especially Shuffle internals and constructor draws.
Never fabricate a private Random checkpoint or equate a canary draw count with authority.

Snapshots already round x/z to two decimals, y to three and fuse to two
(`SS/BlastSession.Snapshots.cs:57–69,97–108,114–117`); they cannot recover hidden bits.
Events retain supplied floats; accumulate them as snapshots arrive (128 entries/120 ticks;
`SS/BlastSession.Presentation.cs:12–13,31–57`).
No new comparer rounding/tolerance; trace hooks are proposals, not existing public APIs.

Internal/custom exclusions: `T/Bombs/BombImpactChecks.cs:53–58,131` (Run hop/Match),
`T/Bombs/KickTimingChecks.cs:48–49` (Match), `T/Bombs/BombTravelChecks.cs:95–96` (Match),
`T/Rooms/PartyPolishChecks.cs:21–37` (lips), `T/Snapshots/PresentationEventChecks.cs:12,29,45`,
and `T/Bots/BotChecks.cs:16,25,42,63`. These cannot be exported as public constructors.
`T/Stages/TopRulesChecks.cs:84–100` lower-kick is internal; :125–134 fast-descent reflects
`_session` and injects a bomb despite public Match. `T/Rounds/TutorialChecks.cs:15,36,122–127`
has a public kick lesson, but completion freezes simulation (:85–88), limiting boundary coverage.
No builds, game/test/runtime execution, source edits or seed searches; document review only.
