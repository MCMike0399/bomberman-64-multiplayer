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
It saves the committed preferences and flushes their file, cancels/disconnects
pending preparation draw callbacks, disables further rendering and synchronizes
submitted renderer commands. The SDK's existing producer cleanup remains in use.

`ShutdownDrain` inserts FIFO fences into the pinned engine's low-priority queue.
It derives the slot count from the unchanged desktop pool settings and processor
count using 4.7.2's published source policy. Every slot must reach a fence before
they are released; the game then reclaims its own fence task IDs. The wait has a
15-second bound and emits an explicit failure rather than claiming a drain when
the bound expires. No private-memory inspection, timeout retry or process kill
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

Phase B still requires current packaged Windows/Linux native early-quit and
normal Low/High controls, plus the actual updater handoff/rollback checks. Hosted
software rendering in those functional tests is not graphics qualification.
