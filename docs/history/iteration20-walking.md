# Iteration twenty: a gripping cartoon scamper

The user still found the v0.18 walk stiff, robotic and cheap-looking after five
earlier gait passes. This pass measured why before tuning, then rebuilt the gait
around three findings. Geometry, the helmet, the compact suit and the short fixed
legs are unchanged; gameplay speed, collision and authority are unchanged.

## What was actually wrong

1. **The boots skated.** A planted boot can only sweep as far as a rigid stubby
   leg reaches (about .14 world units each way at the boot center). The v0.18
   stride covered 2.4 units per left/right cycle, so the "planted" boot slid
   forward at almost the body's speed. The new movement check measures this:
   world-space boot travel while down divided by root travel was **.95** before
   (`artifacts/iteration20-walk-red`) and is now **.04–.08** at 30/60/120 FPS.
   Average grip depends only on boot sweep versus travel per stance, so smoother
   curves could never fix it; earlier passes made strides longer and slower, which
   made skating worse.
2. **The legs were pendulums.** A rigid leg rotating at the hip puts the boot at
   its lowest when passing under the body, so the recovering boot never lifted and
   no heel/toe roll was visible: a wind-up toy.
3. **Nothing carried momentum.** Every part followed a sine of the walk phase.
   Bounce was .025 units, head and arms had tiny phase-locked offsets and the
   antenna was welded to the helmet, so starts and stops had no follow-through.

The original-game recordings cached for earlier iterations are too small to
measure stride timing. This pass is a caricature built from animation principles
and the measured geometry, not reverse-engineered original animation.

## Current gait

- **Cadence:** 1.5 world units per complete stride, about six steps per second at
  full 4.5 cells/s speed. Phase still advances only from rendered travel.
- **Stance (36% per boot):** heel strike toe-up, a nearly steady backward sweep
  while flat, toe-off roll. The rigid leg also slides up to .065 fore/aft under the
  belt, which hides it; the stem never lengthens or separates downward.
- **Swing:** the boot follows through behind, tucks up to .055 into the suit while
  passing, curls its toe and whips forward already moving backward, so touchdown
  has no velocity kink (Hermite segment matched to the stance velocity).
- **Body:** brief airborne beat between contacts, squash toward the planted boots
  after each heel strike, slight stretch on the rise, forward run lean with legs
  compensated to stay under the hips, side-to-side weight transfer and roll.
- **Secondary motion:** damped springs (fixed 240 Hz substeps, so 30/60/120 FPS
  match) drive glove swings, run lean, turn banking, head bob/nod/counter-roll and
  head lead into turns. Forward glove swings reach higher and cross inward.
- **Antenna:** pivots inside the crown, trails helmet pitch/roll, is thrown by
  starts and stops (soft-limited, no hard stop) and jiggles on landings. It swings
  about .32–.39 rad while running and settles upright after release.
- **Boots** squash briefly at contact.

Carry, pump, throw, kick, drop, knockdown, ghost and victory composition is
unchanged; the free-stride weight still mutes the gait during actions.

## Test bounds that changed, and why

| Check | Old | New | Reason |
| --- | --- | --- | --- |
| Skating while down | none | < .30 | New red/green witness for the core defect |
| Strides per 18 units | 7–8 | 11–13 | 1.5-unit stride so short legs can grip |
| Max leg angle | < .53 | < .79 | .62 swing, 7% follow-through, .11 lean compensation |
| Bounce drop in a 30 FPS frame | < .015 absolute | < 75% of range | At ~5 frames per step a pure sine moves 59% per frame; still rejects the old near-total pop |
| Suit compression | .005–.03 | .02–.07 | Readable squash at contact |
| Boot center lift | < .12 | < .135 | Forward reach before heel strike |
| Hip gap | any offset | below/beside hip only | Upward tuck and fore/aft slide stay inside the belt; separately bounded (< .06 tuck, < .075 slide) |
| Antenna | none | swing .08–.9, settles < .01 | Witness that follow-through exists and settles |

Fixed stem length, equal-displacement pose agreement, settle without extra steps,
boot orientation/velocity continuity and the whole-body lateral, roll, shoulder and
head bounds are retained.

## Evidence

- Red: `artifacts/iteration20-walk-red` (skating .948 at 30 FPS).
- Movement: 69 checks pass (`artifacts/iteration20-walk-probe`, latest run).
- `walk-feel` recording: 52 checks; side, three-quarter and normal battle camera
  (`artifacts/iteration20-walk-video`, latest run). Frames were inspected for heel
  strike, toe-off, lifted passing boot, glove pumping, lean, antenna tilt and the
  stop/settle; no detached limbs or stem stretch were observed.
- Action regressions: model-profile 13, charge-aim 104, kick-physics 79,
  presentation 39, roster 24, grapple 198, asset-audit 75, all passing with
  carry, knockdown, kick-windup and held poses inspected.
- Full source graphical profile (`artifacts/iteration20-graphical`): 2,203
  assertions, 177 captures, 32 scenarios, zero runtime errors. The exported
  v0.20.0 app passed 2,202/177 and was installed; see [verification](../verification.md).
- Before/after clip at normal speed: `artifacts/iteration20-walk-compare.mp4`.

Whether this reads as charming and "original" is the user's call; these checks
establish mechanics, not approval.

## Addendum 2026-10-26: ballistic hop timing

The user approved this walk and model as the game's identity, then accepted one
timing-only polish from before/after frames: `BomberModel` squares the contact
weight (`low*=low`) so the body spends most of each beat near the apex of its
hop and drops through a brief landing. Stride, amplitudes, springs and geometry
are unchanged. Movement at 30/60/120 FPS still passes (bounce range .028,
compression .052, skating .06–.09, 12 contacts per 18 units).

The trade is at the opt-in 30 FPS cap. With five frames per step, a landing
that takes one frame moves up to .89 of the bounce in that frame at the worst
phase alignment; the symmetric cosine reached .75 at its worst alignment, and
the old `walk-phrasing` check only ever sampled one alignment, so its earlier
pass was partly luck. The check now sweeps four start offsets at 30 and 60 FPS
with bounds .92 and .55 (60 FPS worst case is .44). The boot-lift ceiling in
`movement` and `walk-feel` rose from .135 to .14 because the three-quarter leg
already sampled .1345 before and a passing boot now sits up to .003 higher
while the body hangs. Evidence: `artifacts/walk-hang/{before,after,movement,compare}`.
