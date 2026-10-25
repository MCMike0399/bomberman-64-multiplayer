# v0.31 candidate review and verification

2026-10-25: PRs #13–#17 were reviewed and integrated into **PR #15**, as requested.
The original PRs remain open for individual review. This is **v0.31.0/build 37,
schema 6**, not a published or deployed release. The complete runtime candidate
verified below is `3c5ed3e39cad02eecc17ac3db7e53ec310097dc2`; subsequent promotion-tool/documentation
follow-ups leave the tested game/client/server binary sources unchanged and have
separate public-contract verification.

## Standards

- **#13:** the new power-blast journey assumed Red existed in a random practice
  cache. A failing native witness preceded an explicit Red-only pool and walking
  to the actual spawned item. Effects remain presentation-owned.
- **#14:** wind-driven ricochets changed stored momentum. The regression now
  preserves reflected momentum separately from wind displacement. Initialized
  properties were moved to the main partial and the simulation notice restored.
- **#16:** real stair meshes had coplanar structural/paving tops. A native geometry
  witness preceded the small cosmetic separation of treads, coping and sills;
  collision remains unchanged. Its operator peer hello now matches admission,
  and its shader cache lives in the main partial.
- **#15:** connection receipts previously could verify the primary repeatedly
  while claiming every worker. Probes now request a region, assert the owning
  server ID, play a real server-hosted match and await actual room retirement.
  Operator tools read the canonical release token. Durable state includes parent
  directory fsync, recovery validates the installed adapter, and no-drain recovery
  calls no host command.
- **#17:** no actionable standards finding was established. Its authority-owned
  landings/CPU recovery and separate presentation changes are preserved.
- The last independent standards round found two remaining privacy violations:
  unmasked workflow variables and HTTPS exceptions could expose worker origins.
  Both were corrected with secret-backed configuration, individual origin/hostname
  masking and private diagnostics with suppressed exception chaining. Retained
  failure fixtures cover transport and subprocess exceptions.

Final independent standards round: **2 findings, both corrected**; its highest
severity was P1 (workflow origin disclosure). No unresolved source finding remains.
The suggested consolidation of repeated stage descriptors remains an optional
refactoring, not a release blocker.

## Spec

- Release promotion now rejects version/build downgrades, including versions
  later marked prerelease to stop new installs, preventing a server
  cutover that existing clients could not follow through the updater.
- Uploads always stage a draft; downloaded bytes must match before publication.
  A publication retry can finish the exact verified deployed candidate after main
  advances, without rebuilding or reapplying its server deployment.
- The live probe goes beyond a welcome message: it starts a server match with a
  CPU, observes advancing fight snapshots and waits for capacity to be released.
- Blizzard bombs now drift off supported spires while retaining side collision.
  Both that defect and the wind-ricochet defect had failing authority regressions.
- The independent Blizzard/Greedy branches advertised incompatible stage sets
  with the same numeric protocol. Greedy's standalone branch now rejects 5; the
  combined candidate unions both stage validators and uses `6/0.31.0/37`.
- Integration preserves both arenas, weather/cover metadata, six-stage menus,
  all stage journeys and the expanded 72-case AI soak.

Final independent spec round: **0 remaining findings** at its reviewed snapshot.
The remaining standards findings from that round were fixed afterward as above.
No source requirement was waived; production activation is a separate prerequisite.

## Failures found by full testing

The full graphical run exposed a further transport defect that a focused replay
missed: the socket worker inherited Godot's synchronization context. Accumulated
native polling stalls delayed queued heartbeats until the real six-second host
lease expired. A controlled slow-poll replay retained a 6.8-second stall witness.
A separate managed HTTP/WebSocket test reproduced the same ownership error by
blocking a UI context for 1.2 seconds and observing stalled transport pings.

`NetworkSession` now starts socket I/O on the thread pool. Peer simulation and
Godot link objects remain on their polling thread. The eight-client fixture polls
its elected authority between guests, including capture/setup waits. The managed
smoke retains `UiContextNetworkChecks`; headless/package gates retain the
controlled `adaptive-slow-poll` journey. Production leases, qualification limits
and game clocks are unchanged. Both red cases and the complete corrected run are
retained; the earlier focused pass is not substituted for full-suite evidence.

The first combined graphical preparation also exposed an import-path defect:
its server staging dynamically imported a helper that now depended on release
identity. It now uses the explicit packaging CLI, with a fresh-process contract
that also verifies private host configuration is excluded.

## Final evidence

All five local gates passed against the integrated candidate:

| Gate | Observed result |
| --- | --- |
| Public | Public-boundary, Python tooling and audio checks pass; includes 60 CI-tool contracts, real fixture P-256 signing/tampering and adapter recovery processes. |
| Managed | 2,854 game, 354 client, 80 AI, 19 audio and 23 placement checks; 72 AI soak cases, real multiplayer, 9 forced-relay and 36 fleet checks; tested server/plugin packaging. |
| Release probe | 78 checks against the staged server, including actual match progress and room retirement. |
| Headless | 1,528 core assertions, 19 peer-room, 9 adaptive and 10 controlled slow-poll assertions. |
| Package | Native controller initialization, strict ad-hoc signature, self-contained export, 255 package-smoke assertions and the peer/adaptive/slow-poll journeys pass. |
| Graphical export | All **50 journeys**, **4,174 assertions**, **406 captures**, **zero runtime errors**. Actual windowed Metal Forward+ in an isolated macOS desktop. |
| Greedy Trap network | Separate server-owned and peer-hosted traversal journey: **43 assertions**, **2 captures**, zero errors or warnings, against the identical exported-app inventory. |

Together, the graphical runs cover **51 journeys, 4,217 assertions and 408 captures**.
The full graphical receipt retains two SCTP reset warnings on shutdown.
Headless/package receipts also retain SCTP reset and ObjectDB shutdown warnings;
these are not represented as warning-free runs. No physical-controller or native
M-series performance certification is added. The owned VM was stopped afterward.

Captures were inspected for the star-core and power-wave effects, both Up & Down
side landings, Blizzard opening/eight-player combat, and Greedy Trap's stairs,
lighting, miniature and lower chamber. Screenshot inspection does not establish
subjective approval or undocumented original-game physics.

The local integration worktree retains receipts under ignored
`artifacts/review/combined-final`. Earlier negative witnesses remain in the feature
worktrees under `artifacts/review` and release-tool failures under `artifacts/qa`.
Raw traces and private operator data are not in Git; fixture signing keys are
temporary and never published.
The [public CI run for the runtime candidate](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40242919902)
passed. Private CI jobs intentionally do not execute PR code; these local results
are separate evidence. Trusted CI must verify the eventual main SHA before release.

## Release methodology and activation

The [release runbook](releases.md) owns the doctor → prepare → verified CI → draft
→ deploy/verify → publish procedure, exact commands, signing and recovery. Builds
are reused across deployment/publication retries. Existing P-256 update trust and
ad-hoc app signing remain the chosen policy; Apple notarization is deferred.

The read-only setup audit found missing `production` environment, dedicated
runner, installed adapter configuration, persistent state setting, target secret
and worker-placement secret. Existing update-signing and private-build credentials
are configured by name. Source/fixture verification does not certify an operator's
host adapter or production rollback. No main merge, production deployment, public
release or installed-app replacement was performed by this review.
