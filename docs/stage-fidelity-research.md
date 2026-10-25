# Original battle-stage fidelity review

2026-10-24. Visual comparison for integrating the Top Rules, Up & Down and Rock
Garden PRs. This records the incoming PR captures and research evidence, not a
claim that later integration fixes or the installed build have been verified.

## Evidence and provenance

- **Publisher manual:** directly inspected the original English booklet's
  printed pp. 24–25, including all three stage images.
  [Nintendo-hosted PDF](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf#page=14).
- **Original official guidebook:** downloaded the scan archive and directly
  inspected `guide-49.JPG` (printed pp. 98–99) and `guide-51.JPG` (pp. 102–103).
  These contain ordinary and oblique original-game views, plus hazard images.
  The scans are primary publisher material hosted by a third party.
  [Baku Bomberman Official Guide Book archive](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).
- **Original-game stills:** inspected the 608×456 HUD-2:00 images of
  [Top Rules](https://static.wikia.nocookie.net/bomberman/images/d/dc/TopRules.png)
  and [Rock Garden](https://static.wikia.nocookie.net/bomberman/images/2/2d/RockGarden.png).
  They agree with the publisher imagery. Their hosting is unofficial and their
  recording/emulator settings are unknown; the pictured software is the evidence.
- **Recorded original gameplay:** downloaded bounded clips and inspected frames
  from [ShadowIori's All Stages Sudden Death](https://www.youtube.com/watch?v=PoTv3FoqwJ4)
  at 0:10, 0:33, 0:50 and 1:15, and
  [TurflamicGaming420's Top Rules](https://www.youtube.com/watch?v=RMV6wCZT63c)
  at approximately 0:14, 0:20 and 0:30. These corroborate Rock Garden's falling
  fireballs, Up & Down's lower-deck inundation and Top Rules' advancing tall side
  walls. Sampled frames do not recover motion constants or collision algorithms.
- **Incoming rebuilds:** inspected PR captures at Top Rules `7366e28`,
  Up & Down `ebda447`, and Rock Garden `3e7e046`, including their renderer and
  layout source where needed. These are Blast Circuit images, never original
  game evidence.

Reference downloads, scans, captured footage and comparison copies stay in the
ignored `artifacts/stage-release/references/`, with a local provenance README.
No reference models, textures, scans or recordings are game assets or distributable
repository content.

## Actual model sources found

The [Models Resource game index](https://models.spriters-resource.com/nintendo_64/bomberman64/)
lists all ten battle maps. Relevant entries are
[Rock Garden](https://models.spriters-resource.com/nintendo_64/bomberman64/asset/328930/),
[Top Rules](https://models.spriters-resource.com/nintendo_64/bomberman64/asset/328934/),
and [Up and Down](https://models.spriters-resource.com/nintendo_64/bomberman64/asset/328931/).
The accessible Up and Down record credits Peardian, lists `map.obj`, `map.dae`,
materials and textures, and contains the ripper's statement that Lemmy's video
plugin was used. This is an extraction provenance claim, not Nintendo's source
model release or proof of complete collision geometry.

Direct asset downloads returned HTTP 403, so **no mesh coordinates or model
previews were measured** in this review. A distinct **Up and Down (Complete)**
entry appears under **Custom / Edited** in the site's
[March 2022 update](https://models.spriters-resource.com/update-1108/); it must not
be confused with an untouched original rip. No original model was imported into
the game. Publisher imagery remains the strongest inspected reference here.

## Comparison and actionable differences

| Stage | Composition preserved in incoming PR | Differences visible against original imagery |
| --- | --- | --- |
| Top Rules | Three parallel upper lanes, one crosswalk, pale junctions, four green stair flights and playable red lower court | Original has tall enclosing side walls with large red/gold banners and dark recesses, charcoal/brown upper panels and a deep red patterned floor. Incoming capture reads as a low freestanding castle shell against cyan sky; its panels are light gray and floor much pinker. Prioritize enclosure, banner scale, dark upper surfaces and red-floor contrast. |
| Up & Down | Suspended high ring, staggered pale links, green crown, two middle piers, central low deck and six purple flights | Original guide's low-angle view shows rounded/fluted support columns beneath pale caps. Incoming foundations read as long square masonry towers. Original flights also have strong purple/white separation; incoming lavender panels are subdued. Improve the support silhouette and tread/edge contrast while retaining verified walkable surfaces. |
| Rock Garden | Enclosed lawn, five columns, taller center, small stepped paving diamond and scattered boulders | Incoming columns and rocks are uniformly pale with weak surface variation; originals have beige tops, vertical cracks and mottled darker sides. The center is also disproportionately tall: source makes it 1.9 units against .9 outer pillars, a 2.11× ratio. Comparable-depth original screenshot silhouettes suggest approximately 1.5×, an image estimate rather than recovered world dimensions. |

Sources for the comparisons are the original screenshots and guide spreads
listed above. Color judgments are qualitative: lighting, capture processing and
emulation prevent treating sampled RGB values as original material constants.
Likewise, screenshot proportions do not establish world-unit dimensions.

For Rock Garden, any center-height correction must update its shared support
height as well as its visible mesh; shortening only the cylinder would create
invisible collision. For Up & Down, decorative round supports may improve the
silhouette inside the existing solid pier footprint, but changing the traversable
space needs separate collision verification. Keep the broad caps and confirmed
underpasses readable in the ordinary battle camera.

## Newly recovered Top Rules mechanics evidence

Guide p. 102 explicitly discusses placing a pumped bomb **beneath a walkway**
so its flames reach the upper level, and stacking ordinary bombs to reach it.
Its pictured lower-floor blast accompanies this explanation. This is stronger
evidence than the previously available wiki description: an unconditional slab
shield between lower and upper actors is a gameplay adaptation, not established
original behavior. Exact radius, penetration algorithm and vertical thresholds
are still unknown. [Official guide scan, p. 102](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip).

## Release comparison checklist

Use the ordinary battle camera at a matched stage/lighting setup, plus a labeled
oblique inspection where surfaces overlap. Confirm silhouette and route
readability before tuning decoration. Retain four original-relative start
regions while labeling eight-player expansion as project tuning. Check Top
Rules wall compression, Up & Down low-to-high flooding and Rock Garden falling
fireballs against the stage-specific research; matching a static model cannot
establish hazard behavior. Verify all changes again in the rebuilt export.

Earlier stage investigations provide the detailed route and timing witnesses:
[Top Rules](top-rules-research.md), [Up & Down](up-and-down-research.md),
and [Rock Garden](rock-garden-research.md).
