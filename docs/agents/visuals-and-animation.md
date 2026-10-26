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
| Character mesh, rig, face and poses | [BomberModel](../../scripts/arena/models/BomberModel.cs); `model-profile`, `movement`, `walk-feel` |
| Root interpolation and facing | [SnapshotMotion](../../scripts/arena/SnapshotMotion.cs), [ArenaView](../../scripts/arena/ArenaView.cs); `movement` |
| Reusable mesh helpers | [ArtGeometry](../../scripts/rendering/ArtGeometry.cs); `asset-audit` |
| Bomb shell/growth and pickup symbol | [BombModel](../../scripts/arena/models/BombModel.cs), [PowerupModel](../../scripts/arena/models/PowerupModel.cs); `pump-growth`, `loot-opening` |
| Terrain visuals and height conversion | [TempleStageView](../../scripts/arena/stages/TempleStageView.cs); `traversal`, `framing`, `hurry-water` |
| Stone/water shaders and lighting | [ArtMaterials](../../scripts/rendering/ArtMaterials.cs), ArenaView; graphical `asset-audit`, `presentation` |
| Perspective framing/shake | [BattleCameraRig](../../scripts/arena/BattleCameraRig.cs); `framing` at supported aspect ratios |
| Blast/action/exit visuals | [ExplosionView](../../scripts/arena/effects/ExplosionView.cs), [KickBurst](../../scripts/arena/effects/KickBurst.cs), [ExitEffect](../../scripts/arena/effects/ExitEffect.cs); `presentation`, `kick-physics` |
| Event/audio playback | [BattleAudio](../../scripts/audio/BattleAudio.cs), [SoundSynth](../../scripts/audio/SoundSynth.cs); audio executable tests plus `presentation` |

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
| Legs | Fixed .190-high white stems; boots share limb transform. Hips may tuck up (≤ .055) and slide fore/aft (≤ .065) only inside the belt, never down or apart |
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

[ModelProfilePlaytest](../../scripts/testing/journeys/rendering/ModelProfilePlaytest.cs) checks actual
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
The v0.20 complete left/right cycle spans **1.5 world units** (about six steps per
second at full speed). Do not lengthen it for "readability": a rigid stubby leg can
only sweep about .14 units each way, so longer strides make the planted boot skate.
That skating (95% of body speed in v0.18) was the main reason earlier walks read as
cheap toys. `movement` now measures world-space boot slip while down.
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

Each boot heel-strikes toe-up, grips while sweeping back, rolls off the toe,
follows through, tucks while passing and whips forward already matching the floor.
The body has a short airborne beat, squashes toward the planted boots and leans
into the run with the legs compensated. Damped springs (fixed 240 Hz substeps)
give gloves, lean, turn bank, head and the crown antenna momentum, lag and
overshoot; avoid replacing them with phase-locked sines. The antenna pivots inside
the crown and is soft-limited. See [iteration twenty](../history/iteration20-walking.md). The carry socket follows `upperParts` with
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
| Longer, slower strides for readability (v0.15–v0.18) | Continuity and whole-body bounds passed | Planted boots skated at ~95% of body speed; measure grip, keep cadence matched to leg reach |
| Phase-locked upper body, welded antenna | Lateral/roll/shoulder spans passed | No momentum on starts/stops; use springs for follow-through and settle |
| “Passing tests means charming” | Numeric bounds describe mechanics | State measured facts; user approval and perceived feel remain separate |

[MovementPlaytest](../../scripts/testing/journeys/combat/MovementPlaytest.cs) measures actual rig
transforms, fixed stem length/hip attachment, bounded reach, boot grip while down,
body/head/shoulder/antenna motion and composed boot continuity. The 1200 Hz diagnostic catches curve-boundary
velocity corners; it is not a rendering-performance claim. Foot-center height
and support/flight fractions are diagnostics, not proof of a planted sole or
terrain IK. Avoid tightening those metrics until they force the rejected stretch.

[WalkingShowcasePlaytest](../../scripts/testing/journeys/rendering/WalkingShowcasePlaytest.cs) walks
ordinary inputs across the real clear courtyard, reverses, settles and repeats
through side, three-quarter and normal battle views. It logs authority/render
positions and rig poses, so a future agent can tell travel stutter from gait issues.
Inspection cameras must remain visible over terrain and restore the original
camera afterward. v0.18's corrected three-quarter offset is evidence of that
specific composition, not a universal camera constant.

