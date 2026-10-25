# Burbuja kernel: implementing smarter multiplayer placement

Research dated 2026-10-24. This extends the
[multiplayer placement proposal](multiplayer-placement-research.md) with a
source-verified implementation map. **No proposed interface in this document
exists merely because it is named here.** No kernel, game or deployment code was
changed, and no runtime tests were run for this documentation investigation.

The user has authorized updating and deploying the kernel to **dev** if needed.
That permits the follow-up development lane; it does not by itself authorize
kernel release promotion or game production deployment. This pass finishes the
requested research before implementation.

The inspected external kernel commit is
`64ec8db830bb058de8ba2aa92b6f195165cd06d4`, matching the current pin. Its private
source remains outside this public repository. Paths below identify source
locations relative to that checkout; they are references for authorized
maintainers, not copied implementation. Existing public product anchors are
linked normally.

The live remote `dev` branch was also fetched and inspected at
`cbd1fec54c30a9a8bd78614670b94e8fa4ec3c98`. The local checkout and game pin were
left at `64ec8db`; only remote-tracking refs were refreshed. There is no diff
between those revisions in `Burbuja.Realtime`, `Burbuja.Realtime.Abstractions`
or the realtime Redis plugin. The newer shared-workload additions below do
matter for reusing the existing cluster coordination.

## What Burbuja already gives us

Burbuja is a strong base for this change. Reuse its authenticated players,
bounded realtime sessions, fixed-step execution, distributed room discovery,
reservations, reconnects, matchmaking settlement, worker placement and operator
draining. Keep game rules, private/public room behavior, peer eligibility and
quality policy in Blast Circuit. The kernel should own reusable allocation,
membership and transport invariants, not Bomber-specific host preferences.

| Existing seam | What was verified | What it does not provide |
| --- | --- | --- |
| `IRealtimeGame` / `IGameSession` | Per-game registration; one sequential session per lobby; product-owned settings and messages | A stable room whose simulation independently moves between workers |
| `IMatchmakingStrategy.Decide` | Pure grouping of a game's tickets into `NewLobbyDecision` or `BackfillDecision` | Alternate authority selection, cross-game routing, async probes or a QoS matrix |
| `ILobbyPlacementStrategy.Choose` | Ranks eligible `LobbyServerStatus` candidates for `PlacementRequest` | Capacity ownership, player-host election or workload-specific reservations |
| `LobbyServer.TryAllocate` | Atomic claim of one integer lobby slot | Separate active-simulation and coordination budgets |
| `RealtimeFleet` | Routes creation/reservation to owners, checks node game registration, retries full targets | Transparent live room or simulation migration |
| `IRealtimeBackplane` | Nodes, lobby directory, codes, seat claims, tickets, leases and node calls | Durable room recovery, monotonic authority epochs or a general application database |
| `Realtime.Redis` | Shared implementation over Redis/Valkey with atomic compare-and-set operations | Replication of live `IGameSession` state or its RNG/timers |
| `IRealtimeGateway` / `IPlayerConnection` | Reservation-token admission and an abstract message connection | A ready-made UDP/WebRTC server or reliability classes |
| Tick/time/sequence facilities | Outbound ticks, admitted-socket `sys.ping`/`sys.pong`, `LastSequence`, lobby performance | Client prediction, applied-action acknowledgements or automatic duplicate rejection |

Source anchors: `src/Burbuja.Realtime.Abstractions/{RealtimeGame,GameSession,Matchmaking,Fleet,Backplane,Transport}.cs`;
`src/Burbuja.Realtime/{Fleet,Lobbies,Matchmaking,Transport}/`; kernel
`docs/realtime-lobbies.md`; `plugins/Burbuja.Plugins.Realtime.Redis/README.md`.
Product composition is in [Bomber.Server](../src/Bomber.Server/Program.cs), and
the current deployment contract is [cluster hosting](cluster-hosting.md).

`Burbuja.Hosting` already accepts product registrations through
`BurbujaHostOptions.ConfigureBuilder`. `AddBurbujaRealtime` installs the generic
services, and `Realtime:Enabled` gates their active surface. Existing
[Blast.Cluster](../src/Bomber.Cluster/ClusterPlugin.cs) is the right extension
point for ranking dedicated workers and reading pressure advice. It must not
become a second allocator whose counters can disagree with the owner.

