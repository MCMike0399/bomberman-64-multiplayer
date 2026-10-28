# Desktop rendering and gameplay parity research

Date: **2026-10-28**. Research only; every experiment and work packet below is a
proposal, not an implemented or passing change.

Repository observations refer to `handoff/desktop-release-20261027` at
`5456a990be91685fb910f2ec9cc2ff2f82e302ff`. The implementation owner retains all
builds, diagnostics, native desktops, services and publishing. This research ran
none of those operations. Handoff measurements below were supplied by the parent;
their original images/logs were not independently remeasured. Public specifications,
tagged engine source and upstream issue/PR bodies were inspected directly.

## Conclusion and decision

Keep Mac authoring, one product simulation, one art/preset policy, the pinned
Godot engine, and Forward+ using Metal, Direct3D 12 and Vulkan. The existing
architecture supports that workflow. The missing proof is a controlled comparison
of the actual packages, followed by target-driver and crossplay evidence.
[Godot's renderer architecture][G1] separates Forward+ from its three API drivers;
selecting the same renderer preserves the intended feature set but does not
guarantee identical shader arithmetic or final pixels.

Treat the user's “pixel by pixel the same gameplay ... same graphics mechanics
everything” as a requirement to **preserve every feature and investigate every
image/state discrepancy**. Begin with exact comparisons. Do not replace that
requirement with a global similarity score or platform quality reduction. Universal
bit-identical images on arbitrary GPUs are not guaranteed by the relevant APIs:
their arithmetic, shader optimization and sampling contracts permit differences.
This is a limit on the guarantee, not evidence that any particular discrepancy is
acceptable. Exact equality can still be demonstrated for a specified scene and
device matrix. Any residual exception needs a localized explanation and explicit
acceptance; it cannot silently become “pixel identical.” [Vulkan arithmetic][V1],
[Metal specification][A1], [DXIL operation semantics][D1].

The highest-value next work is small: a repeatable capture manifest and exact diff,
a depth/MSAA micro-scene using the **same release engine**, a packaged tick-replay
probe, and a capacity-aware actual-package crossplay rendezvous. The long graphical
tour remains integration coverage. WARP/lavapipe results are valuable functional
evidence but cannot certify physical Windows/Linux drivers or playable performance.
[WARP][D3], [Mesa source tree][M1].

## Verified repository observations

Line references identify the inspected revision; they may move during concurrent
implementation. Links point to the owning files rather than transient artifacts.

| Boundary | Verified source | Implication |
| --- | --- | --- |
| Engine and managed target | [Bomberman64Multiplayer.csproj:1](../Bomberman64Multiplayer.csproj) selects `Godot.NET.Sdk/4.7.2`; [Directory.Build.props:7](../Directory.Build.props) selects `net10.0`; [install_godot.py:13](../tools/ci/install_godot.py) pins official editor/template archives and verifies SHA-512. | The engine pin is real. Check a package's engine hash as well as its displayed version. |
| Runtime reproducibility | [.github/workflows/desktop-candidate.yml:111](../.github/workflows/desktop-candidate.yml) and other workflows request `10.0.x`; no root `global.json` exists. [package-desktop.py:37](../tools/packaging/package-desktop.py) reads the included runtime's servicing version from `runtimeconfig.json`. | `net10.0` is not an exact SDK/runtime patch pin. Record the actual toolchain/runtime in every compared artifact; propose pinning them for a candidate qualification. |
| Identity | [Release.props:4](../Release.props) contains `0.44.0`, build `54`, protocol revision `6`. | Source identity is not reservation, publication or qualification. Keep the parent's fixed v0.43 diagnostic baseline separate from final latest-stable update evidence. |
| Renderer | [project.godot:29](../project.godot) selects Forward+, Metal on Mac, D3D12 on Windows and Vulkan on Linux/BSD, with 2× MSAA. [export_presets.cfg:15](../export_presets.cfg), `:46`, `:75` select Mac arm64 and Windows/Linux x86_64. | OS/API comparisons also cross CPU architectures. Record the actual initialized driver to detect fallbacks. |
| Presets | [GraphicsQuality.cs:13](../scripts/rendering/GraphicsQuality.cs), `:47`, `:59`, `:65`, `:68`, `:74`, `:90`: 2× MSAA everywhere; High/Medium native scale; Low 75%; no screen-space AA/TAA; debanding on; High SSIL and near/far DOF; DOF jitter on. | A preset name or matching mean color cannot prove that the premium effects actually rendered. TAA being off does not remove all stochastic/history concerns. |
| Color and materials | [ArenaView.cs:106](../scripts/arena/ArenaView.cs) selects Filmic tone mapping. [ArtMaterials.cs:70](../scripts/rendering/ArtMaterials.cs), `:234` and the Blizzard/Garden/Greedy/Ambient variants use sine-based procedural hashes; water uses `TIME`. | Fix exposure, shader clocks and encoding; procedural speckle may amplify small transcendental differences. This is a hypothesis to measure, not permission to change the art. |
| Existing water witness | [WaterSurfacePlaytest.cs:38](../scripts/testing/journeys/rendering/WaterSurfacePlaytest.cs) freezes water time by modifying the existing shader resource; `:68` compares reflections on/off. At `:74` it disables MSAA for that comparison on detected software renderers. | This is a useful diagnostic and negative control. Its green reflection result does **not** establish the production High+2× MSAA combination. The categorical Mesa-cause comment at `:70` exceeds the evidence supplied at handoff. |
| Capture | [PlaytestDriver.cs:322](../scripts/testing/PlaytestDriver.cs) normally reads the viewport, but an X11 native popup switches to cropped desktop pixels. `:338` waits for post-draw with a force-draw/sync fallback. | Compare images from the same capture surface and encoding. Popup desktop captures are a separate native UI evidence class. Record fallback draws because they can change animation/random/history consumption. |
| Local authority | [LocalMatch.cs:12](../src/Bomber.Game/Matches/LocalMatch.cs), `:70`, `:99`, `:111`: 30 Hz ticks, seeded `System.Random`, time derived from tick count. [PeerMatch.cs:18](../src/Bomber.Game/Matches/PeerMatch.cs), `:36`, `:74` uses the same `BlastSession` with a tick-derived clock. | There is a good replay seam without involving Godot physics. Shared code and seeded time are prerequisites, not a demonstrated cross-OS deterministic-state result. |
| Simulation | [BlastSession.cs:46](../src/Bomber.Game/Simulation/BlastSession.cs) computes bot directions with `MathF.Sin/Cos`; `:73` stores players in a dictionary; `:238` iterates players and `:240` converts tick duration to float. Snapshots broadcast every other tick at `:262`. | Compare native float bits and ordered events at the authority boundary; inspect the first divergence. Do not confuse equal JSON formatting with full hidden-state equality. |
| Inputs and clocks | [LocalSession.cs:34](../src/Bomber.Client/Sessions/LocalSession.cs) caps catch-up at five ticks and consumes queued button transitions. [PeerRoomClient.cs:128](../src/Bomber.Client/Peer/PeerRoomClient.cs) measures `Stopwatch` time; `:170` caps catch-up; `:173` releases stale input using UTC time. [GameRoot.cs:320](../scripts/app/GameRoot.cs) samples/sends input and advances sessions in `_Process`. | One slow rendered frame can differ substantially from one authority tick or elapsed server second. Replay and realtime session tests answer different questions. |
| Native popup workaround | [RosterSelection.cs:30](../scripts/ui/menu/RosterSelection.cs), `:47` limits `ForceNative` to Linux/X11. The [input guide](agents/input-and-ui.md#input-path-and-fairness) describes popup/closing-frame input consumption. | Keep the current workaround scoped. Verify popup focus/input/capture separately from 3D shader parity. |
| Export and receipts | [package-desktop.py:57](../tools/packaging/package-desktop.py) validates source identity, builds/imports/exports and publishes the target updater. [desktop_validation.py:23](../tools/ci/desktop_validation.py), `:30`, `:97` defines software parts, the approved 120-second software-only lease and receipt identity keys. | Extend the existing inventory/qualification system; do not introduce an independent release pipeline or broaden the lease exception. |

## What the graphics contracts guarantee, and what they leave open

### Arithmetic, shader precision and compilation

Vulkan's SPIR-V environment specifies operation accuracy and permits floating-point
rearrangement unless constrained; `NoContraction` and floating-point execution
modes restrict particular behavior, not the whole graphics pipeline. Equal source
expressions can undergo a different number of rounding steps. `highp` is not a
portable “match Metal exactly” switch. [Vulkan SPIR-V environment][V1].

Metal Shading Language specification **4.1, dated 2026-06-04**, sections 1.6.5,
vertex-position invariance and chapter 8, documents fast-math reassociation,
operation accuracy and default contraction: a multiply/add can become a fused
operation. Position invariance requires its corresponding compiler option and is
about consistent position computation, not cross-vendor final-image equality.
The PDF was read as text in memory; this was not a renderer test. [Metal MSL][A1],
[Apple compile options][A2].

For D3D12, DXIL specifies operation semantics and accuracy bounds; its sine
instruction is not defined as one universally identical implementation. Microsoft's
HLSL `precise` rules constrain optimizations affecting the marked result, but do
not eliminate texture sampling/rasterization differences. The D3D11 floating-point
reference is useful background, not a claim that Blast Circuit uses D3D11 or its
HLSL compiler directly. [DXIL][D1], [HLSL precise][D2],
[Microsoft floating-point reference][D5].

The **4.7.2-stable engine source**, rather than current `master`, establishes:

- Metal converts SPIR-V with SPIRV-Cross `CompilerMSL`. The baked path passes
  `-fmetal-math-mode=fast`; the runtime source-compilation path selects
  `MathModeFast` or `FastMathEnabled(true)`. Preserve-invariance is applied when
  the reflected shader requires it. [Metal shader container, lines 175–178,
  323–326 and 713–714][E1], [Metal driver, lines 1161–1165][E2].
- The D3D12 shader container has an NIR-enabled path from SPIR-V to NIR to DXIL.
  Do not prescribe DXC flags as though the product simply compiles hand-written
  HLSL. Confirm the exported template's enabled path before compiler experiments.
  [D3D12 shader container, lines 333–387 and 452–480][E3].
- Godot exposes shader precision qualifiers, defaults to full precision, and
  cautions about lower-precision behavior. No repository evidence here establishes
  blanket `NoContraction`/precise controls for every generated shader. [Shader
  language precision][G2].

**Inference for this game:** `fract(sin(dot(...))*43758.5453)` can turn a small
sine/rounding difference into substantially different fine noise. Therefore do
not assume all allowable API differences are one-channel-unit edge pixels. Probe
this exact function first if residual differences concentrate in stone, ice or
foliage. Replacing it with an integer hash or baked lookup is only a later,
separately reviewed art-preservation proposal; it changes the authored pattern.

### Rasterization, samples and depth

Vulkan exposes subpixel precision and whether standard sample locations apply;
MSAA sample counts alone do not specify every device's sampling behavior. D3D12
also has default/standard patterns and optional programmable sample positions.
Neither specification promises equality with Metal's entire raster pipeline.
First inspect edge coverage, sample positions and formats; do not immediately
blame arithmetic. [Vulkan rasterization][V2], [D3D12 sample-position spec][D4].

Native Vulkan depth/stencil resolve support is queryable. Sample-zero is required
by the depth/stencil-resolve properties contract; further modes vary by device.
However, **that is not enough to diagnose this Forward+ case**. [Vulkan resolve
properties][V3]. In Godot 4.7.2:

- `resolve.glsl`'s depth-only compute path fetches every MSAA sample, averages
  them and stores R32F (lines 42–49).
- Its combined normal/depth path uses a different selection algorithm; for 2×
  it chooses the larger depth sample (lines 114–115), consistent with the
  shader's nearer-sample intention under reversed depth.
- Forward+ explicitly invokes the shader depth resolve, including after color
  resolve (lines 2437–2439). Buffer format selection explicitly uses R32F for
  Forward+ storage-based resolve (lines 812–813).

[Resolve shader][E4], [Forward+ caller][E5], [render buffers][E6].

Consequently, an edge-only difference could involve coverage/resolve semantics;
large in-focus flat regions becoming blurred warrants examining the actual
depth samples, resolved depth, view-depth reconstruction and DOF inputs. Which
resolve pass reaches a consumer must be established by a frame/resource witness.
Reading source alone does not prove which GPU command produced the bad image.

### Color, temporal state and readback

Godot renders in linear space; sRGB color textures need the appropriate
`source_color` hint. Its pinned tone-mapping shader explicitly handles linear/sRGB
conversion and adds debanding before writing the LDR result. Compare like with
like, including texture formats, Filmic settings, exposure, white point, alpha and
the exact point in the pipeline where pixels were read. [Shader color handling][G2],
[4.7.2 tone-map shader, lines 230–237 and 935–951][E7].

`ViewportTexture` specifically warns that HDR-2D readback is linear. A blanket
extra gamma conversion can therefore fix one capture path while corrupting
another. Proposal: record the actual image format/HDR flags and verify the
conversion with known linear and sRGB swatches; compare decoded pixel channels,
not PNG compression bytes. Keep an SDR viewport baseline and test OS/display HDR
presentation separately if supported/claimed. Identical viewport data cannot
prove identical physical display color management. [ViewportTexture][G3].

The project's High DOF uses jitter, and the pinned `bokeh_dof.cpp` generates a
`Math::randf()` jitter seed per invocation. Freezing just the game seed and water
clock will not necessarily freeze this. Debanding is spatial in the inspected
tone-map path; do not automatically describe it as temporal noise. Establish
repeatability for each effect instead. [Bokeh DOF, lines 109–128][E8], [E7].

## Proposed visual comparison protocol

Use a small **rendering fixture** to isolate image parity, and retain ordinary
input-driven journeys to prove integration. Feeding a known authority snapshot
to that rendering fixture is legitimate if labeled as such; it must not be
reported as evidence that a real gameplay action succeeded.

1. **Identify the content and environment.** Record source SHA, release token,
   catalog hash, every target package/download/server hash, engine/template hash,
   runtime patch, OS build, CPU architecture, API, GPU/device/driver IDs and
   versions, display server and software/hardware status. Hash imported resource
   bytes and record their actual texture compression/mip formats. Mac exports
   can contain different texture variants; same source asset does not prove same
   sampled texels. Godot documents platform-dependent compression choices.
   [Image import][G4].
2. **Fix both authority and presentation inputs.** Save stage/rules, roster/IDs,
   seed, authority tick, full accepted snapshot and ordered presentation events.
   Save the simulation PRNG checkpoint or reconstruct it from the same ordered
   draws. Also fix camera transform/projection/near/far/offsets, interpolation
   buffers, poses, spring state, particle/weather/scenery state and light setup.
   `ArenaView`'s clock and stage processing are separate from water `TIME`.
   For the first fixture, replay from a clean start to avoid inventing a complete
   checkpoint serializer prematurely.
3. **Fix the output contract.** Use identical framebuffer pixel dimensions,
   window/DPI scaling, aspect ratio, 3D scale/upscaler, preset, shadow atlas,
   sampler filtering/mips, DOF parameters, exposure, tone mapper and color
   encoding. Keep UI and 3D dimensions distinct. Use ordinary battle camera plus
   clearly labeled close-up crops; no alignment, blur, rescaling or color fitting
   to make a failing candidate resemble the reference.
4. **Control time and histories explicitly.** Drive water, material, scenery,
   weather, animation and particle clocks from a fixture time. Reset/replay
   temporal histories identically, especially High SSIL. Record frame index and
   every render invocation. For DOF, first measure repeated production captures;
   then use a QA-only no-jitter control to isolate depth. That control cannot
   replace the production-jitter acceptance witness. A future deterministic
   jitter hook would require its own engine feasibility review.
5. **Warm and fence.** Use a defined scene/load/draw sequence that covers the
   relevant pipelines, then await post-draw and read back the designated target.
   Verify repeats rather than declaring an arbitrary number of warmup frames
   sufficient. Keep cold-process/first-use receipts separately; do not warm away
   a cold-start defect. The existing force-draw fallback must be visible in the
   manifest and not add unaccounted simulation/animation steps.
6. **Establish reference-repeat noise before comparing platforms.** Repeat the
   frozen scene in one process and in fresh processes on the same package/device;
   repeat on each target. Nonzero self-diff is a fixture/effect question first.
   Preserve individual frames, not only averages.
7. **Run exact decoded-image diff first.** Report equality, changed-pixel count,
   channel-delta distribution/maxima, connected difference regions and an image
   overlay. Whole-frame SSIM/means can be supplemental diagnostics only. Do not
   infer equal sharpness, reflections or mechanics from them.
8. **Classify every residual.** Partition declared ROIs for arena focus, DOF
   background, water reflection, contact shadows, transparent effects and UI.
   Propose tolerances only after same-device repeats and a causal experiment
   explain the discrepancy. A repeatable cross-device difference still needs
   explanation even if it exceeds no single global score. No arbitrary numeric
   pixel tolerance is proposed here. Record and review masks; never mask away
   entire failed features.
9. **Require feature controls and visual review.** Reflection-off must fail the
   reflection witness; DOF-off must fail background separation while the arena
   stays sharp; shifted focus must fail arena sharpness; lower render scale must
   fail fine-detail/edge checks; missing glow/SSIL/shadow or a removed blast must
   fail its own ROI/state witness. These are deliberately defective QA variants,
   never product presets. Review matched full-resolution frames and short
   time-indexed clips for motion, shimmer, occlusion and feel.

### Small initial scene set

These are proposed fixture families, not a new exhaustive platform checklist.
Use the existing geometry/materials wherever possible.

| Fixture | Why it distinguishes a failure | Required feature witnesses |
| --- | --- | --- |
| Depth calibration scene | Flat near/in-focus/far planes, a thin silhouette and a transparent water strip separate MSAA depth, focus and reconstruction. | Per-sample/resolved depth, sharp in-focus ROI, blurred near/far ROI, no-MSAA and no-jitter diagnostic controls. |
| Up & Down water at Midday/Night | Reuses the existing clocked column-reflection witness and adds upper-deck depth discontinuities. | Reflection on/off, unchanged 2× MSAA acceptance capture, shoreline foam, above/below-water blast occlusion. |
| Sunken Temple battle at grazing light | Combines terraced geometry, treads, toy highlights, readable contact shadows, arena focus and far scenery. | Stair separation, stable silhouette/material detail, unchanged character proportions, DOF background separation. |
| Blizzard plus a compact material/pose sheet | Stresses snow/ice premium noise, accepted weather, clearcoat, all pickup families, normal/red blasts and ghost/burnout transparency. | Freeze weather/pose/event ages; identify each effect and fine-detail ROI; short synchronized motion samples. |
| Dense menu/roster and miniature | Covers subviewports, output density, High startup surfaces and native popup composition. | Layout/preset preservation, all seats, real focus/confirm/back, separate popup desktop capture and root viewport evidence. |

High is mandatory in this set. Medium/Low controls verify that existing tiers are
still intentional and consistent, not replacements for High. Extend to all arenas
through the established tour and add a targeted fixture when a new feature needs
one. The first set need not multiply every lighting/preset/arena combination on
every edit.

## Same mechanics: what to prove separately

C# specifies float/double representations, but the language and shared assembly
do not promise universal game-state identity across runtime/architecture choices.
Microsoft explicitly notes that `MathF.Sin` calls platform C runtime functionality
whose exact results can differ by OS/architecture. `System.Random` does not promise
the same sequence across major runtime versions. These are concrete reasons to
test the current arm64/x64 packages, not evidence that they already diverge.
[C# numeric types][N1], [MathF.Sin][N2], [Random][N3].

Do not claim that the JIT arbitrarily fuses all ordinary C# expressions. Record
runtime/architecture/optimization configuration and inspect the first divergent
operation if a replay fails; platform math functions are already a plausible
seam in this code. No fixed-point rewrite is justified by the evidence available.

The product simulation is a pure .NET `BlastSession`, with local/peer 30 Hz
adaptors. A Godot physics determinism claim is unnecessary and unsupported.
`--fixed-fps` disables realtime synchronization for engine frames; it does not
virtualize `Stopwatch`, UTC, an independent server or transport callbacks.
[Godot command-line contract][G5], [.NET Stopwatch][N4]. A frame-based tap, fixed
authority input and 100 ms wall-clock tap are distinct stimuli.

### Proposed inexpensive packaged replay

Add a narrowly scoped QA entrypoint in the **actual exported application** that
uses its shipped `Bomber.Game` and explicitly advances `LocalMatch.Tick()` without
a renderer/server. Run the same immutable trace on each native OS, recording
runtime and payload hashes. Reuse existing managed fixtures to author traces;
do not rebuild a separate test assembly and call that package qualification.

Each trace should contain rules/seed/roster and **per-authority-tick intent plus
button edges in delivery order**. Cover movement/stairs/ledge falls, kick/carry/
charged throw/remote, red and bouncing bombs, blast damage, pickups/effects and
round transitions; include an AI/weather trace that exercises randomized decisions.
Start with a compact set around discontinuities, then extend using failures.

Compare two outputs:

- Exact canonical snapshot/event streams at equal authority ticks. Exclude only
  documented non-authoritative transport metadata such as diagnostic wall-clock
  timestamps. Map generated participant IDs to fixed fixture IDs before starting,
  not by deleting identity-sensitive game fields afterward.
- A QA-only full authority-state digest per tick, including unsent timers,
  cooldowns, pending input transitions, bomb/AI state and PRNG position. Encode
  floats by bits, integers with specified widths/endianness, strings with a fixed
  encoding, and maps in ordinal key/slot order. Preserve meaningful list/event
  order. Canonicalizing output must not conceal different simulation iteration
  orders; log those on the first mismatch.

The current wire snapshot is not a complete rollback checkpoint. `System.Random`
does not expose a public portable checkpoint API; do not reflect private runtime
fields. Initially reconstruct from seed plus the same typed draw sequence, and
record draw count/digest (optionally outputs) through a QA wrapper at the existing
context seam. If mid-run restoration becomes necessary, propose an explicit
versioned stateful PRNG separately, with proof that existing seeded outcomes are
preserved. Record cosmetic engine RNG separately from authority RNG.

A pass proves the trace/package/runtime matrix, not all possible future matches.
A failure should retain the first differing tick, field, operands and random draw.
If randomness/order matches but a trigonometric result differs, test a narrowly
controlled shared lookup/kernel only after demonstrating its consequences.
Never round all authoritative positions for the hash just to obtain a pass.

### Proposed realtime crossplay witness

Use the actual DMG/EXE/AppImage payloads and one exact matching server, one
independent game process per OS. First server-host a shared match: verify common
release admission, accepted inputs, equal authoritative tick/state/event IDs,
rules, shared lighting, results and reconnect. Compare the same received tick,
not screenshots taken at the same wall time. Guest presentation can legitimately
lag while still agreeing on authority. Then separately exercise peer hosting,
direct/relay transport and a host change with each supported authority OS.

Rendezvous should record bounded phases: runner scheduled → download verified →
renderer ready → all clients ready → room admitted → shared match → observations
collected. Start a match-phase budget only after all participants report ready;
bound the overall rendezvous too. Distinguish runner-capacity wait and download/TLS
failure from gameplay failure. Retry only bounded idempotent setup steps and retain
attempt logs; never retry a failed gameplay assertion until green.

Preserve the user-approved **120-second peer lease solely for software graphical
peer QA**; production, headless and hardware retain six seconds. Any peer receipt
using that exception must disclose it. The current shared Quick Play witness
requires cluster hosting; do not apply the peer-lease override to it merely
because its renderers are software. Pair peer QA with the ordinary six-second
hardware/host failure witness. The 1 s/frame injected eight-peer reproduction is not proof of
the three-peer Windows takeover cause, especially after the uncapped control
passed 3/3. A transport-ready download/barrier pass is not a shared-match pass.

## Upstream investigation: matches and non-matches

Issue/PR states below were queried on 2026-10-28. Reports are primary observations
by their authors, not independent reproduction here. Matching a title is not
enough to attribute Blast Circuit's defect.

| Upstream evidence | Version/path correspondence | Confidence and next use |
| --- | --- | --- |
| [Godot #87626][U1], release popup signal errors | Original report: 4.2/4.3-dev2, Manjaro X11, Forward+, AMD RX 6700 XT. Same `focus_entered`/`tree_exited` disconnect and duplicate-connect errors; editor/debug and non-embedded controls differ. | **Strong symptom/trigger match**, despite older versions. Supports retaining the release-template MRP and embedding control. It does not independently prove the exact 4.7.2 mechanism. |
| [Godot #123998][U2], callable hashing fix | Open/unmerged when queried. Author attributes these popup and other signal failures to strict-aliasing undefined behavior in callable hashing, exposed by GCC/LTO; explicitly references #87626/#89657. Tagged 4.7.2 still contains the `uint32_t*` hashing form. [Tagged callable source][E9]. | **Specific mechanistic lead**, much better than a generic focus bug. Do not present the PR as an available shipped fix. Keep ForceNative while qualification proceeds; a future engine comparison requires the same exported MRP and verified template hashes. |
| [Godot #89657][U3] and [#95100][U4] | #89657 is closed, milestone 4.8, with a comment reporting no reproduction on 4.8-dev4. #95100's defensive connection checks remain an open PR and its description acknowledges incomplete disconnect reproduction. | A closed issue is not proof of a landed patch in 4.7.2. Do not copy defensive connection guards or ignore engine errors as the default solution. No engine upgrade recommended here. |
| [Godot #113022][U5] / merged [#113130][U6] | 4.6 development Forward+ regression after #78598: invalid depth storage/uniform resources and missing DOF; fixed by restoring R32F storage resolve. Tagged 4.7.2 already has the relevant R32F selection. | **Related subsystem, not an identified missing fix**. Current excessive blur without that error signature is insufficient to match it. Inspect actual formats and resources. |
| [Godot #110383][U7] / [#80626][U8] | Mobile-renderer MSAA+DOF problems, including 4.4.1/4.5-rc and Linux NVIDIA/llvmpipe reports. Forward+ explicitly worked in #110383. | **Wrong rendering path** for this project. Useful warning against assuming all MSAA+DOF reports share a fix. |
| [Mesa #16607][M2] / merged [!45362][M3] | Reported Mesa 26.0.8 and main `af2f67b2`, aarch64/x86_64: maintenance8 `vkCmdCopyImage` depth/stencil↔color copies left multisample destinations other than sample zero unchanged. Fix commit `0f55ffbf03139be080f313615ad2a3e1c951145a`, merged 2026-10-20. | **Concrete but conditional lead**. Godot's inspected Forward+ depth resolve uses sampled-depth compute, not that copy operation. Only relevant if the failing resource path actually uses the affected copy. Actual runner Mesa/LLVM version and fix inclusion are unknown here. A merge date does not prove distro backport availability. |
| [Mesa !44490][M4] | Merged 2026-09-17; contains independent resolve support and a “blit depth” fix whose commit describes source/destination image dimensionality. | **Not a demonstrated match**. “Depth” in that blit description is image depth/dimensionality, not evidence of this DOF depth-value defect. |

Searches covered official Godot issue/PR APIs and tagged sources, Mesa's official
GitLab issues/MRs (`lavapipe depth`, `llvmpipe msaa`, `lavapipe resolve`, `Godot`)
and Mesa release notes. No directly established upstream cause/fix for the
reported **4.7.2 Forward+ llvmpipe in-focus blur and missing mirror** was found.
This is a bounded search finding, not proof that none exists. Mesa GitLab pages
were inaccessible through the web reader but their official REST API bodies,
commit messages and merge state were successfully read.

## Evidence matrix at this handoff

| Claim | Evidence available | Still unknown / cannot infer |
| --- | --- | --- |
| Unified rules and presets | Direct repository inspection above. | Exact cross-OS simulation bits or rendered feature parity. |
| Software MSAA interaction | Parent reports minimal-scene sharpness `.127 → .059` on llvmpipe with 2× MSAA, versus approximately `.127` on Apple Metal/MoltenVK; disabling MSAA only in the mirror probe produces 666 pixels versus Mac 663. | Numeric depth corruption location, actual Mesa version, physical Linux Vulkan outcome, or Mesa fault attribution. These measurements are not repeated here. |
| Mac Vulkan control | Parent's MoltenVK comparison. | Native Linux Vulkan/driver behavior. MoltenVK is Vulkan implemented over Metal. [Khronos MoltenVK][V4]. |
| Popup workaround | Parent's same-export-engine red/green PCK; scoped source; closely matching upstream reports. | New full-package qualification and all native input/display combinations. |
| Tour coverage | Parent reports 61 leaves/21 tours, Windows 22/Linux 5 parts; source preserves identity/coverage merging. | Final all-target pass. Passing subsets from run 40709838793/payload 7137418 cannot qualify diagnostic SHA `5456a99`. |
| Crossplay | Parent reports actual downloads reach renderer-ready; barrier blocked by a TLS failure and queued Mac runner. | A completed shared match, its authority/state comparisons, direct/relay paths and takeover. |
| Native rendering/performance | Software Windows/Linux functional evidence and parent Mac evidence. | Physical Windows/Linux driver matrix, sustained frame pacing and controller hardware. WARP ~0.75–1 FPS and llvmpipe ~3–7 FPS are handoff observations, not customer-device forecasts. |
| Stability | Parent reports isolated Linux exit 127 after passed tour and software/multiclient memory peaks. | Cause of exit 127; a general product memory leak. Retain process/runner/resource evidence without assigning either cause prematurely. |

## Prioritized experiments and bounded work packets

Each packet is proposed for the implementation owner. Keep existing failing
witnesses, serialize owned builds/desktops, and attach exact package identities.

### P0 — Make image evidence repeatable

**Scope:** extend the existing capture/manifest seam and one static rendering
fixture; add an offline decoded-image comparator. No preset changes.

**Experiment:** repeat a frozen High frame on Mac, WARP and lavapipe independently;
then compare platforms. Record shader clocks, draw count, camera/size/format,
DOF jitter and known history state. Include same-image identity and deliberately
missing-reflection/shifted-focus controls.

**Distinguishing outcomes:** self-diff identifies uncontrolled fixture state;
stable self-images with cross-API differences isolate rendering; control failures
show whether the comparator actually detects lost quality. Exit with exact diff
artifacts and classified ROIs, not a newly chosen permissive global threshold.

### P0 — Localize the High MSAA/depth fault

**Scope:** reuse the release-engine minimal scene and a small diagnostic material
or RenderingDevice probe. Begin with original engine and original driver. Product
High stays unchanged.

**Experiment:** controlled 1×/2× MSAA × DOF on/off, first with simple flat opaque
planes, then a silhouette and water. Keep near/far/projection fixed. Inspect
sample 0/sample 1, resolved R32F, reconstructed view depth and resulting DOF mask;
separate depth-only from normal/depth resolve where applicable. Compare the same
scene on native Linux hardware and the failing software ICD when available.

**Distinguishing outcomes:** wrong raw samples implicate their production or
fetch/view interpretation; correct samples but wrong R32F localize resolve or
synchronization; correct depth but wrong focus localizes reconstruction/DOF;
correct DOF but absent mirror localizes the water consumer. If only lavapipe fails,
it narrows scope without proving a driver defect. If resource capture shows the
maintenance8 copy in Mesa #16607, test that exact fix/version as a second step.
Otherwise do not chase it by title. Only after an isolated reproduction should
the owner propose a driver bisect, upstream report or narrowly reviewed engine
backport. No opaque patching or all-platform Vulkan switch.

### P1 — Find the first authoritative state difference

**Scope:** packaged replay entrypoint plus canonical digest at `LocalMatch` /
`BlastSession`, drawing on existing managed fixtures. No physics rewrite.

**Experiment:** replay identical tick inputs on actual Mac/Windows/Linux packages,
with runtime/architecture identity and PRNG draw digest. Repeat locally and in a
fresh process. Include the `MathF` bot-direction case.

**Distinguishing outcomes:** equal hashes establish bounded deterministic evidence;
first-tick field/PRNG/order differences separate initialization, RNG, math and
input scheduling. Fix only demonstrated causes. A presentation-only discrepancy
with equal authority remains a renderer/interpolation investigation.

### P1 — Complete a real shared match without queue-time ambiguity

**Scope:** improve the existing candidate crossplay phase receipts/rendezvous and
run three independent actual-package clients. No gameplay timeout waiver.

**Experiment:** separately time allocation, download, engine readiness, barrier,
admission and shared match. Verify server-hosted common ticks/events first;
then retain targeted direct/relay and takeover cases. Use bounded setup retries
only for identified transient setup errors.

**Distinguishing outcomes:** failure before all-ready is infrastructure/setup;
state divergence after admission is product/session evidence; stalled presentation
with advancing authority is a separate finding. Log accepted button-edge times
and each fixture's sole advance/drain owner's cadence. Keep the 120/6-second
lease distinction exactly as authorized.

### P1 — Close popup qualification on the actual native surface

**Scope:** retain X11-only ForceNative and the input guard; extend the existing
release-template MRP/real-menu witness if gaps remain.

**Experiment:** repeated open/select/cancel/reopen, mouse versus pad/keyboard,
focus loss/restore, correct closing-frame input consumption, target DPI/resize,
and correct popup capture. Compare embedded and ForceNative on the same release
runtime; editor remains a diagnostic control only.

**Distinguishing outcomes:** errors limited to embedded release behavior support
the upstream match; unintended parent activation identifies input routing; absent
popup in viewport but present on desktop identifies capture-surface mismatch.
Keep upstream #123998 as a future exact-MRP evaluation, not a release dependency
on an unmerged fix.

### P2 — Classify residual material noise and compile behavior

**Scope:** a diagnostic shader using the game's exact sine hash, plus material
ROIs; no global fast-math or noise replacement by default.

**Experiment:** record intermediate dot/sine/hash values and compare a fixed
coordinate grid across APIs. Separate unlit material output, lit result, and
tone-mapped output. Compare original shader and a narrowly controlled arithmetic
variant, preserving draw/resource order. Record both cold and warmed pipeline
behavior.

**Distinguishing outcomes:** hash divergence explains a speckle pattern but does
not authorize changing it; agreement before lighting narrows the discrepancy to
lighting/postprocess; agreement in linear output with differing PNGs identifies
encoding/readback. Any eventual common hash/table proposal needs Mac art review
and repeat comparisons at all presets.

### P2 — Add useful hardware coverage without slowing every edit

**Scope:** targeted jobs/receipts on already-authorized native GPU capacity,
then a full immutable candidate qualification. No provisioning performed here.

Start with Mac Metal, one physical Windows D3D12 device and one physical Linux
Vulkan device. Prefer a paired Windows/Linux GPU when available to separate OS/API
from GPU effects, then add a second vendor. A broad Windows/Linux claim needs
documented AMD, NVIDIA and Intel coverage or a clearly bounded support statement;
one device cannot certify all vendors. Exercise X11 and Wayland where each is
claimed. Cloud GPU jobs are virtualized evidence for that driver, not physical
input certification. GitHub documents Ubuntu/Windows Tesla T4 larger runners;
availability, entitlement, graphical-driver/session suitability and cost require
verification before selecting that option. [GitHub larger runners][C1].

**Distinguishing outcomes:** quick fixtures locate API/driver failures before a
long tour; real-time sustained runs distinguish playable frame pacing from
fixed-step correctness. Native input and focus observations remain separate.

## Mac-first development and release workflow proposal

Preserve one source/asset tree, exact engine/templates, release identity and
shared preset code. Godot supports command-line exports through named presets
and export templates. This repository already drives those operations and target
runtime publication. Keep Mac as the authoring/export host where supported;
leave target-native installer/signing/desktop checks to their appropriate jobs.
Do not confuse producing a Windows package on Mac with testing D3D12 on Windows.
[Godot export workflow][G6], [command-line export][G5],
[repository packaging](../tools/packaging/package-desktop.py).

Proposed cadence:

| Trigger | Smallest useful evidence | Promotion meaning |
| --- | --- | --- |
| Docs only | Links, file references, whitespace. | No builds, GPU or installation. |
| Rules/input/session change | Focused managed contracts and packaged cross-OS replay; affected native input/network journey. | Development feedback; not full-release qualification. |
| Material/preset/camera/render change | Repeatable Mac fixture plus affected D3D12/Vulkan fixture and feature negative control. | Early target failure before the expensive tour. |
| Engine/import/toolchain/packaging change | Startup, cold/warm rendering and real installer/updater rollback on each affected target, with targeted visual/replay probes. | Requires new exact-byte artifacts; old receipts cannot be relabeled. |
| Release candidate | Immutable all-target artifacts/server/downloads; complete tour coverage, targeted hardware parity, actual crossplay, native startup/update/rollback and identity checks. | Candidate qualifies only when every required receipt matches the exact content and declared environment. |

Keep current Mac stable delivery independent while desktop diagnostics remain in
the isolated candidate lane. When ready, integrate current main, select the final
identity, and qualify the resulting bytes against the **actual latest stable**
updater baseline. Do not reuse the fixed diagnostic v0.43 update receipt as that
final proof. For an all-platform release, shared-source changes cannot be published
as Windows/Linux parity merely because Mac qualified first.

Driver pipeline caches are not universal export artifacts. Godot distinguishes
portable intermediate shader compilation from target-driver pipeline compilation;
sharing the latter generally requires matching hardware and driver. [Pipeline
compilation guidance][G7]. Keep source/import caches and driver caches conceptually
separate. Key any reused cache by engine/template, shader/content, API, target
architecture and driver/device identity as appropriate; treat a cache miss as a
measured cold path, not a reason to alter quality. Retain existing first-use
warmup and resource lifetime checks. Use isolated QA cache locations or fresh
owned environments rather than deleting the developer's global driver cache.
Measure cold launch and warmed play separately, and retain compilation counters
with timings; neither a warmed correctness capture nor software frame rate proves
native performance.

## Proposed acceptance contract preserving quality

1. **Same game:** shared rules/assets/release token and exactly matching discrete
   authoritative outcomes in the replay corpus; no unexplained float-state
   divergence. Real crossplay proves accepted commands, common snapshots/events
   and normal session recovery on exact packaged clients/server.
2. **Same intended image:** High retains native scale, 2× MSAA, correct arena focus,
   background DOF, scene-reflecting water, premium materials, contact/shadow/bounce
   effects and all established animation/geometry. Every effect has a positive
   witness and a negative control. No OS-specific downgrade, Compatibility switch,
   blanket MSAA disablement or reduced effect budget to pass parity.
3. **Honest pixel evidence:** exact decoded-image comparison is always reported.
   Zero-diff claims include the specific scene/device/driver/time/color contract.
   Nonzero results remain visible and cannot be called identical. Any accepted
   numerical residual is localized, explained, repeat-tested and separately
   approved; no global SSIM threshold, mean-color score or broad mask substitutes.
4. **Honest environment evidence:** software functional checks, GPU-backed VM
   checks, physical driver checks and physical controllers are labeled distinctly.
   Required hardware gaps remain gaps. A software-only mirror control with MSAA
   disabled cannot satisfy the unchanged production High witness.
5. **Release integrity:** one immutable candidate identity connects all packages,
   actual downloads, server and receipts. No merging old passes with new payloads,
   no qualification from a partial tour/barrier, and no unexplained startup/exit
   failure hidden by a retry. Art approval remains the user's judgment.

No universal numeric tolerance, engine upgrade, custom engine patch, fixed-point
rewrite or new production timing policy is adopted by this document.

## References and source confidence

Links in the text lead to primary sources. `4.7` documentation describes that
release family; engine implementation claims use `4.7.2-stable` source. “Latest”
API specifications describe permitted behavior, not features proven enabled on a
particular runner. Apple MSL 4.1 describes the specification read on this date;
it does not identify the actual compiler/SDK of every exported template.

- **High confidence:** directly inspected project settings/control flow and
  tagged engine algorithms; documented absence of a universal pixel guarantee.
- **Moderate confidence:** upstream popup symptom correspondence and the specific
  callable-hash explanation as an upstream author's diagnosis, pending an exact
  release-template comparison.
- **Unresolved:** root cause of the reported Linux depth/DOF/mirror failure,
  actual runner Mesa/LLVM versions and physical Windows/Linux behavior; latest
  diagnostic run outcomes were not polled by this research task.

Documentation validation: the repository Markdown link checker passed, and its
link-resolution function was also run explicitly on this new, untracked file.
All reference labels resolve; whitespace review passed. No game build, gameplay
test, UI/VM launch, service operation or package qualification was run.

[G1]: https://docs.godotengine.org/en/4.7/tutorials/rendering/renderers.html
[G2]: https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/shading_language.html
[G3]: https://docs.godotengine.org/en/4.7/classes/class_viewporttexture.html
[G4]: https://docs.godotengine.org/en/4.7/tutorials/assets_pipeline/importing_images.html
[G5]: https://docs.godotengine.org/en/4.7/tutorials/editor/command_line_tutorial.html
[G6]: https://docs.godotengine.org/en/4.7/tutorials/export/exporting_projects.html
[G7]: https://docs.godotengine.org/en/4.7/tutorials/performance/pipeline_compilations.html
[V1]: https://docs.vulkan.org/spec/latest/appendices/spirvenv.html#spirvenv-precision-operation
[V2]: https://docs.vulkan.org/spec/latest/chapters/primsrast.html
[V3]: https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceDepthStencilResolveProperties.html
[V4]: https://github.com/KhronosGroup/MoltenVK
[A1]: https://developer.apple.com/metal/Metal-Shading-Language-Specification.pdf
[A2]: https://developer.apple.com/documentation/metal/mtlcompileoptions/mathmode
[D1]: https://github.com/microsoft/DirectXShaderCompiler/blob/main/docs/DXIL.rst
[D2]: https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/precise
[D3]: https://learn.microsoft.com/en-us/windows/win32/direct3darticles/directx-warp
[D4]: https://microsoft.github.io/DirectX-Specs/d3d/ProgrammableSamplePositions.html
[D5]: https://learn.microsoft.com/en-us/windows/win32/direct3d11/floating-point-rules
[N1]: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/types#837-floating-point-types
[N2]: https://learn.microsoft.com/en-us/dotnet/api/system.mathf.sin?view=net-10.0
[N3]: https://learn.microsoft.com/en-us/dotnet/api/system.random?view=net-10.0
[N4]: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch?view=net-10.0
[C1]: https://docs.github.com/en/actions/reference/runners/larger-runners
[M1]: https://docs.mesa3d.org/sourcetree.html
[M2]: https://gitlab.freedesktop.org/mesa/mesa/-/work_items/16607
[M3]: https://gitlab.freedesktop.org/mesa/mesa/-/merge_requests/45362
[M4]: https://gitlab.freedesktop.org/mesa/mesa/-/merge_requests/44490
[E1]: https://github.com/godotengine/godot/blob/4.7.2-stable/drivers/metal/rendering_shader_container_metal.cpp
[E2]: https://github.com/godotengine/godot/blob/4.7.2-stable/drivers/metal/rendering_device_driver_metal.cpp
[E3]: https://github.com/godotengine/godot/blob/4.7.2-stable/drivers/d3d12/rendering_shader_container_d3d12.cpp
[E4]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/shaders/effects/resolve.glsl
[E5]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.cpp
[E6]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/storage_rd/render_scene_buffers_rd.cpp
[E7]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/shaders/effects/tonemap.glsl
[E8]: https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/effects/bokeh_dof.cpp
[E9]: https://github.com/godotengine/godot/blob/4.7.2-stable/core/object/callable_mp.cpp
[U1]: https://github.com/godotengine/godot/issues/87626
[U2]: https://github.com/godotengine/godot/pull/123998
[U3]: https://github.com/godotengine/godot/issues/89657
[U4]: https://github.com/godotengine/godot/pull/95100
[U5]: https://github.com/godotengine/godot/issues/113022
[U6]: https://github.com/godotengine/godot/pull/113130
[U7]: https://github.com/godotengine/godot/issues/110383
[U8]: https://github.com/godotengine/godot/issues/80626
