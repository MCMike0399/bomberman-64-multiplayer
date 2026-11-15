# Bomberman 64 battle-stage expansion research

Investigated 2026-11-15 against the **1997** Nintendo 64 game. This is a
reference and design record for independently authored Blast Circuit stages,
not a certification of an implementation or a recovery of original constants.
The baseline comparison is repository main `2cfe1b4` (v0.45.1).

## Sources and actual inspection

- **G — Shogakukan, Baku Bomberman Official Guide Book**, Hudson-supervised
  contemporary publisher material, hosted by a fan archive:
  [scan index](https://www.randomhoohaas.net/bomb/book/baku1/) and
  [scan ZIP](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).
  Directly inspected full spreads `guide-49.JPG` (printed pp. 98–99),
  `guide-50.JPG` (100–101), `guide-51.JPG` (102–103), `guide-52.JPG` (104–105),
  `guide-53.JPG` (106–107), and `guide-55.JPG` (110–111). Japanese paraphrases
  below are this investigation's reading of these pages. ZIP SHA-256:
  `e2c31cd2c669a57b08298406b7b7858f97fb3840353fa34e12b3f0e691ccc38d`.
- **V — blizzz, Bomberman 64 Battle Mode All Stages**:
  [recording](https://www.youtube.com/watch?v=88xIYg29HlA), duration 9:24.
  Downloaded the 360p MP4 and directly inspected extracted contact sheets:
  a fifteen-second survey from approximately 4:30 through 9:15 and a second
  fifteen-second survey from approximately 4:54 through 8:40. These establish
  visible compositions and corroborate guide imagery, not input intent,
  friction coefficients or every moment between samples. Approximate passages:
  Field of Grass 4:54–5:45; In the Gutter 5:54–6:35; Sea Sick 6:39–7:20;
  Blizzard 7:24–8:10; Lost at Sea 8:13–8:42.
- **M — Nintendo-hosted original English booklet**:
  [PDF](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf).
  Retrieved the PDF; its six-stage spread and rules have already been inspected
  in the repository's [prior primary-source review](../stage-fidelity-research.md).
  The detailed new findings here come from G and V.
- **W — Bomberman Wiki**:
  [roster](https://bomberman.fandom.com/wiki/Bomberman_64),
  [Field of Grass](https://bomberman.fandom.com/wiki/Field_of_Grass),
  [In the Gutter](https://bomberman.fandom.com/wiki/In_the_Gutter),
  [Sea Sick](https://bomberman.fandom.com/wiki/Sea_Sick),
  [Lost at Sea](https://bomberman.fandom.com/wiki/Lost_at_Sea).
  Secondary discovery/corroboration only. Followed its citations back to G
  instead of treating the wiki's prose as the publisher's words.

Research downloads and decoded footage remain outside git in temporary local
storage. No original model, texture, scan or video is a game asset. This note
contains original prose and source links only.

## Complete roster and repository gap

G devotes one numbered page to each of ten battle stages, pp. 98–107; p. 110
explicitly confirms a ten-stage total and distinguishes two unlock pairs.
English names follow the English game roster. Existing coverage comes from
[`BlastStages.Get`](../../src/Bomber.Game/Simulation/Stages/BlastStages.cs) and
the [current visual guide](../agents/visuals-and-animation.md).

| Original order | Original English stage | Original identity | Baseline Blast Circuit coverage |
| --- | --- | --- | --- |
| 1 | Rock Garden | Enclosed lawn, five stone columns, boulders | Rock Garden |
| 2 | Up & Down | Suspended multilevel routes, purple flights, rising water | Up & Down |
| 3 | Pyramid | Golden stepped pyramid with recessed lower court | Sunken Temple, the project's Pyramid adaptation |
| 4 | Greedy Trap | Upper court around a lower reward basin, restricted exits | Greedy Trap |
| 5 | Top Rules | Castle court with narrow intersecting upper walkways | Top Rules |
| 6 | Field of Grass | Grassy lower valley between two sloped banks | **Missing** |
| 7 | In the Gutter | Changing water current, five posts, closing side grates | **Missing** |
| 8 | Sea Sick | Rocking pirate deck, mast/posts and barrels | **Missing** |
| 9 | Blizzard Battle | Snowfield, changing wind, ice cones, avalanches | Blizzard Battle |
| 10 | Lost at Sea | Slippery glass court and nonslip perimeter | **Missing** |

Circuit Garden is an original tutorial arena; it does not replace Field of
Grass and should remain separate. No fifth missing battle stage is established.

**Unlock distinction.** G p. 110 says clearing story worlds 1–6 unlocks stages
7–8; collecting all 120 Gold Cards and clearing worlds 1–6 unlocks 9–10. It
also describes rapid Start input via the Hudson controller as another path to
7–10. Thus “all four unlock simply by clearing the story” is an incomplete
description. Blast Circuit can expose all ten freely; progression gates are
not required to retain the stage identities. [G, p. 110](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

## Field of Grass

**Observed composition.** G p. 103 and V show a broad green lower valley,
raised brown slopes on the left and right, and four original starts on those
slopes. Irregular pale/brown destructible rocks occupy the valley. The normal
camera sees the front edge of the diorama and sky above the far edge; this is
not Rock Garden's five-column enclosed court.
[G, p. 103](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip),
[V, around 5:00](https://www.youtube.com/watch?v=88xIYg29HlA&t=300s).

**Publisher mechanics.** The grass hides approximately the lower half of a
character and conceals normal bombs. The guide recommends kicking normal bombs
from the banks; pumped bombs are large enough to betray their position. This
is concealment by visible grass, not invisible collision walls. Its 0:55 inset
shows falling fireballs. Exact grass height, bomb visibility thresholds,
initial rock count and random placement algorithm are not recovered.
[G, p. 103](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

**Original adaptation direction.** A miniature meadow gulch can use rounded
earth banks, dense short painted grass blades, seed heads and a small rural
exterior. Preserve visible helmets, ordinary aiming markers and hazard warnings;
make normal bomb concealment legible without a full-screen grass effect.
Authoritative slopes and passable routes must match the earth mesh. Eight-seat
spawns, larger cover populations and exact concealment treatment are project
choices requiring actual battle-camera review.

## In the Gutter

**Observed composition.** G p. 104 and V show a shallow blue-green square
water court, tall vertical grates on the left/right, dark red edging, four
tall square posts in quadrant regions, and a fifth shorter center post with
a yellow direction marker. Original starts occupy the four tall post tops.
Light gray masonry cover stands within the shallow water. G's late 0:18 inset
shows the grates compressed toward the center.
[G, p. 104](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip),
[V, around 6:00](https://www.youtube.com/watch?v=88xIYg29HlA&t=360s).

**Publisher mechanics.** Water carries characters and bombs. Its horizontal
direction changes after an interval and the next direction is not fixed.
Moving upstream is difficult; downstream escape is easier. Posts can stop
unwanted downstream travel. The guide recommends maintaining the upstream
position and attacking with kicks. The yellow center ornament is visible in
the images; the wiki additionally calls it a flow indicator. Exact ornament
animation, intervals, speeds and a calm state are not isolated by this study.
[G, p. 104](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip),
[secondary indicator description](https://bomberman.fandom.com/wiki/In_the_Gutter).

**Original adaptation direction.** Build a toy waterworks lock with lacquered
brick posts, brass grates, animated ripples and an original arrow/float marker.
The playable water is shallow and safe; it must not use ordinary lethal-ocean
semantics. Current belongs to the shared simulation, not renderer-only actor
drift. Keep carried bombs attached; verify released/rolling bombs against posts
and moving grates. Closing walls should retain the existing crush effect.
Eight starts, any extra post access, current force/schedule and dimensions are
explicit project tuning. Exterior sluice wheels and channels can enrich the
diorama without entering playable space.

## Sea Sick

**Observed composition.** G p. 105 shows warm wooden deck planks, a very tall
center mast, two shorter square posts in the front half, green/dark gunwale
trim, ropes/rigging, outside stair shapes and destructible barrels. The four
starts occupy deck corners. V corroborates the mast/post arrangement and
changing deck angle. G's 0:58 inset shows falling fireballs.
[G, p. 105](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip),
[V, around 6:45](https://www.youtube.com/watch?v=88xIYg29HlA&t=405s).

**Publisher mechanics.** The guide describes a ship rocked by rough seas and
expressly says there is no other gimmick of this sort. It recommends standard
combat and hiding behind the mast/posts to kick or place surprise bombs.
The inspected evidence does **not** establish gravity-driven actor/bomb drift,
a slippery deck, periodic knockdowns or a rotating authoritative coordinate
system. Adding those would be a new design, not a faithful recovered rule.
[G, p. 105](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

**Original adaptation direction.** Use a rounded toy cutter, painted timber,
chunky rope coils, canvas and a small ocean diorama. Roll the entire rendered
deck/actor/effect assembly coherently, or use restrained camera/world motion;
terrain and input/aim projections must remain aligned. The main identity is
nautical composition plus readable rocking, not a new generic wind mechanic.
Keep masts narrow enough to preserve combat visibility. Barrel counts,
rock amplitude/frequency and eight-player expansion remain project tuning.

## Lost at Sea

**Observed composition.** G p. 107 calls the stage ツルツルガラス, “slippery
glass.” Its central court is deep purple/blue, with diagonal highlights; a
pale silver/white metal perimeter has inset rivets. Four red/orange tall
obstacles stand around the center, each on a square foot, with X-braced crates
near them. The four starts occupy the perimeter corners. V agrees. It is
distinct from Blizzard's pale snowy rink and blue cone cover.
[G, p. 107](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip),
[V, around 8:20](https://www.youtube.com/watch?v=88xIYg29HlA&t=500s).

**Publisher mechanics.** The slippery surface makes movement and quick turns
difficult. Kicked bombs slide as well, so the guide recommends earlier turn
input and throws. The perimeter is explicitly nonslip, providing a strategic
alternative to the center. A 0:58 inset shows meteors. Exact acceleration,
deceleration, residual momentum and bomb friction are undocumented here.
The wiki's below-glass fish image is a useful visual lead; that image was not
separately inspected, so fish visibility is not a finding of this research.
[G, p. 107](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip),
[secondary gallery lead](https://bomberman.fandom.com/wiki/Lost_at_Sea).

**Original adaptation direction.** A marine observation platform can retain
the dark glass, nonslip metal rim, four warm-colored supports and crates, with
an original aquarium/ocean beneath. Use quality-dependent reflection and
depth cues while preserving the existing Low/Medium/High identity. Gameplay
sliding belongs to authority and must clearly stop at the nonslip rim.
Framing, bomb trajectories and CPU anticipation need checks; transparent-looking
art alone does not establish slide behavior. Eight-player spacing and exact
momentum are project choices.

## Refining the six existing battle arenas

These are visual priorities grounded in G's inspected imagery and the current
repository contracts. They are proposals, not claims that baseline visuals
have failed a new screenshot comparison. Keep physical geometry unchanged
unless a separately verified rule change requires it.

| Stage | Identity to preserve | Useful original reinterpretation |
| --- | --- | --- |
| Rock Garden | Five columns, taller central column, cross-shaped pale paving, lawn, irregular boulders; G p. 98 | Mottled warm stone, cracked sides, clear column caps, small garden carvings and meadow life outside the enclosure. Keep the accepted center/outer height relationship. |
| Up & Down | Suspended purple outer ring, green/yellow crown, six purple flights, rounded supports and real lower space; G p. 99 | Stronger purple/pale tread separation, ceramic mosaic details, relief bands and restrained coastal surroundings. Preserve underpasses, visible water and flood progression. |
| Pyramid / Sunken Temple | Golden terraces, narrow summit, recessed cobbled lower court, front stair approaches; G p. 100 | Warm sandstone edge variation, small original friezes, grounded foundations and lagoon archaeology. Retain close framing, complete routes and rising-water readability. |
| Greedy Trap | Enclosing court, narrow middle ring, lower blue reward chamber and restricted escape flights; G p. 101 | Rich brick/stone contrast, readable basin paving, brass waterworks details and reservoir life. Do not turn its safe basin into lethal ocean or add unverified ice physics. |
| Top Rules | Three long raised lanes and one crosswalk, red lower court, green corner stairs, tall banner walls; G p. 102 | Large original heraldic banners, dark slate/brass bridges and rich red tiling. Preserve lower passages and established pumped-blast reach through the thin decks. |
| Blizzard Battle | Pale flat snowfield, violet/cyan cones, high side glaciers, changing wind and bilateral avalanche; G p. 106 | Layered glacial shelves, faceted ice cracks, wind-raked snow and small cold-region life outside play. Preserve cone collision height, clear wind cues and crush death. |

[G, pp. 98–107](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).
The prior [fidelity review](../stage-fidelity-research.md),
[living-worlds research](../living-stage-worlds-research.md),
[visual guide](../agents/visuals-and-animation.md) and stage-specific contracts
retain accepted adaptations and past corrections. The original had four seats;
every eight-seat layout and increased cover density is Blast Circuit tuning.

## Review and delivery implications

Each map PR should include its original-reference links, clearly labeled
adaptations, authority/transport/presentation ownership, focused managed checks
and an ordinary battle-camera journey. Inspect the actual opening, cleared
court, eight-player battle, late-round hazard, Night and Low/Medium/High images.
For geometry with overlapping levels also capture a labeled oblique view.
Menu miniatures must identify the same map, and online admission/validation
must accept exactly the supported stage set.

Current/slide/rocking claims need separate evidence: inspect accepted state for
physics, rendered state for art and camera, and real input for focus/devices.
Reference similarity and green numeric checks do not certify visual quality.
No release, graphical verification or user approval is asserted by this note.
