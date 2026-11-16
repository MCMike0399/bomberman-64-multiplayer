# Blizzard Battle

Blizzard Battle adds an enclosed, flat frozen court to local battle, practice,
server rooms and player-hosted rooms. The [reference investigation](blizzard-battle-research.md)
records the screenshot, original hardware footage and official guidebook pages.
All shipped geometry, textures and animation are original procedural work.

## Layout and adaptations

The 19×19 layout has seventeen playable cells across, tall glacier side walls,
a lower rear rim and a low near rim that preserves the ordinary battle view.
64 pointed violet ice cones are sampled independently within four quadrants.
Only the perimeter loop, central cross and spawn escape cells are reserved.
Coordinates are not mirrored and no fixed cone pattern is filled at 100%.
These authored safety regions and budgets are gameplay tuning rather than a
recovered original placement algorithm.
After the user requested larger spires, their tops rise **1.755 render units**
(1.3 authority units), just over twice the initial height. Their broad bases
span .94 units. Stage `coverHeight` drives both the mesh and projectile
obstruction/landing; tall cover also extends blast shielding.
The first four seats start at the corners; four additional seats start on the
perimeter. Spawn escape cells remain clear. Advanced block percentages apply to
the 64-cone budget; practice has no cover. Bomb destruction and loot use
ordinary rules and the configured item pool/drop probability.

## Weather and sudden death

`BlizzardRules` owns the shared weather schedule: four diagonal winds and calm,
with deterministic seed/round variation and short ramps between gusts. Weather
uses a separate clock so it continues in practice, pauses with local play and
resets with every round. It consumes no loot/bot RNG. Six-second gusts,
1.25-cell/second bomb drift and 65% player response are project tuning, not
original-game constants. There is no added ice-skating acceleration: the
reference evidence establishes wind over a snowfield, not Lost at Sea's ice physics.

Wind displaces living ground/airborne players and free resting, kicked and
thrown bombs through their existing collision paths. Held objects follow the
carrier. Drift may separate a bomb from a body it overlaps but never pushes it
deeper; that includes a fresh bomb's owner. Before 2026-10-26 the owner was
exempt, so a bomb placed facing into a gust slid straight through its owner.
Wind does not change facing, vertical gravity, stored throw momentum,
charge time or fuse ownership. The aim guide samples the same schedule for its
future static arc; moving combatants and future cover changes remain advisory
limitations. CPU players retain the same resources and movement rules.

At Hurry, packed snow advances equally from both side walls, one column every
three seconds. The hazard eliminates players in swept columns, including later
entrants; hearts do not make packed snow safe. Resting bombs in the closure
explode through the existing sudden-death path. CPU safe-exit planning uses the
same interval. New rounds clear both closure counts. Disabled sudden death and
practice retain weather without avalanches.

Online protocol revision **5** prevents older clients from accepting an arena
or wind state they cannot render/simulate. Server, peer host and guests must
use this revision together. This PR does not deploy the shared server.

## Presentation

The stage uses opaque blue ice with restrained subsurface-looking fractures,
frosty violet facets, pale striated glacier walls and powder caps. World-space
shader patterns are the newly authored textures; there are no original-game
textures or model archives in the repository. Opaque ice writes depth on all
presets, so characters and aim markers retain the established depth treatment.
All four lighting profiles and the existing lacquered character palette apply.

A bounded MultiMesh animates snow along accepted wind, with sparse larger flakes
and fine flurries. Low/Medium/High draw 72/152/240 flakes. Low omits fine shader
sparkles. Local pause freezes the snow clock and authoritative gust progression.
Snow never changes collision. Between accepted closures the next snow bank
creeps out from behind the packed snow and reaches its column as that column
becomes lethal, then settles in place (2026-10-26; earlier banks popped in a
whole column every three seconds). The menu miniature uses the same terrain and ice
cones with its exterior cropped to the display plinth.

## Verification entrypoints

