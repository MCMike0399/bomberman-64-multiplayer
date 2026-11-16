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

## Copper Lock presentation

Two core sluice machines frame a rounded burgundy lock. Each has six fat
cast bars, a deep verdigris beam, domed towers, a solid brass gear with four
round through-holes and a seated counterweight. Full-opening channel arms
continue about three cells beyond the gates; their walls support the
mechanisms, meet the upstream arch and lead to a curved spillway. The old
separate islet and sharp spillway fins are removed.

Four burgundy bollards retain their original two-cell support footprints,
rounded cream caps and recessed brass mooring rings. Half-round cream end
coping stays inside the original nonwalkable perimeter crowns. A readable
brass fish vane swings on the center post. Front-facing work lamps provide
restrained warm Night accents. Camera fitting includes the core envelope and
eight-player HUD clearance; miniatures scale that complete silhouette.

Short soft wakes attached to the actual posts/cover, sparse leaves, the vane
and gear ticks receive accepted current. Gear motion eases toward incremented
target angles; it does not multiply clock time by changing flow. Pause freezes
those cues. Paired gate panels follow the existing authoritative closure
extents. No authority terrain, collision footprint, rules or wire fields change.

Court and outer water retain separate palettes and shader materials. Low
keeps the full body and darker outer water; Medium adds clear jade shallows;
High retains actual opaque-scene reflections and restrained caustics. The
mosaic stays quiet. All identity is core geometry; there is no prop-ring or
island dressing hidden behind the Low setting.

Both owned water nodes are physically centered, with localized meshes and
preserved world positions/UVs. The shared shader's radial reflection reach
therefore treats opposite court corners symmetrically. The consumed-input
regression verifies 441 court vertices and 8,281 ocean vertices; maximum world
errors are 3.4e-7 and 5.4e-6 units, with equal 13.435029-unit corner radii.

The court and compact outer-tower reflections use separate cached Copper Lock
High variants with coherent march samples. Twelve coarse steps, three
bisections, refined hit validation, scene samples and Fresnel remain; no extra
per-pixel march work is added. Other maps retain their shared jitter. This
reduces reflection undersampling, not a claim of perfect or removed reflections.
Owned packed surfaces use clockwise winding against their outward normals.

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

## Major-form GO checkpoint

The second study rebuilds the solid gears with four true circular through
holes and ten chunky teeth, doubles gantry-beam depth, widens towers outward,
raises the far towers, adds half-round nonwalkable coping and enlarges the
fish vane. Core outer water remains visible on Low; camera fitting includes
the machine envelope and eight-player HUD clearance. The left counterweight
was moved behind its gear after the actual frame revealed occlusion.

The corrected native study passes 56 assertions with four High/Low/Night/
oblique captures and no runtime errors or warnings. Owned solid-disc,
half-round coping and curved-spillway faces pass the indexed winding audit;
headless shape and common scenery checks pass 21 and 130 assertions.
The reviewed geometry matches the saved mesh-census source hashes.

Independent round-three review approves the major forms for finishing, with
one bounded correction: connect wider/longer channel arms, seat mechanisms on
wall tops, integrate the arch and delete the islet. The white spillway fins,
wake strength, Low water separation and a local outer-water reflection hatch
also remain to be resolved. This is not final visual or release approval.

## Colored finishing candidate and outer reflection control

The colored real-combat/current/reflection run before the final outer sampling
choice passes 103 assertions with ten captures and zero runtime errors/warnings
(20261115T223946.402801Z). It shows actual eight-participant CPU combat in High,
Low and Night plus an oblique view. The focused current recording passes
24 native assertions and retains a validated 27.9-second, 30 FPS, 837-frame
video with accepted fish/gear/leaf/wake reversal. The current logic also passes
eight headless checks.

A fixed-clock, same-device near-left outer-water control compares the real
production scene with legacy jitter, reflection-off and above-water-hit
rejection. The 111×36 output-pixel patch is selected in world space, with
logical camera coordinates scaled to the unmodified 1280×800 PNG. Legacy
jitter counts 266 isolated bright pixels, reflection-off zero, above-water
rejection 266 and coherent sampling 95. Coherent output differs from the
reflection-off control in 1,332 of 3,996 pixels: the reflection is retained as
tower mass, with less undersampling. Full-frame root inspection agrees on
that improvement; remaining approximation must not be described as perfect.

The permanent outer regression compares production to the same-device legacy
control and requires meaningful reflection contrast. It uses a relative noise
comparison, not an absolute GPU-specific count alone. The court regression,
consumed geometry/coverage checks, colored combat and accepted-current witness
are included in tour-23. The final public QA profile passes. Fresh native
qualification of this exact candidate, rebuilt-export checks and final
independent colored-art review still belong to the graphical/release owner.
Major-form GO is not final art or release approval.

## Fresh all-preset advisor correction (2026-11-15)

The fresh plain-Claude advisor's Medium frame read as dry paving. The court now
uses a local Medium specialization with a gentle jade absorption tint and soft
caustic lines, retaining Medium's refraction and zero scene-reflection marches.
High's coherent local reflection shaders and other maps' water remain unchanged.
Rounded soft foam traces the physical bollard and breakable-cover waterlines;
short curved wakes gain modest opacity and follow the same accepted current.
Low retains contact foam and omits wakes. Front-facing work-lamp windows are
larger and warmer, moved clear of the cream tower face; the two shadowless local
lights are hidden on Low, whose emissive windows remain visible.

These are review candidates. The source/public checks and managed build do not
establish the Medium water read or visible current direction. Matching native
Medium opening/combat, Night and six-to-eight-frame current evidence remain the
acceptance gate.

The first native Medium follow-up exposed a missing specialized helper guard:
its caustic call was enabled while the helper still compiled only on High. The
local Medium shader now widens both matching guards; shared water remains
unchanged. The failing native log is the retained regression witness; a fresh
native compile/capture is required before treating this correction as qualified.
