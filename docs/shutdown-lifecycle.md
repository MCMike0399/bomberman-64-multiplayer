# Bounded game-level shutdown

The game stops graphics preparation and drains queued low-priority work before
entering Godot's native cleanup. Export templates and authority physics are
unchanged. No watchdog or forced-exit implementation is currently enabled.

## Observed defect and producer

The retained Linux early-quit sample reached the first worker-pool pre-exit
barrier with both queues empty and only one of four idle workers acknowledged.
The Windows early-quit dump's checked native frame chain reaches the same first
barrier. A pinned-SDK probe with native OS-delay callables reproduces the hang
without game scenes or rendering: pending low-priority work hangs, while high
priority and explicitly drained work exit. These receipts do not prove that
every historical shutdown failure has the same cause.

The application does not issue threaded resource loads or worker-pool jobs during
ordinary play. The renderer does: pinned 4.7.2
`servers/rendering/renderer_rd/pipeline_hash_map_rd.h` submits background
`PipelineCompilation` jobs, including low-priority specialization jobs.
`shader_rd.cpp` separately submits high-priority `ShaderCompilation` groups.
The game's effect warmup and arena previews exercise these pipelines; first use
of a stage or graphics change can also create them during ordinary play.
Therefore the risk is not restricted to the explicit early-quit test. The late
hang samples contain no remaining tasks, so their exact earlier task names are
not recoverable; this attribution is source-traced, not a claim of a captured
pending task list at the instant of quit.

## Save, stop and drain

`ApplicationShutdown` owns normal window-close requests, game/update/test quit
requests and the final autoload exit used by engine `--quit-after` controls.
It attempts to save the committed preferences and flush their file, then cancels/disconnects
pending preparation draw callbacks, disables further rendering and synchronizes
submitted renderer commands. A failed preference write logs
`BLAST_SHUTDOWN_SAVE_FAILED`, still drains and exits unsuccessfully; it cannot
trap the user in an unclosable application. QA always rejects that marker and
the failed-save receipt. The SDK's existing producer cleanup remains in use.

`ShutdownDrain` inserts FIFO fences into the pinned engine's low-priority queue.
It derives the slot count from the unchanged desktop pool settings and processor
count using 4.7.2's published source policy. Every slot must reach a fence before
they are released; the game then reclaims its own fence task IDs. The wait has a
15-second bound and emits an explicit failure rather than claiming a drain when
the bound expires. The helper checks the pinned 4.7.2 engine revision before using that policy;
an unreviewed engine upgrade fails explicitly. No private-memory inspection, timeout retry or process kill
is used by this production path.

A structured `shutdown.json` records save-flush success, the derived pool/slot
counts, drain completion/timing and errors. Lifecycle QA requires that receipt,
matching source revision, normal process exit and no runtime errors. Missing
save/drain proof fails. `BLAST_SHUTDOWN_WATCHDOG_FIRED` or a fired-watchdog receipt
also fails, even if the process returns zero. Any future watchdog may only be a
post-save backstop and cannot supply the red/green evidence for this fix.

## Verification boundary

The SDK regression links the actual game helper, asserts that prior work is
pending before draining and complete before reclaiming it, and runs without a
watchdog. See [probe commands](../tools/qa/shutdown_probe/README.md).
Retained local results: pending control hangs at eight seconds; the fixed
control exits zero in about 0.69 seconds, with no runtime errors. Slot-count
controls 1/2/3 also complete. Source-game headless early/normal exits record saved
state and completed drains. These are scheduling/lifecycle checks only.

Phase B passed on source `81b344e` in native run `40906681115`: both Windows
and Linux SDK pending controls reproduced the hang, while the same helper drained
and exited normally. Actual installer EXE/AppImage early-quit, headless Low and
normal fullscreen Low/High cases all exited zero, with completed save/drain
receipts and no runtime errors or warnings. Both targets also passed signed-fixture
update handoff, deliberate failed-launch rollback and saved High/fullscreen update.
No watchdog was implemented or enabled. These hosted software-renderer checks
verify lifecycle and updater behavior; they are not graphics qualification or
qualification of later release bytes. Raw receipts are retained under
`artifacts/qa/startup-lifecycle-81b/`; `phase-b-gates.json` binds their hashes.


