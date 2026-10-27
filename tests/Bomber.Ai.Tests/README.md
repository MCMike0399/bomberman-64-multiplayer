# Deterministic bot checks and benchmark

Run the behavioral checks without Godot or a network server:

```sh
dotnet run -c Release --project tests/Bomber.Ai.Tests
```

The default run executes 98 checks, including the 2026-10-26 agency regressions below. The original 76 checks cover cached legal terrain edges, timed chain reactions, expired hazards, deliberate power-up pursuit, fuse escape, climbing before flooding, fair difficulty response rates, eight-bot deterministic replay on all three maps, repeated bomb pressure against opponents, and reuse of search memory. Existing game integration checks separately exercise safe own-bomb placement, trapped-bot refusal and actual stair movement.

The planner sees current entities, visible bomb fuses and the known flood schedule. It never consumes future item randomness or changes movement speed, damage, blast radius or inventory. The menu offers **Easy, Medium, Hard**; `medium` and legacy `normal` select the same profile, with `normal` retained as the default wire value for saved clients.

| Level | Reaction interval | Tactical depth | Engagement interval | Typical behavior |
|---|---|---|---|---|
| Easy | .24 seconds | 6 steps | 6 seconds | Gives space and values pickups, with slower attacks and time to breathe |
| Medium | .12 seconds | 12 steps | 4 seconds | Balances pickups and pressure, avoids poorly connected contested positions |
| Hard | .08 seconds | 24 steps | 2.5 seconds | Reengages sooner with persistent goals and stronger escape-route evaluation |

Engagement means a bomb that actually threatens a visible rival; farming cannot
reset that clock. Once engagement is due, a reusable reverse path field rewards
progress around walls and along directed stairs. Sealed cover routes allow
demolition before pursuit. These strategic fields never override timed escape
or authorize movement through cover.

Intervals are quantized by the shared 30 Hz authority. All levels use the same collision and speed rules, plan an escape before bombing, and receive progress recovery. Tests distinguish positioning and risk choices independently of reaction time and require actual attacks/explosions against a stationary opponent on every level. Remote evaluation matches the authority's oldest eligible bomb, including rolling/flying remotes, so it cannot evaluate a safe second bomb but detonate a dangerous first one. Advanced lift/throw/pump combos are not part of this policy.

Terrain adjacency is cached for each immutable stage. A session reuses queues, visitation generations and shared opponent/cover observations. Bomb danger includes fuse times, visible chains, remaining blast lifetimes, conservative loss of soft cover, and short moving-bomb projections. Multiple danger intervals in a cell are merged conservatively. Unpredictable future human throws and remote-button presses cannot be guaranteed; moving-bomb and remote forecasts are approximations, not complete forward simulation.

## Historical iteration-three benchmark and repeatable measurements

```sh
mkdir -p artifacts/ai
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --benchmark --output=artifacts/ai/current.json
```

Each map runs eight AI-controlled participants for 1,800 authority ticks with seeds 64, 123 and 9876. The harness records active fight ticks, live-bot ticks and actual decisions, so ended/ready/round-over work cannot masquerade as faster AI. All nine recorded runs had 1,739 active ticks. These are simulation-only timings: transport serialization, server scheduling, Godot and rendering are excluded. Small fixture/measurement allocations are included. Timing and allocation checks are observations, not CI thresholds.

The following before/after measurements were captured during iteration three, before the later near-foot placement and iteration-thirteen navigation changes on Apple M5 Pro, .NET 10.0.401, Release configuration. The earlier planner was measured before the replacement; bomb kicking, overlap escape, power-up selection and inventory resets also changed in the same iteration. This is an iteration comparison, not an isolated causal AI speedup. Values below are descriptive single-run measurements; JIT, GC and machine load affect them.

| Map | KiB allocated / active tick, before → after | Median of three p95 active-tick times, ms, before → after | Largest observed active tick after, ms |
|---|---|---|---|
| Rock Garden | 13.17 → 5.04 | 0.091 → 0.278 | 2.123 |
| Up & Down | 10.06 → 4.18 | 0.058 → 0.039 | 0.860 |
| Sunken Temple | 11.04 → 4.21 | 0.061 → 0.072 | 0.338 |

The richer planner uses more CPU on Rock Garden and slightly more on Sunken Temple. The measured tails remain below the authority's 33.33 ms tick interval in this small workload; this does not establish fleet capacity. A separate warmed-path check executes 1,000 decisions with zero bytes allocated for repeated search decisions.

Behavior counters across each map's three seeds:

