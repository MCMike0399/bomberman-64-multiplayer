# Gameplay and AI maintenance

Read this guide before changing authoritative actions, terrain collision, rewards,
bot navigation or difficulty. Use the source links for current constants; iteration
documents explain why a rule exists and may describe superseded behavior.
For keys, prompts and device input, read [input and UI](input-and-ui.md).

## Establish the failing behavior first

1. Identify the session: local normal battle, practice or network; record stage,
   seed, human/CPU slots, difficulty and the ordinary input sequence.
2. Observe accepted player/bomb state and the actual symptom in the same run.
   A bomb overhead, an accepted `aiming` flag and a visible target are separate facts.
3. Build a bounded assertion that fails on the symptom before editing its rules.
   Use a managed fixture for authority logic and a native journey for input/rendering.
4. Minimize while keeping the failing path. Practice clears conditions that can
   matter in a normal match; an empty courtyard does not reproduce cover collision.
5. Change the owning layer; repeat the original journey and the focused regression.
   Finish when the original failure is absent and related invariants still hold.

## Authority, transport and presentation

Tutorial setup and progress belong to the local authority, with ordinary bomb, collision and recovery rules. [The tutorial contract](../tutorial.md) explains lesson fixtures, online exclusion, failure/retry and concurrent map integration.

- [BlastSession](../../src/Bomber.Game/Simulation/BlastSession.cs) owns game state;
  its partial files share that authority. [BlastSim](../../src/Bomber.Game/Simulation/BlastSim.cs)
  defines entities and terrain semantics; [BlastStages](../../src/Bomber.Game/Simulation/BlastStages.cs)
  builds the actual arenas.
- [LocalMatch](../../src/Bomber.Game/LocalMatch.cs) adapts the kernel session context
  to deterministic local time/randomness. [LocalSession](../../src/Bomber.Client/LocalSession.cs)
  advances that same simulation through the engine-facing `ISession` interface.
- [NetworkSession](../../src/Bomber.Client/NetworkSession.cs) uses HTTP and WebSocket
  messages. The independent [Bomber.Server](../../src/Bomber.Server/Program.cs) registers
  `BomberRealtimeGame` on Burbuja.Hosting. Online play uses the same product rules.
- Local play needs neither an HTTP server nor Docker. Network verification requires
  an actual available server; local success cannot certify server delivery.
- The generic kernel owns transport/lobby infrastructure, not bomb or bot policy.
  Keep game fixes in this repository's product simulation; preserve its MIT provenance.
- Authority is 30 Hz; complete snapshots arrive at 15 Hz. Await accepted state rather
  than assuming two rendered frames equal two new snapshots.
- [GameRoot](../../scripts/app/GameRoot.cs) composes sessions and input;
  [ArenaView](../../scripts/arena/ArenaView.cs) renders accepted state.
  [SnapshotMotion](../../scripts/arena/SnapshotMotion.cs) smooths presentation.
  Render interpolation, animation and camera work cannot fix authoritative collision.
- Inputs express intent, never positions/hits. Maintain local/network input queues,
  snapshot fields and validation together when extending a gameplay action.
- A separately running server must be rebuilt/restarted when authority changes are
  delivered. Inspect its ownership/version first; external deployment is separate work.
  [Architecture and network boundaries](../architecture.md).

## Network action transitions

`BlastPlayer.ReceiveInput` retains bounded button transitions independently of
latest movement axes. `AdvanceReceivedInput` consumes one transition per authority
tick, so TCP batching cannot erase a press immediately followed by release.
The queue is capped at 16 and is cleared on disconnect, round reset and practice
respawn. Bots retain their per-tick decision path. The kernel acknowledgement is
still the received high-water mark, not a guarantee that every queued action has
already executed. Preserve this distinction if adding prediction later.

## Snapshot and performance budget

Presentation snapshots retain at most 128 accepted effects from the latest four
seconds. IDs continue across rounds; repeated snapshots share an immutable cached
array until an event arrives or expires. Preserve the 96-tick coalesced-client
recovery contract. Long-running tests must observe effects as snapshots arrive,
not assume old effects remain in the latest packet forever. `serverTime` is a
diagnostic generation timestamp; it is not an authority input or prediction clock.