`Burbuja.Compute` is a different mechanism. Its `IComputeBackend` provisions,
executes in, snapshots and destroys workspaces; it is not the realtime match
scheduler. Reuse the existing supervised processes and host-pressure advice.
Sending every tick through a generic compute job or agent task would add an
unnecessary boundary. Source: `src/Burbuja.Compute.Abstractions/Compute.cs`.

### Reuse the newer dev branch's shared-workload support

The fetched dev revision adds `Burbuja.Worker`, an independent bounded native
file-workload host, plus `WorkloadHeadroom`/`WorkloadAdmission` in Compute
abstractions and `tools/cluster` host observation/admission tooling. It is
separate from both execution workspaces and the realtime fleet. Its documented
single admitted request, fixed executable/argument configuration, deadlines,
private scratch files and loopback authenticated endpoint fit batch file work;
they do not provide persistent game execution ownership.

The useful reuse path for multiplayer is its **monitoring and cooperative
headroom contract**, already reflected in the running cluster arrangement:

- `tools/cluster/sample.py` collects host pressure, configured protected-service
  health and authenticated fleet observations. Preserve its private inventory
  and sanitized reporting boundary.
- `admission.py` makes game-admission recommendations with three bad and six
  healthy observations. Keep the product's deployed
  [placement adapter](../tools/cluster/admission_publish.py) and
  [freshness reader](../src/Bomber.Cluster/PressureSnapshot.cs) as the bridge to
  `Blast.Cluster`, instead of starting a competing monitor.
- `monitor.py` publishes `headroom.json`, `background.json` and `realtimeBusy`.
  Existing background consumers can defer or reduce priority while realtime
  demand needs the host. This is cooperative host protection, not a reservation
  of additional game capacity or proof that a candidate will meet tick deadlines.
- Batch admission fails closed when headroom is stale/missing. Game placement
  currently falls back to its measured capped baseline on stale advice. Keep
  those distinct failure policies; a missing monitor must not silently remove
  all game availability or permit unbounded heavy work.

The current `realtimeBusy` calculation treats **any local lobby** as realtime
demand. If peer/waiting capacity grows, copying that rule unchanged could keep
background work deferred for long-lived but quiet rooms. Add typed observations
for active dedicated execution, relay traffic and coordination, then test a
conservative budget policy for them. Do not simply disable protection for peer
rooms: a fully relayed room can still saturate the host's network and CPU.

Source anchors at the fetched dev revision: `docs/shared-workloads.md`,
`src/Burbuja.Compute.Abstractions/Workloads.cs`,
`src/Burbuja.Worker/{Program,WorkerEndpoints,FileWorkloadRunner}.cs`, and
`tools/cluster/{sample,monitor,admission,test_monitor}.py`. These files do not
exist at every older game pin; inspect the named revision. The document's
historical worker/monitor test counts were not rerun here.

Base a future generic PR on the current dev line so it preserves this work.
It is unnecessary to upgrade or restart the game's runtime merely to read or
reuse the separate monitoring contract. Any game pin update still needs the
ordinary compatibility and delivery gates.

## Constraints hidden behind the current three-room limit

`LobbyServerStatus.HasCapacity` only compares `Lobbies` with `MaxLobbies` and
checks draining state. `LobbyServer.TryAllocate` increments that same count;
`LobbyManager.Create` claims it before constructing a session. It is released
when the kernel lobby closes, not when a product match returns to its waiting
room. Idle dedicated parties and peer brokers therefore consume the same kind
of slot. `Load` reports measured busy time and can influence ranking, but it is
not an enforced CPU/relay budget.

The fleet asks the chosen owner to create the lobby. That owner's capacity claim
is authoritative even if a remote heartbeat was stale. Preserve this boundary
when introducing pools or resource claims. Source:
`Fleet/{LobbyServer,LobbyBalancer,RealtimeFleet}.cs` and
`Lobbies/LobbyManager.cs` under `src/Burbuja.Realtime`.

Game support is currently **node-wide**. `RealtimeFleet.NodeRecord` advertises
the process's registered game IDs and all its servers; `CreateAsync` filters
candidate nodes by game. There is no per-server allowed-game list in
`LobbyServerOptions` or `LobbyServerStatus`. A pinned `ServerId` still reaches
`LobbyManager.Create`, which validates the game at node level and claims that
server directly. A placement plugin that merely labels one server "peer" does
not make that label an allocation invariant.

Two less obvious process-splitting constraints matter:

