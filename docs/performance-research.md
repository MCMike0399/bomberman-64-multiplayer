# Performance improvements without visual or gameplay changes

Investigated on 2026-10-26 against the v0.34 source integration. The target was
3024×1964, High, stable 120 FPS on an M5 Pro. **That FPS target is not achieved.**
The retained changes substantially reduce allocations and duplicate geometry,
with small or mixed frame-rate differences in the matched Release samples.
Godot remains 4.7.2. Presets, shaders, lighting, geometry, particle counts,
animation timing, physics, AI policy, tick rates and wire fields are unchanged.

## Retained changes and equivalence evidence

| Change | Evidence |
| --- | --- |
| Index complete procedural vertices after generating normals | Summed mesh vertex entries per character fall from 83,288 to 21,172. All 18 specimen hashes match after expanding indices into ordered positions, normals and UVs. This is not a measurement of unique GPU memory across shared mesh instances. |
| Cache unchanged HUD draw commands | A graphical regression fails before the fix and passes afterward. It covers names, equipment, suit, status, ghost/elimination/respawn, wins, animation settling and native resize. |
| Upload snow transforms in one reusable buffer | All 480 sampled updates through Low→High→Medium→High have identical transform hashes. Mean update time in the same QA VM was 0.00844 ms before and 0.00633 ms after; this microbenchmark is not native FPS evidence. |
| Pack cover state and reuse exact bot direction vectors | All 36 eight-player, three-seed/two-repeat snapshot hashes match across six arenas. Mean simulation/legacy-encoding tick cost fell roughly 2–18%, varying by map; Top Rules p95 did not improve. These paired measurements preceded the local-message change. |
| Retain immutable structured local messages | All 36 actual-local-session snapshot hashes match. Allocations/tick fell 29–32%; mean CPU timing improved slightly. Legacy text events/properties remain available and are checked for order and snapshot lifetime. |
| Reuse arena reconciliation buffers and frame factors | Cover cache invalidates on reset/stage changes; entity scratch collections clear per snapshot/reset. Interpolation uses the same calculations and accepted motion without repeated native position reads. |

