# Native GPU qualification

## Current user acceptance and budget — 2026-10-29

Render inputs must be bit-identical across OSs: meshes, textures, shaders,
presets and controlled game state. Keep the strict decoded-pixel comparator and
all raw residuals. Same-device repeats must be exact where the driver permits;
otherwise characterize the residual. Cross-GPU differences must be no larger
in kind or size than the measured hosted-Mac versus DiveMac reference on the
same frozen scenes. Record the actual adapters and configurations; do not infer
untested physical hardware from a runner label. The envelope comes from those
measurements, never a chosen tolerance, alignment or mask. Feature negative
controls must prove every effect present at unchanged production quality.
The user reviews side-by-side images and heatmaps and explicitly signs off
before publication. This replaces the earlier cross-vendor exact-output bar;
historical strict-comparison verdicts and files remain unchanged.

The renewed authorization is at most $25 total across GPU campaigns, at most
three hours and four g4dn/g4ad Windows/Linux instances per campaign. Check the
shared G/VT vCPU quota before AMD use. Estimate before each launch and retain
resource lifetimes, costs and complete cleanup receipts afterwards. Carry prior
campaign spend in the running ledger and obtain approval before exceeding the
total. Exact account/profile/region, protected existing resources, ownership-safe
cleanup and independent TTL requirements below remain mandatory. The earlier
one-time authorization is superseded; its completed campaigns remain closed.

Hosted WARP/llvmpipe runs cover functional/headless, installation, updates,
startup/exit and networking. They do not qualify graphics or full graphical
tours. Use the local Mac GPU and explicitly authorized NVIDIA/AMD GPU campaigns
for those checks; hosted Mac provides the measured Mac-to-Mac baseline.

GPU use is an explicitly authorized, bounded campaign rather than a standing
release runner. `gpu-once.yml` is off by default: only the isolated QA branch,
exact `BLAST_GPU_RUNNER_SHA`, and unique `BLAST_GPU_RUN_ID` runner labels opt in.
Runner registrations are ephemeral. No workflow creates paid infrastructure or
authorizes future GPU spend. Mac stable continues independently during QA.

Before launch the operator freezes the selected source, portable archives, actual
native download containers, matching server, references and command scripts.
Private account/profile/region/ownership state stays in the operator's local
store. Every AWS call uses the explicitly selected profile and region. Existing
resources are references and protected inventory, never campaign cleanup targets.
The operator records each created ID and verifies ownership before deletion.

The historical diagnostic batch is `d9ad65c301eafeb252b3b50bd1d54ac4c46bcc63`, not
final main-integrated release bytes. Its native 1,154-tick replay has exact
observable equality across three OSs; independent math canaries differ and remain
reported. Its native builder traces identify actual pre-upload arithmetic drift.
Those bounded receipts do not qualify subsequent source or every GPU vendor.

The hardware job checks the actual initialized T4/driver/API and original2x depth
and clear controls first. Windows must run in the disposable interactive session
with WDDM, not LocalSystem session0 or WARP. Linux selects the NVIDIA Vulkan ICD.
It then runs the actual packaged full High fixture with native scale,2xMSAA,
Circle/Low jitter flag, SSIL/DOF/mirror, exact repeats and all feature controls.
Actual shipped replay, saved startups, updates/rollback and complete native
journeys follow only with fixed-lifetime headroom. Insufficient headroom is an
incomplete failed campaign, not a waived test. Raw errors, warnings, device
metadata, commands, hashes, images, complete traces and first differences remain
in the retained result bundle. Collector completion is not release approval.

Cleanup covers normal completion, errors, cancellation and timeout. A fixed AWS
expiry terminates exact owned instance IDs independently of guest/controller
health; it is not complete stack cleanup. A durable exact-creation journal,
request-token reconciliation and ownership-checked stack deletion handle the
remaining resources. Evidence collection is best effort on failure and cannot
veto independently ownership-authorized cleanup. Missing evidence prevents
qualification. Final verification independently checks every created ID, requires
termination rather than stop, rejects retained children/DELETE_FAILED, accepts a
retained DELETE_COMPLETE stack record only when its children are gone, removes
owned runner registrations, and reports protected-state differences without
reverting unrelated changes. No finish claim precedes that final receipt.

