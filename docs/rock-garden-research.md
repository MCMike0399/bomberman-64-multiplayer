# Original Rock Garden research

2026-10-24. Scope: **1997 Bomberman 64**, original Battle Stage 1, as a reference
for independently authored Blast Circuit geometry. Original facts, observations,
unresolved behavior and eight-player adaptations are separated below.

## Sources inspected

- **[M] Primary, original English manual:** [Nintendo-hosted scan][manual],
  printed pp. 8, 22–24 (PDF pages 6, 13–14, one-based). The
  [GamesDatabase mirror][mirror] was also checked. These are the original booklet,
  rather than a fan transcription or the unrelated Japanese 2001 game.
- **[G] Primary, official guide:** [Baku Bomberman Official Guide Book scan ZIP][guide],
  printed p. 98, `guide-49.JPG`, directly inspected. Shogakukan, ISBN
  4-09-102598-6; the colophon credits Hudson supervision/cooperation. Its Japanese
  stage heading is **さいしょのバトル**, approximately “First Battle.” The
  [archive's catalog page][catalog] is secondary; the scanned book is the evidence.
- **[V1] Primary visual evidence, original software recorded by a third party:**
  [Oppaiman100, Rock Garden / four CPU players][cpu]. Frames inspected at
  **0:35, 2:45 and 3:15**, all HUD 2:00, plus the opening gameplay sequence.
- **[V2] Primary visual evidence, original software recorded by a third party:**
  [ShadowIori, All Stages Sudden Death][sudden], Rock Garden **0:00–0:31**;
  sampled throughout, including **0:10 / HUD 0:57**. Recording/emulation conditions
  and hidden engine constants were not independently verified.
- **[U1/U2] Both user screenshots inspected directly:** attachment
  `6dc9c9d3-1981-4515-8710-73d52265df8b.png` (**U1**, 640×360, HUD 1:51) and
  `7276bbfe-a49a-42fc-b854-46fcb5a6543c.png` (**U2**, 608×456, HUD 2:00).
  Their original upload URLs and recording conditions are unknown. They remain
  outside the repository.

## Verified original facts

- The English name is **Rock Garden**. The manual describes nature/ruins and
  five poles; the official guide labels it Battle Stage 1 and describes a flat,
  simple field with five pillars and rocks. [M, p. 24; G, p. 98]
- Original Battle supports **up to four participants**. Players cannot rotate
  or zoom the camera in Battle mode. [M, pp. 8, 22]
- Enabled Sudden Death is a **Single Battle** rule starting one minute before
  the time limit. [M, p. 23] Rock Garden's hazard visibly consists of large
  orange **falling fireballs / meteors**, with shadows and impact effects.
  The guide shows these at HUD 0:53; V2 shows an airborne fireball at
  [0:10 / HUD 0:57][meteor]. Closing columns are not the hazard shown by these
  original sources. Exact spawn cadence, targeting, damage radius and onset frame
  remain unmeasured. [G, p. 98; V2]

## Image observations and floor-plan evidence

Directions here mean **north = far/top of the normal battle view**. U1, U2,
the manual and guide agree on a grass court enclosed by a low pale stone wall,
with five cylindrical, flat-capped stone pillars: **center, north, south, west,
east**. The central pillar is taller. The official guide explicitly discusses
using pillars, especially their camera occlusion, for concealment. [U1/U2;
M, p. 24; G, p. 98]

The center has pale square paving forming a stepped diamond/cross. U2 supports
a reconstruction with row widths **1, 3, 5, 7, 5, 3, 1** (25 tile positions,
including positions obscured by the pillar/rocks). This is a **visual paving
pattern**, not evidence for a 7×7 movement grid. The grass has no visible cell
lines. Numerical arena dimensions, tile/world scale, pillar coordinates/radii
and an exact square aspect ratio are **not established**. U1 is widescreen;
U2 is 4:3, so their pixel proportions cannot establish world dimensions.

**Starts:** U2 and the [0:35 Ready frame][ready] agree on white NW, red NE,
blue SW and black SE, inset within the four quadrants rather than touching the
wall corners. These establish relative locations, not numerical spawn coordinates.

**Boulders:** U2 contains **16 individually visible angular boulders**. The table
records their approximate image-center positions for a reproducible comparison;
these are pixels in the 608×456 image, **not floor anchors or game cells**.
Occluded additional objects cannot be excluded from a still image.

| North-to-south image band | Approximate x positions, left to right | Visible count |
| --- | --- | --- |
| y ≈ 145 | 167, 388, 446 | 3 |
| y ≈ 190 | 161 | 1 |
| y ≈ 240 | 246, 366, 424 | 3 |
| y ≈ 290 | 213, 275, 337, 459 | 4 |
| y ≈ 348 | 142, 208, 340, 405, 471 | 5 |

V1's HUD-2:00 layouts at [0:35][ready], [2:45][round2] and [3:15][round3]
visibly differ while pillars and starts stay fixed. Therefore a single screenshot
does **not** define a universal starting boulder map. Random placement versus
selection among templates is unresolved. U1 is already in progress and cannot
establish initial cover. Opening footage shows rocks disappearing in explosions
and items appearing, while pillars remain. [V1, approximately 0:38–0:50]

Visually, preserve the broad readable grass, small central tiled accent,
weathered gray/beige cylinders, faceted irregular rocks and uninterrupted outer
stone border. The elevated oblique shared camera shows floor tops and pillar
sides, fits the whole court, and leaves the top HUD legible. Texture colors and
sharpness vary across these captures; numerical pitch, FOV and lighting are not
established. [U1/U2; M; G; V1]

## Pillars: evidence limits

Pillars persist through the sampled explosions; their role as permanent
obstacles is strongly supported. The guide proves **visual concealment**, not
an exact collision or blast-blocking algorithm. Whether the original collider is
circular, polygonal or a box, whether blasts pass through it, and whether bombs,
players or team gems can rest on every top remain **unverified** here.

**Secondary lead only:** [M_Fox's 1998 Battle Mode Guide][fan] claims a teammate
can throw a player onto the perimeter and a giant player can throw the team
crystal onto the middle column. This suggests testing finite-height top support;
it is not primary proof and does not establish walk-up access. No inspected
primary source establishes stairs, ramps, pillar tunnels or ordinary walking
through the pillars.

## Actionable project adaptations

1. Retain the flat open court, five-pillar composition, taller center and small
   paving diamond. Keep rendering and authoritative collision consistent; model
   permanent solids explicitly. Top support and blast occlusion need explicit
   project decisions or a targeted original-game witness.
2. Use four quadrant starts as the reference. **Eight starts**, arena enlargement,
   extra cover and spawn clearance are Blast Circuit tuning. Keep the original
   visual sparseness in view when choosing that tuning; do not present 16 visible
   rocks as a mandatory original total or reuse one capture as an exact grid.
3. For original-inspired Sudden Death, implement falling fireballs/meteors.
   Existing closing columns are a project adaptation, not a verified original
   rule. All timing beyond the manual's final-minute rule needs project tuning.
4. Author geometry/textures independently and verify the implementation later
   with the parent's build/gameplay/visual QA. This research changed only this
   document; it performed no game builds, desktop QA, installation or deployment.

[manual]: https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf
[mirror]: https://www.gamesdatabase.org/Media/SYSTEM/Nintendo_N64/Manual/formated/Bomberman_64_-_1997_-_Nintendo.pdf
[guide]: https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip
[catalog]: https://www.randomhoohaas.net/bomb/book/baku1/
[cpu]: https://www.youtube.com/watch?v=TXkDVnExEqE
[ready]: https://www.youtube.com/watch?v=TXkDVnExEqE&t=35s
[round2]: https://www.youtube.com/watch?v=TXkDVnExEqE&t=165s
[round3]: https://www.youtube.com/watch?v=TXkDVnExEqE&t=195s
[sudden]: https://www.youtube.com/watch?v=PoTv3FoqwJ4
[meteor]: https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=10s
[fan]: https://gamefaqs.gamespot.com/n64/196799-bomberman-64/faqs/3163
