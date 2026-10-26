# CPU agency: shuffles, gusts and standoffs (2026-10-26)

The user reported two symptoms: in a neutral situation a CPU walks one cell and
back indefinitely when the player does nothing, and on Blizzard Battle CPUs stop
doing anything while the wind blows. They asked for more agency, smarter and more
proactive bots. This records the diagnosis, each mechanism found, the changes and
matched measurements. Physics, resources, damage and visible information are
unchanged; every change is decision policy in `Bomber.Game`.

## Diagnostic tools

`Bomber.Ai.Tests -- --agency` runs a 72-run matrix (all six battle arenas, three
levels, seeds 64/9876): one CPU against an idle, renewed-invulnerable player, and
the first of four CPUs in an ordinary match, one minute each. It reports
*reversals* (returns to the cell left one move earlier, uninterrupted by a bomb),
threats against the idle player, gaps, stillness and the share of Blizzard gust
time spent moving. Options: `--output=PATH`, `--trace=stage,level,seed[,ticks[,bots]]`
for a per-tick trace, `--sweep` for the moving-target distribution below, and
`--standoff` for bombs placed by all four CPUs in their first 20 seconds.
`--agency-checks` runs the assertions in [AgencyChecks](../tests/Bomber.Ai.Tests/AgencyChecks.cs);
they are also part of the default suite.

## Mechanisms found and changes

1. **Bouncing fuse versus Easy's escape horizon.** A Bouncing bomb's 3.5-second
   fuse begins its danger after Easy's 3.1-second safety horizon, so the bomb's own
   cell looked safe, the escape search returned "stay" and every placement was
   rejected. Its idle step then walked one cell away and back: 84 consecutive
   reversals and one bomb per minute beside the idle player on Up & Down. Escape
   searches now cover the full threat interval at the start cell (capped at 4.6 s).
2. **Wind against the slowing approach.** Before turning, steering aligns within
   .07 of the lane and slows proportionally near it. A gust (≈0.8 cells/s on
   players) balanced that weak input just outside the tolerance, so Easy/Medium
   never turned for the whole six-second gust (10–19% of gust time moving). Hard's
   shorter interval happened to clear it. Steering now leans into the visible wind.
   The danger forecast also follows the public gust schedule for drifting resting bombs.
3. **Flickering routes and goals.** Distant and nearby bodies open and close routes
   as they jitter; nearly equal goals in opposite directions swapped on every
   decision. A still-reachable committed goal now survives score changes under 3,
   and a started step is finished for half a second unless danger preempts it.
   Making distant bodies passable was tried and rejected: it moved the same
   oscillation to the moment of contact (518 reversals on Hard).
