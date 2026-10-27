# Adaptive multiplayer rooms

Adaptive rooms were delivered in v0.29.1. The Quick Play policy below includes
the 2026-10-27 matchmaking QA changes in source; it does not claim a new release.
[Delivery evidence and operating limits](smart-multiplayer-release-029.md) records the original verified release.
This implements the first persistent-room/local-or-peer milestone of the
[placement research](multiplayer-placement-research.md) and
[Kernel implementation assessment](burbuja-multiplayer-research.md).

## Player flow and authority

Online Battle offers **Play online**, **Create private room**, and **Join with
code**. Private hosting defaults to Automatic, with Server only and Player hosted
choices. Server only does not exchange peer candidates and never falls back to
player authority. Private Automatic/Server only still supports one human plus
CPUs; player hosting requires two humans. Codes protect private admission and
are not included in public matchmaking.

Public matchmaking fills compatible waiting rooms in one FIFO population. Two
connected humans start a **60-second automatic countdown**. Each connected
roster change restarts the full minute while at least two remain; below two,
the countdown cancels immediately. Reconnects receive a fresh minute. Later
cleanup of an already disconnected seat does not reset the same roster again.
An unchanged full eight-player room also waits until its deadline.

Quick Play uses server-defined shared rules with no CPUs. Anonymous players
cannot edit those rules or start early, including the first arrival or a
replacement leader. Personal settings and suits remain available. The lobby
shows the shared countdown, explains resets and reports a capacity wait once
the timer expires. New strangers cannot join a public match during gameplay or
results; retained seats may reconnect. After results, the room reopens with a
fresh minute. Continuous roster churn can postpone a start under this policy.

Private rooms retain explicit owner start and editable rules. A start waiting
for capacity retries while membership/rules remain valid; leaving or changing
ownership cancels that intent. Public capacity retries begin only after the
current roster's deadline and are cancelled/reset by roster changes.

`AdaptiveRoomSession` keeps one Kernel lobby and one seat per player. It wraps the
existing `PartySession` and `PeerRoomSession`, selecting one authority for each
match. In private rooms the owner controls settings and start; the selected
player host can be someone else. Public starts and rules belong to the service.
Match numbers remain monotonic across authority kinds. Both
kinds run the same `BlastSession` rules and preserve all battle arenas.

Public matches prefer available dedicated authority. Automatic private rooms
can prefer a substantially better measured direct peer path. Otherwise they use
dedicated capacity, then a qualified peer if available. Player hosted explicitly
waits for a qualified peer and a reserved relay slot. Authority stays stable
during a healthy match; this is not continuous mid-round migration.

## Qualification and delivery

Waiting clients measure the actual worker socket and bounded peer routes.
Pair measurements include direct and authenticated-relay echoes, recent p95 RTT,
loss and client polling gaps. Reports expire after six seconds. Selection uses
mutual reports, requires at least three successful samples, and ranks the worst
participant first, then the mean. Initial eligibility gates are 200 ms worst RTT,
10% expired probes and a 100 ms host polling gap; these are policy bounds, not
validated universal service targets or a complete device benchmark. Latency
reports do not grant authority and cannot prove an honest host.

The existing WebRTC adapter uses reliable ordered control/input and a separate
unordered snapshot channel with zero retransmissions. Complete native frames
are optionally Brotli-compressed, capped at 16 KiB on the channel and 128 KiB
when decoded. A frame that cannot fit uses WSS. A stage-readiness barrier holds
at most one pending snapshot until its stage arrives. Input/action transitions
remain reliable and sequenced; there is no movement prediction or rollback.

The authority relay allowance scales with connected recipients (2 MiB of decoded
messages per recipient per second, at most seven), because the host sends a copy
to each. Guest uploads remain capped at 64 KiB/s. Physical relay concurrency is
separately reserved and must pass guarded load tests.

`peer.state` separates disposable relay snapshots from control. Client and Kernel
queues coalesce those snapshots while retaining control ordering. Exhausted
reliable queues explicitly fail the connection instead of silently evicting a
stage or action. Kernel opt-in queues also cap buffered bytes at one MiB per
connection. TCP retransmission can still delay WSS traffic already on the wire.

Healthy native paths are compared with relay measurements; a silent direct path
falls back after its two-second liveness window. The fallback still traverses the
existing public edge and room worker. No TURN service was provisioned by this
change. Direct peer connections reveal connection addresses to their endpoints;
player hosts remain trusted to execute the rules. Server only is the available
choice for avoiding player authority and direct-peer candidate exchange.

## Resource and placement policy

The deployed pool accepts only `blast-room` and `blast-online`. Legacy `blast`,
`blast-lobby`, and `blast-peer` remain standalone regression interfaces, but
cannot bypass production budgets through the cluster's allowed-game filters.

| Budget | Lifetime | Configuration |
| --- | --- | --- |
| Room coordination | Kernel lobby lifetime | `BLAST_MAX_LOBBIES` |
| Dedicated simulation | Active dedicated match | `BLAST_SIMULATIONS` |
| Peer relay reserve | Active player-hosted match, including direct ones | `BLAST_PEER_MATCHES` |

Every peer match reserves fallback capacity before starting; direct success is
not permission to admit unlimited fallback demand. Per-sender message/byte
limits, bounded buffers and the container's existing CPU/memory ceilings remain.
Room counts are not equivalent to active matches or proven hardware throughput.
Preserve Pi's two and DiveMac's one dedicated simulation limits initially; choose
room and peer limits from guarded measurements and retain the delivery receipt.
The Compose template defaults unconfigured peer capacity to zero.