- `RealtimeFleet.CreateAsync` and `MatchmakingQueue.EnqueueAsync` first require
  the game to be registered **on the API process receiving the request**.
  `/games` also lists local registrations. Registering a game only on a remote
  peer worker does not make every existing API node accept its create/queue
  requests automatically.
- `RealtimeServiceCollectionExtensions.CreateBalancer` requires every configured
  server's `MaxLobbies` to be at least one; an empty server list creates a default
  server. A zero-capacity, all-games catalog node is not an existing supported
  configuration.

Join-by-code is more reusable: `FindByCodeAsync` resolves the shared code and
`ReserveAsync` routes to its owner without requiring that game locally. All
participating API nodes still need compatible player authentication. Source:
`Transport/RealtimeEndpoints.cs`, `Fleet/RealtimeFleet.cs`,
`Matchmaking/MatchmakingQueue.cs` and `RealtimeServiceCollectionExtensions.cs`.

## The smallest useful capacity change

Separate peer-room admission from dedicated-room admission first, preserving
existing room lifetime and failure behavior. This removes the accidental
competition for three identical slots without pretending to have solved host
migration. New peer limits must come from coordination and worst-case relay
measurements; retain the current dedicated limits initially.

Two viable approaches have different costs:

| Candidate | Required changes | What remains limited |
| --- | --- | --- |
| **Generic allowed-game pools inside the existing process** | Proposed kernel per-server game eligibility, advertised in fleet status and enforced at the allocation owner; distinct bounded server pools; product placement/pressure mapping | Still counts complete lobby lifetimes; shares process CPU/memory; not a separate simulation claim at Start |
| **Separate peer-only broker process** | Product startup profiles register only supported games; explicit create/queue API routing; unique node/server IDs and public socket route; separate process limits; shared existing backplane/authentication | More processes/tunnels/operations; mixed hosting and stable room handoff still require later work |

The first is the smaller deployment change but requires a reviewed kernel change
and new pin. Its proposed eligibility check must apply in local allocation,
remote selection **and owner-side pinned creation**. Unknown game IDs fail at
configuration time where possible; a compatibility default can keep existing
single-pool consumers working. Pressure exclusions must identify the intended
pool without accidentally draining its active rooms. Protect aggregate physical
host resources even when pool counters are independent.

The second can reuse the current core allocation unchanged, but is not
configuration-only in this product: all three games are currently registered
together. Route peer create requests to a peer-capable API process and preserve
shared code resolution. Explicit API paths or a product facade are clearer than
assuming the HTTP reverse proxy can infer the game from a JSON body. Do not
register expensive dedicated modes in the broker simply to make ingress work.

A weighted quota is a third, larger option: assign different resource demands
to workload classes and enforce a vector of coordination, simulation and relay
limits. **Weights alone in `ILobbyPlacementStrategy` are insufficient.** Allocation
owners need atomic claims, idempotent operation IDs and release/expiry rules;
status reports only aid selection. A vector is more accurate than one scalar
because spare CPU cannot compensate for saturated upload. Avoid this broader
contract until measured classes justify it.

For all options, do not set peer cost to zero. Peer signalling, membership loops,
bounded buffers and fallback relay have real costs. A direct room can become
fully relayed; admission must reserve fallback capacity or explicitly offer a
bounded fallback guarantee. No capacity increase follows from this report.

### Recommended first kernel PR

Implement **optional per-server game eligibility**, with separate peer and
dedicated pools, before a weighted allocator or cross-worker room migration.
The concrete proposed contract is:

- Add an optional `AllowedGames` configuration property to `LobbyServerOptions`
  (the final serialized name is a review choice). Absent means all games
  registered on that node, preserving existing consumers. An explicit list must
  be nonempty, contain valid unique IDs and refer to registered games; reject
  accidental empty/unknown lists rather than silently enabling everything.
- Publish each server's effective eligible games in fleet status with a
  backward-compatible absent-field interpretation. Eligibility is the
  intersection of the node registry and server allowlist, never an additional
  way to introduce unregistered games.
- Apply the same predicate before `LobbyBalancer` ranks candidates and inside
  the owning `LobbyManager`/`LobbyServer` creation boundary, including explicit
  `ServerId` requests. Preserve atomic `TryAllocate` and close-time release.
  Reject unsupported placement without claiming a slot.
