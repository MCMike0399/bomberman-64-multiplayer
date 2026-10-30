# Worker-pool shutdown SDK regression

This probe runs the actual game `ShutdownDrain.cs` through a linked source file.
It uses the unchanged official Godot4.7.2 .NET SDK and native OS-delay tasks,
without a renderer or game scene. No watchdog or forced successful exit is used.

Run from the repository root:

```sh
python3 tools/qa/shutdown_probe.py --godot /path/to/Godot --output artifacts/qa/new-shutdown-probe
```

The unmodified low-priority pending-work control must reach its quit marker and
hang; the harness kills that owned failed control after eight seconds. The fixed
control inserts the game's FIFO low-priority fences, asserts every original task
is complete before reclaiming it, and must exit zero without engine errors.
This is scheduling evidence, not graphical or final packaged-game qualification.

The distinct resource-finalizer pair uses `--resources-only`. Its pending control
deliberately collects 20,000 unrooted material wrappers while keeping 1,000 native
resources alive, then quits without waiting for finalizers. It must retain the
known native lifetime failure. The fenced control links the actual bounded
`ManagedFinalizerDrain.cs` and must exit zero without engine errors. Its expected
failed control is never a clean product exit, and this SDK pair does not replace
the actual game's headless or installed lifecycle gates.
