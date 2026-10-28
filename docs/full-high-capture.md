# Controlled full High ArenaView capture

`visual-parity` builds the real production `ArenaView` for Up & Down at Night,
using its ordinary camera, meshes, models, lighting, water and shaders. High stays
at native scale with 2×MSAA, SSAO, SSIL, Circle/Low DOF with the jitter flag on,
premium detail and scene-reflecting water. It does not use the software-only
MSAA-off contrast in the older water diagnostic.

This is a rendering fixture, not evidence that an input/network journey happened.
The separate actual-package crossplay and authority replay establish those
boundaries. Captures prove only their recorded package/device/API/configuration;
they cannot establish arbitrary-driver or physical-display equality.

## Recipe and draw accounting

One seeded public `LocalMatch` advances to absolute tick120. Its snapshots drive
the production arena; the presentation callbacks advance118 steps at1/30s after
constructor priming. Node processing is then disabled. This freezes poses,
interpolation, scenery, ambient/weather uniforms and event ages. Selected spatial
shaders replace the TIME token with a fixed4s fixture uniform, in place, and retain
both original/active code hashes. That QA clock substitution is explicit; the
production files and ordinary shader behavior are restored afterward.

Each trial creates a new SubViewport, OwnWorld3D, ArenaView, camera and render
buffers. A preflight warms the same full scene before measured trials. Each
measured trial performs exactly16 planned zero-step draws. For every draw the
main thread flushes Node3D transforms, drains prior rendering commands, schedules
one target update, calls `ForceDraw(false,0)`, drains CPU rendering work and reads
the target synchronously before disabling it. The readback supplies GPU-copy
completion. No await occurs between enabling and disabling the target, and no
automatic/fallback target draw is used. Both main-thread placement and exactly
one pre-draw signal per invocation are checked. Every manual invocation is
recorded; normal engine frame counters alone are not used as a draw count.

The initial once-mode/PostDraw prototype timed out and was retained. The node's
mode getter was not a trustworthy completion signal. A later manual prototype
initially captured stale transforms; visual review rejected the partial arena.
Explicit Node3D transform flushing precedes the accepted full-scene recipe.
Those failed prototypes are not qualification receipts.

The two baseline trials recreate their buffers/history and replay the same
recipe. Baseline A also performs two reads without another draw. The fresh
process repetition must preserve the same conditions. SSIL history is measured
through these actual buffer resets/draws; a matching label or global RNG seed
alone is not a reset. Tagged source establishes Circle ignores the jitter seed;
no reseed, shape change or jitter disable is needed or applied.

## Measured inputs and manifest binding

The producer retains actual storage properties for scene nodes and the resource
graph, including mesh arrays, materials, shaders, font/texture inputs, transforms,
environment and camera attributes. Primitive and packed-array values use Godot
Variant bytes without numeric rounding; large binary input data is retained in
an inventory file with offsets, lengths and SHA256. Resource alias references use
deterministic traversal indices, not instance IDs. Local node names/owners and
scene-unique IDs are locators rather than render inputs and are excluded.
Unsupported stored object/RID inputs prevent package-manifest emission.

Descriptions also record actual viewport settings and the deterministic global
`GraphicsQuality.Apply(High)` policy; stock APIs do not expose getters for every
global quality setting. Original production source is unchanged. The captured
image is raw8bit LDR viewport data after production Filmic processing, with no
additional color conversion or physical-display claim.

Source runs write conditions, inventories, images and observations but do not
invent package provenance. To emit [comparer manifests](visual-parity-comparator.md),
`--parity-provenance=/absolute/file.json` must supply the exact source revision,
actual archive SHA256, archived engine executable SHA256 and entry assembly SHA256.
The producer checks executing source, loaded entry MVID/file hash and actual
engine executable bytes before emitting each PNG-bound manifest. The template
hash describes exported engine bytes, including embedded payload where present.
API version and OS build are recorded explicitly; a separately measured vendor
driver version must not be inferred from that string.