Fast Mac/per-change and free native runtime checks remain useful between campaigns.
Renderer, engine, shader/preset, procedural geometry or GPU binding changes require
affected-path GPU evidence to be reconsidered. Campaigns must remain within the
current user's explicit total budget and per-campaign scope. Main is integrated
into the QA branch now; a small final re-merge may be needed before promotion.
Final promotion still requires the integrated source, an unused release
identity, trusted exact-byte/latest-stable gates and protected publication/fleet
verification. Old-byte diagnostic receipts are not final-source qualification.

## Diagnostic T4 results — 2026-10-29 UTC

Run `40810215205`, runner `2ce8c8276425750366c321dae8b3db761c92bfba`,
tested the frozen diagnostic batch above. Both actual hardware devices passed
the original 2× known-depth and attachment-clear controls. Linux used NVIDIA
595.112.07/Vulkan; Windows used 596.86/WDDM/D3D12 in interactive session 1.
This closes the unsupported free Linux runner's depth investigation; it does
not establish equal output across APIs or all physical drivers.

Linux's installed AppImage passed both fresh High processes (15 assertions each,
no runtime errors or warnings), exact same-draw/target/process comparisons,
DOF/mirror controls and shipped replay. The campaign then timed out at the
first saved startup. A separate saved-startup attempt passed the four Low/Medium
menu cases and timed out at High fullscreen. Its retained PNG and `SMOKE_OK`
show that the smoke completed. Subsequent exact-ELF disassembly maps the sampled
main PC `0x42bfa9` to the first wait in `WorkerThreadPool::exit_languages_threads`,
called during `Main::cleanup`. This localizes that Linux sample to shutdown,
before scripting-language detachment; no heap/runlevel counters were retained
to establish why the barrier failed. Focused Low windowed/extraction and
fullscreen/FUSE/extraction controls passed, so neither fullscreen nor extraction
alone explains the failure. The original failed receipt remains failed.

Windows's installed EXE passed both fresh High processes (15 assertions each,
no runtime errors or warnings), same-draw equality and DOF/mirror controls.
Fresh targets differed at 11 and 13 pixels; fresh processes differed at 9 pixels,
all by at most one channel value. Their measured resource inventories were
identical. Exact repeatability therefore remains failed on hardware, separately
from the existing cross-platform mesh-input differences. Retained raw cross-process
controls also differ: DOF-off at 10 pixels and reflection-off at 6 pixels, each
with matching measured inventories and maximum channel delta 1. These offline
diagnostics remain unqualified (exit 3); neither effect is necessary for the
observed repeat variance, and disabling either is not an acceptance fix.
A saved Low fullscreen
startup printed its successful capture marker but did not exit within 240 seconds.
Later update and complete-native phases were not reached.

Startup timeout cleanup now kills the owned launcher/game process tree before
destroying its desktop. A retained real-process regression demonstrated the
previous surviving descendant and passes with the correction. This prevents
secondary X11 teardown errors from obscuring the original failure; it does not
fix or waive a native startup/exit hang. The two failed hardware job verdicts,
whole-image differences and raw native evidence remain mandatory diagnostics.

`startup-lifecycle.yml` is a separately armed, exact-runner-SHA diagnostic on
free native Windows/Linux runners. It reuses frozen archives and the original
native download containers: headless smoke, native Low/High fullscreen smoke,
and an explicit early-engine-quit control. Every case retains its verdict even
when another fails. Timeouts collect live Linux thread stacks or a bounded
Windows thread minidump before process-tree cleanup. The game/dump helper does
not inherit CI/provider/signing credentials. Headless and early-quit outcomes
are narrowly labeled controls; none of these receipts qualifies GPU graphics or
substitutes for the full saved-settings release matrix.

### Frozen shutdown controls — runner 837fa45

Run `40875730614` reused D9 unchanged. Both native Low/High fullscreen smoke
cases captured and exited zero on hosted Windows WARP and Linux llvmpipe, with
no runtime errors or warnings. Both headless smoke cases also exited zero; the
diagnostic parser incorrectly rejected their empty headless adapter field. The
retained native footer now has a red/green parser regression, while graphical
cases still require a nonempty adapter. Original receipts remain unchanged.