## Materials, stage depth and effects

Circuit Garden has its own grass, range marks, banners and low-terrace composition; its camera leaves room for tutorial instructions. [Tutorial visuals and ownership](../tutorial.md) keep this local training arena separate from the battle-map rebuilds.

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
foam, sky reflections and flood-dependent motion. Its fragment `NORMAL` is in
view space; depth reconstruction handles renderer conventions. The transparent
pass with full alpha is deliberate: sampled depth must contain opaque shore
geometry rather than the water itself. Preserve that rationale when refactoring.
Read [the rendering investigation](../history/iteration8-rendering.md) before replacing
this shader; its fixed daytime Fresnel tint was superseded by the four lighting
profiles below. Verify normal and hurry/flood states from more than one camera.

### Diorama polish (2026-10-25)

The user asked to raise the established chibi look without changing its identity,
citing Pokémon BDSP's water, Tunic's lighting, and Link's Awakening (Switch) /
Echoes of Wisdom. Nintendo describes the latter as a diorama "you're peeking
into" with Link as a ~10 cm figurine, while keeping detail restrained to that
scale ([Haruhana interview](https://nintendoeverything.com/graphic-refining-director-yoshiki-haruhana-on-reimagining-the-visuals-of-zelda-links-awakening/));
reviews describe its tilt-shift edge blur and lacquered, board-game-piece gloss.
Echoes of Wisdom reuses that engine and look ([Digital Foundry summary](https://gonintendo.com/contents/40826-digital-foundry-offers-a-tech-analysis-of-zelda-echoes-of-wisdom)).
The implementation is original shader/material work; nothing is copied.

- **Clear shallows (Medium/High).** `ArtMaterials.WaterShaderFor` compiles the same
  source with `CLEAR_WATER`: refraction of the opaque scene copy, depth absorption
  toward the profile's deep color, drifting caustic webs projected on the actual
  submerged floor, and sparse twinkling glints along the key light's reflection path
  (`StageLighting.ApplyWater` uploads its direction and color). Low keeps the opaque
  variant and pays no screen copy; `TempleStageView` swaps shaders live and re-applies
  uniforms. Flooded courts now show their cobbles; authority water is unchanged.
- **Open sea and seabed.** Nothing lies under the open sea, so its reconstructed
  depth is the degenerate sky/far plane. The shader now detects that explicitly and
  keeps the authored lagoon tone, mottled by real wave height and deepening gently
  away from the island. An unseen opaque `Seabed` box at `TempleStageView.SeabedHeight`
  (matching `seabed_height`) gives depth of field a real surface: Godot's DOF reads
  opaque depth, so without it the whole transparent sea blurred as distant sky.
  Anything at seabed depth counts as open sea and is never revealed. The menu
  miniature hides its seabed.
- **Shoreline foam.** A crisp contact line plus one soft band travelling outward,
  still masked by shore slope and vertical depth so flooded paving shows no foam grid.
- **Diorama depth cues.** `BattleCameraRig.PlayableDepth` records the nearest/farthest
  axial view depth for depth of field; `PlayableDistance` separately measures the
  farthest radial distance for Godot's depth-mode fog. Both use the effective camera
  transform, including projection offsets. `ArenaView.ApplyDepthCues` keeps blur and
  haze outside their respective bounds. `graphics-settings` checks every battle arena
  and Circuit Garden and oblique inspection views, against actual terrain and
  standing-character extents. Camera changes remeasure the bounds.
  Aerial perspective stays off: a camera
  looking down would sample the dark lower sky and grey the sea. Blur is off on Low.
- **Lacquered toys.** `ArtMaterials.Toy` adds a thin clear coat and a soft light rim to
  characters, bombs and pickups. Geometry, proportions and animation are untouched.
- **Painterly terrain.** Stone gains per-block warmth, broad warm/cool weathering,
  risers that deepen toward their submerged roots and an algae fringe at the waterline
  (the castle sets `water_height` far below so it stays dry). Garden turf gains tufts,
  sun-warmed patches, sparse tiny flowers on Medium/High, and grounded wall gradients.
- **Light and bloom.** Cooler, slightly more saturated ambient in Morning/Midday/
  Afternoon gives Tunic-like colored shadows against the warm key (Midday key 1.07).
  A small glow bloom softens highlights; High adds half-resolution SSIL bounce.

Lessons: compare against the baseline capture at full resolution before tuning.
A dropped `vertex()` once zeroed the world-position varying and looked like a fog,
DOF or palette problem; one diagnostic frame writing the suspect values as
emission found it. Close-up concentric banding on the helmet predates this work.

### Blizzard Battle frozen snowfield

[Stage contract](../blizzard-battle.md), [reference research](../blizzard-battle-research.md). `TempleStageView.Blizzard` builds opaque cyan ice, original violet faceted cones and snow-capped glacier walls. `ArtMaterials.Blizzard` owns world-space frost/fracture/strata textures; `BlizzardWeather` animates a bounded MultiMesh from accepted wind. Preserve shared graphics/lighting, depth-writing ice, local pause and camera-safe near walls. The user rejected the initial small spires: their tops now reach 1.755 render units and their bases span .94. Stage `coverHeight` drives both meshes and projectile collision/landing, including the miniature and aim preview. The miniature crops its exterior independently. New artwork is procedural; reference archives never ship. Keep the foundation below the ice slab to prevent coplanar flicker. Godot 4.7 shaders use `CLEARCOAT_ROUGHNESS`; the former `CLEARCOAT_GLOSS` name is rejected by the live compiler.
### Stage scenery and ambient life (2026-10-26)

Every arena now has original surroundings under one `StageScenery` node:
lagoon ruins and palm islets (Sunken Temple), a meadow with a windmill and brook
(Rock Garden), a lighthouse coast (Up & Down), a Black Fortress-style sky citadel
with crag, turrets, banners, clouds and stars (Top Rules), a pine/igloo/penguin
snowfield (Blizzard Battle), boats on Greedy Trap's reservoir and a training
harbor (Circuit Garden). Small animations (boats riding the real water, gulls,
flags, flames, sails, beams, butterflies, penguins) run from
`TempleStageView._Process`. Scenery has no collision and never enters a passable
cell's standing volume. Menu miniatures skip it. Top Rules keeps its dark
backdrop; its emissive stars and flames disable fog. [References, composition
and rules](../stage-scenery.md).

### Greedy Trap presentation

The exterior now uses a Blue Resort-informed fortified reservoir: animated teal
water, quays, blue roofs, sparse rounded trees and a slow waterwheel. It shares
other outdoor arenas' sky, lighting, water presets and depth cues. The landscape
is an original extension, not an exterior recovered from Greedy Trap footage.
All scenery stays outside/below play, remains separate from compression and is
hidden in the menu miniature. [Background sources](../greedy-trap-background-research.md).

Greedy Trap has independently authored cobbles, burgundy brick, teal floor tiles,
pale ring paving and gray-green destructible masonry, plus compression flywheels
and pulsing amber inlays. It uses the enclosed-arena camera, ordinary lighting
profiles and all graphics presets. The basin is safe opaque paving, with no
lethal-water plane. [Stage and visual contract](../greedy-trap.md),
[inspected original references](../greedy-trap-research.md).

### Top Rules castle presentation

[TempleStageView.TopRules](../../scripts/arena/stages/TempleStageView.TopRules.cs)
builds original gray masonry, brass-framed slate bridges, pale junctions,
green stair treads and a patterned crimson court from the authority layout.
Bridge undersides are thin, with visible space below. Keep the red court above
the foundation and deck paving above its structural slab; coplanar tops caused
large gray patches on Low even when the High capture looked correct. Keep decorative banners,
columns and warm edging outside the actual passage volume. No original textures
or models ship. The castle has its own steeper battle-camera fit and a low near
wall so the lower court stays visible; retain HUD-safe framing at 16:9 and 16:10.
Local lighting and graphics presets continue to apply. The `top-rules` journey
captures the real menu miniature, four-player opening, lower traversal/aiming,
upper crosswalk, four stairs, night, eight-player combat, real dropped loot below
a bridge and inward wall closure. Pickup meshes must use the snapshot's accepted
`y`; querying the topmost terrain surface moves lower drops onto a bridge.

### Up & Down suspended arena (2026-10-24)

[Stage contract](../up-and-down.md), [original references](../up-and-down-research.md).
`TempleStageView.UpAndDown.cs` builds pale stone piers, recessed square friezes,
purple perimeter panels and six sloping flights with yellow landing arrows,
a green summit mosaic, thin suspended links and the low four-panel deck.
All geometry/materials are original procedural work. Bridge tiles render their
`below` surfaces separately; no foundation may fill an underpass. Both camera
fitting and framing checks include lower surfaces and standing headroom.
The 2026-10-25 correction separates paving from structural tops (including the
crown) to prevent depth fighting. Up & Down has its own gray stone destructible
cubes with square insets and pale rims. The menu instance uses an opaque circular
sea inside its round stand and trims deep supports at the stand; gameplay keeps
its full foundations and quality-dependent water.
The authority's fractional ramp endpoints drive the render slope; visual treads
sit just above it. The ordinary battle camera remains distinct from oblique
inspection captures. All four existing lighting profiles and graphics presets
apply to the new arena. Older packets without bridge surfaces retain generic
terrain rendering. The shared menu plate builder uses clockwise faces with
outward normals; `asset-audit` retains the winding regression. `menu-preview`
exercises preparation, preset changes, asset inspection and real setup navigation;
setup checks the rendered green summit as well as settings state. Agent image
inspection is still required.

### Four stage lighting variants (2026-10-23)

The user requested **Morning, Midday, Afternoon and Night**, selected personally
for local play and by the server online. Settings → Local lighting persists in
`PlayerPreferences` as `video/stageLighting`; Midday is the default. Changing it
while locally paused updates the current arena without restarting the match.
Online preference edits save the local choice but never override server lighting.

[StageLighting](../../scripts/arena/StageLighting.cs) owns the four coordinated
key/fill/ambient, sky, fog and water profiles. Morning uses soft warm lateral light;
Midday has a higher sun and turquoise water; Afternoon uses amber light, longer
shadows and mauve reflections; Night uses blue moonlight, indigo water and enough
fill to retain silhouettes, stairs and aiming markers. All arenas reuse
these profiles. Menu theatre lighting remains separately authored.

`ArenaView` supplies a procedural sky radiance map for specular reflections.
The fill is `LightOnly` so it cannot invent a second sun. Explicit ambient color
keeps shadow readability independent of the reflected sky. This follows Godot's
[sky radiance contract](https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/sky_shader.html)
and [spatial normal/material outputs](https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/spatial_shader.html).
These are sky reflections, not reflections of characters or terrain; SSR remains
off. Medium/High refract the opaque scene beneath clear shallows (see diorama polish). Fine water ripples fade with pixel footprint, and normal
variance softens distant specular highlights. Stone grain is similarly filtered;
cobble now receives relief and the same irregular, shinier wet band as masonry.
Authority water height, flood damping and the opaque-depth sampling pass remain.

`BlastSession` publishes `stageLighting` on every state snapshot. The lobby seed
selects the first profile, then successive rounds cycle all four; selection never
consumes gameplay RNG. Late join/reconnect uses the same complete snapshot.
Missing/unknown lighting from older servers falls back to Midday. An updated
server is required for online variation; a client update alone cannot provide it.

`stage-lighting` exercises the native settings dial, persistence and paused
session continuity, all twelve stage/time combinations, nighttime bomb aiming,
and actual flooding from battle and shoreline inspection cameras. It belongs
to graphical `all`. `network` checks both clients' shared lighting and rejects a
personal override; managed game checks cover round cycling, seeds and reconnect.

Source verification is retained in `artifacts/iteration24/`: managed passed
1,475 game / 143 client / 53 AI / 19 audio checks, 36 soaks and a real staged
server smoke; headless passed 868 checks. The final graphical receipt and
captures are in `graphical/playtest/20261023T232734.727626Z-all/`. The four looks
were inspected on every stage and from flooded-shore cameras. The same complete
graphical suite passed on the rebuilt v0.24.0 export (2,426 checks / 225 captures).
The signed release and existing shared server are delivered; see
[verification](../verification.md). Agent inspection does not imply user approval
of the art direction.

Production uses Forward+ and Metal on macOS. Settings also exposes saved display
mode and resolution controls; [display behavior](input-and-ui.md) owns their
window sizing, fullscreen 3D cap and restart persistence. `DisplayModeController`
combines that cap with the graphics preset's scale, retaining native menu density.
Settings → Graphics selects Low,
Medium (default) or High; see [research and preset budgets](../graphics-research.md).
`GraphicsQuality` owns the live viewport/postprocess/shadow policy, while
`RenderSurface` sizes decorative menu worlds in output pixels. Root antialiasing
alone does not affect those separate worlds. Keep native Buttons for focus/input
and redraw cached plaques when their state, size, visibility or preset changes.
Opaque menus disable the hidden arena's root 3D pass; paused-match overlays retain it.
Animated menu worlds (backdrop theatre, lobby/online/winner `CharacterPreview`)
render every display frame on Medium/High and at 30 Hz on Low
(`GraphicsQuality.MenuFrameInterval`). Character previews use a small studio rig:
shadowed warm key, cool fill, a rear rim light, sky-fed reflections behind a
transparent background, and SSAO except on Low.

Low uses 2× MSAA, 85% 3D resolution with spatial FSR, 1024 shadows and no SSAO/glow.
Medium uses native resolution, 4× MSAA, 2048 shadows and half-resolution SSAO.
High keeps native/4×, raises shadows to 4096, uses full-resolution higher-quality
SSAO, and adds restrained menu contact shading. Medium/High retain the authored
glow intensity. All menu textures remain at output resolution; no TAA/FXAA.
Low skips fine shader noise/ripples and contact-hardening sun shadows while
retaining the same geometry, lighting palettes, shoreline and gameplay. Low also
keeps opaque water and no depth of field; Medium/High add clear refracting
shallows and diorama blur, and High adds half-resolution SSIL bounce.
Shadow depth bias scales with preset and doubles for Morning/Afternoon; the
smaller atlas otherwise exposes diagonal self-shadow marks under grazing light.

Stone/water share immutable Shader resources but have independent ShaderMaterial
uniforms. Stone seams and fine noise use pixel-footprint filtering; wet highlights
use normal variance. Do not share mutable water height or palette across scenes.
Stable water uniforms are uploaded only when changed; the shader TIME still animates.
`graphics-settings` checks native selection, persistence, malformed saved values,
live scene changes, sampling at supported sizes, idle redraws and all stage/preset
pairs. `network` also changes quality while retaining the same connection and
server lighting. Renderer timings in a VM are not native M-series benchmarks;
zero GPU timing is unavailable evidence, never zero cost.

### First-use effects

Arena selection retains one real miniature per battle map. Startup builds and
draws them sequentially behind “Getting ready…” before enabling menu input;
preset changes redraw the retained scenes behind “Applying graphics…”, using
the new MSAA/environment policy and restoring the selected map. Only the
selected miniature is visible and processing. Keep these resources alive so
revisiting a map does not rebuild geometry or recreate its shader pipelines.
Headless QA builds miniatures on demand without claiming render preparation.

`map-preview-loading` cycles every map in both directions through Setup,
Practice and room settings at every preset, checks actual scene reuse and
inactive processing, and rejects synchronous Mesh/Surface/Draw compilations.
Frame intervals in its fixed-step receipt are stall diagnostics, not a native
GPU throughput benchmark. `setup` retains the miniature bounds and subsequent
gameplay-scene checks.

`EffectWarmup` renders the real bomb/explosion, kick, exit and ghost/fading
variants in a temporary 128×128 world with the current MSAA and arena environment.
Startup shows “Getting ready…”; a changed preset shows “Applying graphics…”
before restoring control. Local matches remain paused and online sessions keep
their normal ongoing authority. No simulation, audio or haptics run in the scratch
world. A bounded draw fallback handles background/new native windows, and actual
readback confirms rendering before the temporary world is removed.

Warm the current format after MSAA changes. Trying to prewarm several MSAA levels
once at startup left Low's first effects creating new pipelines. Keep immutable
shader/material resources alive: the bomb trail material is shared so freeing
scratch instances cannot discard its warmed variant. Do not reset OS shader caches.

`first-use-rendering` runs before other visual tests in `all`, creates ordinary
bombs, waits for actual explosions and checks Mesh/Surface/Draw pipeline deltas
at every preset, including after garbage collection. Background specialization
is recorded separately. `--cold-effects` is a deliberately failing diagnostic
that disables preparation only during that scenario; it retains a red witness.
See [measurements and sources](../graphics-research.md#first-use-rendering-evidence).

A headless shader-source assertion cannot prove GPU compilation, balanced
lighting or quality. Test the real renderer; profile real-time play separately
from fixed-step Movie Maker captures before claiming performance improvements.
Ordinary launch restores saved display settings; QA starts windowed. Neither proves OS Game Mode.

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

Use [character references](../history/iteration16-character.md), [current rig decisions](../history/iteration18-walking.md)
and [asset audit boundaries](../asset-audit.md). Official 2D action art, official 3D
renders, original-game footage and user screenshots answer different questions.
Long-limbed modern artwork is not permission to undo the compact original-game
silhouette. Record source URLs and what was observed; keep downloaded references
out of distributed assets and retain original procedural work/licenses.

Finish a visual change when the reported view/action has an inspected before/after,
required numeric invariants pass, shared poses remain sound, the ordinary battle
camera is checked, and published screenshots/clips match delivered code. Record
what remains subjective instead of claiming that all tests prove artistic success.

## Iteration23 sandbox visuals

Remote blue bombs hide the complete combustion fuse assembly, retaining their
command indicators. Red bombs keep a red shell but use blue blast cores, rings,
shells and billows. Purple bouncing bombs match their pickup color. Fast bomb
equipment and its mint shell/icon were removed on 2026-10-25; the existing gold
boot pickup represents the temporary Haste player buff;
Focus uses a cyan target symbol. Charge progress drives tension before the accepted
pump animation. The fifth-tier Pyramid summit is a low mosaic, keeping playable
space and rear water falls visible. Recheck camera fit on all enlarged arenas.

## Rock Garden reconstruction (2026-10-24)

Rock Garden has its own procedural turf, weathered gray limestone, five round
columns, pale central paving and faceted destructible boulders. It renders dry
land beyond the authority-owned enclosure instead of the temple sea. Scenery
outside the walls is visual only. The column and cover crowns use shared throw
support heights; the paving is a shallow cosmetic layer. The stage selector
uses the real square layout. See the [stage contract](../rock-garden.md).

The menu miniature trims `GardenExterior` to the display footprint, matching the
bounded ocean on other arenas. Duplicate its cached box mesh before resizing so
the gameplay exterior stays full-size. `setup` checks the visible miniature's
bounds and launches Rock Garden afterward to verify gameplay scenery is intact.

## v0.28 reference fidelity

[Stage comparison](../stage-fidelity-research.md) and [explosion sequences](../explosion-fidelity-research.md) record inspected original imagery and limits. The integrated castle has taller authored walls and side banners; Up & Down uses rounded/fluted supports inside its solid footprint; Rock Garden's center crown is 1.35 units versus .9 for its outer columns. Original reference assets remain outside Git and exports.

Explosions use one round world-space fireball, brief sharp charged flares, an expanding cyan power wave and transparent fade. This supersedes the earlier offset billow spheres, ordinary ground torus and brown cloud. Only the cosmetic group compensates the 1.35 tier-height scale; the faint danger shell retains mapped authority extent. Damage radius and fuse stay in the simulation. Castle thin-deck blast permeability is an explicitly researched rules change, separate from the effect geometry.

**Explosion v2 (2026-10-25).** The user asked for more style, identity and a
more modern look. Re-inspection found the original's signature: a white/yellow
**star** inside the fireball (and inside the teal power body), plus white rim
puffs and a thin double ring for power bombs. See the
[research](../explosion-fidelity-research.md#2026-10-25-star-core-and-v2-layers).
The one body is now a lumpy, noise-boiled toon sphere with crisp bands. Its bands
run from an ember rim through orange and yellow to a white-hot center. The layer
order follows:

1. a damped-spring pop, about 4% past full size;
2. a short white flash;
3. a spinning camera-facing star, pulled toward the camera so terrain cannot clip it;
4. a fresnel shock rim;
5. directional spark jets with drag;
6. a brief Medium/High light pulse;
7. a burning-edge dissolve after the hazard window, revealing small dark smoke
   puffs (white rim puffs for power).

Flares remain charged-only and the cyan ground ring remains power-only. Keep
every fade in shader uniforms. `GeometryInstance3D.Transparency` would add a
second pipeline that warmup's two ages do not cover. Flares, sparks and puffs
are each one MultiMesh draw. Low quality and the 16-active-blast reduced-detail
path drop the light and thin the particles. `presentation` now also detonates
a real red-pickup bomb and captures its sequence. Remaining choices are
subjective tuning: how much smoke to show, how long the flash lasts, and the
light's strength.

Castle stone and garden surfaces use bounded terrain-owned materials. Repeated rounds and new closing walls must retain the selected quality preset. Inspect actual captures; shape/aspect/lifetime tests are supporting evidence, not aesthetic approval.