## Required comparisons and effect controls

The native runner installs the actual EXE, FUSE AppImage or DMG in disposable CI.
It requires matching-release/renderer receipts, positive assertions, zero engine
errors and complete captured images. Two fixed fresh processes then require:

- Exact manifest-checked equality for two readbacks of one draw.
- Exact equality for fresh target/history baseline A versus B.
- Exact equality for baseline A across fresh processes.
- DOF-off diagnostic: a genuinely out-of-focus scenery ROI must change while
  an interior focused-stone ROI remains exactly unchanged.
- Reflection-off diagnostic: the water ROI must change while far scenery remains
  exactly unchanged. A difference elsewhere cannot substitute for a mirror.

Controls keep the original MSAA and all other features, replaying fresh histories;
they are deliberately defective diagnostic variants and get no acceptance
manifests. There is no SSIM, tolerance, blur, rescaling, alignment or hidden mask.
The entire image is always compared. Three ROI rectangles add specific feature
evidence rather than replace whole-image differences.

Native jobs are armed only on the isolated QA branch when
`BLAST_VISUAL_RUNNER_SHA` equals the exact pushed runner SHA;
`BLAST_VISUAL_SHA` selects the immutable compiled private payload. Source runs use:

```sh
dotnet build Bomberman64Multiplayer.csproj --nologo -m:1
python3 tools/playtest/run.py --scenario visual-parity --desktop shared --godot /path/to/Godot
```

Announce the owned windowed desktop run first. No performance/physical-controller
claim follows from this fixed-step scene. The first M5/Metal source trials show
exact same-draw, fresh-target and fresh-process images and matching input
inventories; DOF/reflection controls visibly change their intended regions.
Those source diagnostics remain unqualified until actual package provenance is
bound and native/environment-specific results are inspected. Native target
hardware gaps and unresolved High/Mesa behavior stay explicit.

## First frozen-package measurement (3da1eac, 2026-10-28)

Two fresh processes of the actual Mac package on the M5 produce manifest-checked
exact same-draw, fresh-target and fresh-process comparisons. Both feature controls
pass. This qualifies those compared files/configuration, not other platforms or
physical displays. The hosted run `40781067089` retains these distinct failures:

- Hosted Mac: exact same-draw/fresh-target; the original far rectangle accidentally
  included water bordering the island. Reflection removal changed87 water pixels
  there. The interior village rectangle `(440,0,280,24)` has2616 DOF-control changes
  and zero reflection-control changes; focused stone remains exact, water changes6759.
- Linux llvmpipe: exact same-draw/fresh-target; DOF removal changes4600 focused-stone
  pixels and reflection removal changes **zero** water pixels. The interior-village
  correction preserves both failures. Blur and missing reflections remain unresolved.
- Windows software D3D12: same-draw exact; fresh-target differs at **two pixels**,
  maximum channel delta2, despite matching measured inputs. Feature controls pass
  on retained images. No tolerance converts the repeat failure into a pass.

All three application processes complete9 assertions with zero engine errors or
warnings. The first collector stopped after its first failed comparison; the
revised collector retains all valid comparisons and the second fresh process while
preserving a failed verdict. These are new measurements, not edits to old receipts.
Cross-OS acceptance comparison is still ineligible: inventories differ in six
ArrayMesh fields between Mac/Linux (two attribute buffers, four vertex buffers).
Actual bytes remain retained; scene-generation differences cannot be hidden by
matching labels, rounded hashes or a raw-image diagnostic.

### Bounded Linux depth localization

[The standalone probe](../tools/qa/depth_probe.py) uses the checksum-pinned official
4.7.2 editor and three orthographic textured planes at camera depths5/10/20. A
post-transparent compositor reads existing raw depth samples0/1 and resolved depth
into a float32 storage buffer. It leaves `access_resolved_depth=false`, requests no
additional resolve and runs on the rendering callback thread. Actual formats,
sample enums, API/device/engine identity, complete buffer hash, plane samples and
reconstructed depth are recorded. The full buffer and images remain available.