- Test mixed old/default and explicit configurations, wrong-game pinned local
  and remote requests, stale fleet advertisements, pool exhaustion, failed
  creation, drain and 400-way concurrent allocation. Current default consumers
  must retain their behavior.

Then configure a dedicated pool on Pi with the existing limit **2**, and on
DiveMac with **1**, still subject to pressure admission. Add peer pools only with
separately measured bounded limits. Update `Blast.Cluster` server ordering and
pressure-ID mapping alongside deployment configuration. Distinguish dedicated
simulation capacity from peer-room coordination capacity in monitoring; their
sum is not the number of available dedicated matches. This first PR retains
current lobby-lifetime allocation and does not claim Start-time simulation
permits, transferable codes or mid-match migration.

## Reusing matchmaking without assuming missing features

Current `MatchTicket` contains ticket/player/game IDs, optional region, skill and
enqueue time. `BackfillCandidate` contains lobby ID, region, kernel state, open
seats and average skill. Neither contains measured routes, party phase,
authority policy or host eligibility. `NewLobbyDecision` supplies region and
ticket IDs; the queue creates a public matchmade lobby of the **same game ID**.
It cannot currently ask for `blast-peer` while matching a `blast-lobby` ticket.

Keep one public population by introducing one product room game/facade when
unification is ready. The kernel can still group its tickets. Authority selection
then occurs inside that admitted logical room, rather than by making clients
race two queues. Preserve kernel ticket settlement: cancellation and matching
compete for one transition, and a match losing to cancellation releases its seat.

For quality-aware grouping, propose a bounded, versioned measurement context
supplied to the pure strategy by a service outside the tick loop. Generic kernel
extensions might carry opaque matchmaking attributes or an immutable enrichment
snapshot; Blast owns their meaning, trust and freshness. These are **proposed
seams**, not existing `MatchTicket` properties. Do not encode arbitrary RTT maps
inside the region string or mutate identity skill to represent network quality.

Backfill also needs a real eligibility view. The kernel filters public
matchmaking-origin lobbies by `Open`/`Countdown`, or `Running` when the game allows
join-in-progress. Our persistent product room stays kernel-`Running` while its
`party.phase` alternates between waiting and playing. Thus changing only
`AllowJoinInProgress=false` would hide even waiting persistent rooms after their
initial kernel start. A proposed product-aware eligibility snapshot or generic
join-policy extension must advertise whether it can accept new members now.
Source: `RealtimeFleet.BackfillCandidatesAsync`,
[PublicLobbyGame](../src/Bomber.Game/PublicLobbyGame.cs),
[PartySession](../src/Bomber.Game/PartySession.cs),
[PeerRoomSession](../src/Bomber.Game/PeerRoomSession.cs).

Preserve origin and visibility distinctions: hosted rooms receive codes;
matchmade rooms currently do not. Public visibility alone does not make a hosted
room a matchmaking candidate. A unified UI must not place queued strangers into
a private room or publish its code. Source: `LobbyManager.Create`,
`RealtimeFleet.CreateHereAsync` and `BackfillCandidatesAsync`.

## Stable room identity: a staged implementation

### First boundary: one kernel room, local server or player authority

A product wrapper can retain a single kernel lobby, its membership and code,
while choosing between an in-process `BlastSession` and a player host. This
extends the existing composition pattern: `PartySession` already wraps successive
matches, and `PeerRoomSession` already coordinates player authority. Keep social
leader, simulation host and connection route as separate fields.

Acquire a **proposed active-simulation permit** only when starting a local
dedicated match; release it at completion or cancellation while keeping the
room. Peer play consumes coordination/relay budgets without that permit. The
permit service must account for every dedicated path, including legacy `blast`
and `blast-lobby`, or those registrations can bypass the intended limit. Likewise,
do not raise generic lobby caps while old expensive sessions remain ungoverned.

This is implementable without two player memberships, but the dedicated
simulation remains on the room's current node. It does **not** yet let a room on
Pi borrow DiveMac authority between matches. State that restriction explicitly
rather than claim complete independent placement.

Session callbacks cannot await I/O or block. Start/probe/permit acquisition runs
in a bounded service outside callbacks; return results through a product mailbox
drained by the sequential room owner. Bind each result to room revision, roster
revision and request ID. A stale success releases its permit instead of starting
an obsolete match. `ISessionContext` is valid only during callbacks and must not
be retained for background use. Source:
`src/Burbuja.Realtime.Abstractions/GameSession.cs`.

