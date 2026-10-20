# Iteration eight rendering: grounded temple and directional water

Research and implementation: 2026-10-20. All assets remain original procedural geometry and shading. TUNIC and Pokémon BDSP guide readable dimensional forms; this implementation does not claim their proprietary rendering methods.

## Lighting and upper stone

The court's irregular Voronoi cobble calculation and palette are preserved. The upper sandstone gains staggered slab courses, mineral clouding, restrained ochre veins, irregular chamfers and small pits. A derivative-based surface gradient perturbs the view-space normal so these features respond to the actual directional light. World coordinates keep patterns continuous across authority cell meshes. This is shading relief; it does not displace walkable surfaces.

The warm key is reduced from 1.1 to .92 energy, cool ambient from .46 to .29 and cool directional fill from .26 to .12. This increases separation between illuminated tops, shaded risers and contact shadows. SSAO remains enabled, glow is restrained and no depth-of-field blur is introduced. These are artistic starting values that require the actual game capture for visual judgment.

Godot's [environment documentation](https://docs.godotengine.org/en/4.7/tutorials/3d/environment_and_post_processing.html) describes ambient contributions, tone mapping and renderer-dependent effects. The existing Forward+ path retains real directional shadows and SSAO. No SSR is enabled: reliable water reflections would require a separate reflection-source decision and visual/performance evidence, not merely a checkbox.

## A foundation below the island

A closed-perimeter, layered bedrock shell follows the temple footprint. Its crown is at Y=-.24, below the lowest playable court; its sides flare down to Y=-4.5. The narrow upper rock edge meets the initial waterline while the broader underwater apron affects water depth shading. This anchors the masonry in a substantial submerged formation rather than ending the island at a thin hovering plane. Uneven side segments avoid a perfect rectangular plinth.

Only MeshInstance3D geometry is added. There are no collision shapes, physics bodies, navigation surfaces, authority cells or extra stairs. Outer water remains lethal, ramps remain authority-defined and rising water follows the same server-provided level. Bedrock and upper masonry receive the actual smoothed visual water height for a dark wet band; the court's texture is unchanged.

## Water implementation and tradeoffs

Four nonparallel wave components use different wavelengths, amplitudes and speeds, with transverse phase modulation and its exact derivative to break long straight highlights. Broad vertex motion supplies the silhouette while the analytical slope of the same wave field supplies a coherent per-pixel normal. This is directional wave synthesis with small displacement, not a full ocean simulation or a claim of complete Gerstner dynamics. There is no horizontal particle transport, FFT, refraction or caustic simulation.

The [spatial shader reference](https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/spatial_shader.html) specifies model-space vertex inputs and view-space fragment normals. The water explicitly transforms its analytical world normal into view space; it does not send a world-space slope into a tangent-space normal-map output. Roughness and specular remain ordinary light-responsive material outputs. A restrained Fresnel-dependent tint supplies grazing-angle color separation without claiming a real environment reflection.

Opaque scene depth is sampled with `hint_depth_texture`. The [advanced post-processing guide](https://docs.godotengine.org/en/4.7/tutorials/shaders/advanced_postprocessing.html) documents reconstructing view position with the inverse projection matrix. The shader uses Forward+/Metal zero-to-one NDC depth and includes Compatibility's minus-one-to-one conversion. Godot's [reverse-Z explanation](https://godotengine.org/article/introducing-reverse-z/) explains why raw depth subtraction is unsuitable; this shader compares reconstructed view distances instead.

The reconstructed distance behind the water drives deeper teal color versus shallow turquoise and broken contact foam. This follows actual submerged geometry as flooding advances. Foam is deliberately narrow and low contrast so it does not look like a traversable edge. Reconstructed opaque-surface slope suppresses foam across flat flooded floors. Flooding damps displacement and adds at most .12 world units of visual height bias (about .03 at the first terrace), preventing wave crests/troughs from exposing a checkerboard of nominally flooded cells; authority water height and elimination rules are unchanged. Low-frequency noise breaks broad color uniformity; it does not draw a scrolling grid of bright sine stripes.

Writing `ALPHA=1.0` deliberately places the water in the transparent pass while retaining full visual opacity, allowing the copied depth buffer to contain opaque terrain rather than water. This avoids relying on transparent refraction or visible underwater players, but inherits the engine's transparent-material limitations. Water casts no shadow. No screen-color readback is used, and no unsupported screen normal/roughness buffer is assumed. The subdivided water mesh has 180 subdivisions per axis; high-frequency detail comes from fragment normals rather than further geometry.

## Verification boundary

Serial `dotnet build Bomberman64Multiplayer.csproj -m:1 --no-restore` completed with zero warnings/errors. Native windowed framing and hurry-water scenarios then exercised the actual shaders and game: framing passes 29 assertions with six captures across three maps and two aspect ratios; hurry-water passes 11 assertions with three captures, including submerged court and first-terrace elimination while the crown player survives. Both report zero runtime/shader errors.

Final evidence is under `/tmp/iteration8-render-verified/`: `20261020T163501.925750Z-hurry-water` and the subsequent framing run. Captures were visually inspected: the initial court retains its irregular brown cobble, upper slab relief responds to light, exposed foundation meets the shoreline, flooded terrain reads below teal water and the crown character remains sharp. The first visual review exposed excessive broad foam and straight distant highlights; phase modulation, slope-gated foam and flood displacement damping corrected them before final verification. No benchmark or general hardware performance claim is made. The approved BomberModel and transparent HUD are outside this rendering change.
