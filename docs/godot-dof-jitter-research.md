# Godot 4.7.2 DOF seeding: source-only finding

2026-10-28. Scope: one same-device, same-runtime full ArenaView High/2×MSAA fixture. No runtime experiment, build, engine modification or acceptance result. Implementation
evidence below is exclusively official `4.7.2-stable` source; the [tag API][tag] resolves to `ed1daf0bf001b61586d9930840f2f1394092c079`.

**Answer:** `GD.Seed` seeds exactly the global RNG consumed by compute DOF. However, the production preset selects **Circle**: its shader does **not read the jitter flag or
seed**. The CPU still consumes one random value and uploads it. Box/Hexagon use that value; Circle does not. This narrows the earlier report's “random seed per invocation”
finding: it does not establish stochastic sampling in this production shape. Keep jitter enabled and 2×MSAA unchanged. [GD.Seed][gd] [DOF CPU setup][dof] [circular
shader][circle]

Seeding can conditionally reproduce the uploaded value if prior work is drained, all intervening RNG consumers and their order are controlled, and no concurrent consumer
accesses the unlocked global state. A pre-draw signal alone does not establish those conditions. Neither seeding nor a render fence resets SSIL, shader TIME or cosmetic clocks.
**Actual frame reproducibility is UNTESTED.**

## Product facts inspected read-only

Owner reference checkout: `blast-desktop-release`, HEAD `a00ac277c46f3bc86f12c86f07cfbd541157338b`; its working files were read, not changed. This note does not assume the
research checkout's older preset guide is current.

- `scripts/rendering/GraphicsQuality.cs:13,59–69,74–76,83–93`: High uses native scale, 2×MSAA, TAA off, Low-quality **Circle** DOF with jitter argument `true`, near/far blur
  and High-only SSIL (Low SSIL quality, half-resolution).
- `scripts/testing/PlaytestDriver.cs:373–389`: settle process frames; await `FramePostDraw`; after 500 ms, main-thread `ForceDraw(false,0)` then `ForceSync`; then viewport
  readback (native-popup capture is a separate path).
- `scripts/rendering/ArtMaterials.cs`, `.Garden.cs`, `.GreedyTrap.cs` and stage `TempleStageView.CircuitGarden.cs` shader source use TIME; weather/ambient uniforms, model
  pose/event ages and menu `_clock` fields are additional clocks. `ArcadeBackdrop`, `CharacterPreview` and other menu worlds own SubViewports.
- `docs/visual-parity-comparator.md:155–173` requires actual history/jitter provenance, not equal labels standing in for unknown state. The earlier
  `docs/desktop-rendering-parity-research.md:160–166` established CPU consumption; the shape-specific shader qualification above is new.

## RNG identity and actual DOF consumption

`GD.Seed(ulong)` synchronously calls `NativeFuncs.godotsharp_seed`; the native callback table supplies `godotsharp_seed(uint64_t)`, which calls `Math::seed`. `Math::seed`
changes file-static `RandomPCG default_rand` in `math_funcs.cpp`. `Math::randf` calls `Math::rand` once and divides its uint32 result by `UINT32_MAX`; `Math::rand` uses that
same `default_rand`. [R1][gd] [R2][interop] [R3][mathcpp] [R4][mathh] [native callback declaration][native] [callback table][callbacktable]

This is **one process-global instance, not thread-local**. Its seed/rand path has no mutex or atomic state. `RandomPCG` owns `pcg32_random_t`; seeding calls `pcg32_srandom_r`,
drawing calls `pcg32_random_r`, which mutates ordinary state. Seeding itself performs the PCG initialization steps. Do not confuse `Math::randf` with `RandomPCG::randf`, whose
floating-point sampling can consume more words. `GD.Randf` or `GD.Randi` is a one-word diagnostic perturbation; `GD.RandFromSeed` instead constructs a separate local generator.
Concurrent global seed/draw calls risk a C++ data race, not just a shifted sequence. [R3][mathcpp] [R4][mathh] [R5][pcg] [R6][pcgimpl] [native RNG calls][interop]

Forward+ route: `RenderingServerDefault::_draw` updates scenes/particles/probes, then `RendererViewport::draw_viewports`; each eligible viewport calls `render_camera`, scene
culling calls the RD scene renderer, and clustered `_render_scene` resolves MSAA, copies SSIL history, then calls `_render_buffers_post_process_and_tonemap`. Its eligible DOF
branch loops over `view_count` and calls `bokeh_dof_compute` once per view. Transparent-background viewports disable DOF here; tiny/debug-ineligible targets skip effects. Thus
not every menu viewport consumes a DOF word. [T1][draw] [V1][viewports] [V2][cullcamera] [V3][cullrender] [V4][rdscene] [V5][postcall] [D4][post]

