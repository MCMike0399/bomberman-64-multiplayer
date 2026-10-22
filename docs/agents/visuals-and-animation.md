# Character, animation and rendering guide

Read this before changing procedural models, gait, materials, stage presentation,
camera, explosions or sound. This guide preserves the user's corrections and
failed approaches; exact live values remain in source. Use the
[testing guide](testing-and-delivery.md) for build/recording ownership and commands.

## Start with the actual actor

1. Record whether the complaint appears in a menu, practice, ordinary local
   battle or online. Capture the real actor from the reported camera and direction.
2. Compare accepted position/facing/action state with the visible model. Root
   movement, leg pose, torso motion, camera projection and mesh proportions are
   different variables. A smooth leg curve cannot fix pulsing root movement.
3. Build the narrow check or visual witness before changing the model. Keep the
   baseline capture, seed, camera, speed and input sequence for comparison.
4. Change one meaningful dimension or motion relationship at a time; review
   front, side, three-quarter and ordinary battle views before accepting it.
5. Verify carry, pump, throw, kick, knockdown, ghost and victory poses if shared
   transforms changed. Finish with the correct exported app and inspected footage,
   not only a render-free geometry test.

## Source ownership

| Concern | Source / focused witness |
| --- | --- |
| Character mesh, rig, face and poses | [BomberModel](../../scripts/Rendering/BomberModel.cs); `model-profile`, `movement`, `walk-feel` |
| Root interpolation and facing | [SnapshotMotion](../../scripts/Rendering/SnapshotMotion.cs), [ArenaView](../../scripts/ArenaView.cs); `movement` |
| Reusable mesh helpers | [ArtGeometry](../../scripts/Rendering/ArtGeometry.cs); `asset-audit` |
| Bomb shell/growth and pickup symbol | [BombModel](../../scripts/Rendering/BombModel.cs), [PowerupModel](../../scripts/Rendering/PowerupModel.cs); `pump-growth`, `loot-opening` |
| Terrain visuals and height conversion | [TempleStageView](../../scripts/Rendering/TempleStageView.cs); `traversal`, `framing`, `hurry-water` |
| Stone/water shaders and lighting | [ArtMaterials](../../scripts/Rendering/ArtMaterials.cs), ArenaView; graphical `asset-audit`, `presentation` |
| Perspective framing/shake | [BattleCameraRig](../../scripts/Rendering/BattleCameraRig.cs); `framing` at supported aspect ratios |
| Blast/action/exit visuals | [ExplosionView](../../scripts/Rendering/ExplosionView.cs), [KickBurst](../../scripts/Rendering/KickBurst.cs), [ExitEffect](../../scripts/Rendering/ExitEffect.cs); `presentation`, `kick-physics` |
| Event/audio playback | [BattleAudio](../../scripts/Rendering/BattleAudio.cs), [SoundSynth](../../scripts/Rendering/SoundSynth.cs); audio executable tests plus `presentation` |

These are original code-generated meshes/materials/audio, not imported original
Bomberman assets. Menus and matches reuse the same character. Locate the actual
node/viewport being inspected: a hidden preview instance can make a geometry or
animation assertion pass while the visible player remains wrong.

## Preserve the compact silhouette

The user's requested direction is a large softly rounded helmet, separate curved
cream face, small blue/colored pear suit, short white limbs, broad pink boots and
round gloves. They repeatedly requested small proportion corrections, not a
new mascot design. Current authored dimensions, before runtime transforms:

| Part | Baseline to preserve unless explicitly changed |
| --- | --- |
| Helmet | Width .854, height .750, depth .810; half-height .375, half-depth .405 |
| Helmet balance | Shell center biased .045 forward; face stays separate and curved |
| Torso | Approximately .395 wide × .380 high × .370 deep |
| Legs | Fixed .190-high white stems, attached at fixed hips; boots share limb transform |
| Extremities | Preserve broad pink boot/glove mesh dimensions when tuning gait |