| Map | Requested movement with <0.1-cell progress over a 1-second sample, before → after | Observed power-up increases, before → after | New bombs, before → after | Dead at the end, before → after |
|---|---|---|---|---|
| Rock Garden | 131 → 10 | 34 → 70 | 588 → 366 | 8 → 11 |
| Up & Down | 144 → 36 | 18 → 34 | 621 → 369 | 8 → 7 |
| Sunken Temple | 146 → 26 | 6 → 19 | 624 → 414 | 8 → 3 |

The movement sample is a proxy for blocked/slow movement; it can include legitimate braking or waiting. Power-up increases count observed inventory improvements, rather than every item touched. Final deaths are a point-in-time count, not a win-rate or survival guarantee. Fewer bombs and more items do not prove that the game is more fun. Targeted escape, pressure and flood checks establish specific behavior; human playtesting is still needed for pacing and challenge.

## Gameplay soak and stationary intent

```sh
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --soak --output=artifacts/iteration13/soak.json
```

The deterministic matrix runs 60 scenarios: five arenas, Easy/Medium/Hard, three/seven bots, and seeds 64/9876. Each scenario runs up to 2,700 authority ticks (90 seconds), stopping if the five-win match finishes; `--ticks=N` overrides its duration. The JSON records movement, stationary windows, blocked movement commands, safe waits, no-safe-exit waits, new bombs reaching current opponents/cover, destroyed cover, pickups, falling transitions, deaths and round wins. Pressure describes visible blast opportunities, not actual hits. Terrain drops count transitions into the physics falling state, not every descending tick. Deaths include the final elimination that ends a round. These homogeneous difficulty matches describe activity and pacing; their outcomes do not establish pairwise difficulty win rates.

Liveness examines every Fight/Hurry tick for living, non-ghost bots outside carried, stunned, airborne, falling and kick recovery states. A stationary window ends on at least 0.12 cell displacement from its anchor. It includes zero movement commands, so a planner that permanently chooses its current position is detectable. A continuous stationary window exceeding 12 seconds fails only while an adjacent authoritative terrain edge remains uncovered, bomb-free, unoccupied and safe in the visible danger forecast for 0.9 seconds. Windows without a safe exit are reported separately, preserving legitimate trapped-bot waits. This is a bounded local movement check, not proof that every strategic goal is reachable. JSON includes the worst bot's position, goal, command and living opponent count for diagnosis. The harness reads current cover/hazards via reflection to keep measurement out of production policy.

Four fairness checks also require identical initial terrain, spawn positions and player resources across difficulty levels on every arena, and deterministic equivalence of the Medium and legacy Normal setting over 600 authority ticks. Existing behavior/outcome checks remain independent and run without `--soak`.

Iteration 13's recorded matrix passed all 36 scenarios and four fairness checks. The worst eligible stationary window with a safe exit fell from 85.03 seconds in the initial measurement to 1.87 seconds after the navigation fixes; the longest continuous blocked movement command was 0.10 seconds. Final scenarios produced 54 completed rounds over 3,172 simulated seconds (matches can end before the 90-second cap), 1,953 new bombs, 734 visible opponent-pressure bombs and 436 pickups. Initial/final measurements and per-scenario diagnostics are in `artifacts/iteration13/soak-before.json`, `soak-final.json` and `soak-summary.json`. These bounded seeded observations support the specific stall fix; they do not establish that Hard always wins or substitute for human pacing feedback.

## Iteration-thirteen local timing sample

A fresh Release benchmark on Apple M5 Pro ran eight bots on all three maps with three seeds (nine runs, 14,891 active authority ticks). Per-run p95 active-tick times ranged from 0.102 to 0.378 ms; the largest single observed active tick was 4.666 ms. These are single-run local measurements, not a comparative speedup or production-capacity guarantee. Transport, serialization and Godot rendering remain excluded. The separate warmed-search regression still reports zero allocated bytes across 1,000 decisions. Evidence: `artifacts/iteration13/benchmark.json`.

## Iteration-seventeen repeated human pressure

```sh
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --pressure --output=artifacts/iteration17/ai/pressure-after.json
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --stock-human-pressure --output=artifacts/iteration17/ai/stock-human-after.json
```

The normal suite includes twelve 30-second open-arena fixtures: Easy/Medium/Hard, seeds 64/9876, and stationary/moving human opponents. Four additional fixtures use stock Sunken Temple/Pyramid, one human in slot zero and one Medium/Hard bot in slot one, both seeds, original spawns/cover/items, and a stationary human. The moving open-arena opponent follows a square using ordinary 35% movement input. Only the human target's invulnerability timer is renewed to sustain one fight; bots retain ordinary resources, damage, collision and visible information. Stock observations stop on bot death or after 30 seconds. No bot deaths occurred in the recorded cases.