| Compute DOF case | Global RNG draws | Shader/passes |
| --- | --- | --- |
| Any successful invocation, either jitter flag value, any shape/quality | One `Math::randf` at line 128, before pass dispatch | `jitter_seed = randf() * 1000.0`, `use_jitter` and geometry parameters enter push constants. |
| Production Circle + Low | Same one; not one per pixel/pass/near/far component | Full-size blur-size generation, half-size circular gather (`blur_scale=4`), full-size composite. Circular gather never uses jitter. |
| Box/Hexagon | Same one shared across passes | Two directional gathers; Low/VeryLow half-size plus composite; Medium/High full-size. Quality steps `{6,12,12,24}`. When enabled, hash of UV + seed offsets UV. |

Evidence: [CPU setup and all compute passes][dof], [directional shader][directional], [circular/composite shader][circle], [push-constant layout/hash][dofinc]. The shader has
no DOF frame/history counter or TIME input. Circle uses fixed golden-angle steps and pixel-center offsets; other branches use fixed pass directions and half/full-size offsets
plus the seed hash. No additional CPU RNG draw occurs between these DOF passes. GPU shader arithmetic does not advance `default_rand`.

## Missing Environment: exact enablement guards and pilot limits

**No WorldEnvironment does not imply no DOF in this pinned Forward+ path.** The guards are separate from the Environment-dependent SSIL/glow paths:

| Boundary | Exact condition / consequence |
| --- | --- |
| Viewport / render buffers | Eligible viewport update; valid camera and 3D enabled. RD `render_scene` requires valid RD render buffers. Clustered `_render_scene` calls postprocess when its `RB_SCOPE_FORWARD_CLUSTERED` custom data is valid (normal view, not reflection-probe path). [viewport gate][viewportgate] [RD guard][rdguard] [clustered buffers][clusterguard] [postcall][postcall] |
| Camera attributes | `Camera3D.set_attributes` submits its RID; culling prefers camera attributes, otherwise scenario attributes. `camera_attributes_uses_dof` requires a stored attributes object, `(near_enabled || far_enabled)` and `amount > 0`. No Environment test. [camera setter][camerasetter] [attribute selection][attrselect] [DOF predicate][dofpredicate] |
| Postprocess | Non-null render data/buffers; reject reflection probe; target width/height each at least 8; `_debug_draw_can_use_effects`; reject transparent background for DOF. `can_use_effects && using_dof` enters blur. Forward+ inherits storage-capable implementation, selecting compute. [postprocess][post] [storage capability][storagecap] |
| Debug/effect flags | Disabled debug mode permits effects; Unshaded, Overdraw, Wireframe, VoxelGIAlbedo, cluster omni/spot/area/decal/reflection-probe views and InternalBuffer suppress them. Other listed debug modes retain effects. A material's **unshaded shading mode is not viewport Unshaded debug mode**. DOF is not gated by SSIL/SSAO/compositor flags. [debug switch][debugguard] |
| Luminance / Environment | Auto-exposure/luminance reduction follows DOF under its own attributes flag; missing luminance texture selects white exposure at tonemap. It is not a DOF prerequisite. Glow requires valid Environment + glow enabled; SSIL requires non-probe + valid Environment + SSIL enabled. [exposure/glow][exposure] [tonemap fallback][tonemapfallback] [SSIL gate][ssilgate] |

Environment resolution is camera override, then scenario Environment, then valid scenario fallback, otherwise an empty RID. `World3D` construction creates a scenario, not an
Environment. Runtime `SceneTree` does load a fallback when `rendering/environment/defaults/default_environment` is nonempty and loads successfully (default is empty);
applications can also assign one explicitly without a WorldEnvironment node. This toy config sets no such fallback. Editor 3D preview separately creates/attaches its own
WorldEnvironment; that preview is not automatically supplied to the running toy. A default clear color is not an Environment resource. [resolution][envresolve] [world
construction][world] [explicit fallback][fallback] [runtime fallback loader][fallbackloader] [editor preview][editorpreview] [preview creation][editorcreate]

