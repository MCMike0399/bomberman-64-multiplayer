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
clear refracting Medium and scene-reflecting High behavior. A bounded owned
ArrayMesh grid supplies the water surface, avoiding a new engine-primitive
parameter domain. A shallow directional tracer layer and center arrow follow
accepted current; their clock stops on ordinary pause. The outside canal,
quays, lock houses, shrubs and animated sluice wheels stay outside the playable
volume; menu miniatures omit scenery.

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
- Source headless in-the-gutter: 18 assertions, zero runtime errors. It drives
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

No native graphical verification, rebuilt-export verification, user art
approval, publication or deployment is claimed by this source contract.