Both early-engine-quit controls exceeded 240 seconds. The Windows thread dump
and exact-template machine code establish this frame chain: the saved return
at RVA `0x3018e65` is the first condition-variable wait in
`WorkerThreadPool::exit_languages_threads`; its 72-byte frame unwinds to the
`Main::cleanup` caller at RVA `0x6b862`. This early-quit result shares the Linux
T4 sample's barrier, but does not establish every normal T4 shutdown's cause.
The early control records both its initial `--windowed` engine flag and saved
fullscreen preference; its effective mode at the hang was not measured.

The first Linux thread sample lacked executable-backed unwinding because root
GDB could not read the user's FUSE-mounted executable. The collector now supplies
the metadata/checksum-bound archive ELF explicitly. Additional read-only pool
observations are interpreted only for the two exact 4.7.2 template hashes in
`startup_lifecycle_state.py`; unknown templates refuse interpretation. They retain
runlevel/counters, queue heads and bounded task/worker fields, rather than guessing
a cause from idle threads. Linux GDB stops target threads; Windows observations
are explicitly non-atomic. No engine change or product shutdown fix is claimed
from the collector's source/mock checks. A selected early-quit rerun gathers the
missing state without repeating the already-completed smoke cases.


## Baseline refinement — user decision 2026-10-29

A single maximum channel delta is not an acceptance threshold. Compare complete
whole-image delta histograms and percentiles against the tighter of the measured
hosted-Mac and authorized physical Macintosh baselines. Keep all pixels and raw
heatmaps. Their spatial pattern class must also match: shading-precision texture
variation cannot excuse missing or shifted geometry. Feature negative controls
and the user's final visual approval remain mandatory.

Record actual adapters and chips: the hosted baseline reports Apple Paravirtual
device (Apple5), which is not evidence of a bare M1. Sin-based procedural noise
in water/caustic and stone shaders is a source lead for its structured variation,
not a proved cause from images alone. Do not change shaders for this campaign.
A precision-robust hash would change Mac pixels and belongs in the sign-off pack
as an optional user-decided improvement.

The user authorized checking AMD G4ad in `us-west-2` if unavailable in `us-east-1`.
Every command must specify the Sancus profile and the actual region. Verify STS,
regional protected inventory, offering and G/VT quota before allocation. The same
combined $25 budget, three-hour/four-instance limit, ownership/TTL safeguards and
complete per-ID cleanup apply across regions. If unavailable, report the gap and
proceed with the authorized NVIDIA scope; do not silently choose another type.

## Before the next paid allocation

Replay prior campaign failures locally before launch, using recorded response
shapes and the actual command builders. A fresh helper-only mock does not replace
the complete arming and registration path. The preflight must cover:

- Linux SDK installation into a runner-owned prefix, rather than `/usr/share/dotnet`.
- AWS IAM response wrappers, immutable role/profile identities and enrichment of
  missing logical resource names without replacing recorded physical identities.
- The game's actual health CLI contract (`--update-token`), its owned staging
  directory, and cleanup of both success and failure paths.
- Actual device naming across APIs: D3D12 and Vulkan can include different vendor
  prefixes for the same GPU. Retain both game receipts, driver/session information
  and package hashes; do not infer a second device from the prefix alone.
- Exact runner, payload, phase and target wiring through ephemeral registration,
  including the interactive Windows user and removal of registrations afterwards.
- A fixed cleanup start time, independent termination targets, failure-artifact
  export and ownership-checked deletion after interrupted provisioning or tests.

These checks prepare a bounded campaign. They do not grant another allocation,
qualify a renderer, or permit extending its fixed expiry. A collector finishing
successfully must still expose failed or incomplete technical results.

Run the health command/staging regression locally with
`python3 -m unittest discover -s tools/ci -p test_gpu_health.py`. It covers both
API commands, a failed game exit, missing health output and protection of another
owner's staging directory. It does not execute a graphics driver or installer.

The authorized personal Macintosh baseline reports **Apple M5 Pro**, while
DiveMac reports **Apple M5**. Keep that same-generation comparison separate from
the hosted Apple Paravirtual comparison; neither establishes another GPU vendor's
coverage.