Pressure counts newly placed bombs whose real visible blast geometry reaches the current human. It does not count hits or predict future movement. The longest gap includes initial approach and the final measurement tail. Open fixtures have no drops or cover and intentionally give identical observations for both seeds; the stock fixtures retain seeded cover and item drops and record initial cover fingerprints to verify matching baselines. Inventory availability in open fixtures counts ticks with spare capacity, without claiming an escape-safe placement exists on every tick.

Medium and Hard must create at least six pressure bombs in 30 seconds, start within four seconds in the open arena/six on Pyramid, and leave no gap longer than six/eight seconds respectively (since 2026-10-26 the open-arena *moving* gap is bounded across 24 start/phase variants in `AgencyChecks` instead of this single sample). The stock bound allows ramp and pickup detours. Easy has a forgiving minimum of one, twelve seconds to begin, and a twenty-second maximum gap. All require a full 900 active ticks and no bot death. The earlier all-level regression still separately requires a real threatening explosion. `--measure-only` records observations without enforcing thresholds.

Two passivity causes were reproduced with red regressions. A reached tactical goal kept awarding its commitment bonus, discouraging reengagement after retreat. Commitment now expires upon reaching its cell and still stabilizes routes while traveling. On Pyramid, Medium could also retreat into the lower court and idle below a cliff after all bombs had expired: the eight-step ramp detour cost more than the geometric distance reward for getting closer. Goals that reach an opponent's actual visible blast range now receive a pressure reward during attack intent. This lets a valid detour beat that local optimum. Easy receives that reward only during its existing occasional attack turn. Cooldowns, reaction intervals, difficulty profiles, timed route safety, escape validation and physical rules remain unchanged.

| Open difficulty | Stationary pressure bombs, before → after | Stationary maximum gap, seconds | Moving pressure bombs, before → after | Moving maximum gap, seconds |
|---|---|---|---|---|
| Easy | 4 → 4 | 7.20 → 7.20 | 2 → 2 | 10.67 → 13.20 |
| Medium | 4 → 11 | 8.53 → 2.80 | 8 → 9 | 5.87 → 5.33 |
| Hard | 6 → 15 | 9.20 → 2.70 | 10 → 10 | 5.10 → 4.20 |

| Stock Pyramid | Human-pressure bombs, before → after | First pressure, seconds | Maximum gap, seconds | Bot pickups, before → after |
|---|---|---|---|---|
| Medium / 64 | 4 → 7 | 3.07 → 3.07 | 12.27 → 6.93 | 3 → 3 |
| Medium / 9876 | 8 → 8 | 3.07 → 3.07 | 3.87 → 3.87 | 0 → 0 |
| Hard / 64 | 9 → 8 | 2.90 → 2.90 | 3.90 → 4.20 | 3 → 1 |
| Hard / 9876 | 8 → 7 | 2.90 → 2.90 | 3.90 → 4.20 | 2 → 0 |

Stock revealed-item counts were 4 → 4, 1 → 1, 4 → 2 and 2 → 1 in that row order; destroyed cover was 4 → 4, 2 → 2, 4 → 2 and 3 → 2. Every scenario started with 30 cover blocks, and before/after initial-cover fingerprints matched for each seed. The Pyramid results support reduced Medium seed-64 passivity, while Hard creates fewer bombs in those stock cases. They do not establish a universal increase in attacks.

The normal suite passes 52 checks, including zero allocations across 1,000 warmed decisions. All 36 soak scenarios and fairness checks pass: maximum safe-exit stationary window 1.63 seconds and blocked-command window 0.13 seconds. With identical iteration-seventeen loot/stage rules on both sides, the soak produced 2,197 → 2,179 bombs, 740 → 723 opponent-pressure bombs and 61 → 72 completed rounds; deaths rose from 267 to 296. Whole-match outcomes, item routes and durations diverge after a policy change.

The matched soak records eligible bot exposure, allowing pooled rates rather than raw totals alone. The denominator is `sum(EligibleBotTicks) / 30`: living, non-ghost bots in Fight/Hurry outside held, stunned, airborne, falling and kick recovery states. Unrestricted living-bot time is not recorded. These are rates per **eligible bot-second**:

