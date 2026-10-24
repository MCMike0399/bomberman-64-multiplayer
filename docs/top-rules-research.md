# Top Rules reference research

Investigated 2026-10-24 for an original Blast Circuit implementation. Scope:
the **1997** Nintendo 64 game, Single Battle geometry and the supplied Team
Battle screenshot. This is a reference investigation, not reverse engineering
of undocumented constants.

## Sources actually inspected

- **M — original Hudson/Nintendo English instruction booklet**, hosted by
  Nintendo: [PDF](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf).
  Read rendered printed pp. 22–25, especially p. 23 (PDF page 13) and p. 25
  (PDF page 14). PDF pages contain two printed pages.
- **V — recorded original gameplay**, TurflamicGaming420, *Bomberman 64 SUDDEN
  DEATH at Top Rules*:
  [video](https://www.youtube.com/watch?v=RMV6wCZT63c). Downloaded and inspected
  contact sheets plus denser frame sequences around 0:10–0:34, 2:05–2:25,
  4:25–4:37 and 6:39–7:03. These are observations of the game imagery, not
  assertions that the uploader's commentary establishes game rules.
- **X — independent recorded original gameplay**, XFactor1111, *Bomberman 64
  multiplayer gameplay (Nintendo 64)*:
  [video](https://www.youtube.com/watch?v=SPjnYY_Gomo). Surveyed sampled frames;
  inspected the Top Rules round beginning near
  [26:59](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=1619s) and subsequent
  frames through approximately 27:40. This provides another starting layout.
- **S — supplied screenshots**, identified by filename endings: `f988d5f2e24e.png`
  (608×456, Single Battle 2:00), `9fdcac3af039.png` (1280×720, Team Battle 1:54),
  and `8af5763b705a.png` (250×186, Single Battle 0:43). The first is also
  publicly available as an actual-game screenshot:
  [TopRules.png](https://static.wikia.nocookie.net/bomberman/images/d/dc/TopRules.png).
  The screenshot itself is evidence; the surrounding wiki's written explanation
  is secondary and is not used to establish mechanics here.

Research downloads and extracted frames stayed outside the repository. No game
assets or reference videos were added to git.

## Established observations

**Identity.** The English manual names this arena **Top Rules**, describes a
castle with two storeys, and warns about attacks from above. Its screenshot
matches S's red court and intersecting narrow upper walkways. This is not
Pyramid or the 2001 Japanese game with the same English title. [M, printed p. 25](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf#page=14).

**Upper geometry.** S shows **three parallel front-to-back lanes intersected by
one left-to-right crosswalk** near the middle: four runs total, three junctions.
Each junction has a pale square surface. The lanes have dark framed panels,
exposed side edges and no continuous guardrail. Their ends meet the raised
front/rear stone perimeter; footage shows characters moving laterally along
the rear perimeter. Tall castle walls, rear recesses/columns and banners frame
the arena. These should not be mistaken for additional playable platforms.
[S](https://static.wikia.nocookie.net/bomberman/images/d/dc/TopRules.png),
[V, approximately 0:14–0:17](https://www.youtube.com/watch?v=RMV6wCZT63c&t=14s).

**Red lower floor is playable.** Characters move, survive and fight on the red
patterned court; bombs also sit there. It is not a full-floor lava pit. Narrow
glowing yellow/orange strips are visually distinct along portions of the
perimeter. Their exact damage/contact rules were not isolated in this study.
[V, 0:20–0:30](https://www.youtube.com/watch?v=RMV6wCZT63c&t=20s),
[X, around 27:08–27:20](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=1628s).

**Walkways have playable space underneath.** The lower court continues beneath
upper runs. In V's 6:43–6:51 sequence, blue moves through the lower court across
upper-walkway projections, with the deck occluding parts of the character;
blue does not automatically rise onto the upper surface. Bombs also occupy
lower space near/under the decks. An upper run therefore cannot be represented
as a full-height solid wall or as a height field that replaces all lower-floor
occupancy at that horizontal position.
[V, 6:43–6:51](https://www.youtube.com/watch?v=RMV6wCZT63c&t=403s).

**Four corner stairs.** S clearly shows four green striped stair flights, one
near each corner. The rear flights run along the side margins from the raised
rear perimeter down toward the lower court; the front flights run between the
lower court and raised front perimeter. In V, white visibly climbs the
rear-left flight and arrives on the rear perimeter around 0:14–0:16. White
later travels from the lower court up the front-left flight onto the front
perimeter around 2:13–2:15. Thus stairs are access routes between storeys,
not ladders painted onto a wall. [S](https://static.wikia.nocookie.net/bomberman/images/d/dc/TopRules.png),
[V, rear-left](https://www.youtube.com/watch?v=RMV6wCZT63c&t=14s),
[V, front-left](https://www.youtube.com/watch?v=RMV6wCZT63c&t=133s).

**Four original spawn locations.** The supplied 2:00 frame and X's READY frame
show the same arrangement. Directions below are camera-relative; the center
lane starts empty. Exact coordinates and clearances are not established.
[S](https://static.wikia.nocookie.net/bomberman/images/d/dc/TopRules.png),
[X, 26:59](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=1619s).

| Character | Starting region |
| --- | --- |
| White | Rear end of left upper lane |
| Red | Rear end of right upper lane |
| Blue | Front end of left upper lane |
| Black | Front end of right upper lane |

**Blocks.** Five distinct gray masonry blocks are visibly exposed in S's 2:00
frame, all on the lower court and none on the upper lanes. Their approximate
regions in that particular image are listed below. This is an observed visible
count, not proof that no additional block is hidden behind a deck. S's Team
Battle frame has a different arrangement; X's READY frame also differs from S.
Do not claim a single universal original placement pattern or a verified
random-generation algorithm. [S](https://static.wikia.nocookie.net/bomberman/images/d/dc/TopRules.png),
[X, 26:59](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=1619s).

| Region of supplied 2:00 image | Exposed blocks |
| --- | ---: |
| Rear half, left of left upper lane | 1 |
| Rear half, between left and center upper lanes | 1 |
| Rear half, between center and right upper lanes | 1 |
| Front half, left of left upper lane | 1 |
| Front half, between center and right upper lanes | 1 |

**Sudden Death.** M says the optional rule applies to Single Battle and begins
one minute before the limit. V shows the Hurry Up announcement as a one-minute
round drops to 0:59, then the **left and right castle walls move inward**.
Compare the wide opening at 0:10 with approximately 0:45; at 4:25–4:37 only a
narrow central strip remains. The walls cover the side stairs and outer lanes
as they advance. There is no observed rising water or full-floor lava conversion
in these Top Rules sequences. Exact activation tick, travel law, minimum gap,
and damage volume remain unknown. [M, printed p. 23](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf#page=13),
[V, announcement](https://www.youtube.com/watch?v=RMV6wCZT63c&t=10s),
[V, compression](https://www.youtube.com/watch?v=RMV6wCZT63c&t=45s),
[V, late compression](https://www.youtube.com/watch?v=RMV6wCZT63c&t=265s).

## Inferences and implementation implications

- The two right flights appear to mirror the witnessed left flights. Matching
  connectivity is a strong visual inference; a continuous ascent/descent on
  each right flight was not separately isolated. The stair feet connect to the
  lower court, not automatically to the middle crosswalk merely because they
  overlap it in camera projection. [S and V stair sequences above.]
- Preserve a lower collision surface underneath elevated walkways and select
  support by storey/contact history. The same horizontal footprint can contain
  an actor on the bridge and another actor below. This is an implementation
  consequence of the underpass observation, not a recovered original engine
  algorithm. [V, 6:43–6:51 above.]
- Raised perimeter access plus narrow upper routes gives meaningful choices:
  stay high, descend to blocks/items, use stairs to return, and attack between
  levels. The manual supports attacks from above, but this investigation does
  not establish an exact blast-penetration or vertical-reach rule. [M, p. 25;
  V movement sequences above.]

## Still unknown

- Original world dimensions, deck height/thickness, lane width, ramp slope and
  stair step count; screen pixels are perspective measurements, not world units.
- Guaranteed total block count, hidden block positions, respawn behavior,
  randomness/seed logic and drops. The five-block screenshot is a useful
  reconstruction witness, not an undocumented specification.
- Whether every glowing edge strip is lethal, the precise playable boundary,
  and exact stair-side collision. Likewise, every possible route under every
  junction has not been exhaustively tested.
- Detailed bomb throws between storeys, blast occlusion/vertical reach, wall
  crush collision and whether advancing walls destroy or displace each object
  type. No exact undocumented constants should be attributed to Nintendo.
- Eight-player spawns: the inspected original sources establish four. Any
  additional starts are a Blast Circuit design decision.

An original Japanese official-guide scan was sought as a possible additional
source: [scan index](https://randomhoohaas.flyingomelette.com/bomb/book/baku1/).
It could not be retrieved during this investigation. In particular, the wiki's
attribution to guide p. 102 was **not** independently inspected and is not used
to upgrade its mechanics claims into established facts.

## Blast Circuit implementation choices

The implementation uses an original 19×17 layout, tier-two thin decks, four-cell
stair flights, twelve seeded masonry blocks on the lower court and eight safe
starts. The first four occupy the outer lane ends; the other four support this
project's larger roster. These dimensions, counts and timings are project tuning.
Bridge slabs block blasts and upward throws while preserving horizontal lower
passages; exact original blast penetration was not established. The amber court
edging is decorative. Inward wall closure uses the existing authoritative
column hazard. All geometry, materials and textures are generated by this repo.
