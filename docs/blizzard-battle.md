# Blizzard Battle

Blizzard Battle adds an enclosed, flat frozen court to local battle, practice,
server rooms and player-hosted rooms. The [reference investigation](blizzard-battle-research.md)
records the screenshot, original hardware footage and official guidebook pages.
All shipped geometry, textures and animation are original procedural work.

## Layout and adaptations

The 19×19 layout has seventeen playable cells across, tall glacier side walls,
a lower rear rim and a low near rim that preserves the ordinary battle view.
Sixteen pointed violet ice cones follow the supplied screenshot's irregular
composition. Opening footage shows a different arrangement: this composition
is an authored adaptation, not a recovered original placement algorithm.
After the user requested larger spires, their tops rise **1.755 render units**
(1.3 authority units), just over twice the initial height. Their broad bases
span .94 units. Stage `coverHeight` drives both the mesh and projectile
obstruction/landing; tall cover also extends blast shielding.
The first four seats start at the corners; four additional seats start on the
perimeter. Spawn escape cells remain clear. Advanced block percentages apply to
the sixteen-cone budget; practice has no cover. Bomb destruction and loot use
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
carrier. Wind does not change facing, vertical gravity, stored throw momentum,
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
Snow never changes collision. Avalanches swell into their accepted columns,
then settle into packed banks. The menu miniature uses the same terrain and ice
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
