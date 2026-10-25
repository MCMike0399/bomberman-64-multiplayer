# v0.28 stages, tutorial and explosions

Published **[v0.28.0 / build 33](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.28.0)**, integrating PRs #2 (Up & Down), #3
(Top Rules), #4 (Rock Garden) and #5 (Circuit Garden/tutorial). All four PRs are merged into main, and both shared servers run the exact verified
release runtime. The personal Mac app was not replaced.

## Player changes

- Rock Garden is an enclosed lawn with five columns, sparse boulders and
  telegraphed falling fireballs. Up & Down has a suspended ring, six stair flights,
  lower passages and rising water. Top Rules adds two castle stories, three upper
  lanes, four stairs and inward-moving walls. Sunken Temple remains available.
- Circuit Garden provides 28 selectable tutorials with saved progress, live
  status/charge information, keyboard/controller instructions, retry/next and
  free practice. Graduation includes a real Easy opponent.
- Explosions have a coherent round fireball, brief pointed charged flares,
  expanding cyan power wave and transparent fade. Original imagery informed
  these changes; no original models, textures or recordings ship.
- Bridge support, ceilings, held-player clearance, meteor-triggered bomb chains,
  material lifetime and tutorial completion errors were reviewed and corrected.
  [Standards and Spec findings](stage-integration-review.md) retain the evidence.
- Online play requires the v0.28 protocol. Older clients receive an update message
  before incompatible geometry/hazards arrive; updated clients also check the
  actual worker's version. Existing room limits and hosting topology are unchanged.

## Inspected captures

These are actual captures from the final rebuilt local export, using the normal
battle camera and an isolated macOS Metal renderer. They are not original-game
screenshots or physical-controller evidence.

![Top Rules](media/release-028/top-rules-four-player-opening.png)

[Up & Down](media/release-028/up-and-down-battle-camera.png) ·
[Rock Garden](media/release-028/rock-garden-four-player-opening.png) ·
[Charged explosion](media/release-028/blast-large-bloom.png) ·
[Tutorial browser](media/release-028/tutorial-browser.png) ·
[Flood lesson aiming](media/release-028/tutorial-flood-aim.png)

[Stage research](stage-fidelity-research.md) and
[explosion research](explosion-fidelity-research.md) separate publisher/gameplay
observations from project dimensions and visual tuning. Original model listings
were found, but their downloads returned 403; no model coordinates are claimed.
Top Rules' thin decks pass blasts based on explicit official-guide evidence.
Up & Down's corresponding behavior was not established and remains separately
shielded. Physical decks continue to stop bodies and thrown bombs.

## Local verification

Final runtime/harness baseline: `92bb73a`; the later change adds an additional
managed peer-hosted Up & Down traversal check and documentation/media only.

| Gate | Observed result |
| --- | --- |
| Public contracts | Passed, including documentation links and boundaries |
| Game / client / AI | 2,887 / 329 / 65 checks passed |
| Audio / cluster placement | 19 / 19 checks passed |
| AI soak | All 48 combinations passed; longest stationary window 1.27s, below 12s |
| Real network | Full HTTP/WebSocket/peer smoke passed in 153.6s, including 2–8 humans and seven server bots |
| Version admission | 44 checks include legacy update errors, authenticated worker welcome after rejected legacy upgrade, and actual ticket cancellation |
| Layered online routes | Both cluster-hosted and peer-hosted Up & Down traverse the real stairs/underpass and place a bomb below the ring; Top Rules has equivalent real cluster/peer witnesses |
| Headless | 1,228 core assertions plus 19 peer assertions passed |
| Full rebuilt-export graphical suite | 3,260 assertions, 326 captures, zero runtime errors; all registered journeys completed |
| Package | Native controller backend audit, signed export, 255 exported smoke assertions and 19 exported peer assertions passed |

The full graphical run retained one native SCTP stream-reset warning; direct
transport, takeover, relay recovery and eight-peer assertions all passed.
Headless/package shutdown logs also retain Godot ObjectDB warnings. These are
not reported as zero-warning runs. The earlier focused fidelity run passed
448 assertions / 66 captures with zero errors or warnings.

Inspected the final arena silhouettes, ordinary/oblique underpasses, cap depth,
flooding, fireball warnings/impacts, normal/charged/power explosions, tutorial
countdown and live rising-water aim. The final package and graphical exports
have identical managed game assemblies and PCK; native signed-file hashes are
recorded separately. Passing numeric checks do not imply user approval of art,
native-host GPU throughput, universal Internet conditions or physical gamepads.

Receipts are under `artifacts/stage-release/` in the isolated release checkout:
`managed-final`, `headless-final`, `graphical-final`, `package-final`,
`public-final`, `review-regressions/online-version-layered-online`, and
`package-graphical-comparison.json`. Earlier minimal failures remain in the
original checkout's `artifacts/stage-release/review-regressions/`. The unrelated
networking research in the original checkout was preserved.

## Publication and shared service

All four PRs were merged through integration PR #5. Runtime source:
`9566a6745bc21a0eb099a2df46af824a5d25bc73`.
[Trusted CI 40153012524](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40153012524)
and [signed publication 40153538831](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40153538831)
passed. All required main jobs succeeded; the optional hosted Metal job was
skipped, with local exported-app Metal evidence recorded above.

The published update signature was independently verified against the embedded
P-256 public key. The app archive was inspected in place: correct bundle identity,
version/build, complete controller backend and an inventory of 228 files. The
server archive and all 42 runtime file hashes matched their manifest.

| Published artifact | SHA-256 |
| --- | --- |
| macOS archive | `c31e3c1526c697b3a59be1875f21d486c99f5d8dde08949af84ea1d6121a25be` |
| Portable server archive | `579affe94727292c2eaae530b690c56a057572bbf6a1e0e58c26024aabe9103f` |

DiveMac and then raspberrypi were drained and observed empty before their owned
worker restarted. Both running `/app` inventories matched all 42 official hashes,
reported online protocol 2 and retained host configuration, plugin mounts and
resource limits. Rollback runtimes remain on both nodes. Primary remains one CPU /
two rooms; fallback remains half a CPU / one room; both retain 384 MiB and 256 PIDs.

Actual public journeys verified cluster-hosted and peer-hosted Up & Down stairs,
lower-floor traversal and below-ring bomb placement. The initially excluded
fallback recovered through its existing monitor without changing the guard;
a region-selected public Rock Garden match then verified two-player admission,
fireball warning, accepted movement, actual impact and escape on DiveMac.
Final authenticated fleet observations showed both nodes active, zero rooms and
zero players. The public API echoes protocol 2 and returns the actionable update
message to legacy admission requests.

Delivery receipts are under `artifacts/stage-release/release-assets/` and the
`*-install.json`, `*-final.json`, `public-*-gameplay.*` and `public-health.json`
files in the isolated release checkout. No personal app installation, unrelated
service restart, capacity increase or private configuration change was performed.
