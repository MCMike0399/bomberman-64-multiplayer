# Iteration23: map-specific destructible cover and rewards

Researched 2026-10-23 for the enlarged arenas. The user wants more rewards in
Pyramid's lower courtyard and bomb-capacity/fire upgrades to be the common drops.
This note separates original-game evidence from proposed project tuning.

## Primary evidence

The [Nintendo-hosted English manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf),
printed pp. 24–27 (PDF pages 14–15, counting from one), identifies Pyramid's
stepped narrow spaces, Rock Garden's five poles, and Up & Down's layered routes.
Fire raises blast power; Bomb raises simultaneous capacity, capped at eight.
The manual supplies no destructible-block density or item-drop probabilities.

Direct frame inspection of [XFactor1111's original-game recording](https://www.youtube.com/watch?v=SPjnYY_Gomo)
establishes these spatial patterns:

| Map | Direct observation | Inspectable example |
| --- | --- | --- |
| Pyramid | Pale blocks cluster in and beside the recessed front court; upper terraces and central stairs are largely clear. Two round openings have different arrangements. | [06:45](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=405s), [24:47](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=1487s) |
| Rock Garden | Separated stones surround the central ruin and poles, with open space between groups. Early explosions remove stones and leave visible flame pickups. | [00:04–00:12](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=4s) |
| Up & Down | Gray blocks occupy wider side platforms and the lower central route. Narrow sloped connections remain visibly open; the high central platform is clear in the sampled opening. | [11:15](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=675s); compare [10:30](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=630s) |

Pyramid's lower court subsequently floods while terraces remain exposed
([25:23–25:27](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=1523s)). This supports
a low-ground collection opportunity that disappears as water rises; it does not
establish exact original flood or drop constants.

## Application to Blast Circuit

These are **project recommendations**, not reconstructed original algorithms.
Our arenas support eight participants and have different geometry.

| Current arena | Recommended placement pattern |
| --- | --- |
| Pyramid, 25×27 | Give the tier-0 court its own substantial cover allocation. Use staggered small groups across its width, including the western/eastern court pockets, rather than leaving almost all side columns permanently excluded. Keep continuous lanes to both stair groups and a clear route across the court. Put a lighter amount on broad side terraces; preserve central ascent and summit readability. |
| Rock Garden, 23×15 | Distribute separated clusters among the pillar groups and into each spawn sector. Use balanced regional allocations with seeded variation; preserve crossing lanes and room to circle pillars. Avoid either a uniform filled checkerboard or a dense central wall. |
| Up & Down, 23×19 | Put cover in outer-ring pockets and the broader tier-1 platform areas. Protect every ramp and its approach/exit. Keep the small tier-2 summit sparse, especially around its two spawns. Allocate by usable space and access, not equal counts on every tier. |

Retain a nearby reachable opening reward for each spawn. Treat that guarantee
separately from later random drops: an empty courtyard should be fixed by adding
eligible cover and a court allocation, not only by raising a global drop chance.
Seeded layouts should vary individual placements while preserving these regional
budgets and essential paths.

For the user's requested reward balance, make Fire and Bomb the two largest
individual weights and together a clear majority of ordinary drops. A suitable
starting proposal is **35% Fire + 35% Bomb**, with the other six types sharing
30%. Keep the probability that a destroyed block drops anything separate from
the conditional item-type weights. Both percentages are project tuning; neither
is supported as an original Bomberman 64 rate.

Before accepting a layout, check multiple seeds for regional cover counts,
spawn-accessible opening rewards, usable ramps/escape lanes, and rewards that
survive the revealing explosion. Review actual rendered examples on all three
maps; counts alone cannot establish readable or enjoyable spacing.

## Inspection and limits

Opened the official PDF on the web and inspected its locally cached rendered
stage/item spreads. The YouTube page did not render through web extraction;
the public source was located through web search and its existing ignored local
video was sampled directly at the linked timestamps. Existing Rock Garden and
Pyramid contact sheets were also reviewed. No complete-playthrough review,
hidden drop-table analysis, original grid-coordinate transcription, or numerical
drop-rate measurement is claimed. Reference media stays outside git and the
distributed game. This document required no build or game launch.

## Implemented tuning

`StageLayout.CoverAreas` now carries map-owned budget shares and cluster centers.
The authority reserves spawn/ramp/crossing clearance, guarantees two nearby
opening crates per spawn, and fills regional quotas with seeded weighted sampling.
Pyramid uses 90 blocks, targeting 54 in the tier-0 court and 14 on lower wings;
Rock Garden uses 110 in balanced pillar regions; Up & Down uses 88 mainly in
outer pockets, with no summit cover. The court has narrower explicit open lanes
instead of excluding most side columns. Ordinary drops are 35% Fire and 35% Bomb;
the separate chance of a drop remains 70%. Guaranteed opening rewards are basic
upgrades on all three maps. These values remain project tuning.

The regional-layout checks cover 32 seeds per map, seeded variation, protected
lanes and regional budgets. The loot checks independently exercise all eight
spawn openings, actual destruction and rewards on all three maps. The native
`cover-layout` journey captures normal battles and walks into the Pyramid court
to plant, retreat, reveal and collect a real upgrade.