Read-only pilot inspection: `artifacts/qa/dof-repeat-pilot/project/repro.gd`, `project.godot`, `main.tscn`, `fenced-captures/pilot.json` and `fenced-repeat-results.json`. The
fixture assigns Practical attributes directly to its current camera, amount `.075`, far distance/transition `12/10`, near `7.5/2`, Low/Circle; it creates no Environment,
performs no seed calls, and uses unshaded materials. The JSON records official matching engine hash, M5/Metal, distinct drawn-frame triples 20–22, 43–45, 66–68, 89–91, and
eight zero-difference repeat comparisons marked ineligible. These are owner-produced records, not a run or image inspection by this researcher. No DOF-off control is in the
script.

Source inference: nonphysical `.075 * 64 = 4.8` blur radius with Circle/Low's initial radius/scale 4 produces just **one off-center loop sample** (next radius is 5), plus
center and composite filtering. Fairly sharp checker appearance is therefore not proof that DOF was skipped. Near geometry around depth 10 is in focus by the authored ranges;
far geometry around 60 should receive far blur if the traced path executes. These are source predictions, not validation of the captured pixels. [radius setup][dof] [circular
loop][circle]

**Pilot equality establishes neither seeded production DOF nor effect presence.** Circle ignores jitter; this fixture also lacks High's SSIL history. Before interpreting
sharpness, the owner's full ArenaView should retain a diagnostic DOF-on versus DOF-off negative control with the same 2×MSAA, clocks, camera and replayed history; inspect a
genuinely textured out-of-focus region and confirm near/far guards and actual pass execution. Turning DOF off is diagnostic only; the production acceptance image keeps original
High DOF, jitter-on and 2×MSAA.

## Thread, signal and fence ordering

**Normal engine frame:** main-loop processing/message flushes precede `RenderingServer::sync()` (drain previous rendering commands), then `draw()`. `draw()` requires the main
thread, synchronously emits `frame_pre_draw`, then either queues `_draw` to the render pump or calls it inline. The pump flushes the command queue in order. Queue
insertion/consumption uses a mutex, providing publication/order for prior writes; it does not lock global RNG use during the draw. [T2][mainloop] [T3][schedule] [T4][queue]

**C# event evidence:** generated parameterless singleton events use `Singleton.Connect(..., Callable.From(value))`, without deferred flags. Native signal emission calls
non-deferred callables inline; `ManagedCallable` invokes the managed callback/trampoline, and `Callable.From(Action)` invokes the Action directly. Therefore ordinary
synchronous `FramePreDraw += handler` runs on the main emitting thread, before enqueue/inline draw—not on the render thread. Explicit deferred connection or an asynchronous
continuation after another await loses that immediate placement. `ToSignal` uses a one-shot native connection; its signal callback directly invokes the stored await
continuation. Do not generalize that to arbitrary `Task.WhenAny` continuations. [C1][events] [C2][emit] [C3][managed] [C4][delegate] [C5][action] [C6][awaiter] [one-shot
connection][awaiterconnect]

**Post-draw:** `_draw` ends the compositor frame, then invokes post-draw steps inline in single-thread mode, or schedules them with `call_deferred` in separate mode.
`_run_post_draw_steps` calls frame callbacks and emits `frame_post_draw`. The normal deferred route uses the main message queue, flushed by the main loop; the render pump shown
here installs no queue override. Post-draw is not a per-viewport token, and a delayed callback is not proof of which queued draw a later readback observes. [T1][draw]
[T5][postsignal] [T2][mainloop] [message queue selection][messagequeue]

| Seed placement | What source establishes | What is still required |
| --- | --- | --- |
| `_Process` | Before the eventual main-loop draw submission | Previous render may still be running; later process/deferred handlers may consume RNG. Not an isolated interval. |
| Immediately before `ForceDraw` | Before that call's pre-draw handlers and draw submission | `ForceDraw` itself does not first drain previous work. Drain before seed, control signal handlers/queued work and concurrent consumers; sync/read back after draw. |
| Synchronous `FramePreDraw` handler | Before this draw submission; normal main loop already synced previous work | Later signal handlers, scene/render work, other viewports and concurrent consumers still intervene. Manual `ForceDraw` lacks the normal loop's preceding sync. |
| `CallOnRenderThread(Callable.From(seedAction))` before draw | Queued FIFO on separate thread; flush/direct invocation if already on render thread | It orders against prior queued work, not against unrelated global RNG callers. Keep Action synchronous, alive and free of scene-tree access; validate actual packaged callback/thread behavior before use. |

