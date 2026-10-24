# Gameplay research: original Bomberman 64 Battle

Verified 2026-10-19. Sources concern the 1997 Nintendo 64 game, not the unrelated 2001 Japanese game or later Super Bomberman titles. Original media remains in ignored `artifacts/research/`; it is not a distributable project asset.

## What was actually inspected

The [Nintendo-hosted English instruction booklet](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf) is scanned. Its battle, stage and item spreads were rendered locally and read directly, rather than relying on search snippets. Printed pp. 22–27 correspond to PDF pages 13–15.

Two original-game recordings were downloaded at low resolution for research: [XFactor1111, Bomberman 64 multiplayer gameplay](https://www.youtube.com/watch?v=SPjnYY_Gomo), and [GameFace, four-player Battle Part 1](https://www.youtube.com/watch?v=TM25Xa0DgCI). Contact sheets sampled the recordings; focused sequences were inspected more densely. This is frame-based inspection, not a claim that every minute was watched. The recording creators own their commentary/edits; the game imagery is evidence of the original software's behavior.

## Rules established by the publisher

- Printed pp. 22–23: Single Battle supports human/CPU selection, CPU levels 1–3, configurable timer and sudden death. Sudden death begins one minute before the time limit; ghosts disappear during the final minute.
- Printed pp. 24–25 explicitly identify the supplied stepped sandstone/court reference as **Pyramid**. Its narrow stepped platforms are part of its tactical layout.
- Printed pp. 26–27: Fire adds one power level; regular bombs cap at level 3, pumped bombs at level 6. Bomb adds one simultaneous bomb, capped at eight. Power lets bombs break objects weaker bombs cannot. Remote changes bombs to command-detonated bombs and is lost on death/advancing stages. Heart absorbs one extra hit and does not stack.

Source: [Nintendo original manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf). The Power description does **not** say that every solid wall or height boundary becomes transparent to blasts. Keeping permanent pillars/terrain solid while red bombs penetrate destructible cover is a conservative project interpretation, not a proven exact original collision algorithm.

## Direct video observations

### Pyramid sudden death uses rising water

In [recording one at 24:47](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=1487s), a one-minute Pyramid round begins. At approximately 24:49, the HUD reads 0:59 and the pink Hurry Up message appears. At 25:21 the HUD reads 0:36 and the front recessed court still appears dark and dry; around [25:23](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=1523s), with 0:34 remaining, blue water visibly occupies that court. By 25:27, with about 0:31 remaining, the court is submerged while higher stone terraces remain exposed. The round finishes shortly afterwards.

This establishes a rising surface and progressively lost lower ground. It contradicts applying Rock Garden-style closing columns to Pyramid. The HUD clock is a better comparison than elapsed recording time because the recording and in-game clock do not advance identically. The first visibly flooded court occurs roughly 25 in-game seconds after the warning in this observed round. Camera pixels do not establish world-space height, exact collision threshold, rise curve, maximum level or final-crown timing.

### Pumping visibly enlarges the carried bomb

In [recording one around 00:04–00:07](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=4s), the white character holds a black bomb above the helmet. Dense frames around 00:04.9–00:06.5 show its sphere becoming distinctly larger than the character's head, followed by release and flight around 00:06.7–00:07.1. Growth and the overhead carrying pose are visible; a blast-radius-only upgrade would miss this behavior. This footage has no input overlay, so exact accepted press count, button debounce timing and numerical scale ratio cannot be recovered from it.

### Destruction creates visible opportunities to collect upgrades

In [recording one around 00:08–00:12](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=8s), explosions clear nearby soft blocks and small flame icons remain on the terrain. Players continue moving among bombs and these icons. [Recording two around 01:50–02:10](https://www.youtube.com/watch?v=TM25Xa0DgCI&t=110s) likewise shows a partially cleared arena, visible flame pickups and much larger explosions than at the start. This supports readable persistent pickup representations and a progression loop; it does not establish a drop probability or burn/despawn lifetime.

## Deliberately unresolved details

The inspected material does not establish initial bomb capacity, exact fuse duration, held-fuse pausing, three accepted pump presses, pickup drop weights, whether every item burns in every blast, exact death-drop rules, or reset/persistence of each upgrade between battle rounds. These need further controlled original-game observation or code analysis. Public documentation and tests should label our chosen values as project rules.

Likewise, the project's water start height, tier units, speed, drowning comparison and whether blasts are blocked by raised terrain are explicit implementation decisions. Preserve map identity and the visible rush to high ground without describing tuned constants as reverse-engineered original values.

## Resulting design and verification targets

The intended loop is to destroy reachable cover, collect recognizable upgrades, use the increased capacity/strength, lift and visibly pump bombs, then throw while retaining an escape route. Bots should pursue the same loop, re-evaluate hazards and move uphill before their current route floods. These are project AI goals, not claims about the original game's internal policy.

Verify changes through real input and observed authority: collect visible training items by walking, assert accepted three-stage pump state and increasing rendered scale, throw the charged bomb, then exercise real timed Pyramid flooding while low ground becomes unsafe and high ground remains usable. Pair screenshots with state traces. Use named pickups, public gameplay state and presentation diagnostics; do not teleport entities or invent footage.