### Full boundary: independent room and execution ownership

Cross-worker dedicated authority and room-service failover require new contracts.
Keep a stable logical room record for visibility, code, roster, settings, social
leader, protocol and revision. Associate it with an independently leased match
execution: worker or player host, authority generation, readiness and route.
These are proposed records, not current `LobbySnapshot` fields.

The essential membership constraint is one player → one kernel lobby across the
fleet. A player cannot keep a broker lobby seat and also reserve an ordinary
dedicated lobby: `RealtimeFleet.ReserveHereAsync` rejects the second live claim
as `already_in_lobby`. `TransferSeatAsync` is used for stale-claim takeover; it is
not an atomic multi-player live handoff or permission to bypass that rule.

Two coherent models are possible:

1. Keep the logical room as canonical membership, and add a **delegated match
   attachment** to an execution worker. Its short-lived grant binds the player,
   room, execution, protocol and authority generation. It is not another ordinary
   lobby seat. The execution runner reuses simulation/context principles but
   needs an explicit generic lifecycle/admission contract.
2. Keep logical membership outside the existing lobby system and move the single
   live kernel membership between execution lobbies. This requires durable room
   records, reconnect/rebind, coordinated transfer/rollback and logical-code
   resolution independent of the execution lobby ID.

The first expresses the intended room/execution separation more directly; the
second reuses more ordinary execution-lobby machinery but has a much more
delicate transition. Do not mix them accidentally or issue different guest IDs
to evade the one-seat invariant. The exact model should be a reviewed generic
kernel contract before implementation; preserve the simpler one-room milestone
until that review is complete.

An asynchronous Start transition needs these observable states: requested,
capacity reserved, authority prepared, routes/participants ready, committed, or
cancelled. Only the current room owner can commit the current roster revision.
Worker preparation and commit must be idempotent, reservations expire, and a
lost reply must not create a second authority. Clients accept the committed
generation only. Existing fleet timeout cleanup is a useful precedent, not a
complete transaction for this new sequence.

## What the Redis backplane can and cannot safely become

Reuse its shared node discovery, join-code uniqueness, ticket settlement and
seat-claim checks. `PublishNodeAsync` atomically claims a node instance and its
immutable server IDs. `RealtimeFleet` addresses RPC to the live instance and
fences a process after rejected ownership renewal. Preserve those protections.

Do not mistake `HoldLeaseAsync` for complete authority fencing: it returns a
boolean for owner-plus-TTL ownership, not a monotonically increasing generation.
The current `BackplaneLobby` has no transferable owner generation, and lobby
publication/removal does not perform a room-generation compare-and-set. A new
owner reusing the same room ID needs stronger conditional writes/deletes so a
delayed former owner's cleanup cannot remove the replacement.

Lobby records, code mappings and seats are expiry-based directory state. They do
not contain enough state to restore `PartySession` or `PeerRoomSession` after
process loss. A durable logical-room store and fenced ownership transfer would
be new work. Keep application records out of ad hoc `BackplaneLobby.Payload`
objects that `RealtimeFleet` expects to deserialize as `LobbySnapshot`.

If extending Redis contracts, provide matching `LocalBackplane` behavior and
shared contract tests. Group only the necessary atomic keys; the existing Redis
plugin deliberately puts its keys in one cluster hash slot. Pub/sub RPC can lose
requests or replies, so timeout means the caller does not know whether execution
occurred. Use durable/idempotent operation records for new committed handoffs,
and bounded leases for abandoned reservations. Source:
`src/Burbuja.Realtime.Abstractions/Backplane.cs`, `Fleet/LocalBackplane.cs`,
`Fleet/RealtimeFleet.cs`, and
`plugins/Burbuja.Plugins.Realtime.Redis/RedisBackplane.cs`.

Initially, preserve fresh-match recovery on peer-host failure. Durable room
recovery is not exact match recovery: resuming a round additionally needs all
simulation state, including RNG, bot state, timers and pending actions. The
current rendering snapshot is not such a checkpoint.

## Transport, telemetry and endpoint boundaries

Use admitted-socket `sys.ping`/`sys.pong` to measure the actual dedicated worker
path. It runs after reservation admission, so pre-room qualification needs a
separately authenticated bounded probe endpoint or an already admitted room.
Player-peer probes need coordinator authorization and freshness; they cannot
be implemented by pinging only the HTTPS gateway.

