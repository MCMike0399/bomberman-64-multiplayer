# Multiplayer connectivity research

Research date: 2026-10-24. This is a technical options assessment, not an adopted
architecture or deployment instruction. The companion
[placement assessment](multiplayer-placement-research.md) applies these findings
to this repository and the available cluster. No runtime or host configuration
was changed for this research.

The initial audience is primarily Mexico and nearby North America. Start the
network test matrix there; successful regional results would not establish
worldwide coverage.

## Findings that should guide the design

Use one room and matchmaking experience, while treating room membership,
simulation placement and packet routing as separate choices. A private room code
or a public matchmaking ticket should not inherently require a dedicated
simulation slot. That is a proposed project design, not a property provided
automatically by WebRTC.

The strongest incremental direction is to retain the existing simulation and
authenticated coordinator, measure actual candidate routes, then choose between
an eligible dedicated worker and a qualified player host. Keep the Pi useful for
coordination and selected simulations; avoid making its home uplink carry every
peer match. This is an inference from the topology and implementation below,
not a measured throughput improvement.

Three constraints remain independent:

- A successful connection does not establish good latency or fairness.
- Peer hosting saves simulation work; relayed peers still consume relay capacity.
- Faster networking does not remove input scheduling and presentation delay.

## What the standards and current adapter actually support

