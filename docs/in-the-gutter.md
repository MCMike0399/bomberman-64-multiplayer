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
initial-block placement algorithm. Copper Lock's machines and ornament are
independently authored; earlier canal-village prototypes are superseded.

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

## Copper Lock presentation (shape study)

The current art direction replaces the generic waterworks tray with two
core sluice machines, each with six fat cast bars, rounded finials, a bowed
verdigris gantry and an outboard gear/counterweight. Four burgundy bollards
retain their original two-cell support footprints, rounded cream caps and
recessed brass mooring rings. A brass fish vane turns on the center post.
An arched inflow and a weir extend the body asymmetrically. These forms remain
visible on Low; the quiet outer-water/reed-bank scenery is separate.

Straight scanline tracers, repeating houses, wheel clones and shrub rows are
removed. Accepted current drives downstream foam wakes attached to actual
posts and blocks, a few leaves, fish direction and gear ticks. Pause freezes
their clocks. New paired gate panels follow the existing authoritative closure
extents; no authority terrain, collision, rule or network field changes.

The first Copper Lock shape study uses a blocked burgundy/cream/verdigris/brass
palette and a quieter mosaic. It is a review checkpoint, not finished surface
art. The independent review accepts the court, bollards, caps, rings, jade
shallows, pale blocks and rounded plinth; it requests a bounded second pass on
gantry massing, solid gears, integrated channel stubs, a curved spillway,
half-round end coping, a larger fish vane and eight-player HUD clearance.
Bright High caustics and overly straight white wakes also remain unresolved.

The local High reflection specialization is retained: coherent samples replace
per-pixel jitter only on this stage, while twelve coarse steps, three
bisections, refined hit validation, scene samples and Fresnel remain. Other
stages retain their existing shared jitter. No extra reflection march work is
introduced. Owned packed surfaces use clockwise winding against their actual
outward normals; the shape journey checks the indexed arrays.

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

## Copper Lock shape checkpoint

The native source copper-lock-shapes study passes 52 assertions with four
Low/High/oblique/Night captures and zero runtime errors or warnings
(20261115T213318.142138Z). Its actual ring, beam and culvert triangles are
nondegenerate and clockwise; machines remain visible when Low hides scenery.
Headless copper-lock-shapes passes 19 checks. The updated ordinary gameplay
tour passes 31, including monotonic gate pacing through accepted snapshots.
The common all-stage scenery tour now includes Copper Lock's quiet exterior
signatures and passes 132 headless assertions with zero errors/warnings; the
graphical path also verifies core machines on Low and paused current cues.

Independent shape review remains REVISE (narrow). The court is accepted for
further work, while the machine/setting and surface issues above must be
resolved before final art review, rebuilt-export qualification or release.