| Scope | Eligible bot-seconds, before → after | Bombs/second, before → after | Opponent-pressure bombs/second, before → after |
|---|---|---|---|
| All | 7,745.17 → 7,431.57 | .28366 → .29321 | .09554 → .09729 |
| Easy | 3,350.03 → 3,152.00 | .18716 → .19511 | .08239 → .07043 |
| Medium | 2,673.50 → 2,532.73 | .29961 → .32889 | .10885 → .11016 |
| Hard | 1,721.63 → 1,746.83 | .44667 → .41847 | .10049 → .12709 |

Overall exposure-normalized bomb frequency rises 3.37% and pressure rises 1.83%; Medium pressure rises 1.20%, Hard rises 26.47%, and Easy falls 14.51%. Numerators count new bombs on active ticks and are not separately filtered by eligibility, so these are exposure-normalized summaries rather than conditional placement probabilities. Pooled rates weight longer/larger bot exposures; deaths, resources, routes, round restarts and eligibility still diverge. These seeded observations do not prove higher difficulty win rates, improved survival, or fun; human playtesting remains necessary.

Nine local Release timing samples with eight bots had per-run p95 active-tick times of 0.078–0.312 ms and a largest tick of 5.045 ms. The matched prior-policy sample was 0.066–0.414 ms, largest 5.570 ms. These are single local simulation-only samples and do not establish a speedup or production capacity. Artifacts are under `artifacts/iteration17/ai/`: `pressure-before.json`, `pressure-after.json`, `stock-human-before.json`, `stock-human-after.json`, `soak-matched-before.json`, `soak-after.json`, `soak-normalized.json`, `benchmark-matched-before.json`, `benchmark-after.json` and `summary.json`.

Top Rules adds separate bridge/underpass graph nodes. `--top-rules` checks both
levels, per-level danger/occupancy and real CPU stair ascent for all difficulties.
The main soak includes the castle; historical 36-case receipts above retain
their original scope.

`--soak --terrain-regression` retains the temple Hard/seven-CPU/seed-64
crate-overlap witness found while adding layered terrain. Crates must still
block ledge departures; height-only filtering can trap an actor after falling.


## Suspended Up & Down (2026-10-24)

The former filled terrace/farming layout is superseded by the
[stage contract](../../docs/up-and-down.md). `UpAndDownAiChecks` checks separate
bridge/underpass bomb occupancy and danger intervals, then ordinary CPU
planting/escape against fixed visible lower cover in a declared lower-start
fixture. `FrontPlacementChecks` retains the stock three-CPU destruction/drop
witness. Its seven-CPU stock case requires repeated real combat, allowing the
CPU to retain the exposed upper ring instead of forcing a farming detour.
Soak safe-exit observations now use the actor's elevation and both navigation
layers. Earlier pressure/cover measurements above belong to older layouts.

## Proactive pursuit regression (2026-10-25)

```sh
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --tactics
```

Four graph contracts verify exact reversed directed edges on every arena.
Seven declared authority fixtures cover all-difficulty long wall detours,
all-difficulty demolition through a complete cover barrier, and refusal of a
remote opportunity whose visible chain reaches its owner outside the direct
blast, followed by approval once the owner reaches safety. The wall and
remote-opportunity assertions fail on the prior policy;
cover scenarios preserve a positive behavior. Fixtures position a stationary
human explicitly and renew only its invulnerability to sustain observation.
CPU movement, bomb creation, escape and destruction run through authority ticks.
These fixtures are separate from unedited native `ai-play` matches.

[Research, adaptation and matched measurements](../../docs/cpu-ai-research.md).

## Agency regressions (2026-10-26)

```sh
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --agency-checks
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --agency --output=artifacts/ai-agency/matrix.json
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --agency --sweep
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --agency --trace=up-and-down,easy,64
```

`AgencyChecks` (17 assertions, 16 of which fail on the v0.36 policy) covers a
Bouncing-bomb holder shuffling beside an idle player, Easy/Medium movement through
Blizzard gusts, four-Hard-CPU lane standoffs, the longest reversal streak and
idle-player gap across a 72-run arena matrix, moving-target gaps across 24
start/phase variants (variant 0 is the fixed `PressureChecks` fixture), and a
sidestep from the lane of a bomb a walking player is about to kick.
`AgencyDiagnostics` owns the measurements; `--standoff` reports bombs by four CPUs
in their first 20 seconds. [Mechanisms, changes and matched results](../../docs/cpu-agency.md).

The agency matrix and moving-target comparison checks pin their original three-bomb
reference loadout so changes to starting inventory do not change the policy
comparison. CLI diagnostics, fixed-seed pressure checks, stock-human pressure
checks and the gameplay soak retain the current two-bomb default.
