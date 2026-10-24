# v0.26.0 public release and cluster receipt

Published 2026-10-24 from `010cacaef6d51fe56a7800ab3cf2a29da7b83b21`:
[release and downloads](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.26.0).
[Verification](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40127739329)
and [signed publication](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40128407367)
passed. Godot remains 4.7.2. The client includes graphics presets, sharper menus,
first-use preparation, the public matchmaking default and compression negotiation.

The published archive's SHA-256 is
`46a616cf219e6d29083e0d5618c96fea0c1556d1cf1fb8acc45b70f76390c29a`.
Its update signature was verified against the bundled public key. All 217 installed
Mac files match the archive; strict code signing and a native M5 Pro Launch Services
smoke passed with 119 gameplay snapshots and an inspected capture. The equal-version
local preview was replaced explicitly with the official build. Installation backup
and extraction bundles were removed by identity-checked cleanup.

## Live service

Players use `https://blast.burbuja-lab.dev`. Both nodes run the exact 42-file CI
server inventory for this release, mounted read-only, plus the verified cluster
plugins. The kernel pin is `64ec8db830bb058de8ba2aa92b6f195165cd06d4`; its full CI
passed on retry. Local checks included 1,432 kernel tests and 47 HTTP journeys.

| Role | Lobby cap | Players per lobby | CPU ceiling | Memory ceiling |
| --- | ---: | ---: | ---: | ---: |
| Pi primary | 2 | 8 | 1 core | 384 MiB |
| DiveMac idle fallback | 1 | 8 | 0.5 core | 384 MiB |

These are conservative operating limits, not discovered hardware maxima. The
fallback stress guard refused a run because pre-existing host CPU idle fell below
10%; its larger capacity is deliberately uncertified. An actual eight-player
fallback match and node failure/recovery were verified.

## Bounded load and recovery evidence

- Two simultaneous eight-human Pi rooms passed the public HTTPS/WebSocket probe
  with legal 30 Hz inputs and maximum configurable rule caps. All 16 peers
  negotiated compression; worst per-peer p99 snapshot arrival gap was about
  **102 ms**. Sampled game CPU peaked at **26.34% of one core**, memory was about
  **90–102 MiB**, temperature stayed at or below **61.15°C**, and available host
  memory remained above **5.2 GiB**. Existing service guards passed.
- A separate 120-second baseline with two eight-participant rooms, including
  fourteen Hard bots, passed with existing services preserved. These tests use
  normal pickups, deaths and rematches; configured maximum inventory caps do not
  mean every participant held maximum inventory simultaneously.
- Admission checks passed primary preference, overflow, both-full refusal,
  cross-node joining and return to primary preference when a slot became free.
- Stopping only the empty Pi game node and its dedicated game tunnel allowed a
  new eight-player match on DiveMac. Restoring the Pi returned new rooms to it;
  the existing fallback match continued. All test seats were released.

The load receipts precede the final graphics/client commit; server authority and
transport source are unchanged between those measured binaries and the final CI
runtime. The final deployment was checked independently against its CI inventory.
No unrelated service was stopped or reconfigured. Matches on a failed node are
not migrated; the gateway/coordinator is still one failure domain.

Local receipts are under ignored `artifacts/public-hosting/`, including
`pi-compressed-release.json`, `pi-compressed-release-guard/`,
`pi-hard-2-baseline.json`, `failover-smoke.json`, and `release-026/`.
Host-specific protected-service inventories remain in private operator configuration.
See [cluster operations](cluster-hosting.md) for maintenance and repeatable tests.