- `dotnet run --project tests/Bomber.Game.Tests -- --blizzard-battle`
- `python3 tools/playtest/run.py --scenario blizzard-battle --headless`
- `python3 tools/playtest/run.py --scenario blizzard-battle --record`
- `python3 tools/playtest/run.py --scenario blizzard-regression`
- Canonical `public`, `managed` and `headless` QA profiles.

The focused native journey drives the arena selector, four/eight-seat starts,
real snow motion and pause, all graphics presets, night lighting, ordinary
wind-affected aiming/throwing, cone destruction and pickup, bilateral avalanches
and CPU combat. It uses ordinary inputs and accepted snapshots, without changing
entity positions or inventory. Detail captures temporarily use an inspection
camera; opening/combat/avalanche images use the battle camera. `blizzard-regression`
also runs first-use effects, all-stage graphics/lighting, setup, framing, asset
audit and online play. The AI soak now includes all five battle arenas: sixty
stage/difficulty/player-count/seed cases. Public QA validates the expanded receipt.

[Verification and screenshots](blizzard-battle-verification.md) record the inspected export and retained failures.

Headless tests establish rules and resource geometry, not the appearance of
ice, weather or animations. Graphical receipts and manually inspected images
are recorded separately when available. Neither synthetic inputs nor VM graphics
certify new physical-controller support, native GPU throughput or original-game
numerical physics.

## Pending-PR review corrections (2026-10-25)

A deterministic wall-ricochet regression reproduced gusts being added twice to
stored momentum. Collision now reflects the original momentum on precisely the
axes reflected by the shared flight sweep. Wind remains a displacement term.
Another regression reproduced resting bombs trapped atop surviving spires; drift
now uses the shared height-aware static footprint, preserving spire-side blocking
and allowing supported bombs to drift off and fall. Both corrected paths pass
the focused managed Blizzard suite. Final combined graphical evidence is separate.

## Glacier Bowl — fresh advisor correction, 2026-11-15

The fresh plain-Claude review found Low's court floating in a white void and
High Night snow reading as rippling water. The core now includes a sculpted
rounded snow berm over a blue ice shelf/band, built wholly around the unchanged
wall footprint. A connected outboard shelf seats the retained left arch; a few
hanging icicles and three broad crown forms join it into one glacier silhouette.
The core body and arch remain on Low. The snowman is removed; the eastern frozen
pond and penguins remain the focal vignette, with the igloo/background trees
subordinate. A chunky core pennant takes the accepted weather vector and pausable
weather clock; it has no influence on authority. One amber core lantern retains
emissive paint on Low and one shadowless warm light on Medium/High.

High snow normal strength is .055 at Night versus the original .22 by day. This
is a frozen-surface input set by the existing stage lighting handoff, with no
change to the shared Night profile, cone geometry, court ice, wind, avalanche,
physics, inputs, camera or authority. The global footer and bomb readability
corrections belong to the integration owner.

Managed source build and public checks pass. The focused headless Blizzard
journey passes 87 assertions, including actual indexed clockwise bowl faces,
core retention on every preset, Night/day snow normal inputs, emissive-only Low
lantern policy, accepted pennant vector, pause, ordinary throws, cone loot,
avalanches and CPU combat. Its raw log retains 20 ObjectDB exit leaks and no
runtime errors; this is not a clean native qualification claim. The first
sandboxed editor import could not save user editor settings; the authorized
headless import completed with no errors or warnings. Matched Low/Medium/High
opening/Night combat captures and a short wind witness still require native
review and the final advisor gate.

## Glacier Lantern Grotto — 2026-11-16 source candidate

This supersedes the separate arch/shelf in the previous candidate. The user's
close crop exposed a real support mismatch: shelf topY=.16 engulfed the arch's
lower .485 units, the snow feet protruded only .015 above it, and the rotated
left post overhung the shelf by about .80. Parentage/winding checks did not
prove contact. The old posts and detached slab are removed.

One owned three-surface mesh now carries the sculpted field, continuous snow
shoulder, genuinely open through-vault, pale thick ice lining and four rounded
icicles. Its outer shoulder meets the field through a horizontal fillet; the
white/ice lip shares its generated interface. One tilted violet crown samples
the actual captured snow triangles for its seven support vertices. The amber
lantern sits inside the vault. The existing pond/penguins consolidate in its
lee, absorbing the igloo; Low keeps one penguin. Three broad drifts and a muted
blue-grey far field separate the white bowl from its surround.

