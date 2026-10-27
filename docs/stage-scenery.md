# Stage scenery and ambient life

Source work from 2026-10-26. The user asked for more personality around every map,
citing Greedy Trap's reservoir town as the example of a stage that has
surroundings instead of an empty background. This document records the
references, the composition of each arena and the rules the scenery must follow.

All scenery is original procedural geometry and materials. No original Bomberman
assets, textures or models are used or shipped.

## References

The strongest anchor is Bomberman 64 itself. Its battle arenas sit on Planet Bomber,
whose five worlds give each environment a clear theme. The descriptions below
are what we actually read. They are not frame-accurate reconstructions:

| Source | What it established |
| --- | --- |
| [Bomberman 64 (Wikipedia)](https://en.wikipedia.org/wiki/Bomberman_64_(1997_video_game)) | Green Garden is a "ruin-filled natural world". Blue Resort is "a fortress surrounded by water". White Glacier is a large snowy, icy mountain. Black Fortress is Altair's fortress, which descends into Planet Bomber's atmosphere. |
| [Giant Bomb, Bomberman 64](https://www.giantbomb.com/games/3030-17348/) | (Search summary, page not opened.) Blue Resort is a port town along the water. The battle-stage list and sudden-death types (rising water, closing walls, meteors) match this project's arenas. |
| [Greedy Trap background research](greedy-trap-background-research.md) | The existing Blue Resort-informed reservoir: quays, blue roofs, rounded trees and a slow waterwheel. It sets the tone, scale and "decoration stays outside play" rule for every other arena. |

Search-engine summaries of the Bomberman Wiki battle-stage list and of a
Saturn Bomberman battle-stage page were also read. Those pages were not opened
directly, and the Saturn host did not resolve. They only support the general
series pattern: a readable grid inside a themed setting that decorates play but
never hides it. Super Bomberman R 2 searches only turned up mode and editor
descriptions, with no scenery evidence.

## Composition per arena

| Arena | Theme | Scenery | Animated life |
| --- | --- | --- | --- |
| Sunken Temple | Green Garden lagoon ruins | Two sandy palm islets, a sandstone obelisk with a gold pyramidion, a half-sunken colonnade, a broken gateway with vines, low rocks, distant jungle islands | Swaying palms, a sailboat and rowboat that ride the flood, three gulls |
| Rock Garden | Green Garden meadow | A brook with pebble banks and a timber footbridge, a windmill, ruined columns, four stone lanterns, flower beds, hedges, rounded trees, rolling hills | Windmill sails, swaying canopies, four butterflies |
| Up & Down | Blue Resort coast | A lighthouse on a rocky islet with a jetty, grass-capped sea stacks, channel buoys, a far headland with rooftops | Rotating lighthouse beam, blinking buoys, bobbing boats, gulls |
| Top Rules | Black Fortress sky citadel | A rocky crag hanging under the castle, four corner turrets (rear: crenellated braziers; front: low roofed towers), three heraldic banners with an original bomb emblem, floating islets with watchtowers, cloud banks, a starfield in the dark backdrop | Brazier/islet flames, waving pennants, drifting clouds, bobbing islets |
| Blizzard Battle | White Glacier snowfield | Frosted pine groves, an igloo, a snowman, a frozen pond, ice-crystal clusters, snowdrifts, far glacier peaks | Waddling penguins, existing snowfall |
| Greedy Trap | Existing reservoir town | Adds two rowboats and a sailboat to the reservoir | Boats and gulls |
| Circuit Garden | Training harbor | A palm islet, a jetty with a moored sailboat, channel buoys, a floating target raft that echoes the range markings | Boats, raft, buoys, gulls |

Top Rules keeps its deliberate dark background. The stars, flames and lamps use
unshaded emissive materials with fog disabled, so they read against the void at
every time of day, while the crag and turrets still take the lighting profile.

## Rules

- **Presentation only.** Scenery lives under one `StageScenery` node built by
  `TempleStageView.BuildScenery()`. It contains meshes and a single starfield
  MultiMesh, with no collision bodies. Authority terrain, camera fitting, death
  planes and AI are unchanged; `BattleCameraRig` fits only authority cells.
- **Outside the standing volume.** No scenery mesh may enter any passable cell's
  column from its floor to 2.1 units of standing headroom, including Top Rules'
  lower floor under its bridges. Wall-flush banners may protrude at most 8 cm
  from the wall face, the same scheme as the existing castle side banners. The
  check runs on the built scene and again mid-animation.
- **Tall things go back or sideways.** The ordinary camera looks from the front
  (+Z). Turrets, trees, the lighthouse and peaks sit behind or beside the arena;
  front corners keep only low props, and Top Rules' front turrets stay short.
- **Floating props follow the rendered water.** Boats and buoys read the actual
  water plane, so Sunken Temple and Up & Down floods lift them; islets and
  ruins submerge naturally.
- **Authored for the real layout.** Props are placed around each arena's real
  dimensions. A packet whose size differs from the authored stage, such as the
  asset audit's reduced Up & Down packet, receives no scenery.
- **Menu miniature.** `ArcadeBackdrop` builds its cached miniatures with
  `IncludeScenery=false`, so each setup plinth stays clean and preview
  preparation pays no scenery build cost.
- **Cost.** Small props use a low-poly bead mesh. Shared meshes and materials are
  cached. Depth of field and haze keep scenery soft on Medium/High; on Low the
  same geometry renders without blur.

## Source and verification

[Shared props and animation](../scripts/arena/stages/TempleStageView.Scenery.cs)
have one partial per arena: `SunkenTempleExterior`, `RockGardenExterior`,
`UpAndDownExterior`, `TopRulesExterior`, `BlizzardExterior` and
`CircuitGardenExterior`. The last also holds Greedy Trap's added water life.

The `stage-scenery` journey (in `all`, `core-headless`, `stage-fidelity` and the
focused `scenery-regression` suite) checks every arena. For each one it verifies:

- that the signature props are present;
- that there are no collision bodies;
- finite, nonempty, materialed geometry (asset-audit rules);
- the placement rule, both before and after 24 animated frames;
- that animation actually moves nodes.

It also runs a real timed Sunken Temple flood and checks that the sailboat rises with the
rendered water. It then steps the setup arena selector and checks that every
miniature skips its scenery. Graphical runs capture the ordinary battle camera
at Midday and Night, plus a labelled establishing inspection view per arena.
Images, not numeric checks, decide whether the scenery has charm. Agent review is
not user approval.

## Verification receipt (2026-10-26)

The original receipt below covers the first scenery pass. The
[living-worlds references](living-stage-worlds-research.md) and
[current rendering guide](agents/visuals-and-animation.md#living-stage-worlds-2026-10-27-source-work)
describe the subsequent meadow, orbital castle and glacier additions. Their
evidence and screenshots are in [the living-worlds gallery](living-stage-worlds.md);
the counts below belong to the original pass.

Source branch `t3/stage-visual-personality`, rebased on `main` 450df7e (v0.34.0).

- `scenery-regression` in the isolated QA VM (Metal 4.0 Forward+, Apple
  Paravirtual device) ran 14 journeys: first-use rendering, asset audit,
  graphics settings, stage lighting, setup, menu preview, map-preview loading,
  framing, stage scenery and the five stage journeys. Result: **2,843 assertions,
  242 captures, zero runtime errors or warnings**.
- Host headless: `stage-scenery` (105 assertions) and `core-headless`
  (1,740 assertions). The five stage journeys also passed individually, along
  with the `public` profile.
- Before images come from a matched, never-committed capture-only twin of the
  journey on the pre-change commit (a8a7cd6): same seed, roster, camera and Medium
  preset. [Before/after and establishing/night strips](media/stage-scenery/).
- Not covered: native-host GPU timing and physical play feel. Whether the scenery
  has the intended personality is for the user to judge; agent review of the
  images is not approval. Full `all` was not rerun, because the existing v0.34
  fresh-display-child timeout is unrelated to this change. `display-settings` is
  not in the focused suite.
