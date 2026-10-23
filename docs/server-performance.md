# Server performance investigation — 2026-10-23

A three-human, three-CPU party was mostly smooth but had intermittent stutters.
The recent 14-minute room logged one warning for a 38.6 ms tick against the
33.3 ms simulation budget. An earlier 219.4 ms warning belonged to a separate
cold network smoke, not that friends' match. Historical logs did not retain
full tick distributions, GC timing or snapshot delivery times, so they cannot
establish the cause of every reported stutter.

## Measurements

The read-only host check found no CPU quota throttling, OOM or container restart.
The host runs other workloads. Direct tailnet ping was around 10–27 ms. A new
probe created a real private room with three independent HTTP/WebSocket clients
and three Medium CPUs on Sunken Temple. It used ordinary movement/bomb input,
manual leader start, real snapshots and protocol pings. It never edits actor
positions, inventories or outcomes.

The initial 90-second relay measurement received 1,306 snapshots per client.
After warmup, snapshots averaged about 24.3 KB, with about 19.7 KB (81%) being
repeated presentation history. Total snapshot traffic across three clients was
about 1.0 MB/s. Typical packet spacing was 66–67 ms, with two shared gaps over
150 ms; the largest was about 182 ms. A direct probe inside the server's Docker
network also saw occasional larger gaps, so the relay was not the only possible
source of timing variation. That first relay collection completed but its cleanup
misparsed a successful empty HTTP 204; the probe now handles that response.

The controlled Release comparison runs three fixed seeds twice, with three humans
and three CPUs. Tiered compilation is disabled for this timing comparison and the
first 300 ticks are excluded. Every authoritative state hash matches before and
after, excluding only presentation history and the new diagnostic timestamp.

| Metric | Before | After |
|---|---:|---:|
| Mean snapshot | 23.9 KB | 6.2 KB |
| Allocated bytes per simulation/encoding tick | 28.2 KB | 10.0 KB |
| Median of per-run p95 tick times | 0.165 ms | approximately 0.09 ms |

Bytes fell about 74%, allocation about 64%. Timing comes from a shared Mac and
includes the local adapter's JSON encoding; it is not a production capacity
claim. The byte/state comparison is deterministic. Live games have different
seeds and timing, so their throughput/latency comparisons are descriptive.

A separate Docker candidate's 90-second relay run used about 0.324 MB/s total,
roughly 68% less snapshot traffic. Its p95 packet gap was about 76 ms (previously
93–95 ms), and p95 ping was 42–46 ms (previously 71–75 ms). A follow-up 120-second
run used timestamped snapshots: its one shared 227 ms delivery gap had a **67 ms
server generation interval**, indicating delay after snapshot generation. TCP
receive-duplicate counters also increased on the shared SSH transport. This is
evidence of remaining delivery jitter, not proof that every stutter is fixed.

## Changes

The game used to repeat the last 128 presentation events indefinitely, including
old events from quiet periods. It now retains four seconds of recent effects,
with the same 128-event ceiling, and reuses the immutable event array while that
window is unchanged. Sequence numbers remain monotonic across rounds. Four
seconds preserves the existing 96-tick coalesced-client recovery contract; a
two-second prototype was deliberately expanded to avoid weakening it.

The snapshot optimization itself leaves inputs, bot policy, movement, collisions,
authority rate (30 Hz), snapshot rate (15 Hz) and authoritative outcomes unchanged.
The fixed-seed comparison isolates that patch on the v0.21 baseline; the separately
tested v0.22 party-polish features intentionally change their documented mechanics. Existing clients understand
the same event fields. Recent events still recover short delivery gaps; stale
effects are no longer repeatedly serialized and transmitted forever.

Snapshots also include `serverTime`, the kernel step's UTC time in milliseconds.
Differences between successive server-step timestamps can be compared with receive
intervals; the excess includes processing/transport delay and is not absolute
one-way latency between unsynchronized clocks.
The probe excludes deliberate gaps between completed matches.

`PerformanceReporter` writes `game_perf` every five seconds while rooms exist:
connected humans/configured CPUs, the kernel's most recent tick window's mean
and maximum, cumulative overruns/dropped ticks, plus process CPU/allocation/GC
pause deltas and thread-pool queue size. `sample_seconds` is the actual process
measurement interval. Process counters are repeated on each room's row and must
not be summed across rooms. No player names, tokens or join codes are logged.
These metrics preserve future evidence instead of relying only on an overrun
warning. Room tick windows are approximately one second, not five-second maxima.

## Reproduce

```sh
dotnet run -c Release --project tests/Bomber.Performance.Probe -- \
  http://SERVER:PORT artifacts/perf/relay.json 90 3 3
DOTNET_TieredCompilation=0 dotnet run -c Release --project tests/Bomber.Game.Tests -- \
  --snapshot-performance --output=artifacts/perf/simulation.json
```

The network probe writes per-client snapshot size, event volume, arrival gaps,
protocol ping distributions and a timeline. It validates sustained gameplay and
the requested human/CPU population, then releases its own seats. It opens a real
match; use it only on a server authorized for a bounded load test.

Raw receipts stay ignored under `artifacts/iteration22-performance`. The original
production service and an isolated candidate were measured separately. Subsequent
shared-checkout gameplay edits were excluded by using a separate worktree and
pinned kernel checkout at the released baseline. No other host services were
stopped or retuned. New tests reject stale idle history while preserving current
actions, sequence continuity, coalesced recovery and real action consequences.
