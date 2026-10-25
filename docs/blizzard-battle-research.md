# Blizzard Battle reference research

Investigated 2026-10-25 for an original Blast Circuit recreation of the **1997
Nintendo 64** battle arena. This document separates inspected game imagery,
official guidebook text, secondary descriptions and proposed project tuning.
It does not certify an implementation or recover undocumented physics.

## Sources and inspection record

- **S — user-supplied game screenshot**, 608×456, attachment
  `ba77f61a-1dcd-4e29-8f65-58b1908f3ed0-2c0c26e0-9efd-4f5e-90d3-2c8183f81d68.png`.
  Visually inspected directly: opening HUD reads 2:00; four players occupy the
  corners. The wiki also links an [actual-game Blizzard Battle image](https://static.wikia.nocookie.net/bomberman/images/5/57/BlizzardBattle.png/revision/latest?cb=20120616183448).
  The supplied attachment owns the observations below; no pixel-identity claim
  is made about the public image.
- **V — blizzz, _Bomberman 64 Battle Mode All Stages_**, uploaded 2015-08-28:
  [original recording](https://www.youtube.com/watch?v=88xIYg29HlA).
  Its description identifies N64/Framemeister capture; checked live and against
  `artifacts/blizzard/references/footage-info.json`. Inspected decoded
  browser video frames at **7:24, 7:25, 7:27, 7:30, 7:55, 8:00, 8:05, 8:10,
  8:15 and 8:20**. Frame samples only; no continuous viewing.
- **V2 — ShadowIori, _All Stages Sudden Death_**:
  [original-software recording](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=326s).
  Additionally inspected decoded frames across **5:26–6:32**, sampled at six-second
  intervals. These show Blizzard Battle starting at 1:00, then large blue glacier
  faces advancing inward from both sides as the visible court narrows. Capture/
  emulation settings and inputs were not independently established. The downloaded
  reference and contact sheet remain ignored under `artifacts/blizzard/references/`.
- **N — N64 Database, _Bomberman 64 Multiplayer Battle (4 Players)_**:
  [recording](https://www.youtube.com/watch?v=7a1gN_xD-gs).
  Read its live title, author and description, which identifies original N64
  capture. Inspected decoded frames at **2:00, 4:00, 6:00, 8:00, 10:00, 12:00,
  14:00 and 16:00**: the first five show Rock Garden, the last three Up & Down.
  No Blizzard passage was located in those samples. This is a reference lead,
  not independent Blizzard evidence or a claim about every unsampled moment.
- **G — Shogakukan, _Baku Bomberman Official Guide Book_**, printed **pp. 106
  and 110**, primary published material. Followed the wiki's citation to
  [Ragey's scan-hosting page](https://www.randomhoohaas.net/bomb/book/baku1/) and
  downloaded its [scan archive](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).
  Read the Japanese page images:
  `guide-53.JPG` contains printed pp. 106–107; `guide-55.JPG` contains 110–111.
  Paraphrases below are this investigation's translation. Ignored reference:
  `artifacts/blizzard/references/bakuguide.zip`, SHA-256
  `e2c31cd2c669a57b08298406b7b7858f97fb3840353fa34e12b3f0e691ccc38d`.
- **W — [Bomberman Wiki: Blizzard Battle](https://bomberman.fandom.com/wiki/Blizzard_Battle)**,
  read its description and citation to G pp. 106, 110. Its prose remains
  secondary; citing an official book does not make every sentence a verified
  quotation from that book.
- **A — [The Models Resource: Blizzard Battle](https://models.spriters-resource.com/nintendo_64/bomberman64/asset/328938/)**,
  Peardian's archive listing, submitted 2022-01-08. Read the host's file manifest
  through web retrieval. It lists separate `map`, `block` and `avalanche`
  OBJ/DAE/MTL files, numbered PNGs, `sky.png` and `snow.png`. The interactive
  browser page stopped at a Cloudflare verification screen. **No archive,
  geometry, texture pixels or animation was inspected.** The manifest proves
  only what the host lists, not original collision rules or a reuse license.

## What the inspected evidence establishes

**Composition and cover.** S shows a single uninterrupted blue court, tall pale
ice walls on the left/right, low bright rear/front rims and a blue background.
There are **16 distinct pointed ice cones**, with violet/blue faceted faces,
distributed irregularly through the interior. No stairs, raised central deck,
permanent pillar maze, open sea or transparent floor is visible. Corner starts
have clear immediate space. These are image observations, not recovered world
dimensions or proof that every apparently flat region has identical collision.
[S](#sources-and-inspection-record).

**Opening layouts differ.** V's 7:25 cone arrangement differs from S. S's screen
coordinates are not a universal stencil; neither image establishes the placement
algorithm or seed policy.
[S](#sources-and-inspection-record),
[V, 7:25](https://www.youtube.com/watch?v=88xIYg29HlA&t=445s).

**Official wind guidance.** G p. 106 describes changing wind beyond left/right,
deflected thrown bombs and weaker player drift. It advises moving upwind:
against-wind throws lose reach; a strong downwind throw can span the field.
Wind should affect flight, with different player/bomb responses. No numeric
force, interval or formula is given.
[G, printed p. 106](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

**Late-round shape.** G p. 106's 0:31 inset shows a narrower court between side
walls, consistent with W's avalanche report; wall speed/trigger/collision remain
unknown. V2 independently shows both glacier faces narrowing the court during
sudden death. [V2, 5:26–6:32](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=326s).
[G, printed p. 106](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

**Identity.** G identifies stage 9; p. 110 requires 120 cards and worlds 1–6
cleared for its permanent unlock. Blast Circuit need not reproduce progression.
[G, printed pp. 106, 110](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

## Usable footage timings

All links below are video timestamps, not simulation seconds. These entries
describe sampled frames only.

| Recording position | Inspected content | Useful for |
| --- | --- | --- |
| [V 7:25](https://www.youtube.com/watch?v=88xIYg29HlA&t=445s) | READY, cones, corner starts | Opening layout |
| [V 7:27–7:30](https://www.youtube.com/watch?v=88xIYg29HlA&t=447s) | Pale floor, purple cones, snow/bombs | Palette and framing |
| [V 7:55](https://www.youtube.com/watch?v=88xIYg29HlA&t=475s) | Most cones gone | Cleared court |
| [V 8:00–8:10](https://www.youtube.com/watch?v=88xIYg29HlA&t=480s) | Sparse cover, explosions, elimination marks | Effects readability |
| [V 8:15](https://www.youtube.com/watch?v=88xIYg29HlA&t=495s) | Next arena | Blizzard passage ends earlier |

Inputs are unknown; these samples cannot isolate drift, friction, airborne
force or throw range. V2 adds a sampled avalanche sequence; it does not establish per-tick collision,
precise wind strength or original animation curves.

## Secondary reports and remaining unknowns

W reports **four diagonal wind directions or calm**, periodic changes, wind
affecting players/bombs and airborne objects, **16 initial soft blocks**, and
**avalanches advancing from left and right during sudden death**. The cone count
is independently visible in S; G independently supports changing wind and
thrown-bomb deflection. The full five-state wind enumeration, airborne-player
coverage remain **not independently established** by the inspected primary
material. V2 corroborates the inward avalanche motion during sudden death,
while exact collision timing and speeds remain unknown. Preserve those
distinctions when implementing.
[W](https://bomberman.fandom.com/wiki/Blizzard_Battle).

Undocumented tuning includes arena units/aspect ratio, cone footprint/height and
placement rules, wind selection probabilities/duration, acceleration versus
constant drift, separate ground/air response, carried-bomb handling, friction,
wall restitution, avalanche warning/delay/speed and elimination boundaries.
An icy appearance does not establish an additional slippery-movement mechanic.
Extra starts for five to eight players are a Blast Circuit adaptation.

## Original procedural art direction for this repo

The following is a **proposed original implementation approach**, informed by S
and V and aligned with the current
[diorama/painterly rendering guide](agents/visuals-and-animation.md#diorama-polish-2026-10-25).
It is not a description of original-game shader technology.

- Build a compact snow-and-ice diorama: a solid court/plinth, thick icy side
  cliffs, softer snow caps and low front/rear lips. Keep the playable surface
  legible from the ordinary battle camera and leave HUD-safe space.
- Use an **opaque ice floor on every preset**: pale cyan/blue paint variation,
  broad frosty patches, restrained specular response and subtle authored cracks.
  Preserve grounded character/bomb contact and visible aiming markers. The
  water/refraction shader is not an appropriate floor shortcut.
- Generate the cones from original low-sided tapered meshes, with small authored
  silhouette variation and broad blue/violet face shading. Their visual bases
  must agree with authority cover footprints. Keep the clear corner approaches
  and open interior routes rather than adding an unrelated pillar grid.
- Give snow caps matte, broad painterly variation; reserve sharper highlights
  for exposed ice. Retain the existing lacquered characters and restrained
  lighting. Keep blur/haze outside the playable area as the guide requires.
- Author snow particles and, if adopted, avalanche banks procedurally. Convey
  wind direction without obscuring bombs or reticles; render banks against the
  accepted shrinking boundary. Snow motion must visualize accepted wind state.

Do not import, trace texture pixels, redistribute or ship the original game's
models, textures, sounds, scan pages or video frames. A is useful as a future
inspection lead for separating map/cover/avalanche forms, **not an asset source
for the product**. All shipped geometry and materials should be newly authored.

## Gameplay boundary and future evidence

Follow [Gameplay and AI](agents/gameplay-and-ai.md): terrain, cover, wind effects
and avalanche elimination belong in `Bomber.Game`; session/snapshot transport
belongs in `Bomber.Client`; Godot presents accepted state. A visual displacement
cannot stand in for authoritative force. Flight and aim projection need the
same wind assumptions, and local/online sessions must share the rules.

Before calling a later recreation verified, retain ordinary-camera opening and
cleared-cover captures, calm/diagonal wind witnesses, grounded and airborne
player/bomb behavior, throws in both wind directions, and inward-bank collision
with a surviving central corridor. Compare four-player reference composition
separately from eight-player adaptation. These are proposed future checks;
**no build, gameplay change or playtest was performed for this research**.

Only this Markdown document is the research deliverable. Downloaded book scans
remain under ignored `artifacts/blizzard/references/`; browser frame samples
were inspected in tool results. Other agents' concurrent source edits are
outside this investigation's scope.
