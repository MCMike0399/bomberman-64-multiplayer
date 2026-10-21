# Iteration thirteen: active bots and difficulty policy

The reported standstill was reproduced through the real shared simulation. A bot at (3.5,5.5) pursuing a pickup kept pushing into a stationary player backed against a pillar, even though it could go around. Five targeted regressions cover that case, off-center narrow passage traversal, a blocked passage reopening, a detour near a timed blast, and the diagonal body-contact deadlock found by longer match tests.

## Decision layers

The existing finite breadth-first search is retained because it evaluates many candidate goals on small, mostly uniform-cost arena graphs. Replacing it with a named algorithm would not fix a graph that ignores physical bodies. Breadth-first search and goal-directed A* serve different search needs; [Red Blob Games' original explanation](https://www.redblobgames.com/pathfinding/a-star/introduction.html) describes that distinction. Our implementation uses a reusable search workspace, scores reachable goals, and rejects paths against visible danger intervals.

`BotDifficultyProfile` separates decision tuning from simulation rules. Profiles govern reaction interval, tactical depth, preferred spacing, bomb cadence, item utility, escape search depth, safety horizon, dead-end penalty and goal commitment. Easy leaves room, Medium balances pressure and routes, and Hard gives more weight to contested escape routes and persistent goals. A cautious bot still takes an attack turn after six seconds without a bomb attempt. Same-fixture tests verify differences independently of reaction speed and require actual opponent-threatening bombs and explosions on all three levels.

`BlastSession.BotNavigation` observes real displacement, not just whether the AI asked to move. It temporarily avoids a failed next waypoint, takes a bounded safe step when a neutral goal becomes permanent, and probes ordinary axis-separated movement before committing to a lateral or reverse escape from body contact. It respects the same terrain, bombs, player bodies and speed as human input. Knockdown, carry, flight and kick pauses are excluded from stuck detection. There is no teleport, collision bypass, speed boost or access to future item randomness.

The short directional recovery commitment prevents two agents from continually choosing the same impossible diagonal into each other. Its checks allow legitimate small centering movement; the threshold is proportional to requested movement rather than a fixed distance that could reject slow valid adjustments. Safe waiting remains allowed when no legal route is available.

Remote-bomb decisions now examine the exact oldest non-carried remote that the authority will detonate. Rolling and flying bombs are included. Dedicated regressions reject a dangerous moving first bomb even when a safe resting second bomb exists, and permit safe moving-bomb opportunities.

## Verification and boundaries

The independent 36-scenario soak covers three stages, three difficulties, three/seven bots and two deterministic seeds. It measures zero-input standstills as well as blocked commands, excludes legitimate movement interruption, and distinguishes a safe available exit from a trapped wait. The first run exposed 30 failing scenarios; fixes were rerun against the same matrix. Four additional fairness checks preserve starting resources/spawns/terrain and equivalence of Medium with the older Normal setting. See [methodology and results](../tests/Bomber.Ai.Tests/README.md).

These tests establish specific behavior and liveness under the tested conditions. They do not prove that Hard always wins, that every arbitrary layout is solvable, or that subjective pacing is finished. The actual Godot scene is also exercised and recorded for all three levels through `--scenario ai-play`, with a stationary human observer and ordinary CPU play; no entity state is rewritten.

## WASD menu navigation

WASD keys are additional native Godot UI action bindings, alongside the arrows. [Godot's InputMap documentation](https://docs.godotengine.org/en/stable/classes/class_inputmap.html) defines this mechanism. A focused graphical regression verifies the main menu, setup dials, all three displayed difficulty choices, roster popup, Results actions, and ordinary letter input in the online form. Gameplay movement and controller button mappings are unchanged.
