# Deterministic bot checks and benchmark

Run the behavioral checks without Godot or a network server:

```sh
dotnet run -c Release --project tests/Bomber.Ai.Tests
```

The 36 checks cover cached legal terrain edges, timed chain reactions, expired hazards, deliberate power-up pursuit, fuse escape, climbing before flooding, fair difficulty response rates, eight-bot deterministic replay on all three maps, repeated bomb pressure against opponents, and reuse of search memory. Existing game integration checks separately exercise safe own-bomb placement, trapped-bot refusal and actual stair movement.

The planner sees current entities, visible bomb fuses and the known flood schedule. It never consumes future item randomness or changes movement speed, damage, blast radius or inventory. The menu offers **Easy, Medium, Hard**; `medium` and legacy `normal` select the same profile, with `normal` retained as the default wire value for saved clients.

| Level | Reaction interval | Tactical search depth | Typical behavior |
|---|---|---|---|
| Easy | .24 seconds | 6 steps | Gives space, values pickups, slower bomb cadence; approaches to attack after six seconds without a bomb attempt |
| Medium | .12 seconds | 12 steps | Balances pickups and pressure, avoids poorly connected contested positions |
| Hard | .08 seconds | 24 steps | More persistent goals, faster bomb cadence, stronger escape-route and bottleneck evaluation |

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

The deterministic matrix runs 36 scenarios: three arenas, Easy/Medium/Hard, three/seven bots, and seeds 64/9876. Each scenario runs up to 2,700 authority ticks (90 seconds), stopping if the five-win match finishes; `--ticks=N` overrides its duration. The JSON records movement, stationary windows, blocked movement commands, safe waits, no-safe-exit waits, new bombs reaching current opponents/cover, destroyed cover, pickups, falling transitions, deaths and round wins. Pressure describes visible blast opportunities, not actual hits. Terrain drops count transitions into the physics falling state, not every descending tick. Deaths include the final elimination that ends a round. These homogeneous difficulty matches describe activity and pacing; their outcomes do not establish pairwise difficulty win rates.

Liveness examines every Fight/Hurry tick for living, non-ghost bots outside carried, stunned, airborne, falling and kick recovery states. A stationary window ends on at least 0.12 cell displacement from its anchor. It includes zero movement commands, so a planner that permanently chooses its current position is detectable. A continuous stationary window exceeding 12 seconds fails only while an adjacent authoritative terrain edge remains uncovered, bomb-free, unoccupied and safe in the visible danger forecast for 0.9 seconds. Windows without a safe exit are reported separately, preserving legitimate trapped-bot waits. This is a bounded local movement check, not proof that every strategic goal is reachable. JSON includes the worst bot's position, goal, command and living opponent count for diagnosis. The harness reads current cover/hazards via reflection to keep measurement out of production policy.

Four fairness checks also require identical initial terrain, spawn positions and player resources across difficulty levels on every arena, and deterministic equivalence of the Medium and legacy Normal setting over 600 authority ticks. Existing behavior/outcome checks remain independent and run without `--soak`.

Iteration 13's recorded matrix passed all 36 scenarios and four fairness checks. The worst eligible stationary window with a safe exit fell from 85.03 seconds in the initial measurement to 1.87 seconds after the navigation fixes; the longest continuous blocked movement command was 0.10 seconds. Final scenarios produced 54 completed rounds over 3,172 simulated seconds (matches can end before the 90-second cap), 1,953 new bombs, 734 visible opponent-pressure bombs and 436 pickups. Initial/final measurements and per-scenario diagnostics are in `artifacts/iteration13/soak-before.json`, `soak-final.json` and `soak-summary.json`. These bounded seeded observations support the specific stall fix; they do not establish that Hard always wins or substitute for human pacing feedback.

## Iteration-thirteen local timing sample

A fresh Release benchmark on Apple M5 Pro ran eight bots on all three maps with three seeds (nine runs, 14,891 active authority ticks). Per-run p95 active-tick times ranged from 0.102 to 0.378 ms; the largest single observed active tick was 4.666 ms. These are single-run local measurements, not a comparative speedup or production-capacity guarantee. Transport, serialization and Godot rendering remain excluded. The separate warmed-search regression still reports zero allocated bytes across 1,000 decisions. Evidence: `artifacts/iteration13/benchmark.json`.
