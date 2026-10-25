# Original Up & Down research

2026-10-24. Scope: **1997 Bomberman 64**, Battle Stage 2, for an independently
authored rebuild. Publisher descriptions, visual observations and inferences
are distinguished below. Current Blast Circuit geometry is not original evidence.

## Sources inspected

- **Manual, primary:** [Nintendo manual scan](https://www.gamesdatabase.org/Media/SYSTEM/Nintendo_N64/Manual/formated/Bomberman_64_-_1997_-_Nintendo.pdf),
  printed pp. 23–24 (PDF pages 13–14, one-based; two printed pages per image).
  Page 24 calls this multi-layered and recommends using “ladders” for movement
  and attacks. Page 23 places enabled Sudden Death one minute before time expires.
  Its stage screenshot is mid-round, not a starting-layout reference.
- **Guidebook, primary:** [Baku Bomberman Official Guide Book scan ZIP](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip),
  printed p. 99, `guide-49.JPG` (spread 98–99), inspected directly. It describes
  three levels connected by narrow stairs, item blocks on lower/middle levels,
  attacks from above, and Sudden Death flooding. It recommends the upper outer
  perimeter for attacking downward. Page 95 corroborates the one-minute trigger.
  Colophon: Shogakukan, ISBN 4-09-102598-6; first printing 1997-11-10, scanned copy
  fifth printing 1998-03-10. Japanese descriptions here are paraphrases.
- **Original software footage, primary visual evidence:**
  [Oppaiman100, four-CPU Up And Down](https://www.youtube.com/watch?v=8neP1yj-PFs&t=6s),
  sampled at 0:06, 0:10 and 1:15;
  [ShadowIori, All Stages Sudden Death](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=32s),
  Up & Down segment 0:32–1:17, sampled at 0:33, 0:38, 0:50–0:57, 1:05 and 1:15.
  Frame inspection does not establish hidden collision constants.
- **User captures:** all three supplied screenshots were inspected: an empty
  2:00 layout/spawn view, an oblique view, and a low frontal paused view. Two
  show *Up and Down (Bomberman 64)…* / MintCat. Web/YouTube searches did not
  relocate that upload; its URL, timestamps and recording conditions remain
  unknown. The images themselves remain outside the repository.
- **Secondary lead:** [fan wiki](https://bomberman.fandom.com/wiki/Up_%26_Down)
  cites guidebook p. 99 and claims 12 initial soft blocks. The original guidebook
  was inspected; the count is not a publisher-confirmed number.

## Geometry and routes

Directions mean **north = far/top of the normal battle camera**. The captures,
[guidebook p. 99](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip) and
[0:06 footage](https://www.youtube.com/watch?v=8neP1yj-PFs&t=6s) agree:

| Part | Direct observation / reconstruction |
| --- | --- |
| Upper perimeter | Narrow continuous rectangular playable ring above water, with an open interior. |
| Green summit | Broad square at the upper level. Two narrow pale north/south links connect it to the ring: north link meets its north edge toward the **west**; south link its south edge toward the **east**. A staggered route, not one centered straight strip. |
| Middle | Two elongated north/south stone piers, west and east, below ring/summit; each extends on both sides of the near ring segment. |
| Bottom | Narrow north/south central deck with wider panelled sections below the upper structures. Water fills surrounding gaps; there is no full ground rectangle. |
| Purple stairs | **Six flights, three per side:** one pair summit ↔ west/east piers; one pair piers ↔ north end of low deck; one pair piers ↔ south end of low deck. Lateral west/east connections appear diagonal because of elevation; exact plan coordinates need calibration. |

Route graph: **ring ↔ summit ↔ either middle pier ↔ either end of bottom deck**.
Stairs have purple treads, pale side pieces and yellow arrow landings. Stair
crossings in projection do not establish junctions. The pale upper links are not
additional purple stairs.

**True underpasses exist beneath the near upper ring.** In the
[0:50–0:55 sequence](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=50s), blue moves
from the west pier's south portion, becomes occluded at the ring, and appears on
its north portion. Independent walkable surfaces overlap in X/Z; a single
highest-floor map loses this route. The east pier is visually symmetric
(inference). Deck also visibly extends below the upper south link, but clearance
beneath every stair overlap or directly beneath the summit is **not fully
established**. Do not infer tunnels through solid supports from projection alone.

## Spawns, cover and flood

- **Original four starts:** upper ring corners, white NW, red NE, blue SW,
  black SE; visible in the user's 2:00 capture and
  [0:06 footage](https://www.youtube.com/watch?v=8neP1yj-PFs&t=6s).
- **Cover:** that footage shows stone blocks on middle piers and paired blocks
  on the bottom deck; ring, summit and narrow links start clear. Two per pier
  plus eight below is a plausible reading consistent with the wiki's 12, but
  occlusion prevents a complete coordinate census. The empty user capture cannot
  establish ordinary initial cover. Guidebook p. 99 confirms both lower and
  middle item-block regions.
- **Water:** present from the start; Sudden Death floods upward. In
  [the flood segment](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=50s),
  0:50 / HUD 0:52 shows a dry low deck; 1:05 / HUD 0:40 shows water on its lower
  portion; 1:15 / HUD 0:31 shows it submerged while middle piers and upper ring
  remain dry. Guidebook p. 99 also shows a flooded HUD 0:13 view. The one-minute
  rule trigger does not establish immediate visible deck inundation or a rise formula.

## Rebuild recommendations and uncertainties

Preserve the suspended three-level composition, six stairs, staggered upper
links, exposed edges and corner starts. Use layered support surfaces with
separate fall, ceiling and blast checks; retain the confirmed pier underpass.
Keep upper routes clear and concentrate original-inspired cover below. Eight
spawns, expanded cover and dimensions are project tuning, not original facts.
Author stone, water and decoration independently.

Still unresolved: numerical dimensions/heights, stair offsets/step counts,
every underside clearance, exact fall/recovery boundaries, starting block
coordinates, inter-level blast propagation, and flood onset curve, lethal contact
plane and final safe area. No inspected source establishes numerical collision
or water constants. No ripped models/textures were used or distributed; external
scans stay in `/tmp` and attachments outside the repository.
