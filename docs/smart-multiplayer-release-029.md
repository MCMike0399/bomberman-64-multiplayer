# Adaptive multiplayer and Rock Garden preview — delivered

2026-10-25: **v0.29.1/build 35** is published and both shared workers run its
verified portable server. It includes adaptive multiplayer plus merged PR #6,
which confines Rock Garden's menu grass to the miniature without changing the
full-size gameplay exterior. All battle arenas and the tutorial are preserved.

- [Release and signed update](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.29.1)
- [Exact-source verification](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40207413952)
- [Publication](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40208353142)
- Runtime/tag source: `cda244e1ec5f95ea222191756c16ce57dff256ba`.
- Kernel pin: `8c861a941c6619b8719ad5658ba6bd921cf47ff5`, following its generic,
  consumer and dev-deployment gates. No stable Kernel promotion was performed.

The published update signature, both archive checksums, bundle version, source
metadata and inventories were independently verified. The Mac archive contains
228 files; each deployed server matches all 42 official runtime files and 15
matching optional-plugin files. Both containers are healthy and advertise
protocol 3. Final observations showed both nodes active with no remaining test
rooms or players. Rollback copies retain the previous runtime and configuration.
The earlier v0.29.0 release remains immutable; v0.29.1 adds the requested preview
fix rather than replacing an already published archive.

## Delivered behavior

[Adaptive rooms](smart-multiplayer.md) defines the complete contract: one public
queue with automatic countdown, private room codes and hosting preferences,
measured host qualification, owner/authority separation, independent execution
budgets, reliable actions, disposable native snapshots and authenticated relay
fallback. Guest reconnects and ordinary departures retain healthy peer matches.
Losing the authority returns the same room to a fresh match.

The shared monitor distinguishes idle coordination rooms from active resource
claims. Pressure defers new placement and execution while preserving existing
matches. CPU/memory ceilings, signing credentials and unrelated workloads were
preserved. Intentionally stopped services are excluded from active-service QA.

## Verification

| Gate | Observed result |
| --- | --- |
| Final managed CI | 2,910 game, 336 client, 65 AI, 19 audio and 23 placement checks pass; all 48 AI soak scenarios pass |
| HTTP/WebSocket | Full smoke passes; nine forced-relay checks include real socket interruption, reset input sequences, orderly departure and authority loss |
| Real two-worker lab | 36 checks pass with independent worker processes and Valkey: execution overflow, waiting-room reuse, pressure, crash/recovery, surviving matches, legacy-admission rejection and room limits |
| Final Linux headless | 1,228 core, 19 legacy peer and nine adaptive assertions pass, with zero runtime errors |
| Final official Mac package | 255 package-smoke, 19 peer and nine adaptive assertions pass, with zero runtime errors |
| Local rebuilt graphics | Adaptive: ten checks/four captures; network: 71/four; final preview/setup: 97/seven. Captures were inspected; the final setup and adaptive runs had zero errors and warnings |
| Public private room | Three real clients create/join by code and run shared dedicated gameplay; repeated successfully after the final patch deployment |
| Public matchmaking | Two real queued clients share a room, start without a manual request, acknowledge ordinary inputs and observe bombs |
| Public coordination limit | Eight Pi rooms accepted, ninth refused, released slot reused; all nine owned test rooms subsequently retired |

CI retains native SCTP reset and ObjectDB shutdown warnings in some receipts;
all runtime-error gates remain strict. The optional hosted Metal job
is disabled; graphical evidence comes from the isolated local Mac desktop.
Headless runs do not establish GPU or physical-controller behavior.

Regression witnesses exposed and fixed owner turnover after dictionary reuse,
pending starts surviving departures, malformed probe/quality fields, reconnect
sequence resets, repeated lobby redraws and native channels closing before the
server processed departure. Eight-player relay traffic also exposed a per-host
byte allowance sized for one recipient; both enforcement layers now account for
up to seven recipients while retaining bounded flood protection.

## Measured operating limits

| Node | Coordination rooms | Dedicated matches | Peer relay reservations | CPU ceiling | Memory including swap |
| --- | ---: | ---: | ---: | ---: | ---: |
| raspberrypi | 8 | 2 | 1 | 1 CPU | 384 MiB |
| DiveMac | 1 | 1 | 0 | 0.5 CPU | 384 MiB |

The Pi passed a guarded public-endpoint test with **24 participants**: two
8-player dedicated matches and one 8-player forced-WSS-relay match. The peer
qualified first, then both dedicated matches ran for 120 seconds of overlapping
load. This used v0.29.0's multiplayer runtime; v0.29.1 changes presentation and
version metadata, and its official runtime received fresh hash and gameplay
checks. The test used Top Rules, ordinary action inputs, real deaths/rematches
and unchanged physical limits.

- Worst client p99 snapshot arrival gap: 229.7 ms dedicated, 230.3 ms relay.
- Worst dedicated p99 snapshot generation gap: 100 ms.
- Peak Pi temperature: 68.85°C; minimum available memory: 4,892 MiB.
- Maximum observed swap growth: 2 MiB; protected-service/host guard passed.
- The separate eight-player relay test passed with p99 arrival gaps of 77–114 ms.

These are update-gap measurements, not one-way or input latency guarantees.
The opposite startup order—both dedicated matches first—did not qualify a new
all-relay host within the probe deadline. That failure is retained. Relay echo
RTT reports under combined load reached about 380 ms, above the 200 ms new-host
qualification gate. Configured reservations therefore do not guarantee that a
new peer match can start under every load/order/network condition. Direct paths
avoid the relay workload when healthy.

DiveMac's CPU headroom guard declined the new cross-machine native load before
launch. Its capacity was not increased, pressure exclusion was not overridden,
and no fresh physical fallback-load/crash or Macintosh–DiveMac direct-path result
is claimed. Native eight-client behavior and multi-node failure handling have
the separate local/lab evidence above. No universal NAT, maximum fleet capacity,
TURN, prediction/rollback or exact mid-round migration claim is made.

Raw receipts remain under ignored `artifacts/smart-multiplayer/`; CI carries its
curated receipts. Owned Macintosh and DiveMac export copies were identity-checked and removed.
The published Mac ZIP was inspected in place, and the personal installation was
not replaced. [Cluster operations](cluster-hosting.md) owns subsequent updates.