The last row is supported by the exact C# callable bridge above and [`call_on_render_thread` implementation][rendercall], not an assumption that all C# APIs are render-thread
safe. Calling `ForceDraw` from a separate render thread is explicitly invalid (single-thread mode shares the main thread). There is no unresolved source-level “C# callback
automatically marshals to main” question: this bridge invokes directly. Runtime dispatch/lifetime behavior in the owner's exact build remains untested, as does absence of
competing RNG users.

`ForceDraw`/`ForceSync` bind to `draw`/`sync`. Here `sync` only waits for queued CPU rendering commands (or flushes them inline); do not infer a dedicated GPU completion wait
from its broader XML description. `ViewportTexture.GetImage` routes through `texture_2d_get`'s synchronous server wrapper to RD texture readback. For the ordinary non-CPU-read
texture path, RD adds the texture copy, flushes/stalls before mapping bytes. That supplies readback completion, but does not reset history or replay an earlier frame.
[T6][bindings] [T3][schedule] [T4][queue] [B1][viewportimage] [B2][imagewrapper] [B3][wrapmacro] [B4][textureimage] [B5][readback]

## Concrete interference and history hazards

- **Other views:** child-first topological viewport order, sibling activation order, visibility/update modes and per-view count determine earlier DOF calls. An extra eligible
  DOF viewport consumes another word even with Circle. A transparent preview need not. Inventory actual draws, not scene node count. [V1][viewports] [D4][post]
- **Cosmetics/system callers:** `GPUParticles3D` eligible one-shot emission calls `Math::rand`; `CPUParticles3D` also uses it for point emission selection. These are concrete
  conditional hazards, not claims that this fixture contains those nodes. RD GPU particle processing takes its stored particle seed; it is not evidence of one global RNG call
  per GPU particle. Any `GD.Rand*`/global randomization, other signal listener, background producer or custom compositor callback must be accounted for. [P1][gpu] [P2][cpu]
  [stored particle seed][particles]
- Global seeding can alter engine cosmetics and any gameplay using Godot's global RNG, thereby changing scene state. Product authority's separate .NET `System.Random` stream
  (`Matches/LocalMatch.cs:99,114`) is not seeded by `GD.Seed`; that does not protect all presentation or engine consumers. No whole-engine absence proof is claimed.
- **SSIL is independent history:** fixed rotation matrices are generated from pass indices; the shader picks them by pixel coordinates, not `Math::randf`. It samples
  reprojected `last_frame`; camera matrices update each render. History allocation clears a new texture; later draws copy the internal color and mip chain **before DOF**.
  Seeding does not reset these textures/matrices. Equal final RNG seed cannot repair unequal SSIL warmup inputs. [H1][ssconstants] [H2][ssshader] [H3][sscamera] [H4][sshistory]
  [H5][resolve]
- Each actual `_draw` calls compositor `begin_frame`: increments renderer frame, accumulates supplied frame step and sets scene/canvas time. `ForceDraw(false,0)` leaves that
  accumulated time unchanged but can consume DOF RNG and replace SSIL history without advancing gameplay. `Engine.GetFramesDrawn` increments in the normal main loop, so it
  alone does not count manual force draws. Readback can flush RD work; it does not itself call the scene/DOF draw chain. Record normal, manual, fallback and readback operations
  separately. [H6][beginframe] [T2][mainloop] [B5][readback]

## Smallest discriminating experiment — proposal only, owner executes

1. One full real ArenaView, one fixed camera/state/lighting/dimensions, High, native scale, 2×MSAA, Circle/Low, jitter flag **on**, SSIL on. Freeze authority,
   pose/interpolation, event ages, shader TIME and custom animation uniforms; record effective values. Finish material/effect warmup before the recipe. Recreate render
   buffers/scene or use a source-justified reset, then replay a fixed ordered warmup draw sequence. Merely waiting N ticks is not a history reset. Replay that reset/warmup for
   each trial; consecutive static frames alone may have different SSIL history. Inventory all active viewports.
2. Repeat the same recipe twice within one process and in two fresh processes, on the same GPU/backend/binary. First use no global reseed as baseline. Then compare seed S in
   `_Process`, immediately before explicit draw (first drain with `ForceSync`, then seed, then draw), and synchronous `FramePreDraw`; only one seed site active per trial. Use
   fixed draw ordinals, register post-draw observation before submission, perform ordered readback and reject an unplanned timeout/fallback draw. Do not stack a forced capture
   on an uncontrolled automatic draw. If the owner permits, compare single/separate rendering modes as diagnostics, never acceptance substitutes.
