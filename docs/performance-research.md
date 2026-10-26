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
build/runtime/power conditions.

## v0.36 integration re-check (2026-10-26)

PR #25 was merged with main through v0.35.1, which added arena scenery and the
saved-High startup fix, then given the v0.36.0/build 43 identity. All gates
were rerun on that exact tree:

- Public, managed and headless profiles passed (2,880 game, 354 client, 81 AI
  and 72 soak checks plus release admission, network, relay and fleet steps).
  Package passed, and so did hosted PR Verification.
- The rebuilt export passed the complete **57-journey Metal Forward+ suite in
  the isolated VM: 5,002 assertions, 496 captures, zero runtime errors**. The
  only warning was the known SCTP stream reset during peer teardown.
- Every capture was pixel-compared with the same-named v0.35.0 suite capture.
  113 are identical; the character model profiles and the eight-player Blizzard
  match are identical apart from randomized pickups and explosion particles.
  Other differences come from time-animated water/scenery, network timing and
  server-chosen online lighting. Agent review is not user approval of the art.

The first two graphical attempts failed in `display-settings`: the process had
loaded a stale `qa-preferences-<pid>.cfg` (75% volume, windowed) left by a
killed earlier run, because a rebooted guest reuses PIDs. The guest contained 23
such files. Default QA processes now use a random GUID path. The rerun passed
with that stale file still present; this was a harness defect, not a PR change.

A matched native pair used the same tree with only the PR's product files
reverted (both .NET 10.0.12, 3024×1964 High, all six arenas, AC power, VM stopped).
Runs were ordered baseline, candidate, candidate, baseline:

| Arena | Baseline FPS (run 1 / 4) | Candidate FPS (run 2 / 3) | Allocations/frame baseline → candidate |
| --- | ---: | ---: | ---: |
| Sunken Temple | 56.5 / 56.3 | 50.8 / 54.7 | 60 → 34–36 KiB |
| Rock Garden | 75.9 / 70.0 | 70.0 / 75.9 | 38–41 → 20–21 KiB |
| Up & Down | 56.9 / 50.8 | 49.9 / 56.7 | 50–55 → 25–28 KiB |
| Top Rules | 79.7 / 72.1 | 73.9 / 81.6 | 41–45 → 22–24 KiB |
| Blizzard Battle | 75.2 / 66.7 | 70.1 / 74.7 | 37–41 → 19–20 KiB |
| Greedy Trap | 67.1 / 54.2 | 61.6 / 68.0 | 46–56 → 24–26 KiB |

The second consecutive run is slower for either build, so the order effect
(sustained load) is larger than any code difference. At matching positions,
frame rate is equivalent within that noise; allocations are consistently
40–50% lower. Menu FPS was bimodal (about 123 or 150) for both builds in four
further alternating runs. Stable 120 FPS remains unachieved. Spotlight indexing
was active during the first pair. Receipts are under ignored `artifacts/release-036/`.

## GPU pass costs and exact shader early-outs (2026-10-26)

This follow-up targeted GPU time on v0.36 (including arena scenery). It used a
diagnostic-only Godot 4.8 beta source run with `--gpu-profile` at 3024×1964 High
on the M5 Pro; the product remains on 4.7.2. Sunken Temple battle GPU time was
about 17–19 ms per frame:

| Pass | ms | Pass | ms |
| --- | ---: | --- | ---: |
| Opaque | 3.8–4.0 | Depth of field | 0.9 |
| Transparent (water) | 3.0–3.1 | Depth pre-pass | 0.8–0.9 |
| SSAO | 2.5–2.7 | Tonemap | 0.7 |
| SSIL | 2.1 | MSAA resolves (four) | 1.6 |
| Sky / fog-only sky | 0.4–0.5 | Screen + depth copies | 0.4 |
| Directional shadows | 0.13 | Glow | 0.25 |

Arena totals were Temple 17.2, Up & Down 17.3, Greedy Trap 14.9, Rock Garden 13.1,
Blizzard 13.1 and Top Rules 12.3 ms. Every arena is GPU-bound above the 8.33 ms
120 FPS budget. Shadows are not a target. With fog enabled, Godot draws a
fog-only sky pass even with a color background, so changing the background saves
nothing. Valid ceilings came from disabling a shader body inside `if(false)`
(Godot rejects `return` in `fragment()`, which silently drops the material):
water fragment math costs about 1.36 ms and stone about 1.0 ms. The remainder is
lighting, MSAA, SSAO/SSIL, DOF and engine passes that define the look.

Shipped edits skip work whose contribution is exactly zero: water shore bands
outside 0.16 < thickness < 0.5, glints in unlit cells or dark twinkle phases,
stone pits until the octave is at least 43% resolved, garden crack noise outside
a crack, garden moss from height .25 upward, and ice sparkle outside sparkle cells.
Derivatives stay outside the new branches.

Byte identity was proven on native Metal with a one-off journey. It paused real
eight-player battles on all six arenas (High and Low, two pinned shader times and
a flooded configuration). It rewrote each live `Shader` resource's code in place,
original versus optimized, and compared captured frames. **All 36 shipped
configurations were byte-identical**, with reproducible frozen frames. Two lessons
for future shader work:

- Replace shader *code*, not shader objects. New `Shader` instances change draw
  sorting and flip z-fighting seams; that alone produced 7,800-byte differences
  with identical code.
- Sine hashes (`fract(sin(x)*43758.5)`) amplify single-bit rounding. Under Apple's
  fast-math compiler, mathematically exact restructuring can still change speckle.
  Two candidates failed and were rejected: skipping the refraction read where
  transmission is zero (3–50 bytes in five of nine configurations; it also saved
  about nothing) and skipping stone wet/algae noise above the waterline (8 bytes
  in flooded Up & Down).

Measured gain with matched clocks (normalized by untouched passes): Sunken Temple
−1.9% and Greedy Trap −1.7% GPU time; other arenas fell within ±1% thermal noise.
Sustained load visibly throttled the GPU (Up & Down rose from 17 to 23 ms between
runs), which dominates any such saving. This is the practical ceiling for
visually exact shader work. Further FPS needs lower effect cost (a visual change)
or engine changes. Harness sources, edit lists and profiles are under ignored
`artifacts/equivalence-harness/` and `artifacts/equivalence-*` in the work tree.