`stageLighting` is cosmetic server-owned snapshot state: morning, midday,
afternoon or night. The lobby seed picks the first variant; rounds cycle all four
without consuming gameplay RNG. Include it in complete snapshots for late joiners
and reconnects. Local clients use their saved preference instead. See the
[lighting contract](visuals-and-animation.md#four-stage-lighting-variants-2026-10-23).

Use [the server performance guide](../server-performance.md) for the three-human,
three-CPU probe and fixed-seed encoding benchmark. Compare state hashes as well as
bytes/timing when optimizing. The server's five-second `game_perf` log reports the
kernel's most recent one-second tick window and cumulative drops/overruns; process
GC/CPU counters cover the separately labeled sampling interval. Delivery gaps can
remain even with fast simulation, so do not infer server CPU trouble from a client
freeze alone or promise that a bandwidth reduction eliminates all network jitter.

## Adaptive rooms (v0.29)

[Adaptive multiplayer](../smart-multiplayer.md) owns the current online flow.
Keep social ownership distinct from simulation hosting; reserve simulation or
relay capacity before starting, and retain ordinary guest departure/reconnect
without resetting a healthy peer match. The cluster pool excludes legacy IDs
so they cannot bypass active-execution budgets.

## Legacy random lobbies and player-hosted rooms

[Peer hosting](../peer-hosting.md) owns the v0.27 connection and failure contract.
`PublicLobbyGame` uses FIFO waiting-room admission and `PartySession` with a
two-human start minimum. `PeerRoomSession` owns membership, election and epochs;
`PeerMatch` runs the unchanged `BlastSession` on the elected client. Do not
confuse the cluster lobby host with the peer simulation host. When adding an
arena, update `PeerFrameRules` stage IDs/dimension bounds alongside
`SessionSettings` and `MatchOptions`; otherwise peer clients reject its stage.
Review protocol-version/capability compatibility when changing the supported
state or stage set, so older peer clients cannot start an unsupported room.

## Persistent private parties

`PartySession` wraps fresh `BlastSession` matches inside one kernel lobby. It
owns authenticated leader election, an explicit `party.start` command, saved
suits and the five-second results-to-lobby transition. Its context intercepts
`EndMatch` so a finished game does not close the party's transport. New matches
reset simulation state and scores; neither bots nor a full room authorize start.
Kernel connection lifecycle and its six-hour room limit remain separate from
the product's lobby/playing/results phases. The kernel may tick a waiting party
without starting its simulation. `party.status` carries that product state.
The leader may send `party.settings` only while waiting in the lobby; edits are
validated atomically, broadcast in `party.status.settings`, and used for the next
match. The existing HostSetup editor stays in the same room when opened from its
Room settings button. Guests and mid-match edits cannot change rules.
Legacy `blast` private rooms opt in via `party=on`; the `blast` queue and old
API rooms retain their previous lifecycle. Current Random lobby uses
`blast-lobby` and always owns a persistent party with a two-human minimum.
Kernel pin changes require the generic and consumer gates plus product validation.

`PartySessionChecks` and `PartyNetworkChecks` cover waiting, leader authorization,
results, repeated matches, identity retention and leadership transfer. The
latter must run against the exact staged server before remote deployment.

## Bomb actions: preserve both input paths

`A`, `B`, `R` below mean semantic primary, carry and remote actions, not physical keys.
Current bindings belong to [PlayerControls](../../scripts/input_devices/PlayerControls.cs), the README
and the input guide; older iteration tables are historical.

- [BombActions](../../src/Bomber.Game/Simulation/BlastSession.BombActions.cs) owns
  primary edges, hold candidates, placement validation and deliberate contextual kicks.
- A primary tap places near the feet, with a small forward bias and supported-center
  fallback. Exclude the placing owner when validating its initial overlap.
- `OwnerMayLeave` grants one geometric departure from that overlap. Clear it on
  separation, height change, pickup/launch or owner loss; it is not blast immunity.
- Walking away leaves the fresh bomb stationary. Returning into it after separation,
  or making a fresh primary press beside it, can deliberately initiate the kick.
- Holding the original primary placement while stationary lifts that same bomb after
  the hold threshold. Movement intent cancels the candidate so a departing player
  does not retrieve a bomb from behind and accidentally pump instead of planting.
- Carry-held plus primary creates directly overhead. Further accepted primary edges
  queue a charge level on the carried bomb (maximum three). Normal charge takes
  .45 seconds per level; Focus takes .15. Throwing cancels incomplete levels.
  Holding primary does not generate repeated requests; the HUD shows progress.
- Primary-lifted bombs throw on a carry press; carry-held pickup/create throws on
  carry release. Preserve `CarryFromPrimary` and edge consumption for both paths.
- Placement rejection publishes `bomb-blocked` for capacity/space feedback. Diagnose
  input acceptance, inventory, support and overlap before changing offsets or radii.
  [Placement regression](../history/iteration12-bombs-research.md),
  [action checks](../../tests/Bomber.Game.Tests/BombActionsChecks.cs),
  [placement checks](../../tests/Bomber.Game.Tests/BombPlacementChecks.cs).

## Throws, precision aim and fuse ownership

- [BombThrowRules](../../src/Bomber.Game/Simulation/BombThrowRules.cs) supplies shared
  launch/flight/static projection math; [ThrowAim](../../src/Bomber.Game/Simulation/BlastSession.ThrowAim.cs)
  owns accepted direction/range, fine-start ramps and reset/retention rules.
- Aiming plants the carrier. X/Z intent moves a landing cursor in arena directions,
  with a shared 6-cell/second limit and fine-start ramp. Range is 0–9 cells; the
  cursor can pass through the carrier to aim behind them, and self-lobs can hit.
  Releasing Aim restores movement while retaining selected direction/range; ending
  the carry/new carry resets it. This supersedes the old relative rotation model.
- Digital taps and partial analog intent use the same bounded fine-start adjustment.
  Ordinary movement returns to full normalized speed when Aim is inapplicable.
- Pumps increase blast power; they do not change deliberately selected range.
  Ordinary non-aim throws retain their separate movement/pump-dependent launch rule.
- Project the full bomb footprint against terrain, cover and pillar tops. Keep launch,
  gravity and landing-height equations aligned between authority and preview.
- The projection is advisory: moving players/bombs and future terrain/water changes
  can interrupt it. A visible marker is not a guaranteed hit or automatic targeting.
- The endpoint renders shapes only: safe landing ring, warning X, offscreen arrow.
  `EndpointLabel` is diagnostic metadata, not visible text. Match/phase gating and
  explicit hide-before-QueueFree prevent a carrier's overlay surviving into Results.
  [Reticle diagnosis](../history/iteration17-reticle.md), [aim rules](../history/iteration16-aim.md).
- Timed bombs pause their fuse while carried and resume afterward. Stopping a normal
  bomb against a corner must retain its fuse; wall contact never grants remote mode.
- Blue remotes wait while their owner can command them. Losing owner/connection,
  living status or remote capability resumes the remaining ordinary fuse.
- Remote command selects the oldest eligible owned remote, including moving bombs.
  AI opportunity evaluation must inspect the exact bomb authority would detonate.
- Black bombs are the starting type. One bomb slot holds red, remote or bouncing equipment; one independent
  ability slot holds heart protection or Focus. Capacity/fire still stack. A
  replacement drops the old equipment for anyone else to collect immediately;
  the former holder has a 1.5-second pickup lock. All items expire after the configured lifetime (20 seconds by default), including
  practice caches. Duplicate equipment and capped stacks remain for others until expiry.
- Existing bombs keep their mode, subject to orphaned-remote fallback. Red blasts
  pierce soft cover and have 25% more physical radius; their flame is blue.
  Fast bomb equipment was removed on 2026-10-25. Haste speeds the player by 50%
  in the existing temporary-effect slot; it preserves bomb/ability equipment,
  existing remote ownership, bomb travel and fuse timing. Its former drop weight
  now belongs to Haste. The `motionRate` snapshot field remains a constant 1 for
  peer-frame compatibility.
  Bouncing bombs reflect from walls, roll normally when kicked, and rebound up to three times from charged thrown landings and
  retain a finite 3.5-second fuse. Stop cancels kicked-bomb travel; gravity still settles a bomb over a drop.
  Uncharged throws settle on first landing. The user explicitly rejected automatic
  hopping during rolls; do not restore that superseded experiment. All other normal fuses remain 2.5 seconds.
- Each charge level increases gravity by 20%. Head contacts cap upward rebound
  and dissipate horizontal speed; large bombs must not relaunch like balloons.
  Authority, preview and deliberate aimed launch share the same weight/flight math.
- Focus nudges a neutral aimed cursor toward a visible living opponent within
  1.1 cells, at .7 cells/second. Explicit steering wins; no hard lock through stone.
  [PowerUpRules](../../src/Bomber.Game/Simulation/PowerUpRules.cs),
  [mode checks](../../tests/Bomber.Game.Tests/PowerUpModeChecks.cs).

## Contact, terrain and recovery

- Ordinary ledge falls use heavier gravity, 45% of status-adjusted ground speed
  and bounded air acceleration. Departure uses collision-resolved velocity;
  release/reversal preserve a short coast. Full ground-speed air steering is
  superseded. Thrown/knocked players and bombs retain their own trajectories;
  ordinary landing restores controls without stun. [References, tuning comparisons
  and regression entrypoints](../falling-physics.md).
- [Kicks](../../src/Bomber.Game/Simulation/BlastSession.Kicks.cs) commits direction
  during a planted wind-up and revalidates the bomb at strike time. Fuses continue;
  stun/fall/death/disconnect/reset cancel pending strikes; consumed edges stay consumed.
- [BombTravel](../../src/Bomber.Game/Simulation/BlastSession.BombTravel.cs) sweeps the
  full radius. A visible launch hop does not let a rolling shot pass through a wall.
  Billiard transfer preserves fuse/mode; impact knockdown uses capped impulses.
- A kicked bomb can leave a downward lip, fall and resume rolling on landing.
  `ResumeRollingAfterFall` distinguishes that behavior from ordinary carried throws.
  Ignore only the existing trailing ledge overlap; preserve uphill and pillar collision.
  Shallow diagonal ramp entries need the same escaping-overlap check while still rolling:
  the center may descend less than the falling threshold before its rear clears the upper
  floor. `PyramidLedgeChecks` reproduces the four courtyard stair corners and sweeps all
  downhill cardinal/diagonal edges at three alignments with stock geometry and no cover.
- Stop is a separate edge-triggered intent. Only the last kicker can stop a rolling shot;
  its original owner keeps remote/fuse ownership. Stopping a falling kick removes horizontal
  velocity while retaining gravity; it never pauses a fuse or stops an ordinary carried throw.
- [BodyContacts](../../src/Bomber.Game/Simulation/BlastSession.BodyContacts.cs) resolves
  height-aware player circles with terrain-aware shared corrections. Preserve tangent
  travel and bounded yielding; ghosts/carried players are not solid blocking bodies.
- [Grapple](../../src/Bomber.Game/Simulation/BlastSession.Grapple.cs) owns finite stumble,
  held victims, effort cooldowns and thrown-player flight. Direction changes/fresh action
  edges accelerate recovery; holding a single direction/button does not repeat effort.
- Bomb contact grants only 0.35 seconds of impact protection, independent of
  `InvincibleTimer` (heart/respawn damage protection). Grounded downed players
  can be hit again after that cooldown. Knockdown, throw and hold escape must
  never grant blast immunity; genuine blast geometry still determines hits.
  `BombImpactChecks` covers remote hit → immediate detonation, heart consumption
  and contact chains; the `grapple` native journey includes a remote snipe.
- A carrier may lift a downed opponent, move, then throw. Hold relationships are paired;
  escape, expiry, death, disconnect and reset must detach both sides safely.
  Recovery consumes its action edges even on the get-up tick.
- Ghost latching is finite, follows its victim and supports shake-off reversals.
  Keep ghost nuisance/expiry authoritative; ghost contact can destroy a pickup.
- `StageLayout.AtHeight`/`SurfaceAt(x,z,y)` select bridge versus underpass from
  the entity's actual elevation. Navigation uses separate nodes for overlapping
  floors. `CellDef.Below` is stage-owned geometry, serialized in complete stage
  messages. Suspended slabs have ceiling, side and blast collision; treating them
  as solid walls down to the sea removes the confirmed underpasses.
- `StageLayout.SurfaceAt` and `CanCross`/`CanWalk` in BlastSim are distinct: ramps
  support connected walking; directed downward exits may initiate falling.
- Water/void tier metadata is not the landing plane. Use actual water/void heights;
  a player's center leaving support starts falling, while radius-only shoreline grazing
  remains safe. Keep accepted water-death presentation from rewinding a practice respawn.
- Water/void casualties return to the nearest unblocked dry land cell as interactive ghosts
  during Fight when ghosts are enabled. Keep the death event at the actual contact plane,
  emit a separate ghost respawn, and prevent held outward input from immediately re-killing
  the ghost. Sudden death still removes ghosts and disables new comebacks.
- The user's latest correction supersedes the v0.22 rear barrier: the temple's
  rear edge is an open water pitfall, with no hidden walkway or invisible wall.
  The 25x27 arena has five raised tiers, broader upper platforms, and a larger
  courtyard. Rock Garden is a 19x19 enclosed grassy court with five columns. Up & Down is a 24x26 suspended arena with
  three elevations, six long flights and real underpasses; see the
  [stage contract](../up-and-down.md). Its twelve cover blocks intentionally
  replace the former filled terrace layout and 88-block budget. The summit uses a flat mosaic rather than a blocking prop.
- Ghosts pass through bombs and cannot place, carry, kick, stop, pump, throw or
  detonate them. Movement, finite latching and water/void comeback remain.
- Default battle rules are first to two wins, 120 total seconds including 60 seconds of
  sudden death. Water begins rising immediately at Hurry; the former 15-second delay is
  superseded. Custom round lengths and sudden-death/ghost toggles remain available.
- Rock Garden uses telegraphed falling fireballs during Hurry, with shared
  blast damage and AI warning avoidance. See the [stage contract](../rock-garden.md).
- [FloodRules](../../src/Bomber.Game/Simulation/FloodRules.cs) owns flood timing. Layout `RisingWater` selects flooding for Temple and Up & Down; Top Rules uses closing walls.
  [TempleStageView](../../scripts/arena/TempleStageView.cs) applies render height scale
  (currently 1.35) to authority Y. Decorative bedrock, foam, icons and camera fitting
  cannot add walkable floor, ramps or collision height.
  [Travel/water diagnosis](../history/iteration11-kick-research.md).

## Blizzard Battle (2026-10-25)

[Stage contract](../blizzard-battle.md) owns the flat enclosed snowfield, sixteen authored ice cones (1.3-unit stage-owned cover height), eight seats, shared seeded weather and bilateral avalanche closure. Wind affects free bombs and living players through collision, with weaker player response, and the advisory arc uses the same future schedule. Preserve fuse clocks and carried attachment. CPU closure avoidance uses the three-second avalanche interval. Protocol revision 5 gates the expanded stage/weather contract.

## Top Rules castle (2026-10-24)

`top-rules` adds the two-story castle researched in [the original manual and footage](../top-rules-research.md).
Three one-cell raised lanes intersect a central crosswalk; four corner stairs
join the red court and raised perimeter. Their four-cell flights use fractional
ramps (half a tier per cell), with continuous authority and rendered treads. `StageLayout.Underpasses` marks thin
upper slabs with a second tier-zero floor beneath. `SurfaceAt`/`CanCross`/`CanWalk`
accept a support ceiling; use the actor's preceding accepted height for movement
and flight. The default remains the topmost support for map authoring/spawns.
Do not replace bridge cells with full-height cliffs or snap lower actors upward.
Keep crate collision during ledge departure: filtering crates only by the
player's rounded height let a falling temple CPU enter a crate. The focused
`Bomber.Ai.Tests -- --soak --terrain-regression` retains Hard/seven CPUs/seed 64.

Castle wall cells publish an optional `ObstacleHeight`; their visible crowns,
throw collision and blast blocking agree. Null preserves existing pillar rules.
Bombs, dropped items, body contacts, blast occlusion, throw authority and advisory
projection retain the occupied story. Slabs block blasts and upward throws;
lower horizontal travel remains open. The CPU graph has separate lower nodes
only on maps with underpasses. Use its `X`/`Z` helpers and height-aware `Cell`
lookup; dividing a layered node by arena width invents an out-of-bounds Z.

The 19×17 footprint, tier-two decks, extra four starts and twelve seeded lower
court blocks are Blast Circuit tuning, not recovered original constants. The
first four starts follow the reference's outer lane ends; upper lanes, stairs
and lower crossing routes stay clear. Optional sudden death uses shared column
closure with visible castle walls on both sides. The original glowing edge
strip's damage behavior remains unknown; our amber edging is decorative.

Peer-frame validation accepts the fractional ramps and bounds the underpass
indices and optional wall crowns before allocating presentation geometry. The
three-peer direct/relay journey uses this castle; the eight-peer burst retains
the temple. Relay smoke also observes a host traversing the lower passages.

Focused checks: `Bomber.Game.Tests -- --top-rules`,
`Bomber.Ai.Tests -- --top-rules`, the `top-rules` native journey and the real
network castle room/traversal in `PartyNetworkChecks`. The standard AI soak
now covers five stages (60 combinations), including Blizzard Battle.

## Inventory, cover and pickup rendering

Greedy Trap uses three solid stories, six flights, eight bottom masonry blocks
and ordinary closing-column Hurry. Its turquoise basin is safe floor; it must
not inherit ordinary water elimination or an unverified sliding rule. The
[stage contract](../greedy-trap.md) owns topology, eight-seat adaptations and
focused authority/AI checks. Online revision 5 gates the newly supported arena.

- [Loot placement](../../src/Bomber.Game/Simulation/BlastSession.Loot.cs) uses map-owned
  `CoverAreas`: regional budget shares and cluster centers, with seeded weighted
  sampling. Temple seeds two nearby crates per spawn; Rock Garden seeds one plus a central paving cluster. Top Rules keeps a sparse twelve-block lower court. Up & Down keeps its upper ring clear, placing eight crates on the low deck and two on each middle pier. All policies preserve spawn clearance, ramps and reserved lanes.
  Every destroyed crate uses the same independent configured drop chance; there are
  no guaranteed opening rewards.
- Pyramid targets 60% of 90 blocks in the lower court, 15% along the lower wings,
  and a lighter remainder on upper side terraces/base approaches. Its top two tiers
  and ascent stay clear; thin court lanes replace the old broad empty strips.
  Rock Garden has sixteen boulders with clear center routes. Up & Down has twelve
  lower/middle blocks, keeping its ring, summit, bridges and flights clear.
- Advanced match settings default to a uniform independent 40% chance per
  destroyed block. Item weights are Fire/Bomb 30% each,
  Heart/Focus/Tiny/Big/Haste/Slow 5% each; Remote/Red/Bounce divide the remaining 10% equally.
  Thus upgrades lead (60%), buffs/debuffs follow (30%) and bomb types are rare (10%). Disabled types are
  removed before drawing and remaining weights are renormalized. An empty pool
  yields nothing. Death/swap/training items also respect the enabled pool.
- `AdvancedRules` owns drop chance (0–100%), block amount (0–100% of the authored
  regional budget), item lifetime (5–60s), blast cap (1–5), bomb cap (1–8), effect
  duration (5–30s) and per-item exclusions. Defaults: 40%, 100%, 20s, 5, 5, 10s.
  Starting capacity is min(3, configured cap); blast starts at one, and three
  pumps can raise a maximum five-stack blast to effective level eight.
- Tiny uniformly scales the model to .65 and movement to 1.5; Big uses 1.4/.65.
  Haste/Slow affect movement only at 1.5/.65. One temporary effect replaces the
  previous one; expiry/death/respawn/round reset clears it. Collision dimensions
  remain unchanged so growth cannot trap a player. AI uses the same speed and
  cap rules. See [original manual research](../status-items-research.md); separate
  Haste/Slow are project extensions rather than claimed original pickups.
- Local and online rules share serialization and validation. Waiting-room edits
  remain leader-only and atomic; the advanced subpage must retain the live room,
  its code, practice mode and unsaved basic settings when navigating back.
- Richer farming areas intentionally permit collection detours. CPU goals use
  marginal upgrade value and an engagement clock that crate farming cannot reset.
  After their difficulty-specific engagement interval without threatening a rival,
  CPUs favor a reachable attack and do not stop to bomb incidental cover while an
  open terrain route toward that rival exists. Easy allows six seconds between
  engagements, Medium four and Hard 2.5; real pressure resets this clock.
  Recheck this priority after orienting, immediately before the actual bomb
  press. A plan selected just before the deadline must not execute overdue
  incidental farming and force another escape. Refresh pursuit state for the
  current bot before this recheck; sealed routes still permit demolition.
  The stock pressure contract now requires five threatening bombs in 30 seconds
  (previous layout: six), while retaining eight-second first/gap bounds and zero
  self-deaths. Preserve the measured tradeoff instead of claiming unchanged pressure.
- Low-magnitude bot steering must still probe collision and track failed progress.
  A .1 threshold in recovery allowed a .075 command to stall for 16 seconds in
  Rock Garden Easy / seven bots / seed 64 with the new loot. The focused
  `--soak --idle-regression` retains that real case; full soak remains required.
- Inventory caps, exclusive modes and pickup counters belong to PowerUpRules and
  authority item processing. New cover rewards survive their revealing explosion;
  test through the full fuse/hazard lifetime before collecting.
- [PowerupModel](../../scripts/arena/models/PowerupModel.cs) separates icon geometry from
  its base plate. Icon scale/lift is visual; changing its size must not alter pickup
  collision, inventory or plate dimensions accidentally.
- `loot-opening` verifies normal battle plant → retreat → surviving reward → collection,
  matching real inventory/counters and item-model removal. Its focused inspection camera
  is identified separately from normal battle-camera evidence.
  [Loot checks](../../tests/Bomber.Game.Tests/LootAvailabilityChecks.cs).

### Item density tuning (2026-10-25)

The requested priority means relative appearance frequency: capacity/blast upgrades,
then buffs/debuffs (including Heart and Focus), then bomb equipment. Every crate
still rolls independently, including opening crates. There is no guaranteed first
reward or forced chronological unlock. Local, peer-hosted and server matches share
these rules; explicitly configured drop percentages still override the new default.

A deterministic sweep compared 30%, 35%, 40%, 45% and the former 55% across all four
battle maps and seeds 0–127 (2,560 matches per sweep). It shuffles the actual cover,
then destroys every block through `DestroyBlocksInBlast`. Collection and expiry are
excluded: these are upper bounds on simultaneous clutter, not observed average
on-screen counts or a human fun rating. The same sweep was run before and after
the weight change; probability totals match because the pool still totals 100.

With the new weights, Temple's average total drops / probability of any upgrade
within the first eight opened crates were:

- 30%: 26.64 / 79.7%; sparse-map early-upgrade rates fell as low as 75.0%.
- 35%: 31.02 / 85.2%; all three smaller maps reached only 83.6%.
- 40%: 35.10 / 89.8%; smaller maps reached 85.9–86.7%.
- 45%: 39.57 / 94.5%; more clutter for roughly four extra early-upgrade points.
- 55%: 49.16 / 96.1%; the former density leaves substantially more items.

40% is the selected compromise: roughly 27% fewer expected drops than 55%, with
24 upgrade, 12 effect and four bomb-type drops expected per 100 destroyed blocks.
Measured all-block totals at 40% versus the former 55% were Temple 35.10/49.16,
Rock Garden 6.49/8.82, Up & Down 4.66/6.58 and Top Rules 4.80/6.48. Their 40%
95th-percentile totals were 42, 10, 8 and 7. This preserves the honest possibility
of dry openings: 10.9–18.0% of sampled matches had no item in their first four
opened crates. Raising the rate to hide that would restore clutter.

Default Temple and Circuit Garden practice caches now sample six authored
pickups: three upgrades (two Fire, one Bomb), two effects and one bomb type.
Six is the smallest cache with all three categories present in strictly
decreasing counts; smaller caches force a tie or omit a category.
The seed chooses which effects/mode appear. The Godot session entrypoints choose
a fresh seed for each normal/free practice start or reset, so the same fixed
cache is not repeated forever; explicitly seeded managed sessions remain fully
repeatable. Normal battle and authored lessons retain their existing seed policy.
Circuit Garden has a second authored Fire candidate to support that mix.
Candidates are filtered before sampling. Explicitly restricted pools of six or
fewer are kept intact for targeted practice; larger pools use category budgets
and may yield fewer than six if a category is disabled. Authored lesson fixtures
and initial map items remain independent of the cache selection. All ordinary
items retain the configured lifetime (20 seconds by default), without automatic
refill; equipment exchanged or dropped during play may temporarily add pickups.

Raw local receipts are ignored under `artifacts/item-density/`: baseline/tuned
sweeps and the retained failing priority witness. Reproduce with
`dotnet run --project tests/Bomber.Game.Tests -- --loot-density-sweep`; use
`--loot-density` for seeded category/cache/filtering/expiry regressions.
The separate Fast-bomb-removal work owns the equipment roster; recheck the weights
and full-type assertions when integrating that change.

## AI policy and known failure mechanisms

- [Bots](../../src/Bomber.Game/Simulation/BlastSession.Bots.cs) selects ordinary intent;
  [BotNavigation](../../src/Bomber.Game/Simulation/BlastSession.BotNavigation.cs) observes
  displacement and steers around visible bodies/collision.
- [NavigationGraph](../../src/Bomber.Game/Simulation/Ai/NavigationGraph.cs) caches directed
  stage walk edges. An open graph edge does not prove an off-center body can turn.
- [TimedDangerField](../../src/Bomber.Game/Simulation/Ai/TimedDangerField.cs) predicts
  visible fuse/chain/hazard intervals conservatively. Merged intervals may reject a
  real safe gap; they must not invent a safe passage through an active threat.
- Difficulty profiles change reaction, depth, spacing, cadence and tactical utilities;
  keep equal speed, collisions, resources, damage and visible information.
  `medium` and legacy wire `normal` select the same Medium profile.
  Exact policy values live in [BotDifficultyProfile](../../src/Bomber.Game/Simulation/Ai/BotDifficultyProfile.cs).
- [BotObjectives](../../src/Bomber.Game/Simulation/BlastSession.BotObjectives.cs)
  computes reverse distances along actual directed walk edges once engagement is
  due. This rewards a necessary detour away from a rival. If cover seals every
  route, a second field ignoring cover guides demolition; it grants no movement
  permission. Tactical search still enforces bodies, bombs, cover and timed safety.
  Reverse edges must preserve drops and separate bridge/underpass nodes.
- Remote opportunity checks force the oldest eligible remote to time zero in a
  separate danger field before approving its trigger, including visible chains
  back to the owner. This strengthens the opportunity boundary independently of
  the main planner's urgent escape branch. Candidate detonation forecasts cannot
  rely on current soft cover: authority destroys cover before recursively
  detonating chained bombs, and lingering hazards see the resulting terrain.
  Permanent walls still shield. `ProactiveTacticsChecks` executes the actual
  candidate command in paired soft-cover/permanent-wall fixtures, proving both
  the rejected lethal chain and the retained safe attack.
- Preserve escape validation and timed route safety when increasing pressure.
  Easy has occasional attack intent so cautious spacing cannot suppress every attack.
- A body backed against stone needs body-aware route checks. Off-center diagonal
  contact needs ordinary axis-separated motion probes and brief alternate steering.
- Measure zero-input idle as well as blocked requested movement. Neutral score optima
  can freeze without issuing movement. Bounded safe exploration and expiring failed
  waypoints recover without teleporting, increasing speed or bypassing collision.
- Exclude legitimate stun/held/airborne/kick pauses from stall diagnosis.
- Goal commitment stabilizes travel; expire it on reaching the committed cell so the
  reward cannot keep a bot in its post-blast refuge indefinitely.
- Stock Pyramid exposed a second optimum: geometric distance favored the lower court
  over the ramp detour into real blast range. Attack-intent scoring now rewards actual
  visible blast positions. Changing reaction delay alone does not fix that route choice.
  [AI fixture definitions and measured limits](../../tests/Bomber.Ai.Tests/README.md).

## Choose the test that owns the symptom

| Symptom | Read first | Focused regression/journey |
| --- | --- | --- |
| Tap ignored, depart/pickup wrong | BombActions, placement validation | BombPlacementChecks; `placement`, `bomb-handling` |
| Kick stops at a ledge, fuse stalls | Kicks, BombTravel, UpdateBombs | KickTimingChecks, BombTravelChecks; `kick-physics` |
| Stuck crowd, recovery or victim hold | BodyContacts, Grapple | BodyContactChecks, GrappleChecks; `body-contacts`, `grapple` |
| Shore death or ramp mismatch | BlastSim, landing heights, FloodRules | TraversalAndGhostChecks; `traversal`, `hurry-water` |
| Aim misses or guide disappears | BombThrowRules, ThrowAim, ArenaView | AimedThrowChecks; `precision-aim`, `reticle` |
| Missing upgrade/icon | Loot, PowerUpRules, PowerupModel | LootAvailabilityChecks; `loot-opening`, `asset-audit` |
| Bot freeze | BotNavigation, body collision, timed search | NavigationLivenessChecks; `--liveness`, `--soak` |
| Bot retreats forever or never threatens | BotGoalScore, commitment, blast geometry | `--pressure`, `--stock-human-pressure`, `--tactics`; `ai-play` |

Managed entrypoints: `dotnet run --project tests/Bomber.Game.Tests` and
`dotnet run --project tests/Bomber.Ai.Tests -- --stock-human-pressure`.
Native journey: `python3 tools/playtest/run.py --scenario reticle`.
See [testing and delivery](testing-and-delivery.md) for safe execution and evidence gates.

## Interpret evidence and original references correctly

- Pressure is visible opponent-threatening blast opportunity, not raw bomb count,
  confirmed damage, wins or fun. Track first pressure, maximum gap, safe exits, pickups,
  deaths and completed rounds; preserve active/eligible time denominators.
- Compare policies with identical stage/loot/seed/slots. Normalized rates are descriptive
  when routes, survival, resources and round counts diverge; report mixed results.
- Sustaining an invulnerable human target is an isolated fixture, not an ordinary match.
  State fixture edits explicitly; recorded gameplay must use ordinary inputs.
- Use [primary-source research](../history/research.md), [manual controls](../history/iteration16-controls-research.md)
  and [recorded observations](../history/gameplay-research.md) before claiming original behavior.
  Footage can show a kick/throw without revealing which button caused it; pump growth
  alone does not establish exact timing/radius/distance formulas.
- Store reference media outside distributed game assets. Distinguish publisher claims,
  direct frame observations, inference and project tuning. Read the latest correction
  before reusing older controls, gait or numerical choices from an iteration note.

Iteration23 AI regressions: match authority's extra .5-cell bomb chain margin;
allow safe timed transit through threatened cells during an escape instead of
requiring those cells to remain safe for the whole planning horizon. Bots ignore
utility from their own swapped equipment. If orienting invalidates a planned
bomb placement, they navigate before retrying, avoiding endless tiny turns. The enlarged stock Pyramid pressure
check allows eight seconds to first pressure (previous smaller map: six), retains
five threatening bombs in 30 seconds, an eight-second maximum gap and zero deaths.

## Integrated stage contract (v0.28)

`CellDef.Below` is the single lower-surface representation for Top Rules and Up & Down; `Underpasses` is derived metadata, not independent terrain. Preserve height-aware support, fractional stairs, authored wall crowns, full held-body clearance and swept projectile ceilings. Meteor warning times seed bomb-chain prediction before propagation. See [review findings and regressions](../stage-integration-review.md).

Top Rules thin decks pass blasts as demonstrated by the original official guide; solid masonry still shields and physical bodies/throws still collide with decks. Up & Down's corresponding blast penetration remains unverified, so it retains its authored shielding. [Evidence](../stage-fidelity-research.md#newly-recovered-top-rules-mechanics-evidence).

Protocol 2 introduced the stage contract; protocol 3 added adaptive rooms and protocol 4 removed Fast bomb semantics. Greedy Trap advances admission to protocol 5 at HTTP, the actual WebSocket worker and matching peer hello so loadout-compatible older clients cannot receive an unsupported arena. A coordinated client/server release is required; this map PR does not deploy it. Preserve readiness/operator/leave/cancel paths and test protocol-3/4 update feedback before accepting game state. Lower cells and authored wall crowns must pass bounded schema validation before rendering. [Online protocol](../peer-hosting.md#online-stage-protocol-2-v028).