3. Perturb one variable: S→T; S then exactly one discarded `GD.Randi`; an extra known DOF viewport before the target; one additional zero-step full draw. Replay each from the
   same initial history. Also try the extra draw **before** the final reseed: this restores the target RNG sequence under the stated conditions, while retaining the changed
   history. This separates RNG position from history changes. Render-thread seed callback is a conditional follow-up only if the main-thread cases expose scheduling ambiguity.
4. **Controls:** compare a captured image to itself and two readbacks without a new draw; require exact decoded-pixel identity. Retain all differing pixels, coordinates and
   channel deltas—no invented tolerance. For production Circle, S/T or one-word changes are expected **not to change DOF sampling**. If they change pixels, investigate other
   consumers/state/history; do not label it DOF control. A positive jitter control needs either native push-constant evidence or, only as an explicitly permitted separate
   diagnostic, Box/Hexagon with jitter still on and 2×MSAA retained. The latter should respond to changed seed in textured out-of-focus regions and repeat under S; it cannot
   certify Circle acceptance. No seed contrast visible in a flat/in-focus region is inconclusive.
5. **Stop/interpret:** any unknown draw, clock, viewport, buffer reset or callback thread invalidates “controlled” provenance. If native seed values differ, resolve
   consumption/order before pixel comparisons. Equal seed values with unequal frames implicate another input/history/numerical source; do not loosen tolerances. Equal Circle
   pixels across seeds do not prove seeded jitter. Exact same-device replay supports only that recorded recipe; fresh-process failures require startup/history investigation.
   Production acceptance always returns to the original Circle High/2×MSAA/jitter-on fixture.

Stock APIs expose seeding and scheduling, **not the actual internal DOF seed or global RNG state at consumption**. Honest `jitterId` can describe “Circle, jitter flag true;
tagged shader does not consume jitter; uploaded seed unobserved,” but must not rename it “disabled” or claim a measured controlled seed. `historyId` must identify the real
reset and complete replay, or remain explicitly unknown and ineligible for controlled acceptance under the comparator's contract.

If stronger evidence is needed, the narrow seam is `bokeh_dof_compute` line 128 and each push-constant dispatch: observe actual float bits, shape/flag, render ordinal,
view/target identity and thread; correlate SSIL allocation/copy ordinals and camera history. A source debugger with suitable symbols or GPU capture may expose this without
modifying the engine; availability is unverified. Automated logging requires native engine instrumentation, not a stock `GD.Seed` property. Do not perform an extra RNG draw to
“log” the value. Observation should precede any proposed per-invocation seed override; neither is implemented or authorized here. No engine fork/upgrade or preset reduction is
recommended.

## Primary source ledger / review

Fetched exact tagged files from official raw GitHub; anchors below refer to those files. Source/text/link review only; the pilot reports the matching engine hash, but binary
correspondence was not independently audited and production reproducibility remains untested. All repository work is this note; source cache stayed in `/tmp`.

