# Preparation draw callback fallback — 2026-11-16

A visible but unfocused native Mac window stopped completing normal draw fences.
Process frames continued and forced rendering succeeded, but each fence paid a
500 ms fallback deadline. The expanded roster needs 47 fences, so accumulated
waiting alone exceeded the existing 20-second preparation bound.

Each startup/preset preparation now owns one disposable `PreparationDrawBatch`,
shared by its overlay, effect warmup and miniature preparation. The first missing
normal draw keeps the 500 ms deadline. Remaining fences use a cancellable process
update followed by actual ForceDraw/ForceSync; every model, draw and nonempty
pixel readback stays. Disposal clears the active force policy. The normal callback
path, cancellation, warmup contents and 20-second bound remain unchanged.

The standalone native starvation probe withholds only warmup callback completion
and still renders real previews. Its expected count uses the actual planned
miniatures and explicit fixed 7 + 4 per-miniature contract: main's six maps require
31 fences, and the final ten-map roster requires 47. The source renderer loops
retain their original draw counts independently of this assertion contract.

## Retained expanded-roster native witness

Probe-only `c0e257c` retained the old policy. The native
`starvation-before/20261116T035959.849585Z-graphics-preparation-starvation-probe`
failed at the original 20 seconds after nine checks, before the final capture,
with only 8/10 readbacks. This was an integration build with the ten-map roster.

The native-proven scoped implementation `8fe9d2b`, now extracted onto current
main without its maps/art, passed
`starvation-after/20261116T042106.320608Z-graphics-preparation-starvation-probe`:
19 assertions, one real retained Setup-preview capture, zero errors/warnings.
Starved Low preparation took **0.5905465 seconds**, one normal wait, 47 forced
renders, 10/10 readbacks and disposed/reset policy. Root inspected the image.
The subsequent batch attempted normal callbacks afresh, demonstrating reset.

The focused extraction adds managed coverage of both 31 and 47 fences, the 23.5s
legacy-lifetime control, normal callback completion, fresh/disposed scopes and
cancellation. The pure policy is linked into client tests; it introduces no
Godot dependency into Bomber.Client. Main's six-stage roster, release identity,
art and diagnostic121 code remain. This extraction's source build and 14 focused /
527 full client checks pass; cached NuGet audit access warnings were recorded in
the Godot build. New native qualification belongs to the release owner; the
managed models do not replace real rendering evidence.
