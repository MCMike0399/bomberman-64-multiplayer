# Random lobbies and peer hosting

Online Battle offers **Random lobby**, **Host peer room**, the existing
cluster-hosted **Create private room**, and **Join room** for either room code.

Random lobby uses the shared cluster queue. Its FIFO admission can place the
first player in a waiting room immediately; later players fill available rooms.
The first connected member becomes lobby host and chooses when to start.
At least **two connected humans** are required. CPUs do not satisfy that minimum.
The cluster runs this match, so changing lobby host does not move its simulation.
Existing private cluster rooms retain their human-plus-CPU practice-friendly rule.

Peer rooms use the same product simulation on the elected player's device.
Authenticated cluster membership routes WebRTC signalling and elects the host.
Gameplay uses encrypted direct WebRTC data channels when healthy; otherwise it
uses the authenticated cluster WebSocket relay. The HUD identifies **Direct P2P**,
**P2P + relay**, or **Relay**. Relay changes the route, not the simulation owner.
No port forwarding, listening HTTP service, or shared host password is required.
The cluster remains required for room membership and host authority.

## Failure behavior

- A peer host leaving or losing its signalling connection transfers hosting to
  the oldest connected eligible member. Reconnecting does not reclaim hosting.
- A six-second host heartbeat timeout handles a stalled lobby or simulation whose
  socket remains open. A responsive former host becomes eligible again without
  displacing the current host. The cluster alone grants replacement authority.
- A new host returns the same room to its lobby. The interrupted match is
  discarded, with no winner awarded. Scores and mid-round physics are not
  checkpointed or migrated. The new host starts a fresh match with two humans.
- Peer membership changes, including a former host reconnecting during the next
  match, return the room to its lobby, keeping
  every participant on the same roster. Room code and chosen rules survive.
- Each peer match and host change increments a generation number. Old-generation
  signalling, inputs, snapshots and completion messages are rejected.
- Direct-channel probes detect silent data loss within two seconds and select
  the relay. Probes continue so a healthy direct path can resume. Missing remote
  input is released after half a second, preventing stuck movement/buttons.
- A room's cluster node failing still loses the room, as with existing cluster
  matches. New-room cluster failover is separate from player host transfer.

## Boundaries

The coordinator assigns signalling/relay sender identity from the authenticated
connection. Guests can send input only to the elected host; only that host can
send stage/state snapshots to guests. A versioned handshake prevents older clients from starting an unsupported peer
match; clients that do not negotiate within ten seconds are removed with an
update message. Frame size, per-connection traffic, relay
byte budgets, client buffers and rendering payloads are bounded. Player-hosted
stage/state/result shapes are validated before presentation.

Peer hosting trusts the host to run fair game rules; it is not an anti-cheat
service. Direct peers necessarily learn each other's connection addresses.
Private rooms are protected by their shareable code, not a separate account or
friend-approval system. Keep codes and raw connection traces out of public logs.

Peer authority uses a monotonic real-time clock at 30 Hz, independently of
rendered frames. Signalling has a separate bounded outbound queue so initial
eight-player stage/appearance traffic cannot evict ICE offers or answers.
Loopback sessions use local candidates without an Internet STUN lookup.

The client uses the official [Godot WebRTC extension](https://github.com/godotengine/webrtc-native)
1.2.2-stable, pinned by SHA-256 in
[the bootstrap](../tools/networking/bootstrap_webrtc.py). It wraps libdatachannel,
ICE, DTLS and SCTP. Native binaries remain ignored build dependencies; packaging
includes the required notices. [Cloudflare's public STUN endpoint](https://developers.cloudflare.com/realtime/sfu/get-started/connection-patterns/)
provides public address discovery. If STUN or direct UDP cannot work, the game
uses its existing cluster relay; it does not depend on a separately provisioned
TURN service.

## Verification entrypoints

- `dotnet run --project tests/Bomber.Game.Tests -- --online-rooms`: minimum human
  count, FIFO admission, authenticated routing, host transfer, stale generations
  and host lease expiration.
- `dotnet run --project tests/Bomber.Client.Tests`: real simulation snapshots
  plus malformed and oversized rendering payload rejection.
- `dotnet run --project tests/Bomber.Smoke -- SERVER --peer-only`: three actual
  HTTP/WebSocket clients, forced relay gameplay, departure and stalled-host
  takeover. `--quick-only` checks random waiting rooms and explicit start.
- `python3 tools/playtest/run.py --scenario peer-rooms --start-server --build`:
  real WebRTC links, gameplay, direct packet counters, silent-link failure and
  relay recovery, replacement direct connections, eight-player direct delivery, real-time pacing,
  and graphical UI captures. The standalone `peer-capacity` scenario retains the
  eight-player signalling-burst regression.
- The opt-in `peer-external` harness scenario runs independent processes on
  different machines. Start a creator, read its private `peer-external.json`,
  then start two processes with `--peer-code=CODE`. All use the same
  `--playtest-server=URL`. It asserts direct gameplay and a replacement host.
  It is excluded from the automatic suite because it needs external peers.

Local loopback checks, cross-machine checks, public cluster routing, rendered
screenshots and arbitrary Internet/NAT compatibility are distinct evidence.
Consult [verification](verification.md) for the actual tested release receipts.

## Online stage protocol 2 (v0.28)

All online admission and WebSocket upgrades require `X-Blast-Protocol: 2`; both
the gateway-facing API and actual worker echo the supported version. Older
clients receive an actionable update error before receiving unsupported terrain
or fireball snapshots. Health/readiness, operator authentication and leaving or
cancelling retain their existing paths. The client also rejects older API/worker
responses before consuming gameplay, which covers a mixed-version fleet.

Peer hello uses the same revision. A mixed peer roster cannot start, and
incompatible clients receive an update notice. Lower terrain cells are validated
as one bounded optional layer, including fractional ramps and authored wall
heights; a third layer or malformed lower cell is rejected before rendering.
