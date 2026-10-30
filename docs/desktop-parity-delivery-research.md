# Desktop parity: transport, timing and release research

Date: **2026-10-28**. Research and implementation proposals, not release approval.
Source inspected at `0558c50` unless stated otherwise. No builds, game launches,
service changes or release operations were performed by this research owner.
The [rendering and gameplay research](desktop-rendering-parity-research.md)
contains the detailed graphics specifications, upstream leads and replay plan.

## Preserve the outcome and shorten the feedback loop

Keep Mac as the authoring machine and retain the same rules, assets and Forward+
presets on Metal, D3D12 and Vulkan. Exporting and validating are separate operations:
Mac can remain the development/export host, with native packaging and target-OS
checks performed remotely. This is already the project's
[desktop delivery design](desktop-clients.md#build-and-verify).

The user requested matching graphics and mechanics, including pixel comparisons.
The research does not approve visual downgrades or replace that goal with a loose
similarity score. Exact image equality must be measured under controlled inputs;
the graphics APIs do not guarantee it universally. Preserve High's MSAA, DOF,
water, lighting and materials in the actual acceptance witness. An MSAA-off
diagnostic can isolate a failure but cannot certify the MSAA-on product.
See the [source-backed acceptance proposal](desktop-rendering-parity-research.md#proposed-acceptance-contract-preserving-quality).

Mac iteration need not run the entire desktop suite on every edit. Extend the
existing pipeline with small replay/capture/transport probes selected by the
changed boundary, then qualify immutable complete artifacts before releasing
them. That saves iteration time without claiming that old Windows/Linux packages
are compatible with a new exact-token server. During the current isolated QA
phase, Mac stable remains independent. After all-platform delivery is activated,
publishing a new shared server identity still requires matching supported clients;
a separate compatibility policy would be a product change, not a CI shortcut.

## Transport: a closed direct link is a separate problem from a host lease

The implementation owner reported the new Windows experiment's sequence as
consent loss, SCTP failure and a closed peer link, with coordinator WebSockets
remaining connected. The original takeover failure's cause was still awaiting
the control run. This research did not independently reproduce those runtime
events; the source and protocol findings below explain what to test next.

[RFC 7675, section 5.1](https://www.rfc-editor.org/rfc/rfc7675#section-5.1)
defines consent freshness independently of application host leases. Consent
expires after 30 seconds without a valid response; traffic must stop on that
candidate pair. Reusing expired credentials does not restore consent: a new
session or ICE restart is required. A single measured 7.8-second engine-frame gap
therefore does not establish that it caused consent expiry. Collect the complete
transport timeline and actual implementation configuration; do not disable consent
checks or increase the game's peer lease to mask this failure.

The browser [WebRTC specification](https://www.w3.org/TR/webrtc/#dom-rtcpeerconnection-restartice)
defines `restartIce`, but that is not proof that Godot's native API exposes the
same call. The pinned official native extension gives a more useful answer:

- [bootstrap_webrtc.py](../tools/networking/bootstrap_webrtc.py) pins
  `1.2.2-stable` and the archive hash.
- In the [tagged native implementation](https://github.com/godotengine/webrtc-native/blob/1.2.2-stable/src/WebRTCLibPeerConnection.cpp),
  `_create_offer` rejects states other than `STATE_NEW`; `_create_pc` disables
  automatic negotiation. Repeatedly calling `CreateOffer` on a closed connection
  is therefore not a supported recovery plan for this pinned implementation.
- The same implementation queues SDP/candidate callbacks and dispatches them in
  `_poll`. Native transport progress and Godot callback delivery are distinct.
  Poll delays can delay signaling without proving that all native network work
  stops. Maintain the existing engine-thread ownership of Godot calls.
- The tagged repository's `thirdparty/libdatachannel` gitlink is
  `6b1e2e620f1e37f0eafeee702eaea0043cb305fd`. Use the exact dependency/build
  when inspecting timeout configuration rather than assuming current upstream
  behavior matches the shipped binary.

Locally, [IPeerLink](../src/Bomber.Client/Peer/IPeerLink.cs) exposes a connected
boolean but no distinct terminal-state/recovery contract.
[GodotPeerLink](../scripts/app/GodotPeerLink.cs) initializes fresh peers, consumes
offers/ICE, and explicitly closes channels on disposal.
[PeerRoomClient](../src/Bomber.Client/Peer/PeerRoomClient.cs) can fall back to relay
when probes are stale; that does not itself recreate a failed native link.
These are source observations, not a claim that a specific recovery implementation
has been validated.

### Bounded recovery experiment

Proposed work for the implementation owner:

1. Keep the existing temporary direct-packet-loss test. Add a distinct test that
   actually closes one native peer/channel while the coordinator, match identity
   and relay remain healthy. The retained failure must identify both endpoints,
   link generation, room epoch and exact failure state.
2. Verify continued authoritative input/state delivery over the existing relay.
   Then demonstrate whether the current implementation can return to direct
   transport. A relay pass is useful, but does not satisfy a direct-recovery claim.
3. If terminal failure is demonstrated, propose a small link-lifecycle seam for
   fresh native-peer negotiation. Use one designated offerer per pair, bounded
   backoff, and an attempt/generation identifier separate from authority epoch.
   Ignore stale callbacks, answers and ICE from replaced links; clear attempt-local
   early-ICE queues. Keep healthy peers, match state and relay traffic intact.
4. Test transient loss without unnecessary recreation, terminal close followed by
   recovery, delayed old signaling, simultaneous endpoint failure, repeated failure
   without an unbounded retry/resource loop, and ordinary room host takeover.

This is an experiment/design proposal. Recreating links, changing signaling or
adding generation fields needs review of both endpoints and server forwarding.
Do not make speculative native-library or engine upgrades to obtain a green test.
Retain six-second production/headless/hardware peer leases and the explicitly
approved 120-second software-peer QA exception. The cluster-hosted three-OS
Quick Play witness uses its ordinary server rules.

## Countdown evidence needs one coherent status observation

The failed Linux check compared a server deadline to local wall time after
waiting for slow render frames. `0558c50` records socket receipt time, which
removes that rendering delay from the comparison. Two limitations remain visible
in [NetworkSession.Socket.cs](../src/Bomber.Client/Sessions/NetworkSession.Socket.cs)
and [NetworkPlaytest.cs](../scripts/testing/journeys/online/NetworkPlaytest.cs):

- Party state and `PartyReceivedAtUnixMs` are published as separate volatile
  fields and read separately. They can describe different updates. Prefer one
  immutable observation containing status, receipt time and sequence/generation,
  atomically published/read, or capture an equivalent coherent QA event.
- Every party status replaces the receipt timestamp. The test needs the first
  observation of the relevant reset deadline/roster generation, not an arbitrary
  later status with the same deadline. Also, subtracting local UTC from server UTC
  includes clock offset and network transit time. Receipt time is not the server's
  reset time.

These are source-level risks, not reproduced explanations of the new candidate.
Use deterministic server-clock tests to prove reset-to-60-seconds and no early
start. Use the native journey to prove that the matching authoritative deadline
and roster reach the correct UI, with measured receipt-to-render delay. Retain the
actual common server countdown and accepted gameplay witness. Do not change a
60-second rule to accommodate slow rendering or treat a broadened remaining-time
range as proof of the exact server rule.

## Crossplay setup must fit inside every enclosing lifetime

The first diagnostic run reached real Windows/Linux renderer readiness, then
failed before shared gameplay: a GitHub API TLS timeout and a queued Mac job.
The implementation owner measured readiness at 90.6 and 133.8 seconds. These
measurements do not show a slow game-launch failure. `0558c50` adds retries and
a gate waiting for all client jobs to start; the next run is evidence pending.

[GitHub Actions limits](https://docs.github.com/en/actions/reference/limits)
separate total concurrency from macOS-specific capacity and bound hosted job
execution. A generated matrix does not reserve simultaneous runners. Check the
actual account capacity and other running work; a larger timeout cannot create
missing capacity. Keep setup allocation separate from the gameplay clock.

Concrete source findings in the new scheduling implementation:

- [.github/workflows/crossplay.yml](../.github/workflows/crossplay.yml) permits
  up to 1,800 seconds for all peer jobs to start and 4,200 seconds for server-side
  artifact waiting, with a 75-minute server job. However,
  [crossplay.py](../tools/ci/crossplay.py)'s `server-serve` loop still stops the
  owned server/tunnel after **1,200 seconds from its own start**. A late but
  permitted client allocation can therefore outlive the server. Fix/validate
  the nested lifetime contract, not just the outer job timeout.
- `wait_artifacts` checks whether **all** named producing jobs remain unfinished
  when extending its deadline. A successfully finished client whose artifact
  already exists should not make waiting for another active producer invalid.
  Evaluate liveness for the still-missing artifacts/producers. Include a case
  with one successful producer, one delayed producer, and one failed producer.
- The 900-second child lifetime in
  [crossplay_runtime.py](../tools/ci/crossplay_runtime.py) encloses readiness,
  artifact handshakes and gameplay. Allocate/check headroom across those phases;
  independently allowed waits must not exceed their enclosing lifetime.
- `github()` currently retries every subprocess error alike. Preserve bounded
  retry for read-only transient transport failures, but classify permanent
  authentication/configuration errors and rate-limit responses. Bound each
  attempt by remaining outer time; retries must not silently overrun the barrier.

GitHub recommends efficient conditional polling when webhooks cannot be used,
and respecting `Retry-After`/rate-reset headers with bounded exponential backoff
for secondary limits. Read-only does not mean every error is transient.
[GitHub REST guidance](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api).

Test these scheduler cases with a fake clock and mocked job/artifact responses,
then rerun only the existing crossplay diagnostic on the same frozen payload when
runner code alone changed. Retain payload and runner revisions separately. If
compiled client/server code changes, make new diagnostic packages instead of
relabeling the old ones. Final promotion must use the trusted workflow's required
exact-byte evidence and may not import diagnostic passes blindly.

## Implement the smallest useful parity evidence next

The full [research packets](desktop-rendering-parity-research.md#prioritized-experiments-and-bounded-work-packets)
are intentionally separable. Start with these exit criteria:

1. **Transport/clock blockers:** focused old/new native results, explained terminal
   link handling, coherent countdown evidence, and one completed actual-package
   three-OS match. Finish current diagnostics before another full matrix.
2. **Production High witness:** one frozen scene with sharp arena/far DOF and
   reflecting water, original 2x MSAA enabled. Repeat on each target before cross-
   comparing decoded pixels. Preserve exact diff/feature-negative-control outputs.
   Add broader fixture coverage only after repeatability is established.
3. **Mechanics witness:** a compact per-tick replay using each exported package's
   shipped authority assembly; compare canonical states/events and retain the
   first differing field. Include one AI/math/RNG case. Shared source alone is
   not the result of this experiment.
4. **Native GPU scope:** inventory already-authorized Windows/Linux GPU capacity
   and run targeted scene probes there when available. If no such capacity exists,
   state the precise evidence gap and options; do not purchase/provision implicitly
   or claim WARP/software Vulkan proves physical-driver appearance/performance.
5. **Delivery:** keep the isolated platform QA baseline fixed while Mac stable
   ships. Once ready, integrate current main once, select the next available
   identity, build all targets and matching server, qualify all required receipts
   including latest-stable updates, then use protected publication and verify the
   public downloads/fleet. Record this as the repeatable release path.

Research completion does not mean these packets have passed. The implementation
owner remains responsible for exact current results and publication readiness.
