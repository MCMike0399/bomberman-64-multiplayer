# Sea Sick

Source work, 2026-11-15. Sea Sick adds a freely selectable local, Practice,
server-room and player-hosted battle arena. This source change requires a
coordinated client/server release before public delivery.

## Reference and original adaptation

The Hudson-supervised **Baku Bomberman Official Guide Book**, printed p. 105,
shows the pirate deck, tall center mast, two shorter forward posts, plank floor,
barrel cover, green gunwales, rigging and outside stairs. Its description says
the ship rocks and recommends ordinary combat around the mast/posts; it does
not establish gravity-driven drift, ice, knockdowns or a water current.
[Publisher scan archive](https://www.randomhoohaas.net/bomb/book/baku1/),
[guide ZIP](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

The page was inspected directly, together with frames from approximately
6:39–7:09 of [Bomberman 64 Battle Mode All Stages](https://www.youtube.com/watch?v=88xIYg29HlA&t=399s).
The footage corroborates the three-post composition, barrel field and changing
ship angle. The guide's 0:58 inset shows a falling fireball. These observations
do not recover original dimensions, timings, forces or random-cover constants.
Reference scans and footage remain outside the repository and game assets.

Blast Circuit's original adaptation uses a 19×19 deck, eight supported starts,
48 randomly sampled barrels in four regions, two raised side galleries and
four two-wide stair approaches. The first four starts retain the deck-corner
composition; four extra starts use the galleries. These dimensions, galleries,
cover population and extra seats are project tuning. Clear cross-deck routes,
stair approaches and spawn exits remain open. The shared advanced block amount
scales the barrel budget, and Practice clears it.

## Authority and presentation

`BlastStages.SeaSick` owns the dry deck, fractional stair support, three hard
posts and enclosing low gunwales. Ordinary movement, carrying, kicking,
projectiles, blasts and bot navigation use the same shared rules locally and
online. The central mast is genuinely taller than the two forward posts.
There is no ship current, slide rule, changed gravity or authoritative rotation.

Falling fireballs reuse the existing Rock Garden Hurry system; closing columns
and flooding stay off. Impact height, rendered warning height and CPU danger
use the target's actual supporting surface, including raised galleries. The
same fixed warning/impact semantics remain on Rock Garden's flat lawn.

`ArenaView.StageAssembly` contains the ship terrain, players, bombs, cover,
pickups, explosions, falling fireballs and throw guides. Sea Sick gently rotates
that single parent around the deck center: at most about 0.60° roll and 0.20°
pitch, with approximately 11.4/14.6-second periods. Sea, seabed and exterior
boats stay in fixed world space. Camera and HUD stay still. This restrained
motion is an original comfort/readability adaptation, not recovered timing.
Other arenas keep an identity parent transform.

Pointer rays are converted into deck-local coordinates before terrain picking;
forward screen projection and screen-space throw endpoints convert back through
the parent. Accepted state and aim trajectories stay in authority coordinates.
Local pause freezes the motion clock and parent transform. Ocean rendering
retains the existing Low/Medium/High policy. Low hides the escort boats, gulls,
buoys and distant harbor islet; the playable ship remains. Menu miniatures omit
the exterior ocean/life and retain the original ship geometry.

The **Tubby Cutter** shape study (2026-11-15) runs the ship left to right:
left sterncastle and right forecastle reuse the existing raised galleries.
A continuous loft of curved U-shaped sections gives the body a broad belly,
with its rail at the hull top and at most a .15-cell outboard ledge. The wale
flares about .30 cell below it. The right bow is a rising cambered whaleback
with converging rails and a 2.2-cell bowsprit raked 20°; the left has a broad
transom, curved taffrail and rudder. Raised ends exaggerate the sheer while
the waist stays low. All masses remain outside or below the unchanged court.
The mast is round and tapered, and the two forward obstacles read as a capstan
and bitts within their existing authority footprints. Long planks follow X with
staggered butt joints. Previous small cabin/porthole dressing is excluded while
the sculptural body is reviewed.

`sea-sick-shape-study` is an explicit grayscale material inspection of an actual
ordinary local battle with cover disabled through advanced settings. It retains
Low/High battle views, both observed roll extrema on High, a wide oblique view
and a lower profile view. It keeps the actual desaturated ocean as a fixed
water reference. Sea Sick's battle fit reserves roughly 78% of frame width for
the playable court and lifts it so the near wale clears the hint row. Other
stages keep their framing. Far shrouds and high forestay/backstay cast no
shadows; near shrouds and stub yards are omitted. The upper mast extension is
cosmetic; its authority obstacle height remains unchanged. Material inspection is off
in ordinary play; it never changes accepted state. The study
must pass native grayscale/320px silhouette review before surface and ambient
life refinement. Numeric winding/extent checks do not approve the massing.


## Verification and review recipe

Managed `--sea-sick` covers topology, fully connected galleries, ordinary stair
walking, gunwale contact, no spontaneous player/bomb drift, advanced barrel
budgets, mast shielding, fireball escape and exact room-setting validation.
Peer-frame checks accept the real layout and reject a missing authored mast
height. The standard AI soak includes Sea Sick; `--soak --stage=sea-sick`
provides a focused matrix.

The `sea-sick` journey (`tour-24`, `all`, `core-headless`) uses ordinary inputs
for setup selection, four-player opening, both gallery stair routes, gunwale
contact, real barrel destruction/loot/collection, eight-player CPU battle and
fireballs. It checks fixed ocean vs moving ship, idle authority invariance,
raised/lower pointer roundtrips, actual landing-marker/reticle alignment and
local pause. Graphical runs retain Low/Medium/High, Night, 16:10/16:9 and a
labelled oblique inspection capture. `sea-sick-network` observes the stage,
a host's gallery ascent and a supported bomb from a second client in actual
server-backed and player-hosted rooms.

```sh
dotnet run --project tests/Bomber.Game.Tests -- --sea-sick
dotnet run --project tests/Bomber.Client.Tests
dotnet run --project tests/Bomber.Ai.Tests -- --soak --stage=sea-sick --ticks=1800
python3 tools/playtest/run.py --scenario sea-sick-regression --build --start-server
python3 tools/qa/run.py --profile public
```

Root owns graphical/export review. Source headless checks establish behavior
and geometry, not rendered appearance or user art approval. Actual ordinary
battle captures and motion must be inspected before approving this stage.

Non-graphical source verification completed on 2026-11-15: the full game suite
passed 4,050 checks, the client suite passed 495, and the focused 12-case
60-second AI soak passed (longest eligible idle with a safe exit: 1.53 seconds).
The ship journey passed 131 assertions, real server/peer journey 43, and scenery
journey 133, with zero runtime errors. Verbose source-headless exits retained
only audio stream/playback warnings; they do not certify a clean exported
shutdown. The public profile, source layout, producer inventory, documentation
links and whitespace checks passed. Graphical/export evidence remains pending.
