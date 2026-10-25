# CPU decision research

Investigated 2026-10-25 for Easy, Medium and Hard CPU improvements. These are
primary-source ideas for Blast Circuit, not recovered Nintendo 64 CPU behavior.
The sources use grid games; Blast Circuit has continuous bodies, spherical
blasts, directed stairs, underpasses, moving bombs and arena hazards. Preserve
its authority geometry and ordinary player inputs when adapting them.

## Sources and useful ideas

**Pommerman's reference SimpleAgent.** Its policy first escapes danger, then
attacks adjacent enemies, pursues nearby opponents/items/wood, and otherwise
explores. It keeps a short recent-position history to discourage revisiting
uninteresting cells and remembers its previous direction for enemy standoffs.
This is an inspectable example of explicit priorities and small tactical memory;
its random choices and cross-shaped blast assumptions should not be transplanted
directly. [Original SimpleAgent source](https://github.com/MultiAgentLearning/playground/blob/master/pommerman/agents/simple_agent.py).

**BorealisAI's action pruning.** The authors' repository describes comparing
minimum escape steps with the earliest bomb lifetime covering a cell. Its
`lookahead` placement option inserts a hypothetical bomb, recomputes chained
lifetimes, and checks that escape takes less time than the available fuse. The
Skynet paper explicitly treats the filter as a safety check around strategy.
This supports preserving a shared safety boundary while changing offensive
utility, rather than making higher difficulty ignore danger.
[Author repository](https://github.com/BorealisAI/pommerman-baseline),
[actual placement filter](https://github.com/BorealisAI/pommerman-baseline/blob/master/action_prune.py),
[Skynet paper, ActionFilter](https://arxiv.org/html/1905.01360v1#S2.SS1).

**Real-time tree search with pessimistic scenarios.** Osogami and Takahashi
evaluate survivability through safe reachable time/position pairs, including
the visible opponents' options. Their dypm agent clips its own survivability
above a tuned threshold to encourage aggression: once sufficiently safe,
additional safety need not outweigh reducing an opponent's options. They also
separate combat from intermediate objectives such as breaking walls and
collecting items. Their discussion warns that a blocked move can become a wait.
[Author paper, sections 3.2–3.3](https://arxiv.org/html/1902.10870v1#S3.SS3).

**Developing a Successful Bomberman Agent.** Kowalczyk and colleagues compare
search approaches for Hypersonic. Their evaluation prunes states that cannot
survive a bounded BFS forecast and includes a separate short trap check.
They describe passive late-game opponents after box scoring is settled, and
simulate beyond that terminal-scoring incentive. The useful lesson here is to
make survival a prerequisite while retaining a positive reason to contest an
opponent after farming ends. Their leaderboard result is evidence for their
specific game and evaluation, not a performance prediction for Blast Circuit.
[Author paper, sections 4–5](https://arxiv.org/html/2203.09608v1#S5).

## Existing planner and opportunities

The inspected [bot policy](../src/Bomber.Game/Simulation/BlastSession.Bots.cs)
already has bounded graph search, hypothetical placement escape checks,
marginal pickup values, goal commitment and a clock that eventually favors
engagement. [Navigation recovery](../src/Bomber.Game/Simulation/BlastSession.BotNavigation.cs)
also detects blocked movement and neutral idle. This is an existing heuristic
planner; replacing it with reinforcement learning is unnecessary for this task.

Three code observations motivate focused experiments:

- Tactical goals use geometric nearest-enemy distance. A wall or height change
  can make geometric proximity less useful than a reachable approach or actual
  blast opportunity; existing Pyramid regressions already document this class
  of local optimum.
- The planting entry gate requires adjacent cover or an enemy already in blast
  range, while bomb utility also considers nearby enemies with few exits. That
  pressure term cannot independently unlock placement through the entry gate.
- Any forecast danger within 4.05 seconds currently selects escape mode.
  Hypothetical escape planning is necessary, but the urgency threshold and
  refuge selection can separately affect how readily a CPU resumes pressure.

These are source observations, not proof of gameplay failures in every match.
Retain a failing scenario before changing a corresponding rule.

## Proposed adaptations and evaluation

The following are project design proposals, not claims from the papers or a
record of features already delivered:

1. Make proactive goals reflect reachable attack positions and useful cover
   destruction. Use real blast occlusion and current visible rivals rather than
   rewarding distance through stone or across unsupported elevations.
2. Consider route pressure only where a bomb actually threatens useful visible
   escape space. Keep the hypothetical placement/chain escape check mandatory;
   a raw count of nearby opponents is insufficient to justify planting.
3. Give each difficulty a distinct engagement interval, pickup/attack tradeoff
   and commitment strength. Easy should leave readable pauses but still attack;
   Hard should sustain goals and exploit pressure opportunities. Keep speed,
   damage, inventory, collision, observations and RNG access identical.
4. Reuse short progress/goal memory to reduce reversals, while allowing danger,
   unreachable targets and reached goals to invalidate commitment immediately.
   Increasing commitment alone can reward staying in a refuge forever.

Use matched stage/seed/roster/rules before and after changes. Retain focused
checks for distant proactive approach, occluded targets, useful cover routing,
placement refusal when trapped, timed chain escape and goal abandonment after
a threat. Run existing all-difficulty pressure, stock Pyramid pressure,
determinism/fairness, full four-arena soak and allocation/timing checks described
in the [AI test guide](../tests/Bomber.Ai.Tests/README.md). Inspect native
`ai-play` gameplay separately from managed authority results.

Report time to first pressure, longest pressure gap, eligible bot exposure,
pickups, safe-exit idle, deaths and completed rounds together. More bombs alone
do not establish better pressure; homogeneous bot matches do not establish
pairwise difficulty strength. Automated outcomes and recordings can support
pacing changes, but human play remains the judge of fun.

## Implemented adaptation and matched authority results

The October 25 change keeps the heuristic planner and its timed safety boundary.
`BlastSession.BotObjectives` uses cached reversed **directed** walk edges to
measure progress toward living rivals once engagement is due. A necessary move
away from an opponent can now beat geometric proximity. If no cover-free route
exists, a second distance field ignores crates to identify demolition progress;
only the existing legal timed search can issue movement. No search RNG, future
loot, physics or resources change. Easy/Medium/Hard reengage after 6/4/2.5 seconds
without a real threatening bomb. These numbers and the progress utility weight
are project tuning, not values recovered from the cited agents.

Remote opportunity evaluation additionally predicts the oldest eligible remote
at time zero and rejects visible chain danger at its owner's position. The
focused red test reproduces unsafe approval at that opportunity boundary. It
does **not** establish a death through the complete prior policy: its urgent
escape branch can independently prevent calling that boundary while threatened.

Matched observations use baseline `b0f7b8e`, identical stage/seed/roster/rules,
Release .NET authority, and the existing 48-case matrix (four arenas, all levels,
three/seven CPUs, seeds 64/9876, up to 90 seconds each). Evidence lives under
ignored `artifacts/cpu-ai/`; the source fixtures remain in the AI suite.

| Observation | Baseline | Updated |
| --- | ---: | ---: |
| Easy long-wall fixture reaches rival and pressures within 25 seconds | No | Yes |
| Medium long-wall fixture reaches rival and pressures within 25 seconds | No | Yes |
| Hard long-wall fixture reaches rival and pressures within 25 seconds | Yes | Yes |
| Complete cover barrier crossed safely, all three levels | Yes | Yes |
| Remote opportunity refuses owner-threatening chain | No | Yes |
| Soak scenarios passing activity/liveness checks | 48/48 | 48/48 |
| Longest stationary window with safe adjacent exit | 1.27 s | 1.27 s |
| New bombs / opponent-pressure bombs | 3126 / 2551 | 3112 / 2539 |
| Death transitions / completed rounds | 201 / 26 | 150 / 15 |

Pressure counts visible blast opportunities, not hits. Normalize by eligible
bot exposure (living, non-ghost, Fight/Hurry, outside held/stunned/airborne/falling
and kick-recovery states), because survival and round duration diverge:

| Difficulty | Eligible bot-seconds, baseline → updated | Pressure / eligible bot-second, baseline → updated | Pickups, baseline → updated |
| --- | --- | --- | --- |
| Easy | 3671.83 → 3747.70 | .1381 → .1313 | 181 → 188 |
| Medium | 3770.17 → 3682.03 | .2427 → .2471 | 151 → 171 |
| Hard | 4031.10 → 3998.37 | .2801 → .2844 | 153 → 130 |

The twelve stationary/moving open-arena and four stock Pyramid human-pressure
fixtures retain their thresholds with zero CPU deaths. Easy's stationary/moving
pressure changes from 5/5 to 4/3 bombs in each seed's 30-second open fixture,
consistent with its longer engagement interval. Medium remains 11/10 and Hard
15/10. Stock Hard/9876 improves from five to seven human-pressure bombs and its
longest gap falls from 7.9 to 6.3 seconds; other stock pressure counts are stable.
Human targets renew invulnerability only to sustain the measurement; these are
declared authority fixtures rather than ordinary native gameplay.

Fewer deaths and completed rounds are mixed pacing results, not proof of higher
win rates or greater fun. Homogeneous bot matches do not measure a Hard-vs-Easy
skill ladder. Timings are local simulation observations, excluding rendering,
encoding and network scheduling. Nine baseline runs had per-run p95 active
ticks of .093–.811 ms (largest tick 4.852 ms); nine final runs had .169–1.642 ms
(largest tick 11.781 ms), over 14,392 active final ticks. The extra pursuit search
has a measurable cost; JIT, machine load and diverged match outcomes also affect
these single-run timings. This is not a production capacity or speedup claim.
The measured search still allocates zero bytes across 1,000 warmed decisions. Human pacing feedback remains necessary.

## Native gameplay and completion checks

A freshly rebuilt self-contained export passed the normal windowed `ai-play`
journey in an isolated macOS desktop, with Metal Forward+, real focus, 13 positive
assertions, nine reviewed captures, and zero runtime errors or warnings. Each
20-second observation uses one stationary human and three ordinary CPUs without
actor edits. All three CPUs move at every difficulty:

| Difficulty | New bombs | Observed blast IDs | Longest eligible idle |
| --- | ---: | ---: | ---: |
| Easy | 15 | 13 | 1.03 s |
| Medium | 20 | 19 | .67 s |
| Hard | 16 | 15 | .17 s |

Opening/battle/end captures show actual cover destruction, item collection,
stair movement and combat around living rivals after the human becomes a ghost.
The capture labeled outcome is the end of the observation, not proof of a
completed round. Different raw bomb counts do not establish relative skill.
VM rendering proves the windowed product journey, not physical controller or
bare-metal GPU performance. The runner stopped its owned VM and removed its
temporary export after the run.

The managed gate also passed the game/client/AI/audio/cluster suites, 48-case AI
soak, independently staged real-server smoke, forced adaptive relay and fleet
checks. The final AI suite contains 76 checks, including four new reversed-edge
contracts, the seven tactics fixtures and zero allocation across 1,000 warmed
search decisions. Public boundary/tooling checks and documentation links pass.
Useful repeatable commands:

```sh
dotnet run -c Release --project tests/Bomber.Ai.Tests
dotnet run -c Release --project tests/Bomber.Ai.Tests -- --soak
python3 tools/qa/run.py --profile managed
python3 tools/qa/run.py --profile public
python3 tools/playtest/run.py --scenario ai-play --godot /path/to/Godot --vm YOUR_STOPPED_QA_VM
```

Receipts: `artifacts/cpu-ai/managed/summary.json`, `managed-final/summary.json`,
`public/summary.json`, `behavior-final.log`, `soak-before.json`, `soak-final.json`,
`benchmark-before.json`, `benchmark-final.json`, and
`native/20261025T165500.628382Z-ai-play/runner-summary.json` under that same ignored
artifact directory. No installation, public release or external deployment is
part of this PR.