4. **Lane standoffs.** Pairs met two cells apart in one-wide lanes. With fire 1
   neither could reach, and bombs were only considered with a rival already in
   reach or a crate adjacent. Four Hard CPUs placed no bomb in 20 seconds on Top
   Rules and Greedy Trap. Once its engagement interval has elapsed, a CPU may fence
   a rival walking straight at it (judged from that rival's steady visible heading)
   and may take an off-center shot when the rival is actually in reach. Raised-lane
   drops count as exits, so the older dead-end fencing term never applied there.
5. **Trailing walkers.** At fire 1 a walking target is usually between cells,
   just outside reach of the nearest open cell. Goal fields now use each rival's
   visible velocity, leading it by .15/.35/.5 seconds for Easy/Medium/Hard.
6. **Kick-backs.** A target walking into a CPU's bomb kicked it straight back
   (12 cells/s) while the CPU fled along the same line. The lane ahead of a human
   walker steadily heading at a resting bomb is now an impact threat, rolling bombs
   are projected until something stops them, and a stale stuck-recovery sidestep
   can no longer override a reversed plan (it had walked an escaping Hard CPU back
   into the lane). CPUs do not create kick lanes: they route around bombs and their
   seats are labeled; including them suppressed ordinary CPU duels.
7. **Mirroring and boundary flicker.** Two CPUs keeping their distance mirrored each
   other across one boundary; a body pressed against an obstacle on a boundary
   alternated cells, and its escape with it. After three reversals between the same
   two cells without a bomb, both lose appeal as goals for two seconds and the next
   goal is held firmly. Planning keeps the previous cell until the body is .12 past
   its edge.

Timed transits through threatened cells now keep a .25-second margin (was .08):
a body still overlaps a cell after its center leaves, and alignment adds time.

## Matched measurements (v0.36 baseline → this change)

All runs are local, deterministic, simulation-only. Raw receipts were kept under
the ignored `artifacts/ai-agency/v036/`.

| Agency matrix (72 one-minute runs) | Before | After |
|---|---|---|
| Longest reversal streak, idle player / CPU match | 84 / 99 | 9 / 16 |
| Longest gap without threatening the idle player | 60.0 s | 14.9 s |
| Threats against the idle player | 448 | 486 |
| CPU self-defeats against the idle player (36 minutes) | 3 | 1 |
| Share of Blizzard gust time moving | 35% | 92% |

Four-CPU bombs in 20 seconds (`--standoff`): Hard Top Rules 0 → 27–30, Hard
Greedy Trap 0 → 21–26. Medium is lower in this opening window on Top Rules
(31 → 24–25) and Greedy Trap (21–23 → 13), with fewer early crate bombs; the
whole-match soak below shows Medium threatening rivals more often overall.

Moving-target sweep (open arena, 24 start cells/patrol phases, variant 0 is the
fixed `PressureChecks` fixture):

| Level | Mean hits | p90 gap | Max gap | Deaths |
|---|---|---|---|---|
| Medium | 9.5 → 9.5 | 8.9 → 8.3 s | 17.1 → 9.9 s | 0 → 0 |
| Hard | 9.2 → 10.5 | 12.2 → 7.8 s | 20.3 → 11.8 s | 1 → 0 |
| Easy | 2.8 → 2.6 | 15.1 → 17.1 s | 21.5 → 21.2 s | 0 → 0 |

Against a perfectly stationary open-arena target, Medium and Hard place fewer
hits (11.1 → 9.7 and 13.0 → 11.3 mean) while gaps stay at 2.7–3.6 s. This is a
measured tradeoff, not an unchanged result.

72-scenario soak, rates per eligible bot-second:

| Scope | Bombs | Rival-threatening bombs | Moving | Deaths |
|---|---|---|---|---|
| All | .276 → .380 | .232 → .286 (+23%) | 88.6% → 93.5% | 136 → 127 |
| Easy / Medium / Hard threats | | +47% / +8% / +25% | | |
| Blizzard Battle | .264 → .645 | .215 → .498 | 60.2% → 96.8% | 45 → 18 |
| Sunken Temple | .315 → .348 | .222 → .208 (−6%) | 95.3% → 95.1% | 18 → 19 |

The longest stationary window with a safe exit fell from 5.80 to 1.43 seconds.
Single-run Release benchmark p95 active-tick times rose on Rock Garden (256–582 µs
→ 498–816 µs) and fell on Up & Down and Sunken Temple; every observed tick stayed
below 7 ms of the 33 ms authority interval. These are local samples, not capacity
claims. Zero-allocation warmed decisions are preserved.

## Completion gates (on v0.36.0, uncommitted worktree)

Default AI suite 98/98, `--agency-checks` 17/17 (16 fail on v0.36), 72-scenario
soak, `--idle-regression`, `--terrain-regression`, `--pressure`,
`--stock-human-pressure`, `--tactics`, `--top-rules`, `--up-and-down`,
`--front-placement`, `--liveness`, 2,880 game integration checks and the `public`
QA profile passed. The native `ai-play` journey (three real four-player temple
matches, one per level, in the isolated Metal VM) passed 13/13 with a recording.
An earlier `public` attempt failed two harness self-tests only because another
session's native GPU benchmark held the host GPU lock; it passed once that finished.

## Limits

These are seeded simulation observations against scripted targets and other
CPUs. They do not establish human-rated fun or difficulty. The moving-target
fixture remains a single sample; its longest-gap bound moved to the 24-variant
distribution because small timing changes moved that one value by seconds in
either direction. Planner and step-probe safety windows still differ (search
accepts a .45-second stay, steering probes the full horizon), which can produce
short recovery sidesteps; it was left unchanged because it predates this work.