## Save-failure and Mac regression controls

`python3 tools/qa/shutdown_save_probe.py --executable PATH --source-project --output NEW_DIRECTORY`
creates a read-only preferences file and requires natural process exit 1,
the distinct save marker, completed drain and no watchdog. Omit `--source-project`
when testing a packaged executable. Its success means the expected failure was
handled correctly; it is never a clean product QA receipt.

The Mac-only `--qa-mac-native-quit=close-button` / `cmd-q-menu` controls invoke the
application's actual AppKit red button or Command-Q menu item on its main thread,
after a rendered capture. They do not call the game's quit function, send global
input, change privacy permissions, or claim physical keyboard delivery. Each
requires an owned absolute `--smoke-output` directory. The exported app must
exit naturally with its save/drain receipt. The updater fixture can additionally
require the current old process's shutdown receipt with
`BLAST_UPDATE_REQUIRE_OLD_SHUTDOWN=1`; leave this off for historical published
apps that predate the receipt contract.

## Managed resource finalizers — separate teardown boundary

PR #74's source headless run `40998681677` passed all 1,953 gameplay assertions,
saved preferences and completed the worker fences, then aborted in native C#
binding cleanup (`script_bindings.is_empty`) with unsafe-resource-reference
errors. This is later than the previously localized worker-pool barrier.

A small official-SDK control on the M5 host queues 20,000 transient material
wrappers while retaining 1,000 resources. Quitting with pending finalizers
reproduces a native mutex abort on .NET 10.0.11; waiting for finalizers exits zero.
The signature differs from the Linux assertion, so this is related resource
lifetime evidence, not an identical Linux reproduction. The tracked SDK's
`--resources-only` control links the actual game helper. Its deliberate failed
control is explicitly not a clean game exit.

`ManagedFinalizerDrain` collects and waits on a dedicated background thread, with
a five-second bound and no Godot calls on that waiter. Rendering cleanup and the
unchanged FIFO worker fence follow it. A failed waiter logs
`BLAST_SHUTDOWN_FINALIZERS_FAILED` and makes RequestQuit exit 1. Schema-2 receipts
require both drains, while historical schema-1 receipts remain readable. Runtime
QA rejects the marker even without an `ERROR:` prefix or with a nominally good
receipt. No watchdog hides either failure.

Local normal-case timings were about 9 ms for the M5 source headless Core
(3,907 finalizers at collection), and 12 ms for isolated Mac Metal smoke
(733; actual Apple Paravirtual adapter). Both exited zero without runtime errors.
The read-only-preferences control exited exactly 1, with both drains complete.
These observations are not packaged Windows/Linux proof; native CI remains
required. The subsequent exact CI Mac DMG package from PR #74 passed AppKit red
button and Command-Q quits on DiveMac, with finalizer times about 15 and 14 ms.
Current-runtime update/relaunch and failed-launch rollback also passed, with
old-process finalizer times about 6 and 10 ms. The update test uses a synthetic
older plist identity while preserving the current managed assembly bytes; it is
not a substitute for the main gate's actual latest-public updater baseline.
All owned temporary app bundles were removed after these controls.

## Historical Linux exit 127 remains unassigned

The retained `6094a71` Linux `tour-15` observation completed all 122 assertions,
then returned 127. Its runner has zero classified errors/warnings and the raw
runtime log contains only the Godot/Vulkan/llvmpipe startup header. There is no
native stack, finalizer signature or shutdown receipt for that observation.

Its sequence is compatible with a teardown failure, but the retained evidence
does **not** establish the pending-finalizer cause. Exit 127 is not the observed
native-abort signature by itself. Keep it unassigned; the new fence does not
retroactively qualify or explain that failed receipt. This classification used
only the retained summaries/log, without new runs or a retry waiver.
