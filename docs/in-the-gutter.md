# In the Gutter

Source contract, 2026-11-15. An original Blast Circuit toy waterworks arena
inspired by Bomberman 64 (1997), battle stage 7. No original assets are included.

## References and adaptations

Directly inspected the Hudson-supervised Shogakukan *Baku Bomberman Official
Guide Book*, printed p.104, from the
[scan archive](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip),
and sampled original-game footage around
[6:00](https://www.youtube.com/watch?v=88xIYg29HlA&t=360s).
The guide shows four tall square starting posts, a shorter center marker post,
shallow water, masonry cover, red edging and side grates. It describes changing
horizontal flow carrying players and bombs, difficulty moving upstream, and
using posts to arrest drift. Its 0:18 inset shows the compressed side grates.

The 21×21 dimensions, enlarged two-cell post caps, eight seats, 48 regional
cover blocks, safe open corridors, eight-second flow cycle, eased reversals,
calm state and current speeds are project tuning. The original evidence does
not recover any exact world dimension, force, interval, seed algorithm or
initial-block placement algorithm. The canal village, sluice wheels, shrubs
and brass arrow are independently authored scenery.

## Authority and transport

BlastStages.InTheGutter builds a safe tier-zero floor. Its blue appearance is
shallow water: it never uses lethal CellKind.Water. Four 2×2 tier-two caps
hold the first four starts. Additional starts occupy clear floor routes. A
short center pillar holds the current marker. Descending from a cap uses normal
ledge-fall physics; post sides obstruct actors and bombs. Cover is sampled
independently in four channel regions, with normal advanced density settings.

GutterRules publishes deterministic left/right/calm flow from public seed,
round and accepted weather time. Free supported bombs move at up to 1.5 cells/s;
living grounded players receive 75% of that displacement. Normal input remains
available upstream/downstream. Collision-safe advection respects posts, cover,
other bombs and bodies, including a fresh bomb overlapping its owner. Fuse
clocks and kick momentum are preserved. Airborne bombs/players, carried
objects, dry caps and cover tops do not receive shallow-water drift.

Bots countersteer against the visible current and conservatively forecast
ground-bomb drift. Closing left/right grates reuse the authoritative column
hazard and existing crush effect. The current can move actors after an earlier
closure, so closed-column lethality is checked every tick as well as on each
new closure. Practice and disabled sudden death retain the normal exclusions.

Complete snapshots add a nullable gutter object with time, seed, round and x.
PeerFrameRules bounds every consumed current field and accepts the new stage
and authored crowns; room settings and ordinary menus accept the same
identifier. Release admission/version coordination remains the release
owner's responsibility.

## Presentation

The court is original brick, pale wet paving, cream post caps and dark metal
grates with brass trim. Shared Low/Medium/High water shaders retain opaque Low,
clear refracting Medium and scene-reflecting High behavior. Gutter's cached
High variant uses coherent reflection samples for its upright post/cover
contacts: the per-pixel march jitter is zero, while the existing twelve coarse
steps, three bisections, refined hit validation, scene-color sample and Fresnel
remain. Shared High shaders on other arenas keep their jitter, which protects
their suspended-deck reflections from banding. This is a local shallow-court
exception, not removal of scene reflections or extra ray-march work.

A bounded owned ArrayMesh grid supplies the water surface, avoiding a new
engine-primitive parameter domain. The submerged paving sits above the
shader's open-sea cutoff, so it is treated as real shallow geometry. A shallow directional tracer layer and center arrow follow
accepted current; their clock stops on ordinary pause. The outside canal,
narrow canals, textured quays, gabled keepers' houses, rimmed sluice wheels,
brass work lamps, planted banks and gauge floats stay outside the playable
volume; menu miniatures omit scenery. Slow wheel, foliage, float and pump-gauge
motion supplies small-scale life. The setting has a finite masonry/earth foot
instead of a large flat cyan base.

The closed volume has a low metal base and a thin opaque grille at its leading
face. Existing closure interpolation paces it to accepted column seals. The
battle camera uses the enclosed-arena angle and includes standing post-top
players. The online fallback diagram also depicts the correct water/post map.
New geometry uses managed portable boxes, the existing sphere template and
owned SurfaceTool surfaces; no native primitive fallback is introduced.

## Verification

- Clean client and server builds, zero warnings/errors.
- Nine focused In the Gutter integration checks pass, covering safe water,
  elevated starts, randomized cover, current schedules, grounded drift, post
  drops, held attachment, body/post obstruction, airborne exclusion and
  continuous grate lethality.
- Complete game suite: 4,053 checks pass; existing Blizzard focused suite: 13.
- Complete client suite: 497 checks pass, including 191 peer-frame checks with
  actual Gutter stage/state acceptance and malformed-current rejection.
- Refined source headless in-the-gutter: 19 assertions, zero runtime errors,
  including an audit of the detailed exterior's standing-volume clearance.
  It drives
  ordinary selection, post descent, bomb placement, pause, eight-player CPU
  combat and closing grates. This is not visual approval.
- Graphical tour-23 contains in-the-gutter; its native run must inspect the
  opening, every preset, Night, labeled oblique inspection, shallow-court play,
  eight-player battle and crush images.
- Source headless in-the-gutter-network: 100 assertions, zero runtime errors
  or warnings, with the pinned WebRTC extension. It exercises two real clients
  through both server-backed and player-hosted private rooms, including the
  accepted shallow-floor player, actual bomb, current and stage. Its owned
  loopback server is stopped by the runner.
- The complete public QA profile passes. The mesh producer inventory records
  reviewed geometry-producing calls; source layout and documentation-link
  checks pass.

## Shallow-contact diagnosis and remaining review

The initial native Gutter tour passed its functional checks but exposed noisy
reflection fringes. A fixed 1280×800 Night view at water-clock 4 retained the
actual court, cover, players and opaque scene inputs. The broad contact probe
counted 2,251 isolated bright water pixels against 441 with only scene
reflection disabled. Removing tracers, flattening waves, correcting the
open-sea cutoff, restricting hits above water, increasing bisection depth and
doubling march samples did not resolve the defect. Coherent samples reduced
the count to 541 while preserving visible block/post reflections; root and
implementation-agent inspection agreed on that technical improvement.

The permanent gutter-water-contact journey compares actual production High
against a reflection-disabled control at Midday and Night. It requires contact
noise excess at most 150 isolated pixels and at least 250 pixels of real
reflection contrast; shared High must retain its existing jitter. The earlier
red diagnostic captures remain in ignored artifacts. This regression is part
of graphical tour-23.

The refined candidate builds and passes public static checks and the 19-check
headless tour. The graphical owner's final native source regression passes
31 assertions with four captures and zero runtime errors/warnings. Midday
contact counts are 1,420 reflected versus 1,372 control (excess 48), with
30,861 changed reflection pixels; Night is 1,391 versus 1,400 (excess -9), with
27,540 changed reflection pixels. Underwater paving/caustics contribute to both
control counts; the original excess from reflected fringes is absent.

The final source captures are gutter-contact-midday-high and
gutter-contact-night-high in the ignored 20261115T211031.433422Z native receipt.
Rebuilt-export review remains with the graphical owner. The user's subsequent request for a stronger cohesive visual
identity supersedes earlier agent art acceptance: independent visual review
is pending. No user art approval, publication or deployment is claimed here.
