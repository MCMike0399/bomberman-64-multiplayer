# Lost at Sea

Lost at Sea is a flat battle arena with slippery violet observation glass, a pale
nonslip perimeter, four coral-orange towers and breakable shipping crates. Local,
dedicated-server and player-hosted sessions run the same authority. Original
procedural marine scenery includes fish swimming beneath the glass, coral shelves,
swaying kelp and a small research bathysphere. Reference media never ships.

## Inspected source and explicit adaptations

The primary source inspected on 2026-11-15 is printed page 107, right page of
`guide-53.JPG`, in *Baku Bomberman Official Guidebook*, Shogakukan with Hudson
supervision/cooperation, ISBN 4-09-102598-6. The scanned fifth printing is dated
1998-03-10; the first printing is dated 1997-11-10. The
[reference archive](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip)
has SHA-256
`e2c31cd2c669a57b08298406b7b7858f97fb3840353fa34e12b3f0e691ccc38d`.

The guide names slippery glass and depicts its purple-blue floor with diagonal
highlights, silver riveted rim, four tall orange towers in a diamond, central
X-braced crates and four corner starts. It describes slow acceleration/direction
changes, recommends anticipating turns, says kicked bombs also slide, recommends
throwing, and identifies the outer rim as nonslip. The 0:58 inset shows meteor
Hurry. The [all-stage recording around 8:13–8:42](https://www.youtube.com/watch?v=88xIYg29HlA&t=493s)
is supplementary footage for the composition.

The 21×21 footprint, two-cell-wide rim, eight starts, tower positions/heights,
32 seeded crates and all acceleration/braking constants are explicit Blast
Circuit tuning for eight players. They are not recovered original constants.
The marine outpost, fish geometry and surface artwork are original interpretations.
The arena retains a continuous flat floor and four isolated towers, with no
invented permanent pillar grid, raised combat level, flooding or closing walls.

## Authority contract

`BlastStages.LostAtSea` owns the closed court, terrain, supported rim starts and
four regional cargo pools. Block density scales the budget; seeds reproduce it;
Practice clears it. The rim and protected approach/cross lanes remain clear.

`SlipperyGlassRules` applies ground inertia only inside this stage's glass region.
Ordinary rim movement retains direct traction. The same movement function handles
humans, CPUs and carriers. Release coasts to a bounded stop; countersteering changes
velocity gradually. Solid contact removes momentum on the blocked axis. Planted
aim and kick anticipation stop movement, and input/reset/recovery/respawn paths
clear inertial state. Ghosts use their ordinary movement. Standing on a crate
uses its grippy wooden support. Other arenas retain their original movement
arithmetic and physics.

Only kicked rolling bombs receive stage-specific drag: mild braking on glass and
strong braking on the rim. Resting placements and carried bombs stay stable;
ordinary throws land normally. Collision, finite fuses, remote ownership, bounce,
Stop and blast shielding retain the shared authority paths. CPU steering brakes
before turns and compensates its own observed lateral momentum; timed searches
budget extra traversal time. Every difficulty uses the same physical traction.

Hurry uses the existing seeded Rock Garden meteor schedule. There is no random
player drift, changing wind or render-only movement. Complete snapshots already
carry accepted player/bomb positions; the new internal velocity does not add a
wire field. Exact-release admission prevents an old published client from
receiving an unsupported stage after a coordinated release.

## Presentation and verification

The violet glass is translucent and writes depth, exposing actual marine scenery
beneath the playable plane without refraction or a new water hazard. A pausable
CPU clock drives the glass finish and surrounding marine life. The ivory rubber
treads and rivets show the grippy route. Every preset retains the same floor,
rim, tower and cover geometry; Low hides decorative marine life. Menu miniatures
omit scenery and the deep ocean bed.

Focused `Bomber.Game.Tests -- --lost-at-sea` checks terrain, eight rim seats,
ordinary-input coasting/countersteering, immediate rim stops, unchanged ordinary
arenas, planted carried aim, finite settling/reset, real kicked-bomb glass/rim
contrast with unchanged fuses, seeded scalable cargo and meteor casualties.
The `lost-at-sea` journey adds actual menu/scene construction, presets/lighting,
moving fish, rim/glass input comparison, a real kicked-bomb slide, eight-player CPU
combat and meteor impact. `lost-at-sea-network` drives real dedicated and peer
rooms, with a second client witnessing the stage and post-release inertia.
Both are in `tour-25`, included in `all`; the local case is also in core headless
and stage fidelity. Headless assertions cannot approve the glass appearance,
under-floor fish visibility, gameplay readability or art quality.

Source verification on 2026-11-15: all 4,052 game and 494 client checks passed;
the eight focused authority checks and twelve 2,700-tick CPU soaks passed (longest
eligible open idle 1.33 s). The local headless journey passed 32 assertions and
the real dedicated/peer journey passed 41, with zero runtime errors. Public QA,
source layout, mesh inventory and documentation checks passed. The first local
journey retained a failing tower-identity assertion: Godot renamed duplicate
scene names; the four towers now have unique stable names. Source headless exit
logs retain five local/two network ObjectDB cleanup warnings. Exported/native
classification, actual glass/fish/contrast image review and release delivery
remain separate; no user approval of this art is claimed.

The independent art-direction review rejected the initial presentation and calls
for a Glass Buoy shape study before decorative finishing; art acceptance remains
withdrawn. Native review also exposed a technical defect: both violet floor
triangles were back-facing, so default culling removed the intended material
entirely. The correction reverses their emitted order and keeps culling enabled.
An actual-scene mesh audit was retained red (two glass faces, alignment -1), then
green (36 meshes / 282 triangles, minimum alignment 0.9999999). It also checks
that floor faces point upward independently of supplied normals. The original
native missing-glass capture and source-derived red/green checks remain in the
private evidence directory. The corrected headless journey passes 33 assertions;
corrected native material appearance and the full art brief still require review.