Local face-forward is +Z. Inspect actual mesh bounds and composed transforms,
not just constructor constants: the living model's root scale and hierarchy
also affect appearance. The helmet, face and torso use cached procedural meshes;
rebuild/restart the game after mesh-code edits instead of judging a stale app or
an already-created static cache. Ship Godot-generated resource UIDs when new
script assets require them.

The flat-side-face problem was not solved by indefinitely inflating the back of
the helmet or extruding a thick rectangular mask. The final shell is a continuous
ellipsoid, with a curved face patch and returning rim. Adjust shell/face together
and preserve neck transition, front-to-back balance and readable eye placement.
The user preferred a middle point: a separate plate without a paper-thin side
profile, a slightly shorter oval head, and no massive rear/forehead volume.

[ModelProfilePlaytest](../../scripts/Testing/ModelProfilePlaytest.cs) checks actual
shell ellipsoid vertices, outward unit normals, torso depth, nonzero face depth
and nondegenerate clockwise face triangles. Godot mesh winding matters: apparently
missing faces may be backface culling, not a request to add thickness. Keep normals,
UVs, vertex bounds and winding coherent; use `asset-audit` to catch invalid data.
A menu close-up alone cannot validate side views under the battle camera/light.

## Separate travel, gait and action

`ArenaView` supplies `BomberModel.Animate(dt, rendered_planar_distance)` after
advancing the accepted root position. `SnapshotMotion` buffers tick-stamped player
samples around a .1-second playback delay. It interpolates continuously, rejects
stale/nonfinite samples, holds on starvation and snaps discontinuities. Preserve
that separation; resetting a lerp on each 15 Hz snapshot caused visible pulsing.
There is no client movement prediction or rollback replay.

Gait phase advances from actual travel, not raw stick values or wall-clock time.
The v0.18 complete left/right cycle spans 2.4 world units. Rendered gameplay speed
is unchanged; slower cadence gives the same distance fewer, more readable steps.
A blocked player should not keep running in place; after release the current pose
settles without taking another step. Test equal travel at 30/60/120 FPS and inspect
starts, reversals and stops, not only a steady loop.

The current hierarchy separates the upper body from the hips:

```text
BomberModel root (accepted position/facing)
  body (weight transfer, small spring, action pose)
    legs -> fixed stems and boots
    upperBody (waist pivot at y=.37)
      upperParts (offset y=-.37; preserves authored geometry positions)
        torso/belt, arms/gloves, head/face
```

Shoulders follow the foot phrasing with lag, arms counter the leading leg, and
the head counterbalances the torso. The carry socket follows `upperParts` with
the hands; a new pivot without updating the socket can separate bomb and hands.
Keep local/world conversions explicit (`ToGlobal`, arena-local attachment and
`TempleStageView.HeightScale`). `BombThrowRules` owns authority launch height;
changing a cosmetic socket cannot repair a low physical throw launch.

## Rejected experiments: do not repeat them blindly

| Failure | Why it passed superficial checks | Better check / retained lesson |
| --- | --- | --- |
| Careful robot/prototype walk | Legs moved and curves were continuous | Judge the whole body at ordinary speed; character needs coordinated weight, shoulders, arms and head |
| Stretching/IK-like white legs | Foot-contact/clearance metrics improved | Actual stems reached 2.49× rest length and hips separated; keep fixed short limbs, accept stylized glide |
| Large foot return overshoot | Smooth endpoints hid excess reach | Measure actual composed reach, not only continuity; the old curve overshot intended amplitude by 40% |
| Almost entire bounce dropped in one frame | Animation looked active in stills | Sample per-frame body displacement and high-frequency derivatives |
| Tiny restrained v0.17 upper-body motion | Leg/boot tests passed | Add actual body/shoulder/head measurements and inspect at battle scale; v0.18 doubles lateral weight travel |
| Side-only inspection | Side view hid torso twist and head balance | Include front/three-quarter and normal camera views |
| Three-quarter camera behind court wall | Actor/state checks and screenshot capture passed | Inspect image content; move the inspection camera so the entire body is unobstructed |
| “Passing tests means charming” | Numeric bounds describe mechanics | State measured facts; user approval and perceived feel remain separate |

