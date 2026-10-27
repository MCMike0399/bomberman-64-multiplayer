# Denser battle cover

The 2026-10-27 pass expands the five sparse battle arenas using Pyramid's
regional cover as a reference. The user's follow-up explicitly preserves areas
that should remain empty, especially Up & Down's upper routes. These budgets
are Blast Circuit gameplay tuning, not reconstructed original-game counts.

| Arena | Previous → new default | Placement and protected space |
| --- | --- | --- |
| Pyramid / Sunken Temple | 90 → 90 | Existing lower-court clusters and ascent lanes remain the reference. |
| Rock Garden | 16 → 64 | Four seeded boulder groves and a smaller central cluster. Clear perimeter loop, inner spawn avenues and paired central cross routes. |
| Up & Down | 12 → 36 | Twelve blocks on each middle pier and twelve on the low deck, in paired shelves facing clear aisles. No blocks on the upper ring, crown, bridges, underpasses or flight crossings. |
| Top Rules | 12 → 48 | Four lower-court stockpiles. Upper perimeter, all bridges, stairs and the lower through-routes remain clear. |
| Blizzard Battle | 16 → 64 | Four mirrored chevron ice banks. Clear central cross and two diagonal corridors wide enough for cardinal steps. |
| Greedy Trap | 8 → 64 | Four upper stockpiles totaling 48 blocks and four bottom islands totaling sixteen. Clear perimeter loop, upper stair approaches, entire middle ring and bottom cross. |

Circuit Garden remains a cover-free teaching arena. Practice on every battle map
also remains empty. The existing Advanced block percentage scales the full
authored budget. Item probabilities, collision dimensions, terrain, spawns,
flooding and CPU policy keep their existing rules. Local and network sessions
use the same authoritative layouts; this PR does not deploy them.

## Verification

The full game suite passes **3,945 integration checks**; the 72-case CPU soak
passes across six arenas, three difficulties, three/seven CPUs and two seeds.
It observes real block destruction and pickups on every arena, with a maximum
open stationary interval of 1.9 seconds (12-second liveness bound).


`dotnet run --project tests/Bomber.Game.Tests -- --cover-layout` covers all six
battle arenas, seeds 0–31 and 0/25/50/75/100% block settings. It checks seeded
repeatability, full budgets, empty practice, spawn clearance, ramp approaches
and protected routes independently of the placement candidate sets. Height-aware
navigation must reach the key lower courts/piers from every spawn, including
Top Rules' lower passages. Each spawn also needs an open route at least four
cells away. This caught a sealed Rock Garden corner during development; the
perimeter loop and connecting spawn avenues retain that regression.

Up & Down and Blizzard Battle retain fixed authored arrangements at 100%; lower
percentages select seeded subsets. The other arenas retain seeded variation.
The existing stage journeys exercise actual walking, bomb destruction, loot
collection, CPU combat and sudden death. `cover-layout` additionally captures
every full layout from the ordinary battle camera at High, with two human seats
and no bots to destroy the cover before the capture. Its deterministic loot
fixture uses 100% upgrade-only drops; opening cover itself is the default layout.

```sh
python3 tools/qa/run.py --profile managed
python3 tools/qa/run.py --profile headless
python3 tools/playtest/run.py --scenario cover-regression
```

The six-journey `cover-regression` run passes **646 assertions**, with **79
captures**, zero runtime errors and zero warnings, using a freshly rebuilt
self-contained app. It includes actual bomb/loot cycles, navigation, CPU combat
and sudden death for every changed arena. The 29-journey headless core passes
1,765 assertions; its separate peer/adaptive room journeys also pass.

Graphical verification uses the isolated macOS Metal desktop. These checks
establish the observed layouts and bounded gameplay behavior, not user approval
of pacing or art.

## Reviewed battle views

Unedited 1600×1000 High-preset captures from the ordinary battle camera. The
[manifest](images/denser-cover/manifest.json) records their source and checksums.
Pyramid is included as the unchanged density reference.

| Rock Garden — 16 → 64 | Up & Down — 12 → 36 |
| --- | --- |
| ![Rock Garden boulder groves](images/denser-cover/rock-garden.png) | ![Up & Down pier shelves and clear upper ring](images/denser-cover/up-and-down.png) |
| **Top Rules — 12 → 48** | **Blizzard Battle — 16 → 64** |
| ![Top Rules lower-court stockpiles and clear bridges](images/denser-cover/top-rules.png) | ![Blizzard Battle ice banks and clear cross](images/denser-cover/blizzard-battle.png) |
| **Greedy Trap — 8 → 64** | **Pyramid reference — 90** |
| ![Greedy Trap upper stockpiles and bottom islands](images/denser-cover/greedy-trap.png) | ![Unchanged Pyramid regional cover](images/denser-cover/sunken-temple.png) |