The kernel already reports `LobbyPerformance` busy time, average/max tick time,
overruns and dropped ticks. Add route RTT/loss/jitter, send-queue age and bytes
around transport boundaries. Feed fresh measurements into placement policy;
do not make a heavyweight monitoring poll part of every game tick.

`LastSequence` is the highest received sequence on the current connection. In
`Lobby.OnFrame`, acknowledgement advances before `OnMessage`, and older or
duplicate numbered messages still reach the session. It is not an exactly-once
action guarantee or proof that the simulation applied every input through that
number. Unordered/redundant input and prediction need product-owned sequence,
action deduplication and applied-input acknowledgements. Keep reconnect resets
explicit. Source: `Lobbies/Lobby.cs`, `GameSession.cs` and
`tests/Burbuja.Realtime.Tests/GameDataAndNetcodeTests.cs`.

`IPlayerConnection.TrySend` accepts bytes and `FrameFormat` (text/binary), not a
delivery class. The current WebSocket writer drops the oldest queued frame when
full; TCP reliability does not protect messages discarded before transmission.
A future reliable-control/replaceable-state split therefore needs queue and
protocol semantics across the whole path, not merely a UDP URL or a native
channel flag. Keep stage-ready barriers, bounded retries and action acknowledgements
explicit. Source: `Transport.cs` and
`Transport/WebSocketPlayerConnection.cs`; see
[networking research](multiplayer-networking-research.md).

New product room, probe or TURN-token routes need the same player-authentication
model as existing realtime routes, explicit authorization by membership,
protocol checks and request/rate limits. Extend the public gateway allowlist
only for those reviewed player routes. Do not give clients kernel operator keys
or expose generic administration. The standard route group uses anonymous ASP.NET
mapping because its handlers call the player-authentication chain explicitly;
copying the mapping without those checks would omit authentication. Source:
`Transport/RealtimeEndpoints.cs`, `Players/PlayerAuthentication.cs`,
[client protocol middleware](../src/Bomber.Server/ClientProtocolMiddleware.cs).

## Verification map for implementation

Existing tests are regression anchors, **not tests run during this research**:

| Kernel surface | Existing evidence to preserve | New required cases |
| --- | --- | --- |
| `FleetAndMatchmakingTests` | `Concurrent_placements_never_exceed_capacity`, drain/region behavior | Concurrent pool/vector claims; owner rejects pinned wrong-game allocation; aggregate bounds |
| `FleetTests` | Stale-heartbeat capacity refusal, racing reservations, cancelled tickets releasing seats | Delayed prepare/commit reply, cancel-versus-start, duplicate start, roster changed during preflight |
| `BackplaneContractTests` | Node/server instance fencing, one-seat claims, claim refresh without revival, expiring codes | Owner-generation CAS, late old-owner publish/remove, lease loss during commit, idempotent release |
| `RedisBackplaneTests` | Same contract against real Redis/Valkey; two-instance contention | Real multi-process handoff races and lost notifications; no skipped Redis coverage accepted as a pass |
| `LobbyLifecycleTests` | Reservations, reconnect and lifecycle cleanup | Room remains after simulation permit release; no capacity leaks after every failure path |
| `GameDataAndNetcodeTests` | Tick/sequence serialization and transport behavior | Duplicate/out-of-order input semantics, stage barriers under queue pressure, current-generation-only acknowledgements |

Add product checks for private code continuity, private/public exclusion,
one-human-plus-CPU private behavior, dedicated-only privacy/trust policy, absent
public leader, safe peer joins and exact host-replacement messaging. Add
network journeys through real direct, relay and pressure-admitted paths; a
fake-time contract check does not establish NAT connectivity or perceived lag.

Generic kernel changes require its own `make test`, independent Workbench checks
where contracts are affected, and its documented consumer/CI gates before a
reviewed immutable pin update. Product gates then test the game against that
exact dependency. Deployment and capacity expansion remain subsequent explicit
steps under their applicable authorization, with the existing drain/empty/verify
workflow. The user's kernel-dev authorization covers that development lane;
production promotion remains separate.

The practical order is instrumentation, independently enforced peer/dedicated
capacity, one persistent room with qualified local-or-peer authority, then the
larger cross-worker logical-room/execution contract. This uses Burbuja's existing
strengths while making the few missing primitives explicit instead of replacing
the networking stack or pretending a placement plugin already supplies them.