[tag]: https://api.github.com/repos/godotengine/godot/git/ref/tags/4.7.2-stable
[gd]: https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/glue/GodotSharp/GodotSharp/Core/GD.cs#L601-L604
[native]: https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/glue/GodotSharp/GodotSharp/Core/NativeInterop/NativeFuncs.cs#L579-L593
[interop]: https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/glue/runtime_interop.cpp#L1404-L1432
[mathcpp]: https://github.com/godotengine/godot/blob/4.7.2-stable/core/math/math_funcs.cpp#L36-L58
[mathh]: https://github.com/godotengine/godot/blob/4.7.2-stable/core/math/math_funcs.h#L681-L690
[pcg]: https://github.com/godotengine/godot/blob/4.7.2-stable/core/math/random_pcg.h#L54-L79
[pcgimpl]: https://github.com/godotengine/godot/blob/4.7.2-stable/thirdparty/misc/pcg.cpp#L6-L25
[dof]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/effects/bokeh_dof.cpp#L93-L297
[directional]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/shaders/effects/bokeh_dof.glsl#L70-L112
[circle]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/shaders/effects/bokeh_dof.glsl#L180-L230
[dofinc]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/shaders/effects/bokeh_dof_inc.glsl#L1-L42
[post]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/renderer_scene_render_rd.cpp#L455-L526
[draw]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/rendering_server_default.cpp#L76-L134
[viewports]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_viewport.cpp#L798-L929
[cullcamera]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_scene_cull.cpp#L2670-L2783
[cullrender]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_scene_cull.cpp#L3706
[rdscene]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/renderer_scene_render_rd.cpp#L1504
[postcall]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.cpp#L2545-L2551
[mainloop]: https://github.com/godotengine/godot/blob/4.7.2-stable/main/main.cpp#L5044-L5096
[schedule]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/rendering_server_default.cpp#L415-L465
[queue]: https://github.com/godotengine/godot/blob/4.7.2-stable/core/templates/command_queue_mt.h#L136-L264
[events]: https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/editor/bindings_generator.cpp#L3319-L3337
[emit]: https://github.com/godotengine/godot/blob/4.7.2-stable/core/object/object.cpp#L1288-L1294
[managed]: https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/managed_callable.cpp#L92-L101
[delegate]: https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/glue/GodotSharp/GodotSharp/Core/DelegateUtils.cs#L71-L95
[action]: https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/glue/GodotSharp/GodotSharp/Core/Callable.generics.cs#L31-L44
[awaiter]: https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/glue/GodotSharp/GodotSharp/Core/SignalAwaiter.cs#L33-L65
[awaiterconnect]: https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/signal_awaiter_utils.cpp#L34-L45
[postsignal]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/rendering_server_default.cpp#L215-L230
[messagequeue]: https://github.com/godotengine/godot/blob/4.7.2-stable/core/object/message_queue.h#L148-L159
[rendercall]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/rendering_server_default.h#L1212-L1223
[bindings]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/rendering_server.cpp#L3569-L3578
[viewportimage]: https://github.com/godotengine/godot/blob/4.7.2-stable/scene/main/viewport.cpp#L195-L202
[imagewrapper]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/rendering_server_default.h#L239
[wrapmacro]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/server_wrap_mt_common.h#L153-L165
[textureimage]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/storage_rd/texture_storage.cpp#L1882-L1900
[readback]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/rendering_device.cpp#L2673-L2739
[gpu]: https://github.com/godotengine/godot/blob/4.7.2-stable/scene/3d/gpu_particles_3d.cpp#L49-L52
[cpu]: https://github.com/godotengine/godot/blob/4.7.2-stable/scene/3d/cpu_particles_3d.cpp#L938-L952
[particles]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/storage_rd/particles_storage.cpp#L860-L868
[ssconstants]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/effects/ss_effects.cpp#L73-L99
[ssshader]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/shaders/effects/ssil.glsl#L203-L309
[sscamera]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.cpp#L1448-L1480
[sshistory]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/effects/ss_effects.cpp#L366-L420
[resolve]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.cpp#L2435-L2453
[beginframe]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/renderer_compositor_rd.cpp#L123-L136

[dofpredicate]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/storage/camera_attributes_storage.h#L93-L97
[camerasetter]: https://github.com/godotengine/godot/blob/4.7.2-stable/scene/3d/camera_3d.cpp#L545-L565
[attrselect]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_scene_cull.cpp#L3689-L3694
[viewportgate]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_viewport.cpp#L385-L388
[rdguard]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/renderer_scene_render_rd.cpp#L1359-L1366
[clusterguard]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.cpp#L1704-L1719
[storagecap]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/renderer_scene_render_rd.cpp#L1164-L1166
[debugguard]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/renderer_scene_render_rd.cpp#L998-L1044
[exposure]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/renderer_scene_render_rd.cpp#L554-L580
[tonemapfallback]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/renderer_scene_render_rd.cpp#L679-L689
[ssilgate]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.cpp#L1831-L1839
[envresolve]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_scene_cull.cpp#L3722-L3740
[world]: https://github.com/godotengine/godot/blob/4.7.2-stable/scene/resources/3d/world_3d.cpp#L185-L187
[fallback]: https://github.com/godotengine/godot/blob/4.7.2-stable/scene/resources/3d/world_3d.cpp#L106-L118
[editorpreview]: https://github.com/godotengine/godot/blob/4.7.2-stable/editor/scene/3d/node_3d_editor_plugin.cpp#L10283-L10307

[fallbackloader]: https://github.com/godotengine/godot/blob/4.7.2-stable/scene/main/scene_tree.cpp#L2181-L2212
[editorcreate]: https://github.com/godotengine/godot/blob/4.7.2-stable/editor/scene/3d/node_3d_editor_plugin.cpp#L11135-L11143
[callbacktable]: https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/glue/runtime_interop.cpp#L1835-L1843