ICE gathers host, server-reflexive and relayed candidates, checks candidate pairs,
and selects a working pair. Its priorities generally favor more direct paths.
It does not choose which player should run the game simulation, and its successful
connectivity checks are not a room-wide quality assessment. STUN supplies address
discovery/checks; a STUN server does not relay the game's packets.
[IETF ICE RFC 8445](https://datatracker.ietf.org/doc/html/rfc8445#section-2).

TURN provides relaying when direct communication fails. TURN supports UDP or
TCP/TLS on the client-to-relay leg; the base TURN specification uses UDP on the
relay-to-peer leg. TCP/TLS support helps where outbound UDP is blocked, but it
does not make restrictive networks universally reachable.
[IETF TURN RFC 8656, transports](https://www.rfc-editor.org/rfc/rfc8656.html#section-3.1).

The [Godot API](https://docs.godotengine.org/en/stable/classes/class_webrtcpeerconnection.html)
accepts STUN/TURN servers with credentials and permits data-channel ordering and
bounded retransmissions. The pinned
[webrtc-native 1.2.2 implementation](https://raw.githubusercontent.com/godotengine/webrtc-native/1.2.2-stable/src/WebRTCLibPeerConnection.cpp)
actually parses these options. Its initialization does **not** expose
`iceTransportPolicy`; adding that dictionary key cannot be assumed to enforce
relay-only privacy. This is source inspection, not a successful TURN test on our
distributed binary.

Upstream [libdatachannel's API](https://github.com/paullouisageneau/libdatachannel/blob/master/DOC.md)
provides a relay-only transport policy and selected-candidate-pair access. Those
upstream facilities are not automatically available through our Godot wrapper.
Application echo probes can measure route RTT independently, but reliable
direct-versus-TURN labels and a strict relay-only policy need a verified adapter
capability. Recheck native dependency compatibility before changing the pin.

Our [GodotPeerLink](../scripts/app/GodotPeerLink.cs) currently configures one STUN
endpoint and one reliable ordered channel. Our
[PeerRoomClient](../src/Bomber.Client/Peer/PeerRoomClient.cs) uses recent pongs to decide
whether the native link is alive; it does not measure its RTT distribution or
compare it with relay quality. Native links begin when a match starts.

Adding TURN introduces a naming trap: a connected WebRTC link may use a TURN
relay. The current `Direct P2P` label would no longer establish direct routing.
Transport status must distinguish native direct, native relay and application
WSS relay, or use honest broader wording until route metadata is available.

## Routing options on this hardware

These paths are design comparisons. Current deployment is documented in
[cluster hosting](cluster-hosting.md) and [peer hosting](peer-hosting.md).

| Path | What it can improve | Remaining cost or limitation |
| --- | --- | --- |
| Guest → player host, direct WebRTC | Avoids the cluster's gameplay path; host near the group can have better latency | Host upload, CPU scheduling, departure, trust and connectivity affect everyone |
| Guest → public TURN → player host | Keeps peer simulation when direct UDP fails; can avoid the home-worker relay detour | Relay egress and allocation limits; depends on public reachability and native implementation |
| Guest → public edge → game worker → public edge → player host, current WSS relay | Reuses the authenticated fallback that already works | Home-worker and tunnel traffic plus reliable stream queuing remain |
| Guest → public edge → Pi/DiveMac authority | Trusted authority and no player-host departure | Dedicated capacity, home uplink and public-edge tunnel remain in the active path |
| Guest → reachable UDP authority on Pi/DiveMac | Potentially avoids TCP and/or edge detours | New server transport and secure exposure/NAT arrangement; not a configuration toggle |

A TURN experiment belongs first at an existing publicly reachable edge, subject
to its actual CPU, bandwidth and port availability. Putting TURN on the Pi behind
the same WebSocket tunnel would not itself remove the tunnel detour or TCP
queuing. A truly reachable Pi UDP relay is another candidate only if measurement
shows it helps without compromising other workloads.

TURN/TLS on port 443 is not HTTPS. The existing Caddy HTTPS listener cannot be
assumed to proxy TURN as an ordinary HTTP route. Port ownership and an appropriate
transport listener/multiplexer would need a separate design. Retain WSS fallback
while testing TURN; a reachable TLS socket alone is insufficient evidence of a
working game route. This follows from TURN's transport and Caddy's ordinary HTTP
proxy behavior. [Caddy reverse proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy).

There is no universal ordering in which direct always beats relay. A closer relay
or better network path can help; an unnecessary distant relay can hurt. Valve's
own [SDR documentation](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay)
warns that relaying dedicated servers far from its relay sites can increase some
players' latency. For our fleet, measure the actual participant-to-authority path.
Pi versus DiveMac compute placement alone does not create geographic coverage.

## Separate timely state from guaranteed messages

WebRTC data channels support reliable and partially reliable delivery, with
ordering scoped to a stream. RFC 8831 explicitly identifies disposable game
position/state and reliable game control as different use cases. It also warns
that large messages can monopolize an SCTP association without message
interleaving. Multiple channels still share congestion control; splitting them
does not manufacture bandwidth.
[IETF WebRTC data channels](https://www.rfc-editor.org/rfc/rfc8831.html#section-3.1).

Our single reliable ordered channel therefore leaves newer snapshots behind a
lost earlier message. A candidate change is a bounded, unordered channel for
replaceable state, plus reliable control. This requires a protocol review first:
stage and appearance initialization must arrive before dependent state; action
edges need sequencing, deduplication and acknowledgement/redundancy; transient
events must survive skipped snapshots or be deliberately reconstructible. Do not
switch all input to unreliable delivery and lose short bomb presses/releases.

WSS is still a valuable reachability fallback, but application snapshot
coalescing only removes stale data before it enters the transport. TCP exposes
an ordered reliable byte stream with retransmission, so packets already queued
behind missing bytes remain delayed. TURN over TCP/TLS also inherits stream
behavior on that leg.
[IETF TCP RFC 9293](https://www.rfc-editor.org/rfc/rfc9293.html#section-2.2).

The architecture records 30 Hz simulation, 15 Hz full snapshots and roughly
100 ms interpolation without prediction. Better host selection addresses the
network part of delay. It does not erase that presentation buffer. A useful
later experiment is local movement prediction with authoritative reconciliation,
alongside measured interpolation adaptation. Prediction must preserve bomb,
carry, body-contact and multilevel-terrain rules.
[Current architecture](architecture.md).

Unity's versioned explanation distinguishes immediate visual anticipation from
full rollback/replay prediction. That distinction is useful here: an optimistic
animation is not evidence of reconciled collision or authoritative outcomes.
This is a conceptual reference, not a recommendation to change engines or adopt
Unity's package, and the cited documentation is archived version 2.2.
[Unity client anticipation](https://mp-docs.dl.it.unity3d.com/netcode/2.2.0/advanced-topics/client-anticipation/).

## Host selection and matchmaking measurements

The following is a proposed evaluation policy, not an established scoring model.
Before starting a match, reject incompatible clients and candidates lacking a
working route to every participant. Probe real gameplay paths with small bounded
echoes; do not substitute HTTPS gateway latency for worker or peer-path latency.
Use one peer star for gameplay, not an all-to-all simulation mesh. Testing several
possible hosts may require temporary pair probes, bounded to the room's eight
participants and removed afterwards.

Rank feasible authorities using the worst participant's RTT/jitter first, then
the group's distribution, loss/stalls, simulation tick headroom, host stability
and required relay bandwidth. A good average must not hide one unusable link.
Keep lobby ownership (who edits settings/starts) distinct from simulation
authority. Never let self-reported CPU or latency alone grant trusted authority.

Use stability windows and hysteresis. Initially reselect authority only in the
lobby or between matches. A faster candidate appearing mid-round is not enough
reason to discard an active match. Route switching can be less disruptive than
authority switching but still needs duplicate/stale-frame protection.

Public matchmaking should fill compatible waiting rooms before creating more
fragmented pools. Use measured latency bounds, with explicit gradual relaxation
of optional preferences as waiting time grows. AWS documents both per-location
latency rules and time-based rule expansion; these are useful patterns that can
be implemented locally without adopting a paid matchmaking service.
[FlexMatch latency rules](https://docs.aws.amazon.com/gameliftservers/latest/flexmatchguide/match-rules-reference-ruletype.html),
[rule expansion](https://docs.aws.amazon.com/gameliftservers/latest/flexmatchguide/match-rulesets-components-expansion.html).

Separate capacity accounting into lightweight room memberships/signalling,
running simulations, relay allocations/bytes and host service-health admission.
For a host sending a full snapshot of average serialized size `S` to `n−1`
guests at rate `f`, payload upload is approximately `(n−1) × f × S`, before
framing, encryption, retransmissions and control traffic. This is an accounting
identity, not an estimate of our measured snapshot size. Benchmark eight-player
worst cases, including bots and bursty effects, rather than extrapolating from
idle two-player rooms.

## Public strangers, privacy and host trust

Direct WebRTC exposes connection-address information to the remote endpoint;
encryption does not hide that endpoint from its peer. The IETF documents this
privacy/performance tradeoff, including VPN and multihomed address exposure.
[WebRTC IP handling RFC 8828](https://www.rfc-editor.org/rfc/rfc8828.html#section-3).

The existing peer host is trusted to execute fair rules; frame-shape validation
does not prove honest simulation. A relay changes the path, not this trust model.
Keep trusted dedicated authority as a clear option for public play. Before
automatic stranger-to-stranger peer hosting, make its host-trust and address
policy deliberate. A strict address-hiding option must avoid sharing direct
candidates in the first place and verify relay-only behavior on the actual
native adapter. [Current peer boundary](peer-hosting.md#boundaries).

If deploying coturn, issue short-lived credentials through authenticated room
admission, keep the signing secret off clients, and bound allocations and bytes.
Coturn documents time-based credentials, per-user/global allocation quotas,
bandwidth controls and denied peer-address ranges. Apply limits before load
tests, including restrictions against relay access to internal services.
[coturn server documentation](https://raw.githubusercontent.com/coturn/coturn/master/README.turnserver).

Replacing WebRTC with GameNetworkingSockets does not supply free worldwide
relays. Valve's open-source P2P library still requires signalling, identities,
matchmaking and a relay fallback; its SDR implementation is not open source.
Steam's cross-store access has eligibility and SDK requirements. Keep this as a
future platform/distribution decision, not the first fix for current room limits.
[GameNetworkingSockets P2P requirements](https://raw.githubusercontent.com/ValveSoftware/GameNetworkingSockets/master/README_P2P.md),
[Steam SDR requirements](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay).

## Experiments required before choosing defaults

| Matrix axis | Required evidence |
| --- | --- |
| Networks | Same LAN, independent residential ISPs, mobile/CGNAT, IPv6 where available, UDP-blocked network, VPN and restricted proxy path |
| Participants | Two, four and eight clients; mixed direct/relay links; distant player among nearby players; no claim that eight local processes equal eight home networks |
| Faults | Inject delay, jitter, loss, reordering and bandwidth limits; blackhole a working direct path; disconnect/rejoin; host stall; coordinator loss |
| Shared hardware | Pi services active; DiveMac ordinary compute/transcription active; protective admission still effective |
| Route identity | Actual selected candidate class where available; successful end-to-end messages and bytes; forced WSS, TURN/UDP and TURN/TLS tests |
| Quality | Join success/time, RTT p50/p95, snapshot age and gaps, input-to-visible response, tick duration/deadline misses, correction distance, per-room relay bytes |
| Safety of transitions | No stale generation accepted, no double bomb action, no stuck input, no false winner, stable room code and membership semantics |

Start with instrumentation and bounded lobby preflight; then test authority
selection and separate capacity budgets. Trial state-channel changes and an edge
TURN route independently so improvements can be attributed. Prediction and
checkpointed host migration are separate larger work items. Retain the existing
fallback until its replacement passes the adverse-network matrix.

This research establishes API/protocol capabilities and identifies implementation
gaps. It does not establish additional room capacity, worldwide low latency,
universal NAT success, safe public peer authority, or current edge egress cost.
Those need runtime measurements and product choices before rollout.