The existing accepted-wind pennant remains at its original post and follows the
same accepted vector/pausable clock. Court ice, spires, wind forces, avalanche,
weather draw budgets and all authority/camera behavior remain unchanged. All
principal masses and the lantern's emission remain on Low; only the existing
quality effects, companion penguins and real light vary by preset.

The fresh inspected [White Cross image](https://www.konami.com/games/bomberman/r/s/img/ss_battle09_l.jpg)
informs pale matte snow versus thick blue ice; [Konami's rounded editor slab](https://www.konami.com/games/bomberman/r2/s/img/castle/original_en_3.jpg)
informs continuous shoulders/undercuts. The grotto is original art, not a
recovered N64 landmark or mechanic. No reference image is distributed.

`glacier-grotto` is the focused native/probe recipe. It records actual packed
world bounds, every surface's winding, the through-vault ray, roof thickness,
icicle/crown contact and ordinary 16:10/16:9 projected bounds. Its native leaf
captures High/Medium/Low, High Night combat, front three-quarter, rear support
and low vault views. The ordinary `blizzard-battle` and `stage-scenery` journeys
remain regressions. These probes support visual review; they cannot approve
the rendered materials or charm.

Source build passes with zero errors/warnings. Headless checks pass 73 grotto,
87 Blizzard and 113 scenery assertions, with zero runtime errors (exit ObjectDB
warnings retained). Python playtest contracts pass32; mesh census matches.
Captured hero bounds are X[-6.8,-.7], Y[-.3,5.05], Z[1.28,8.72]. The unchanged
1280×800 camera places them at normalized X[.047245,.236016],
Y[.163616,.473298]: 60 pixels from the left and 19 below the HUD-safe boundary.
16:9 also passes. The actual roof surfaces measure Y1.7 and3.7; all48 icicle
collar samples are within .0112 units of the captured lining, and the seven
crown bases within .000001 of captured snow. Native shader compilation and
rendered front/rear/below joins remain root's gate.

The first native grotto run passed its geometry checks but its low-vault image
contained only sky/floor. That capture is invalid underside evidence. The close
pose inherited ordinary battle projection offsets, reapplied every frame by
the camera rig. Its replacement uses a temporary zero-offset inspection camera,
requires the actual captured opening rim to occupy a substantial HUD-safe area
and checks an unobstructed captured-mesh mouth ray before saving the image.
The ordinary battle camera and grotto geometry are unchanged by this correction.
The stricter guard first rejected a slightly cropped test pose; it was moved
back without relaxing the bounds. The corrected headless run passes75 checks,
projecting all38 actual opening-rim vertices into X[.138,.822], Y[.288,.754],
with a clear mouth ray. Native recapture is still required to replace the invalid
underside image; the other accepted native images remain unchanged.

### Lantern clearance correction

The valid low-vault image exposed a real left-icicle/lantern-cap intersection:
one generated icicle vertex at(-4.57,.56587,8.175) lay inside the cap's rounded
volume, not only its AABB. The lantern moves deeper fromZ7.95 to7.45 and .15
units toward the wider cavity, X-4.55 to-4.40. Shapes, court and cameras do not
change. A Z-only move also exposed contact with the vault's left lining; that
red witness is retained alongside the first native image.

The final actual packed-mesh probe checks all2,622 lantern body/cap vertices
against snow/lining, all-icicle separation, and ordinary-camera window
sightlines. It finds zero embedded vertices, .29-unit icicle separation and
three of four clear emitting-face samples. Partial visual framing by the cave
is allowed; root judges recognizable light in pixels. Ray exits from shared
triangles are merged by a .00001 distance, avoiding a false odd crossing when
1.0658500 and1.0658501 straddle a quantized rounding midpoint. Genuine clearance
checks remain strict. The corrected headless journey passes78 assertions with
zero runtime errors and a retained exit ObjectDB warning.
