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
