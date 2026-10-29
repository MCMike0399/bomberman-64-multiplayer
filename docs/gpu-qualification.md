# One-time native GPU qualification

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

The current diagnostic batch is `d9ad65c301eafeb252b3b50bd1d54ac4c46bcc63`, not
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
affected-path GPU evidence to be reconsidered. A later paid campaign requires its
own available resources and authorization; this one-time approval must not become
automatic recurring spend or an impossible free-software2x gate mislabeled green.
Final promotion still requires then-current main integration, an unused release
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