[MovementPlaytest](../../scripts/Testing/MovementPlaytest.cs) measures actual rig
transforms, fixed stem length/hip attachment, bounded reach, body/head/shoulder
motion and composed boot continuity. The 1200 Hz diagnostic catches curve-boundary
velocity corners; it is not a rendering-performance claim. Foot-center height
and support/flight fractions are diagnostics, not proof of a planted sole or
terrain IK. Avoid tightening those metrics until they force the rejected stretch.

[WalkingShowcasePlaytest](../../scripts/Testing/WalkingShowcasePlaytest.cs) walks
ordinary inputs across the real clear courtyard, reverses, settles and repeats
through side, three-quarter and normal battle views. It logs authority/render
positions and rig poses, so a future agent can tell travel stutter from gait issues.
Inspection cameras must remain visible over terrain and restore the original
camera afterward. v0.18's corrected three-quarter offset is evidence of that
specific composition, not a universal camera constant.

## Materials, stage depth and effects

The user's direction combines the original pyramid's terrace/courtyard composition
with restrained modern stylization inspired by Tunic and Pokémon BDSP. Preserve
the recessed cobbled court, warm readable sandstone, grounded bedrock/foundation
and contrasting water. Avoid solving depth with oversized UI panels, excessive
brightness, decorative geometry over walkable cells or a large empty-water frame.

`BlastStages` owns terrain; `TempleStageView` builds from it. Authoritative tiers
convert to render height through one `HeightScale` (currently 1.35). Keep floors,
ramps, shoreline, actors, bombs and previews consistent. Foundation/water art may
suggest the pyramid grows from the ground without inventing a safe floor. A prettier
waterline cannot change where authority actually kills or supports a player.

Stone detail uses shader variation/relief and wetness. Water uses several bent,
nonparallel wave components with matching analytic slopes, depth-derived contact
foam, Fresnel coloring and flood-dependent motion. Its fragment `NORMAL` is in
view space; depth reconstruction handles renderer conventions. The transparent
pass with full alpha is deliberate: sampled depth must contain opaque shore
geometry rather than the water itself. Preserve that rationale when refactoring.
Read [the rendering investigation](../iteration8-rendering.md) before replacing
this shader. Verify normal and hurry/flood states from more than one camera.

Production uses Forward+ and Metal on macOS with shadows/SSAO/MSAA and restrained
glow. A headless shader-source assertion cannot prove GPU compilation, balanced
lighting or quality. Test the real renderer; profile real-time play separately
from fixed-step Movie Maker captures before claiming performance improvements.
Fullscreen is product behavior, windowed is QA behavior; neither proves OS Game Mode.

Pickup **symbols** were enlarged independently of their baseplates: `PowerupModel`
scales only its icon subtree and lifts it clear of the plate through its bob cycle.
Preserve the requested distinction. Bomb mesh size is also separate from collision
and blast radius; changing visuals must not silently enlarge damage or block paths.

Explosions evolve with pump tier through their actual ignition/bloom/decay shapes,
not merely a uniformly scaled sphere. `presentation` witnesses tier variants and
hand/kick/water/death cues through accepted events. Follow event sequence handling
so repeated snapshots neither replay every sound nor lose short actions. Audio
is original synthesized PCM: keep bounded peaks, clean edges, caching and voice
limits; listen to captured output as well as checking sample statistics.

## References and finish criteria

Use [character references](../iteration16-character.md), [current rig decisions](../iteration18-walking.md)
and [asset audit boundaries](../asset-audit.md). Official 2D action art, official 3D
renders, original-game footage and user screenshots answer different questions.
Long-limbed modern artwork is not permission to undo the compact original-game
silhouette. Record source URLs and what was observed; keep downloaded references
out of distributed assets and retain original procedural work/licenses.

Finish a visual change when the reported view/action has an inspected before/after,
required numeric invariants pass, shared poses remain sound, the ordinary battle
camera is checked, and published screenshots/clips match delivered code. Record
what remains subjective instead of claiming that all tests prove artistic success.
