# Graphics presets and Apple Silicon rendering investigation

Research date: 2026-10-24. Source baseline: `82493cc`, Godot.NET.Sdk 4.7.2,
desktop Forward+ with the native Metal driver on macOS. This document records
baseline code observations, implementation decisions and primary sources. Timing
and visual evidence are recorded below, with native and virtual measurements
kept separate.

## What the supplied menu screenshot suggests

The user's image shows stepped thin rails, rough plaque edges and softer 3D text
than the ordinary footer text. It establishes a visible complaint, but cannot
identify the render resolution or separate geometry aliasing from texture
upscaling on its own.

The baseline code has two relevant facts:

- [project.godot](../project.godot) enables 4× MSAA for the root viewport.
  [ArcadeBackdrop](../scripts/ui/menu/ArcadeBackdrop.cs),
  [ArcadeModeButton](../scripts/ui/ArcadeModeButton.cs),
  [CharacterPreview](../scripts/ui/menu/CharacterPreview.cs) and
  [ResultMedals](../scripts/ui/menu/ResultMedals.cs) explicitly select **2×** for their
  separate 3D viewports. Raising the root setting alone misses the visible menu.
- Those components use `SubViewportContainer.Stretch=true`. Godot resizes their
  render targets to the container's local size divided by `StretchShrink`;
  constructor sizes such as `1440×900` are not persistent resolution locks.
  The root uses `canvas_items` stretch at a `1440×900` reference size. A local
  texture can therefore be enlarged by the parent canvas transform on a larger
  display. Verify the actual texture dimensions against the transformed display
  rectangle, including window stretch, before treating this as a confirmed
  runtime cause. [Godot container implementation](https://github.com/godotengine/godot/blob/4.7-stable/scene/gui/subviewport_container.cpp),
  [container sizing contract](https://docs.godotengine.org/en/4.7/classes/class_subviewportcontainer.html#class-subviewportcontainer-property-stretch).

Recommended correction: let decorative 3D views render at their actual output
pixel density, with bounded allocations and invalidation on resizing. Keep the
native Buttons responsible for input/focus. This is a rendering change, not a
reason to replace dimensional menus with flat cards. The `Viewport.oversampling`
property is for font/DPITexture oversampling and does not supersample 3D geometry.
[Viewport API](https://docs.godotengine.org/en/4.7/classes/class_viewport.html#class-viewport-property-oversampling).

## Antialiasing appropriate to this art

MSAA smooths geometric coverage without a full-image blur. It does not resolve
shader/specular aliasing. TAA can address those artifacts but introduces blur
and possible motion trails; FXAA is cheap but also softens the image. The game's
rounded characters, colored silhouettes and thin architecture favor native
resolution plus 2×/4× MSAA, with procedural filtering for material detail. This
is an art/engineering recommendation, not a measured comparison of this game.
[Godot 4.7 antialiasing guide](https://docs.godotengine.org/en/4.7/tutorials/3d/3d_antialiasing.html).

Do not make 8× MSAA the universal Mac High preset. Apple's current feature table
lists a maximum of 4 samples for Apple GPU families 2–9 and 8 for family 10.
Apple's MSAA sample explains how tile-based resolves can reduce memory traffic;
that does not mean every Godot pass is free or that 8× is a sensible default.
[Apple GPU feature tables](https://developer.apple.com/metal/Metal-Feature-Set-Tables.pdf),
[Apple MSAA sample](https://developer.apple.com/documentation/metal/improving-edge-rendering-quality-with-multisample-antialiasing-msaa).

Keep TAA and temporal upscaling off initially, especially on menu viewports that
render intermittently. FSR2 at native resolution adds work, while rendering
below native trades image quality for fragment savings. If Low uses a modest
3D scale, keep 2D controls and menu labels sharp. A scale of 0.85 processes about
72% of native pixels, an arithmetic reduction rather than a promised 28% frame
time improvement. CPU-bound scenes may see little benefit.
[Godot resolution scaling](https://docs.godotengine.org/en/4.7/tutorials/3d/resolution_scaling.html).

## Renderer and platform choice

Forward+ and Mobile both support Metal, Vulkan and Direct3D 12 through Godot's
RenderingDevice abstraction. Retaining Forward+ permits one desktop feature
policy across macOS, Windows and Linux, while the platform selects the driver.
Changing to Compatibility is not an Apple Silicon optimization by itself and
changes the supported effects and lighting response.
[Godot renderer comparison](https://docs.godotengine.org/en/4.7/tutorials/rendering/renderers.html).

Mobile is worth a separate measured experiment for this relatively small scene.
It reduces base complexity and uses tile-friendly subpasses, but depth texture
reads and post-processing interrupt those benefits. This game's water explicitly
samples depth, so even Mobile would not automatically keep the entire frame in
one tile pass. Its different HDR format and missing effects also require visual
review. Do not silently switch rendering methods when selecting a live preset;
keep this first preset implementation within Forward+.
[Godot rendering architecture](https://docs.godotengine.org/en/4.7/engine_details/architecture/internal_rendering_architecture.html#mobile).

Apple's tile-memory guidance supports avoiding unnecessary render-target traffic
and repeated passes. It does not establish a particular speedup on M1, M5 or a
virtual GPU. Native GPU measurement remains necessary.
[Apple tile-based rendering guidance](https://developer.apple.com/documentation/metal/tailor-your-apps-for-apple-gpus-and-tile-based-deferred-rendering).

## Implemented preset budget

`GraphicsQuality` implements these live settings. Stage lighting choice, physics,
inputs, model proportions and camera framing remain identical across presets.

| Setting | Low | Medium | High |
| --- | --- | --- | --- |
| Gameplay MSAA | 2× | 4× | 4× |
| Gameplay 3D scale | 0.85 | 1.0 | 1.0 |
| Menu texture density | Output pixels | Output pixels | Output pixels |
| Directional shadow atlas | 1024 | 2048 | 4096 |
| Shadow filtering | Soft very low; no sun PCSS | Soft low | Soft medium |
| Arena SSAO | Off | Half-resolution, medium quality | Full-resolution, high quality |
| Menu theatre SSAO | Off | Off | Restrained contact shading |
| Glow | Off | Restrained intensity, small soft bloom | Restrained intensity, small soft bloom |
| Temporal AA / FXAA | Off | Off | Off |
| Stone/water microdetail | Reduced | Filtered | Filtered |
| Water | Opaque; no screen copy | Clear refraction, caustics, glints (one screen copy) | Same as Medium |
| Diorama depth of field | Off | Far/near blur outside the arena, low quality | Same, medium quality |
| Indirect bounce (SSIL) | Off | Off | Half-resolution, medium quality |

Medium is the default. High improves contact shading, bounce light and shadow
detail without globally increasing saturation or contrast. The 2026-10-25
diorama polish ([rendering guide](agents/visuals-and-animation.md#diorama-polish-2026-10-25))
added the water, depth-of-field and SSIL rows. Its VM receipts show render-thread
CPU near 1 ms and one extra draw call (the seabed); GPU timings were unavailable
there, so native GPU cost still requires `graphics-benchmark`. All four stage lighting
palettes and the compact character silhouette are preserved. Low uses spatial
FSR for its gameplay buffer; 2D UI and menu textures remain at output resolution.
The choice persists in `video/graphicsPreset`, applies without restarting a match,
and never affects online authority. Unknown/malformed preferences use Medium.

Shadow distance and splits are as important as atlas size. The current arena
uses a 65-unit shadow range; the menu uses 35 with four splits. More splits can
repeat geometry submissions. Positive light angular distance enables expensive
contact-hardening shadows. Reduce that work before removing meaningful depth,
and inspect the afternoon's long shadows, stairs and ordinary battle camera.
[Godot directional shadows and PCSS](https://docs.godotengine.org/en/4.7/tutorials/3d/lights_and_shadows.html#directional-shadow-mapping).

Apply live changes through the actual `Viewport`, `Environment`, lights and
RenderingServer methods. Directional atlas size/filter quality are global;
they cannot be independently tuned by a menu subviewport. Save one validated
preset value in player preferences and reapply it to newly created scenes.
Do not assume that modifying a ProjectSettings value updates existing renderer
objects. [RenderingServer shadow APIs](https://docs.godotengine.org/en/4.7/classes/class_renderingserver.html#class-renderingserver-method-directional-shadow-atlas-set-size),
[Environment/post-processing quality settings](https://docs.godotengine.org/en/4.7/tutorials/3d/environment_and_post_processing.html).

## Focused opportunities in the code

1. **Avoid idle plaque redraws.** The baseline `ArcadeModeButton._Process`
   requests a complete 3D render each frame even after focus/lift/color settle.
   Draw during transitions, then only on focus, text, theme/preset or size
   changes. Hidden views already skip processing. This removes real submitted
   work without changing the visible geometry; measure active versus idle menus.
2. **Share immutable shader resources.** `ArtMaterials.StoneMaterial` creates a
   new identical `Shader` for each material, and `Water` does the same. Share
   the compiled shader resource while keeping materials independent: water
   height, palettes and stage uniforms are mutable per scene. Godot recommends
   material/shader reuse to reduce state changes. Static terrain batching is a
   larger candidate, but joining an entire stage can worsen culling and should
   follow draw-call evidence rather than a blanket rewrite.
   [Godot GPU optimization](https://docs.godotengine.org/en/4.7/tutorials/performance/gpu_optimization.html#reuse-shaders-and-materials).
3. **Filter procedural stone detail.** Existing grain fades with pixel footprint,
   but masonry mortar/chips/pits/mineral octaves and Voronoi cobble transitions
   can still become subpixel. Widen transition widths using derivatives and
   fade fine relief with distance; preserve broad block boundaries. Fade a noise
   octave toward its mean rather than zero to avoid changing the overall color.
   `fwidth`, `dFdx` and `dFdy` are available fragment derivative operations.
   [Godot shader functions](https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/shader_functions.html).
4. **Keep water's established depth contract.** The baseline already has analytic
   wave slopes, filtered capillary ripples and normal-variance roughness. Its
   full-alpha transparent pass deliberately obtains opaque shoreline depth.
   Moving it to the opaque pass changes the sampled scene and is not a safe
   performance shortcut. The 160-unit water plane currently uses 180 subdivisions
   per direction, also in the scaled-down menu diorama. Lower tessellation and
   skipping genuinely invisible microdetail are candidates; inspect displacement,
   coastline and flooding at ordinary and close cameras before accepting them.
   [ArtMaterials](../scripts/rendering/ArtMaterials.cs),
   [TempleStageView](../scripts/arena/TempleStageView.cs).
5. **Preserve authored lighting rather than adding expensive features by default.**
   The arena already has sky reflections, SSAO, glow, filmic tonemapping and fog.
   SSR, SDFGI, volumetric fog, depth of field and motion blur are not prerequisites
   for modern stylized graphics. First improve sampling, stable highlights and
   contact shadows. The existing four coordinated lighting profiles are an art
   constraint, not an optimization target.
   [ArenaView](../scripts/arena/ArenaView.cs),
   [StageLighting](../scripts/arena/StageLighting.cs).

## Wider codebase performance review

The renderer changes are deliberately separate from these remaining seams:

| Boundary | Observed implementation | Decision |
| --- | --- | --- |
| Local authority | [LocalSession](../src/Bomber.Client/LocalSession.cs) advances on the Godot thread at fixed 30 Hz, with at most five catch-up ticks. [LocalMatch](../src/Bomber.Game/LocalMatch.cs) encodes snapshots, then the adapter parses/buffers them. | Keep simulation cadence independent of quality. A typed local snapshot path might reduce allocations, but requires parity/lifetime evidence before changing that boundary. |
| Network | [NetworkSession](../src/Bomber.Client/NetworkSession.cs) sends input asynchronously at 30 Hz and allocates a receive stream per envelope. [MessageBuffer](../src/Bomber.Client/ISession.cs) already coalesces state snapshots while retaining control messages. | Do not reduce input rates for Low. A bounded receive-buffer improvement needs fragmented-frame and ownership tests; no current client profile establishes this as the limiting cost. |
| Snapshot presentation | [ArenaView](../scripts/arena/ArenaView.cs) decodes/scans the cover bitset per snapshot and creates temporary collections. | Change detection is a possible allocation reduction; preserve initial, reset and destruction behavior. Profile before redesigning. |
| Animation | Actor interpolation, carried sockets and [BomberModel](../scripts/arena/models/BomberModel.cs) spring animation run each frame; spring integration uses fixed 240 Hz substeps. | Preserve timing and the approved silhouette across presets. Numerical animation simplification is not justified by the current evidence. |
| Terrain | [ArtGeometry](../scripts/rendering/ArtGeometry.cs) already caches meshes. The stage creates many instances, but measured empty-practice root draw calls are only 48/73/115 across Garden/Up-and-Down/Pyramid. | Node count alone is not draw-call count. Defer manual batching until a submission bottleneck is measured; static regional batches must keep destructible cover separate. |
| Battle effects | [ExplosionView](../scripts/arena/effects/ExplosionView.cs) uses transparent lobes, with existing crowd reduction after 16 tracked explosions. [BombThrowPreview](../scripts/arena/effects/BombThrowPreview.cs) creates 24 cosmetic trajectory dots with default shadow casting. | Eight-player explosion overdraw and unnecessary marker shadows are future focused candidates. Preserve full danger extent and trajectory information; empty-practice results cannot rank their costs. |

The [managed snapshot probe](../tests/Bomber.Game.Tests/SnapshotPerformance.cs)
measures tick/encoding work, while the [network probe](../tests/Bomber.Performance.Probe/Program.cs)
measures server generation and delivery intervals. Neither includes the Godot GPU
or the complete client frame. Historical [server performance](server-performance.md)
results are not evidence of current client throughput and do not justify changing
the shared server for this graphics task.

## Performance measurement boundaries

Use the same stage, seed, resolution, lighting, camera and accepted input sequence
before and after. Warm shaders before sampling. Cover an idle main menu, focus
transitions, eight-player battle, explosions and flooded Pyramid. Record frame
time distributions and draw calls, not just one FPS average.

Enable render timing on **each active viewport**. Godot exposes per-viewport GPU
and rendering CPU milliseconds; the latter excludes gameplay scripts. Sum active
viewport measurements when comparing the multi-viewport menu. A zero result can
mean unsupported/unavailable measurement and must not be reported as zero cost.
Keep VSync, power state and sample duration recorded.
[RenderingServer timing API](https://docs.godotengine.org/en/4.7/classes/class_renderingserver.html#class-renderingserver-method-viewport-get-measured-render-time-gpu).

The isolated macOS VM can establish real shader compilation, preset application,
UI persistence and visual results without occupying the user's desktop. Its
Apple Paravirtual GPU is not a bare-metal M-series performance benchmark. Movie
Maker is fixed-step capture, and headless mode does not render; neither proves
real-time GPU throughput. Follow the [testing guide](agents/testing-and-delivery.md)
and state those limits in delivery notes. Windows/Linux portability still needs
their own native rendering runs before claiming validation.

## First-use rendering evidence

The first ordinary bomb/explosion created five Surface and one Draw pipeline;
the repeated effect did not. The retained initial witness is
`artifacts/graphics/first-use-red/20261024T181133.892933Z-first-use-rendering/`.
This distinguishes real deferred rendering work from an unsupported guess that
all pauses come from shader loading. Audio streams were already generated during
`BattleAudio._Ready()`.

`EffectWarmup` now renders production bombs, both blast colors, kick flashes,
water/death exits, fading/ghost states and debris geometry in a small temporary
viewport. It matches the current MSAA and environment, observes actual draw
boundaries and reads back the rendered image. The bomb trail material stays alive
after temporary nodes are freed. A brief preparation screen moves the work before
gameplay; it is also used after changing presets because MSAA changes invalidate
pipeline assumptions. Simply loading shader strings or prewarming several AA
levels once was insufficient. [Godot pipeline compilation guidance](https://docs.godotengine.org/en/4.7/tutorials/performance/pipeline_compilations.html).

The final focused witness uses real planted bombs, accepted explosions and garbage
collection at all three presets. It records zero new Mesh/Surface/Draw pipelines
in all six first/repeated trials. Worst measured single-frame intervals were
6.9–14.1 ms in the isolated Metal VM. These are fixed-step diagnostic intervals,
not M5 throughput. Receipt:
`artifacts/graphics/first-use-live-presets/20261024T184234.515360Z-first-use-rendering/`.

The matching `--cold-effects` negative control disables preparation and fails
again with five Surface/one Draw pipeline and a 293.1 ms interval:
`artifacts/graphics/first-use-negative-control/20261024T184523.999560Z-first-use-rendering/`.
Preparation is intentionally a startup/settings cost (3.32 seconds in that guest
run), not an assertion that compilation no longer exists. The earlier M5 native
first-use run also recorded zero synchronous pipelines for both explosions.

## Native GPU timing and engine decision

On the physical M5 Pro, Godot 4.7.2's viewport GPU timer remained zero despite
being enabled. This is an engine/backend limitation, not evidence of zero GPU
work. The maintainer reports the timestamp fix in 4.8, without a backport to 4.7.
[Godot issue and maintainer update](https://github.com/godotengine/godot/issues/102968).

The harness's `--metal-hud` path obtains frame command-buffer durations from
Apple's process-local HUD logging. It joins console timing arrays to PID-filtered
system-log timestamps and deduplicates repeated records, without changing logging
privacy or global defaults. Whole batches inside guarded UTC sample windows
provide per-preset summaries. This is a different scope from Godot's root viewport
timer. [Apple logging](https://developer.apple.com/documentation/xcode/monitoring-your-metal-apps-graphics-performance),
[metric definitions](https://developer.apple.com/documentation/xcode/understanding-metal-performance-hud-metrics).

The stable-engine native benchmark recorded approximately 120 process FPS at
1920×1200 for all presets, including eight-player combat. The separately built
4.8 beta candidate passed native checks and produced positive internal GPU times
that agreed in scale with Apple HUD data. Instrumentation and preparation state
differed, so these are not a matched engine speedup comparison. The main project
stays on 4.7.2: profiling access is useful, but a gameplay benefit sufficient to
justify the beta migration has not been established. Exact receipts, native
timings, compatibility findings and future gates are in the
[engine evaluation](engine-upgrade-research.md).
