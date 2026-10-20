# Deterministic bot checks and benchmark

Run the behavioral checks without Godot or a network server:

```sh
dotnet run -c Release --project tests/Bomber.Ai.Tests
```

The 15 checks cover cached legal terrain edges, timed chain reactions, expired hazards, deliberate power-up pursuit, fuse escape, climbing before flooding, fair difficulty response rates, eight-bot deterministic replay on all three maps, repeated bomb pressure against opponents, and reuse of search memory. Existing game integration checks separately exercise safe own-bomb placement, trapped-bot refusal and actual stair movement.

The planner sees current entities, visible bomb fuses and the known flood schedule. It never consumes future item randomness or changes player movement, damage, blast radius or inventory. `bot-difficulty` selects reaction intervals: easy 0.24 seconds, normal 0.12 seconds, hard 0.08 seconds, quantized by the shared 30 Hz authority. Default is normal. Remote detonation is considered only when the oldest remote threatens cover/an opponent and the bot is outside its blast. Advanced lift/throw/pump combos are not part of this policy.

Terrain adjacency is cached for each immutable stage. A session reuses queues, visitation generations and shared opponent/cover observations. Bomb danger includes fuse times, visible chains, remaining blast lifetimes, conservative loss of soft cover, and short moving-bomb projections. Multiple danger intervals in a cell are merged conservatively. Unpredictable future human throws and remote-button presses cannot be guaranteed; moving-bomb and remote forecasts are approximations, not complete forward simulation.

## Repeat a measurement

```sh
mkdir -p artifacts/ai
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --benchmark --output=artifacts/ai/current.json
```

Each map runs eight AI-controlled participants for 1,800 authority ticks with seeds 64, 123 and 9876. The harness records active fight ticks, live-bot ticks and actual decisions, so ended/ready/round-over work cannot masquerade as faster AI. All nine recorded runs had 1,739 active ticks. These are simulation-only timings: transport serialization, server scheduling, Godot and rendering are excluded. Small fixture/measurement allocations are included. Timing and allocation checks are observations, not CI thresholds.

The following before/after measurements were captured during this iteration on Apple M5 Pro, .NET 10.0.401, Release configuration. The earlier planner was measured before the replacement; bomb kicking, overlap escape, power-up selection and inventory resets also changed in the same iteration. This is an iteration comparison, not an isolated causal AI speedup. Values below are descriptive single-run measurements; JIT, GC and machine load affect them.

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