Indexing preserves primitive order, including transparent ghosts. It follows
Godot's [SurfaceTool index contract](https://docs.godotengine.org/en/4.7/classes/class_surfacetool.html#class-surfacetool-method-index)
and [exact attribute comparison implementation](https://github.com/godotengine/godot/blob/4.7.2-stable/scene/resources/surface_tool.cpp).
Do not replace it with triangle-reordering cache optimization without separate
transparency validation. Face checks now traverse indices when present.

Snow retains all 72/152/240 particles and their existing integration. Its buffer
uses Godot's [three-row 3D transform layout](https://docs.godotengine.org/en/4.7/classes/class_multimesh.html#class-multimesh-property-buffer).
HUD invalidation uses the engine's [cached CanvasItem drawing commands](https://docs.godotengine.org/en/4.7/tutorials/2d/custom_drawing_in_2d.html).

Local messages use `JsonSerializer.SerializeToElement` rather than a string
serialization followed by parsing. The existing message buffer retains owned
immutable elements through `Clone`, consistent with Microsoft's
[lifetime/allocation contract](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.clone?view=net-10.0)
and [serialization API](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer.serializetoelement?view=net-10.0).
The server transport continues to use the kernel's existing serialization.

## Matched native Release comparison

Both apps use Godot 4.7.2 and the same self-contained .NET 10.0.12 runtime.
Measurements use 3024×1964, High, 1.0 3D scale, seed 64, one human plus seven
normal bots, three-second warmup and ten-second battle windows. Both retain
foreground focus, real bombs/explosions and all six arenas. The QA VM was stopped,
and the runner held its GPU ownership locks. No shader or engine experiment is
included in the candidate.

| Arena | FPS before → after | Frame p95 before → after | Allocations/frame before → after |
| --- | ---: | ---: | ---: |
| Sunken Temple | 56.7 → 57.1 | 18.23 → 18.14 ms | 59.8 → 32.2 KiB |
| Rock Garden | 78.7 → 79.3 | 13.41 → 13.23 ms | 36.9 → 18.7 KiB |
| Up & Down | 57.4 → 56.7 | 18.04 → 18.12 ms | 49.4 → 24.5 KiB |
| Top Rules | 82.8 → 82.9 | 12.69 → 12.52 ms | 39.5 → 21.2 KiB |
| Blizzard Battle | 78.6 → 78.7 | 13.69 → 13.41 ms | 35.1 → 17.3 KiB |
| Greedy Trap | 67.0 → 67.9 | 15.60 → 15.36 ms | 45.8 → 23.3 KiB |

Allocations/frame fell approximately 46–51% in these battle samples, including
benchmark instrumentation. FPS differences are small and mixed; this single pair
does not establish a broad frame-rate improvement. Every battle remains above
the 8.33 ms budget. These are windowed native process-frame timings, not thermal
endurance, displayed-frame counts, other-device certification or input-to-photon
latency. Godot 4.7.2's unavailable Metal GPU counters are reported as unavailable.

Receipts remain under ignored `artifacts/performance/release-baseline/` and
`release-candidate/`. Earlier focus-interrupted and VM-contended samples are not
accepted comparisons. An earlier all-map Metal-HUD parsing conflict invalidated
its aggregate GPU receipt; the final matched Release pair omits that logger.
See [native timing guidance](agents/testing-and-delivery.md).

## Pi stress and latency

The existing public production route was tested using the exact deployed
v0.33.0/build39 probe: 120 seconds with eight real clients, then 120 seconds with
one client and seven Hard bots. Both passed and cleaned up their owned rooms.
The protected-host guard passed, swap growth was zero, sampled game CPU peaked
at 14.17% of one core, and peak temperature was 61.7°C. Game logs showed no dropped
ticks or overruns; sampled one-second-window tick maxima reached 5.56 and 7.49 ms.
Those maxima are not a complete per-tick distribution.

| Public workload | Worst-client ping median / p95 / p99 | Generation-gap p99 | Arrival-gap p99 |
| --- | --- | --- | --- |
| Eight clients, initial run | 73 / 122 / 423 ms | 99 ms | 188 ms |
| One client + seven Hard bots | 53 / 912 / 2016 ms | 99 ms | 212 ms |
| Eight clients, quiet repeat | 75 / 82 / 109 ms | 100 ms | 124 ms |

The quiet repeat ran while our VM, builds and GPU tests were idle. It passed
120 seconds, with no cleanup errors or swap growth, 59.5°C peak temperature and
29.43% peak sampled game CPU. A preceding attempt stopped during baseline because
the game container was starting; it launched no load. The confirmed live release
remained v0.33.0. This task did not deploy or restart a worker.

The multi-second latency spikes did not reproduce in the quiet repeat; that
neither proves their unique cause nor guarantees Internet latency. RTT includes
processing/transport queues. Snapshot gaps are not input latency. These are
production baseline measurements, not evidence of a deployed improvement from
this branch. Raw host inventories, traces and logs remain outside Git.

## Investigated alternatives kept out of the change

- Equivalent shader arithmetic changes produced no repeatable gain, so the
  original shaders remain intact.
- Godot 4.8 beta 1 has [Metal synchronization changes](https://github.com/godotengine/godot/pull/121392).
  An isolated same-resolution six-map sample measured about 58/78/52/83/77/67 FPS,
  including a 980 ms Up & Down outlier. This does not establish a smoothness win.
  The candidate remains available for future matched testing; the production pin
  is unchanged. See the [earlier engine evaluation](engine-upgrade-research.md).
- The water samples only LOD zero, but changing its sampler hint alone does not
  eliminate Godot 4.7.2's [unconditional screen-copy mip loop](https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/renderer_scene_render_rd.cpp).
  A camera-local one-mip compute-copy prototype rendered successfully. Adjacent
  samples were 57.65 versus 57.00 FPS, with about 45.3 MiB additional GPU storage.
  One pair is insufficient to establish that small gain. Its code and captures
  are archived under `artifacts/performance/prototypes/`, not enabled here.
- Moving the distant seabed out of the depth prepass cannot guarantee identical
  SSAO/SSIL inputs and does not eliminate their sky-pixel sampling loops. The
  [SSAO](https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/shaders/effects/ssao.glsl)
  and [SSIL](https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/renderer_rd/shaders/effects/ssil.glsl)
  kernels have screen-space sampling, weighting, filtering and history effects.
  Greedy Trap's reservoir bed is deliberately visible through refraction.

A diagnostic beta GPU profile attributed approximately 3.7 ms to opaque shading,
3.4 ms to transparency, 2.6 ms to SSAO, 2.1 ms to SSIL and 0.25 ms to screen copying
in the temple. Its initial menu exceeded the timestamp-query limit, so that
receipt fails and its pass breakdown is diagnostic only. Reducing those effects'
quality would violate this task's visual constraint.

## Reproduce and validate

```sh
DOTNET_TieredCompilation=0 dotnet run -c Release --project tests/Bomber.Game.Tests -- \
  --snapshot-performance --eight-player --output=artifacts/perf/legacy.json
DOTNET_TieredCompilation=0 dotnet run -c Release --project tests/Bomber.Client.Tests -- \
  --local-performance --output=artifacts/perf/local.json
python3 tools/playtest/run.py --scenario graphics-benchmark --desktop shared \
  --app /path/to/BlastCircuit.app --benchmark-resolution 3024x1964 \
  --benchmark-preset high --benchmark-stage all
```

The legacy benchmark forces text materialization inside its timed `Tick()`.
The local benchmark measures input, authority, delivery and retained state,
with hashing excluded. Do not compare those different scopes as if they were
interchangeable. Preserve both failing and passing evidence and use matching
build/runtime/power conditions. Final regression receipts are recorded with the
PR after the public, managed, headless and complete exported graphical gates.