The two fixed processes use2xMSAA and an explicitly defective MSAA-off control.
Both retain Circle/Low/jitterON, DOF and SSAO/SSIL. This is **resource localization
in a separate scene/editor**, not the shipped game's full High qualification.
Its exit0 proves clean complete collection, even if depth is wrong. Numerical
sample discrepancies remain visible. A synthetic half-depth regression verifies
that collection cannot conceal a resolve error. Truncated/unwritten output fails.
The frozen export rejects external path overrides, so it is not falsely described
as executing this standalone script. The actual-package fixture remains separate.

Run locally on an announced owned windowed desktop:

```sh
python3 tools/qa/depth_probe.py --godot /path/to/pinned/Godot \
  --driver metal --output artifacts/qa/depth-new
```

The Linux-only `depth-diagnostics.yml` runs only on the isolated QA branch and exact
`BLAST_DEPTH_RUNNER_SHA`. Its purpose is to distinguish raw samples, resolved depth
and later focus reconstruction before selecting a driver/engine remedy; no preset,
shader or production engine change follows from a subsystem's name alone.

The first Linux resource run (`40782793580`) records zero in **every** raw MSAA
sample and resolved pixel at post-transparent. Its MSAA-off control records the
expected depths at all three planes; M5 records those depths in both cases. The
checker planes remain visibly rendered. This is a bounded resource observation,
not yet a driver-versus-engine attribution. Tagged4.7.2 performs its final depth
resolve before this callback ([render order](https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.cpp#L2431-L2458)).

A third, separately labeled process adds pre-opaque and post-opaque observers,
retaining the post-transparent observer. All still leave resolve-request flags
disabled. This distinguishes already-invalid prepass samples from later loss and
also exposes observer-induced synchronization changes. It must not be treated as
a production fix if merely observing earlier makes the later output correct.
All three stages reproduce expected depths on the M5. Each stage requires its
own complete data; a missing stage fails collection. The initial missing-stage
prototype was rejected because mutating a returned compositor-array property did
not register the new effects; explicit setter publication resolves that fixture
bug. The original two-process Linux receipt remains unchanged.

The staged Linux run (`40783739068`) finds the same zero raw/resolved depth before
opaque, after opaque and after transparent. Every retained buffer hash matches
the original zero-depth buffer; adding earlier observers did not correct it.
The MSAA-off control remains correct. This narrows the remaining split to depth
production/preservation versus multisample depth fetch/view interpretation.

A fourth diagnostic process clears independently owned2xMSAA depth and color
attachments to `.75`, then reads both samples using the same compute sampler and
records complete buffers. These attachments are not scene inputs and cannot fix
or qualify the game. This separates a generic multisample-fetch problem, a depth
fetch/storage problem and a failure specific to the scene's depth path. On M5,
both samples of both clear attachments are exactly`.75` across all98304 pixels.
Scene observations are saved before the custom clears. A wrong clear readback is
retained explicitly rather than converted into a successful depth verdict.

The independent clear run (`40784541653`) also reads zero from **both depth and
color**2xMSAA attachments on Linux. Both clear attachments read exactly`.75` on
M5. This rules out a fault unique to the scene's depth-writing shader; generic
multisample resource/sample interpretation remains implicated.

The next capability control records native`vulkaninfo` and a separately labeled
4xMSAA process. Pinned Godot's Vulkan driver
[falls back to a lower supported sample count](https://github.com/godotengine/godot/blob/4.7.2-stable/drivers/vulkan/rendering_device_driver_vulkan.cpp#L2219-L2236).
If a device advertises1x/4x but not2x, the requested2x resource can therefore become
single-sampled in the backend while the higher renderer path still plans
multisample reads. This is a falsifiable explanation, pending the actual runner
capabilities and4x result. The4x process is diagnostic and cannot replace the
required original High2x witness or qualify an unsupported physical driver.
