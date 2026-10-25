# Greedy Trap reference research

Investigated 2026-10-25 for an independently authored Blast Circuit recreation.
Scope: **Bomberman 64 (1997), Battle Stage 4**, primarily Single Battle.
This records inspected imagery and publisher material, not recovered simulation
constants. Directions are camera-relative: north is the far/top edge.

## Sources actually inspected

| ID | Source and inspection | Evidence class |
| --- | --- | --- |
| S | User's 608×456, HUD 2:00 screenshot, attachment `479c69cf-68ac-4ddc-b190-37db299b9917-4716db90-dcf7-4bd0-833c-e33381932238.png`; retained as `user-greedy-trap-608x456.png` below | Direct still-image evidence; recording conditions unknown |
| M | [Nintendo-hosted English instruction booklet](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf), rendered PDF pages 13–15, printed pp. 22–27 | Primary publisher source |
| G | [Baku Bomberman Official Guide Book scan ZIP](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip), `guide-50.JPG`, right page **101**; also `guide-47.JPG`, pp. 94–95, and `guide-56.JPG`, colophon | Official contemporary guide, hosted by a fan archive; Japanese paraphrases below |
| V | ShadowIori, [Bomberman 64 — All Stages Sudden Death](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=141s), Greedy Trap chapter 2:21–3:25 | Recorded original software imagery; platform/emulation conditions not verified |
| A | Super Nintendan, [Bomberman 64 [Nintendo 64] Battle Mode on Greedy Trap](https://www.youtube.com/watch?v=fUvsHrsS70c&t=4s) | Recorded original software imagery; uploader describes a human versus three medium CPUs, captured 2023-02-23 |
| B | HUnknownGamer, [Bomberman 64 Battle Mode — Greedy Trap](https://www.youtube.com/watch?v=9yn0hcJWMB4&t=32s) | Independent recorded original software imagery; includes added reaction captions |
| W | User-provided [Bomberman Wiki: Greedy Trap](https://bomberman.fandom.com/wiki/Greedy_Trap) | Secondary description; cites G p. 101 |

G's colophon identifies Shogakukan, Hudson supervision/cooperation, ISBN
4-09-102598-6, first printing 1997-11-10; this scan is the fifth printing,
1998-03-10. The scan itself was read, rather than relying on W's citation.
W was readable through web retrieval; a separate `curl` download returned 403.

**Actual inspected video samples, source time rather than game time:**

- **V, detailed:** 2:23 through 2:30 at one-second intervals; 2:32 through
  2:46 at two-second intervals. Additional full-frame samples: 2:22, 2:24,
  2:26, 2:30, 2:35, 2:40, 2:45, 2:50, 2:55, 3:00, 3:05, 3:10, 3:15,
  3:20 and 3:23. The black 2:22 transition establishes no gameplay fact.
- **A, detailed:** 0:04–0:19 at one-second intervals, and 0:14–0:19.5 at
  half-second intervals. Survey: 0:00–1:35 at five-second intervals, then
  1:40, 1:50, 2:00, 2:10, 2:20, 2:30, 2:40, 2:50, 3:00, 3:20, 3:40,
  4:00. Later unsampled portions were not inspected.
- **B:** 0:00–1:20 at five-second intervals; additionally 0:32–0:35 at
  one-second intervals, then 1:30, 1:40, 1:50, 2:00, 2:10, 2:20 and 2:30.
  Boot/menu/result frames are context, not arena-mechanics evidence.

These are extracted-frame inspections, including dense short sequences, not
an input-controlled original-game experiment or continuous playback of every
downloaded minute. Sources can establish visible movement and changing geometry;
they cannot establish controller intent, friction, collision equations or ticks.

## Layout, height and access

**Observed:** a square masonry enclosure surrounds a broad upper stone court.
A rectangular central recess has an intermediate pale-tile ring around a still
lower blue basin. The ring is roughly one tile wide; the upper court is several
tiles wide in open regions. It is a three-level composition, not a single flat
floor with a blue decal. The basin is wider east–west than north–south in the
normal camera. Absolute dimensions and height ratios remain uncalibrated.
[S; G p. 101](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip),
[B, 0:32](https://www.youtube.com/watch?v=9yn0hcJWMB4&t=32s).

| Connection | Direct image reading | Recreation implication / uncertainty |
| --- | --- | --- |
| Upper court ↔ middle ring | **Two stair flights**, centered north and south. The pale ring projects toward each flight; the dark treads climb outward to the court. | These are the restricted final escape routes. Do not add east/west upper exits by mistaking recesses for stairs. |
| Middle ring ↔ bottom basin | **Four short stair flights**, at the basin's northwest, northeast, southwest and southeast side regions: two along each west/east side. | Preserve four distinct access points. Exact ramp endpoints and landing widths are not measured. |
| West/east ring projections | Narrow, inset horizontal side pockets extend outward into the upper court's footprint, bounded by the gold-edged retaining wall. | They read as middle-height pockets/cul-de-sacs, not two additional ascending flights. Full pocket collision was not isolated. |
| Inner lips | Visible drops between levels, with low trim and exposed retaining faces rather than a tall enclosing guardrail. | Entering the pit can be easier than escaping by stairs; do not turn every inner edge into an impassable wall. |

The connection reading is supported by S, G's oblique view and
[A, 0:04–0:19](https://www.youtube.com/watch?v=fUvsHrsS70c&t=4s).
In A, white moves from the basin's northeast region at 0:15–0:15.5 onto the
northeast ring around 0:16–0:16.5, then runs west along its north side. This
supports real layer traversal; it does not independently validate every flight.
No underpass or bridge-over-basin route is established here.

**Four original starts:** white northwest, red northeast, blue southwest,
black southeast, all on the broad upper court. S and
[B, 0:32](https://www.youtube.com/watch?v=9yn0hcJWMB4&t=32s) agree. Original battle
supports up to four participants according to
[M, printed p. 22](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf#page=13).
Eight starts are necessarily an adaptation.

## Basin, water, slippery-floor claim and items

**Publisher description:** M calls the center a **water tank** and warns about
the gutter. That establishes the intended water/basin theme, not automatic
drowning on contact.
[M, printed p. 25](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf#page=14).

**Observed:** characters remain alive on the blue lowest surface, traverse it,
place/fight around bombs there and collect exposed items. Red occupies the
basin across A's 0:06–0:19 samples; multiple characters fight there in
[V, 2:35–2:45](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=155s).
The dark blue floor has a visible square grid/panel pattern under a cyan,
water-like appearance. Do not map the entire basin to Blast Circuit's ordinary
immediate water-death semantics. These samples do not prove whether its effect
is physically shallow water, a safe wet surface, or another original collision
classification.

**Slipperiness is unverified.** Neither the inspected manual description nor
G p. 101 establishes a special ice/friction rule. Visual motion without recorded
input cannot distinguish sliding from held stick, knockback or normal movement.
No coefficient, acceleration change or stopping distance is justified. Treat
an authored slippery floor as a separately labeled design choice pending a
controlled original-game test.

**Destructibles:** light gray-blue masonry blocks begin **inside the basin**;
upper court and ring are clear in the inspected opening views. Blocks vanish
in explosions and expose pickups: compare
[A, 0:10–0:13](https://www.youtube.com/watch?v=fUvsHrsS70c&t=10s).
The arrangement differs between S, A's first opening, and B's later opening
at 1:10. Do not freeze one screenshot's masonry silhouette into permanent
architecture or claim a verified random-generation algorithm.

**Secondary count:** W says **eight soft blocks**, with four lower stairs and
two upper stairs. The images corroborate the location and route structure;
overlapping masonry and small frames prevent an independently reliable block
census here. Eight is a useful secondary-sourced starting count, not a recovered
coordinate table.
[W](https://bomberman.fandom.com/wiki/Greedy_Trap).

**Items and tactical intent:** yellow flame and pink heart pickups are visibly
exposed in A's 0:12–0:14 sequence. M's item illustrations identify Fire as a
bomb-power upgrade and Heart as one extra hit. No stage-specific drop weights,
guaranteed contents or respawn cycle were measured.
[A, 0:12](https://www.youtube.com/watch?v=fUvsHrsS70c&t=12s),
[M, printed pp. 26–27](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf#page=15).

G p. 101 explicitly frames the tradeoff: useful items lie lower, the upper
position is advantageous, few stair routes make returning difficult, and players
can wait until opponents are distracted before collecting. Its advice includes
kicking bombs to release items and blocking someone's return stair route.
Thus the ordinary **trap is the reward pit and contested escape**, without
evidence here of a separately cycling trapdoor mechanism.
[G, p. 101](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

## Trap animation and closing-wall sudden death

**Trigger, publisher rule:** enabled Sudden Death is a Single Battle option
beginning one minute before the time limit. G p. 95 agrees and illustrates
press walls. This is an original rule description, not a measured activation tick.
[M, printed p. 23](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf#page=13),
[G, p. 95](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

**Observed progression in V:**

| Source time | HUD / visible state |
| --- | --- |
| 2:23–2:25 | Full-width arena; HUD 1:00, READY/opening sequence. |
| 2:26 | HUD 0:59 and pink Hurry Up announcement. |
| 2:40–2:45 | Side walls visibly farther inward; outer court narrows. |
| 3:00 | HUD 0:30; broad side regions are gone and side access is being overtaken. |
| 3:15 | HUD 0:16; narrow central corridor retains north/south stairs and a strip of basin. |
| 3:20–3:23 | HUD 0:11–0:09; still narrower strip, with north/south court portions visible. |

[V, onset](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=143s),
[V, middle](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=180s),
[V, late](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=195s).

The **left and right tall wall faces move laterally toward the center**. The
fixed central stair and floor landmarks remain recognizable as the walls cover
the sides; the whole board does not simply scale narrower. Dark vertical faces
bound the shrinking playable opening. The remaining strip still has the blue
basin and pale landings; no rising-water takeover is visible in this segment.
This is the witnessed environmental trap animation. A pressure/crushing hazard
is consistent with G's press-wall description, but individual deaths amid bombs
do not isolate the exact wall contact plane or prove an instant-kill equation.
[V, 2:24, 2:45, 3:15](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=144s),
[G, p. 95](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

**Unresolved:** wall travel speed/law, start delay, minimum final gap, whether
walls push before crushing, how they treat bombs/items, sound/rumble, and exact
last survivable coordinates. Source seconds and HUD seconds are not interchangeable
measurements. No invented closing duration or periodic opening/reopening cycle
should be called original behavior. A's 2:30/0:56 sample still looks wide; its
settings are not established, so it cannot invalidate the enabled rule in V.

## Asset, model and texture palette observations

These observations come from S, G's normal/oblique views and
[B, 0:32 and 0:55](https://www.youtube.com/watch?v=9yn0hcJWMB4&t=32s), not extracted
game assets or measured original material parameters.

| Element | Visible reference character | Independently authored treatment |
| --- | --- | --- |
| Outer enclosure | Tall red/burgundy brick rear/side walls; low near wall leaves the arena readable. | Modular brick wall meshes with separate moving side sections; retain camera visibility. |
| Upper court | Warm cream, ochre and tan irregular cobblestones, broad dark seams. | Procedural rounded paving texture/relief, warm restrained variation. |
| Basin rim | Continuous ochre/gold lip following the stepped recess outline. | Clean narrow trim makes height and boundaries legible; avoid a blocking rail unless intended. |
| Middle ring | Large pale gray/ivory square tiles, darker joints; dark framed retaining faces. | Contrasting tile modules with visible side depth; keep playable width clear. |
| Stairs | Charcoal/gray tread faces with bright pale riser edges, pale side borders. | Six readable flights with distinct ramp collision and cosmetic treads. |
| Lowest floor | Deep navy/teal square panels with brighter cyan water-like areas. | Safe basin floor plus original water/wet treatment; separate cosmetic water from death checks. |
| Soft blocks | Small gray/cyan mortared brick masses, brighter than basin. | Distinct destructible masonry pieces with readable silhouettes and original break effects. |

The look is a compact **sunken masonry courtyard**, not an open ocean, ice rink
or grassy garden. Source palettes vary between recordings; no hex palette or
original texture resolution was measured. Preserve Blast Circuit's own diorama
lighting/material style and author every distributed mesh/texture independently.

## Explicit adaptation implications for eight players

The following are design recommendations inferred from the observed topology,
not claims about the four-player original:

1. Preserve the three levels, four lower flights, two upper escape flights and
   reward concentration below. Expanding every opening into a broad ramp would
   weaken the documented risk/reward identity.
2. Add four separated upper-court starts with comparable travel to both escape
   stairheads. Check opening blast reach and body clearance; no exact eight-seat
   spawn coordinates are justified by the references.
3. Increase useful circulation area deliberately, especially ring corners and
   stair landings, while retaining contestable routes. A one-body choke copied
   literally may become a persistent eight-player jam. Do not silently add two
   more upper stairs to solve it; label any topology change as adaptation.
4. Treat eight blocks as the secondary-sourced four-player baseline. Any extra
   cover, drop count, item lifetime or distribution is Blast Circuit balancing;
   doubling participant count does not establish a need to double every block.
5. Keep the basin playable. Reuse ordinary authoritative bomb/height rules and
   model stairs/falls explicitly; visual water alone must not trigger drowning.
   Leave friction ordinary unless a separate chosen rule is documented.
6. Drive moving walls, remaining support, hazards, pickups and CPU escape paths
   from shared authority so local/online behavior agrees. Animate walls to the
   accepted state. Eight-player compression pacing and final width need project
   tuning; the inspected recording supplies visual stages, not constants.
7. Review the normal camera at the crowded opening and late central-strip state.
   Keep floor height, bomb ownership and pickup silhouettes readable without
   replacing the original red/cream/gold/teal material separation.

## Retained local evidence and limits

All downloaded copyrighted media and extracted frames are under the ignored
directory **`artifacts/greedy-trap/references/`**. Nothing there is a distributable
game asset. Only this findings file is intended for Git.

| Relative to that directory | Contents |
| --- | --- |
| `user-greedy-trap-608x456.png` | Exact supplied screenshot copy |
| `manual-en.pdf`, `manual-pdf-page-13.png` through `manual-pdf-page-15.png` | Publisher PDF and inspected rendered spreads |
| `bakuguide.zip`, `guide-50.JPG`, `guide-47.JPG`, `guide-56.JPG` | Guide archive, stage spread, rules spread and colophon |
| `PoTv3FoqwJ4.webm`, `.info.json` | Downloaded sudden-death video and source metadata |
| `greedy-sudden-sheet.jpg`, `sudden-*.png`, `PoTv3FoqwJ4/onset.jpg` | Timestamped sudden-death samples; onset folder also contains individual frames |
| `fUvsHrsS70c.webm`, `.info.json`, `fUvsHrsS70c/` | Normal-play video/metadata, timestamped individual frames, `survey.jpg`, `opening.jpg`, `stairs.jpg` |
| `9yn0hcJWMB4.webm`, `.info.json`, `9yn0hcJWMB4/` | Independent video/metadata, individual frames, `survey.jpg`, `starts.jpg` |
| `extract-sheets.py`, `youtube-search.jsonl` | Reproducible frame extraction helper and discovery results |

Each `.info.json` uses the corresponding full video basename. A `sudden-death-survey.jpg`
also exists as an untimestamped discovery sheet; use the explicitly timestamped
samples for claims. Additional uninspected guide images or complete video tails
being present locally do not imply they were researched.

No game builds, desktop interaction, source edits, installation or commits were
performed for this research. The inspected evidence supports topology, material
identity, a safe-to-occupy basin and closing-wall progression. It leaves friction,
precise block coordinates, contact damage, timing constants and eight-player
balance unresolved.
