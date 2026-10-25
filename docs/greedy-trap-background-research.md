# Greedy Trap exterior/background references

Researched 2026-10-25 for the request to replace empty black surroundings with
an attractive background consistent with Blast Circuit's other dioramas.
Research only; implementation, builds and graphical QA belong to the parent.

**Recommendation:** an original **fortified reservoir courtyard**: retain the
burgundy arena walls and teal inner tank, ground the enclosure on a broad
masonry foundation, surround it with quiet blue-green water, and suggest a
small waterside town beyond. This is a creative adaptation informed by Blue
Resort, **not an original Greedy Trap landscape recovered from footage**.

## Primary imagery actually inspected

Video times below are source-player times, not HUD/game time. Frames were
extracted and visually inspected; this was not a controlled original-game run.
Footage records original-game imagery, but capture/emulation fidelity is
unverified. No secondary wiki supplied the visual conclusions.

| Source | Exact inspected views | Observations |
| --- | --- | --- |
| [Super Nintendan, Greedy Trap](https://www.youtube.com/watch?v=fUvsHrsS70c&t=5s) | **0:05**, existing `references/fUvsHrsS70c/0005.00.png` | Burgundy enclosure, warm cobbled court, gold rim and cyan/teal tank. Black surrounds the rendered board; no exterior terrain is established. |
| [ShadowIori, All Stages Sudden Death](https://www.youtube.com/watch?v=PoTv3FoqwJ4&t=146s) | **2:26**, existing `references/sudden-146.png` | Dark space outside the brick walls; no visible town, moat or horizon to reconstruct. |
| [LongplayArchive, Bomberman 64 (US/EU) HD](https://www.youtube.com/watch?v=2esnoiMgiQ8&t=1335s) | Blue Resort Stage 1: **22:15, 22:25, 22:45** | Warm retaining masonry above blue water; pale paving and stairs; green rails and round trees on small lawn insets. |
| [Same longplay, Blue Resort Stage 3](https://www.youtube.com/watch?v=2esnoiMgiQ8&t=1800s) | **30:00, 30:10, 30:20, 30:40** | Tall masonry quays ground the town in water. At 30:40 a blue pointed roof caps a small corner tower beside green railing. |
| [Same longplay, Black Fortress Stage 1](https://www.youtube.com/watch?v=2esnoiMgiQ8&t=4420s) | **1:13:40, 1:13:50** | Industrial metal lanes, yellow arrows/lights and translucent cyan barriers. These samples are a weaker material match than Blue Resort. |
| [Nintendo English instruction booklet](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf#page=9) | PDF **page 9**, printed **14–15**, rendered locally | Official Blue Resort illustration: masonry town, water channels, bridges, towers, pale roofs and rounded trees. Text identifies water throughout the town and water-gate switches. Illustration is concept/publisher art, not a gameplay-camera screenshot. |
| [Baku Bomberman Official Guide Book scan](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip) | `guide-13.JPG`, printed **26–27**; `guide-14.JPG`, **28–29**; `guide-15.JPG`, **30–31**; existing `guide-50.JPG`, **100–101** | Blue Resort images show compact towers, brick quays, blue/teal roofs and connected waterways. Greedy Trap's normal, oblique and closing-wall views show enclosure/dark surroundings; they do not supply an exterior landscape. |

The guide is the contemporary official Shogakukan/Hudson-supervised publication
identified in [existing Greedy Trap research](greedy-trap-research.md), hosted
by a fan archive. Its page images were inspected directly. Also sampled
longplay **23:05**, which is black/transition and establishes no scenery fact.

## Proposed composition — creative adaptation

- **Foreground/base:** a thick, slightly wider burgundy masonry plinth with
  cream coping visibly meets the water. Give the arena weight and a complete
  miniature silhouette rather than a floating floor above a dark void.
- **Middle distance:** a restrained moat/reservoir band around the plinth,
  with a few pale banks or retaining ledges. Keep this water visually quieter
  than the playable tank. Neither the moat nor its location is original-stage
  evidence.
- **Far/top edge:** a low, offset quay, one short causeway and two small tower
  or roof silhouettes. Use Blue Resort's scale relationships and masonry/water
  vocabulary; author a new arrangement. An isolated round tree or two can soften
  the far bank without making Greedy Trap another garden map.
- **Backdrop:** blue-green atmospheric fill and soft distant shapes wherever
  the battle camera exposes gaps. Lower contrast, haze and the project's
  existing diorama depth treatment keep attention on the court. Geometry and
  flat color must still read well on Low, where blur is absent.

Retain the sunken-court silhouette and clear sightlines to its stairs. Background
towers sit beyond the enclosure rather than obscuring its rear wall or HUD.
Decorative causeways do not imply new playable exits. Moving side walls must
remain visually independent of the static exterior; do not attach scenery to
their moving faces. Check normal battle, closing-wall and menu-miniature views
after implementation. All scenery is presentation only.

## Proposed material palette

These are authored starting colors, not sampled original texture values.

| Material | Suggested color | Treatment |
| --- | --- | --- |
| Arena/plinth brick | Burgundy `#713D46` | Rounded masonry, restrained seams; exterior slightly quieter than arena |
| Coping, quays, distant towers | Warm cream `#D8C9A8` | Soft rough stone, broad readable blocks |
| Narrow trim | Muted gold `#B38D4E` | Preserve the inner basin rim as the strongest gold detail |
| Exterior water | Dusty teal `#4C8990` | Gentle broad ripples and restrained reflections; time-of-day responsive |
| Roof/rail accents | Slate teal `#487477` | Small shapes connect architecture to the tank without equal saturation |
| Sparse vegetation / haze | Sage `#71826A` / blue-gray `#A5BEC2` | Low contrast toward the distance |

Borrow Blue Resort's waterside masonry, not Black Fortress's industrial arrows
and glowing panels. Keep Greedy Trap's court/tank identity dominant; the town
is atmosphere. Use Blast Circuit's original procedural geometry and materials.

## Retained evidence and limits

New downloads, video sections, inspected frame PNGs, two Blue Resort contact
sheets and manual/guide renders are under ignored
`artifacts/greedy-trap/background-references/`. Clips start at source **22:12**,
**29:56** and **1:13:35**; frame filenames carry absolute source minute/second.
Original Greedy Trap evidence remains untouched under `references/`.
Copyrighted reference imagery is research-only and must stay out of Git/exports.

No source, simulation, build, installation or graphical desktop was changed.
This research does not establish original exterior geometry beyond the visible
walls, nor prove the proposed composition works in Blast Circuit's camera;
that requires the parent's actual visual review.
