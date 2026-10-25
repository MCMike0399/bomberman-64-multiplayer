# Stage release review

Reviewed for the v0.28 integration against main `4f6c5af`. Independent Standards
and Spec reviews first examined Top Rules `25e63f7` / `7366e28`, tutorial
`c335037`, Up & Down `ebda447`, and Rock Garden `b9274c6` / `3e7e046`.
Rock Garden's later `56662ec` adds a correct eight-member roster convergence
wait; it is also retained. PR descriptions, stage contracts and the user's
request supplied the specs; no separate issue-tracker configuration exists.

The source heads are combined on the tutorial integration branch. Public merge
and release status belong to the final verification receipt, not this review.

## Standards

| Area | Finding | Resolution and retained check |
| --- | --- | --- |
| Top Rules | P1: peer revision 1 admits clients that reject new stage geometry | Shared online/peer revision 2; mixed peer rosters cannot start. HTTP and actual worker upgrades also reject old clients before unsupported state. `OnlineRoomChecks`, `VersionCompatibilityChecks`, `ServerCompatibilityChecks`. |
| Top Rules | P2: per-cover material creation grows across rounds; new walls/cover can miss the current Low policy | Terrain-owned cover/wall materials are shared and initialized for the selected preset. Restarted castle and closing-wall checks verify bounded materials and Low detail. |
| Up & Down | P1 integration gap: fractional stairs conflict with current-main's integer peer schema | Bounded finite fractional heights accepted; actual stage frames checked. |
| Up & Down | P1 integration gap: nested `below` bypasses peer validation | Exactly one complete optional lower cell is validated, including kind, height and ramp fields; malformed/infinite/deeper payloads are rejected before renderer parsing. |
| Rock Garden | P2: meteor impact does not advance chained bomb danger times | Published meteors seed bomb times before chain propagation. A large bomb's distant danger cell is checked at impact time. |
| Rock Garden | P2: garden shaders ignore Low's fine-detail policy | Cached garden materials now receive `quality_detail`; fine noise/fissures branch on it. Graphical preset checks inspect the actual materials. |
| Tutorial | P2: an earlier thrown blast can immediately clear a later planted bomb | Safety credit is bound to the planted bomb ID. Ordinary-input negative regression retained. |
| Tutorial | P2: collecting Focus credits an already completed normal charge | Credit comes from an accepted pump while Focus is equipped. Ordinary-input negative regression retained. |

The suspected second-ghost-death issue was **withdrawn** after ordinary inputs
showed the existing movement guard keeps ghosts on land. The regression instead
checks that the ghost remains alive at the boundary and can return to latch.
A possible terrain-query data-clump smell was addressed by unifying bridge
support on `CellDef.Below` / `StageLayout`, preserving authored wall heights.

Standards total: eight confirmed findings addressed; the highest severity was
unsupported/unvalidated peer stage admission. One suspected issue was withdrawn.

## Spec

| Area | Requirement and finding | Resolution |
| --- | --- | --- |
| Top Rules | Versioned handshake must prevent unsupported peer matches | The same protocol/admission fix above covers this requirement; it is deliberately reported on both axes. |
| Up & Down | Both floors and fractional ramps must reach peer guests | Unified layered stage serialization and bounded lower-cell validation retain the complete terrain. |
| Up & Down | Slabs block rising bodies; held players could overlap the underside | A real hold outside the ring followed by walking under it reproduced the overlap. Movement checks the carried body's full height; pickup refuses a hold that cannot fit. Legal throws over an edge remain possible. |
| Rock Garden | Bots plan around the published warning and ordinary chain rules | Meteor-triggered chain propagation now agrees with the earlier impact threat. |
| Tutorial | “Watch its timer” promised a countdown hidden with the battle HUD | A compact live status/charge/inventory line shows remaining effect time and is checked while it decreases. |
| Tutorial | Graduation promises an Easy opponent, but practice suppressed bomb placement | Normal Easy attack planning and its cooldown clock are enabled only for graduation; a real opponent-owned bomb is witnessed. |

The final review also corrected tutorial flood metadata so live aiming uses the
accepted rising-water plane; its failing and passing journey is retained.

The integrated trajectory review also found a swept roof-contact regression:
a fast charged throw could clear the roof horizontally at the endpoint while
penetrating it at an intermediate sample. Shared flight/projection now resolves
ceilings at the swept samples; the minimal failing trajectory is retained.

Spec total: six confirmed original-PR findings addressed, plus the integration
trajectory regression. Unsupported peer matches were the highest-severity spec
finding; held-body/roof contact and meteor timing were the principal physics gaps.

## Original-game fidelity follow-up

The user explicitly added resemblance and explosion research to the release
scope. [Stage research](stage-fidelity-research.md) and
[explosion research](explosion-fidelity-research.md) record inspected publisher
imagery, actual gameplay, model-listing provenance and what remains unknown.
Original reference downloads remain ignored and are not distributed assets.

The integration improves castle enclosure, side banners and dark panel/red-floor
contrast; rounded/fluted Up & Down supports and purple/white separation; and Rock
Garden's center-column ratio and stone variation. Heights used by support and
rendering stay aligned. These are image-informed adaptations, not recovered
original model measurements.

Official guide p. 102 explicitly supports explosions reaching above Top Rules'
thin walkways. Castle decks therefore pass blasts while remaining solid to bodies
and thrown bombs. Vertical/diagonal geometry and an actual lower blast eliminating
an upper player are checked. Up & Down's guide does not isolate blast penetration,
so its separate shielding policy remains unchanged.

Explosions now use a coherent round cosmetic fireball, brief pointed charged
flares, a distinct expanding cyan power wave and a clean fade. A faint danger
shell keeps the existing authority-height mapping; radius/fuse/damage rules remain
separate from the visual reconstruction. Actual graphical onset/bloom/fade review
is required in addition to numeric aspect/lifetime/rendering-budget checks.

## Evidence locations

Minimal failing and passing witnesses are retained under ignored
`artifacts/stage-release/review-regressions/`. The initial old-server admission
check failed; the updated owned server passed 44 HTTP/worker handshake checks, including an authenticated welcome and actual cancellation.
The incoming PR images and original references remain available under
`artifacts/stage-release/references/` for comparison. Final full-suite, exported-app,
CI, signed-release and deployed-runtime evidence is recorded separately.