`Blast.Cluster` 1.2 ranks eligible nodes by available simulation capacity, then
peer capacity, then waiting-room capacity, with Pi preference within each class.
The allocation owner checks actual claims atomically. Fresh pressure exclusions
apply both to new room placement and new execution inside an existing room.
`PressureAdmission` refreshes advice off the game loop; the callback reads only
cached eligibility. Existing claims and releases survive pressure and drains.
Missing/stale advice preserves the previous capped baseline, not unlimited work.

Operator fleet records expose resource limits, available resources and each
room's claims. Node-wide available resources repeat on pool records and must not
be summed across pools. The shared-workload monitor recognizes resource-managed
rooms: quiet waiting rooms do not indefinitely suppress background work, while
active simulation/relay claims do. Legacy/incomplete observations stay
conservative. Batch-work stale admission remains fail-closed.

## Departure, reconnect and infrastructure failure

A normal peer guest departure removes its actor without restarting the other
players' match. Temporarily lost inputs release after half a second. Retained
seats can reconnect during play: stale outbound work is cleared, callbacks are
bound to their connection generation, input sequences reset, and stage/state
are refreshed. A retained participant absent from a replacement host's initial
roster joins as a pending ghost until the next round. New unrelated peers wait
until the active peer match finishes instead of resetting its roster.

Host departure or the six-second lease timeout discards the interrupted peer
match and returns everyone to the same room. The owner can start a new private
match; public rooms use their countdown. No interrupted winner is awarded.
Room settings and code survive player-host replacement, but exact physics,
RNG and scores are not checkpointed for mid-round migration.

Each room still lives on one worker. It can select that worker's simulation or
a player, but cannot borrow another worker's simulation while keeping its current
attachment. Losing a worker loses its rooms. Healthy-worker matches continue,
new rooms use eligible survivors, and primary recovery does not move existing
matches. The edge/backplane remains a shared failure domain.

Orderly departure first stops local sends, submits the authenticated leave and
waits within the bounded cleanup timeout for the seat to disappear, then closes
native channels. This gives remaining clients the membership update before their
peer disappears. Abrupt network failure still uses normal reconnect/relay recovery.

## Verification

- `--adaptive-rooms` in `Bomber.Game.Tests` checks selection, leadership,
  reservations, privacy policy, countdown, initial settings and reconnect input.
- `dotnet run --project tests/Bomber.Game.Tests -- --public-matchmaking` covers
  countdown boundaries, late/full rosters, owner permissions, departure,
  reconnect, capacity cancellation/recovery, fallback-host loss and rematches.
- `Bomber.Smoke -- --quick-only` exercises the actual one-minute server wait,
  queue backfill, churn, permissions and exclusion of active public matches.
- `python3 tools/playtest/run.py --scenario quick-play` exercises the rendered
  countdown, absent public owner controls, personal settings and departure text
  against a real server in the isolated graphical desktop. `network` includes it.
- The normal managed smoke covers adaptive dedicated play. Its second owned
  instance disables simulation capacity and runs `--adaptive-peer-only`, proving
  real relay gameplay, TCP interruption/reconnect and guest/host departure.
- `Bomber.Cluster.Tests -- --fleet SERVER_DLL PLUGINS_DIR OUTPUT` uses two owned
  processes and a uniquely labelled local Valkey container. It verifies real
  cross-node routing, separate budgets, pressure, crash/recovery, queueing in
  the same room, forbidden legacy admission and coordination limits. It fails
  if its owned container cannot be cleaned up. This is not public-WAN evidence.
- `adaptive-rooms` is an eight-client native journey: preflight, actual channel
  modes, gameplay packets, injected snapshot loss and departure continuity.
  It runs in headless/package gates and the graphical `all` suite. Captures
  establish UI/rendering only when actually reviewed.
- Keep the guarded physical capacity procedure in [cluster hosting](cluster-hosting.md).
  Test public routes after deploying the exact verified runtime and matching
  plugins. Local Docker, native WebRTC, arbitrary NAT coverage and physical
  hardware capacity are different kinds of evidence.

### Quick Play source QA — 2026-10-27

Retained failing witnesses caught the old eight-second start and a socket
claiming a pre-start reservation after gameplay began. The final authority
passes 47 focused matchmaking checks and the complete 2,937-check game suite.
The rebuilt real HTTP/WebSocket Quick Play smoke passes the full sixty-second
wait, roster churn, cancellation, automatic start and active-match admission,
including rejection and release of that delayed reservation.

The complete managed profile also passed: 377 client checks, 98 AI checks,
72 AI soak scenarios, 19 audio checks, 23 cluster contracts, 78 release connection
checks, 2–8-player network smoke, nine adaptive relay checks and 36 real fleet
checks. The final delayed-reservation guard was additionally checked with the
rebuilt full game suite and focused real-network smoke.

The rebuilt exported client/server passed the isolated windowed Metal `network`
journey with 93 assertions, eight captures and zero runtime errors or warnings.
Reviewed captures show the live public countdown, cancellation below two players,
absent public owner actions and preserved private-room controls. This is local
source/export verification; publication, maintained-worker deployment, public
WAN testing and physical controller coverage are outside this receipt.
