# Iteration seventeen: a visible throw endpoint in normal matches

## Reproduction and cause

The focused `reticle` native scenario starts an ordinary two-human Sunken Temple match with LocalSession's deterministic seed 64. It holds Space and presses J through the production keyboard adapter; the authority confirms a bomb overhead. Four brief WASD turns retain clear-floor landing markers. Holding Aim and S reduces range to 2 cells, where the stock cover stops the projected arc at `(10.5, 1.61, 14.39285)` with `Obstructed`.

Before the fix, `python3 tools/playtest/run.py --build --scenario reticle --output artifacts/iteration17-reticle-red` failed the endpoint-marker assertion. Evidence: `artifacts/iteration17-reticle-red/20261021T234440.786294Z-reticle/normal-reticle-short.png`. The old renderer explicitly hid its target unless the endpoint was `Landing`; blocked, fuse, water and void outcomes had no target. Separately, the initial normal-match screenshot showed the depth-tested label partly hidden by two cover blocks. Input and local ownership worked in this reproduction; disappearing markers did not require a network session or practice mode.

## Change

The world arc remains anchored to the shared authoritative throw projection. A separate screen-space endpoint draws a cyan outlined landing ring/crosshair for safe ground, or a gold outlined cross for blocked, fuse, water and void outcomes. Targets outside the safe viewport frame become a directional edge arrow. The reticle contains shapes and color only: no player, range, pump or outcome text. The old world label is removed as well. Screen position updates each rendered frame as the camera moves; it does not alter direction, range, throw physics or target selection.

Local ownership still comes from ArenaView's controlled-player inputs and accepted carrying state. Each local human has a separate marker; release or session cleanup removes that carrier's overlay. Rendering visibility follows the active match screen and round phase, including Results and hidden screens. `LandingMarkerVisible` retains its original safe-landing meaning; the separate `EndpointMarkerVisible` describes both landing and warning indicators. `EndpointLabel` remains semantic test metadata only and is never drawn.

## Verification

The text-free source run `artifacts/iteration17-reticle-clean/20261021T235725.667131Z-reticle` passed 34 checks with 13 captures and zero runtime errors. Earlier recordings containing labels are superseded and are not the final appearance.

- The normal-match journey verifies Space+J, four movement directions, a real blocked endpoint, two simultaneous carriers, independent release and cleanup.
- Pixel checks require both colored marker pixels and a contrasting outline. Separate renderer contracts cover all five endpoint types and an offscreen arrow; these are not claimed as authority gameplay outcomes.
- A later integration review caught a lifecycle bug: Results retains the arena background, so arena visibility alone could leave a winner's marker over the menu. Preview creation now requires Match and Fight/Hurry; clearing immediately hides nodes before deferred deletion. A normal duel regression lets the opponent die to their own ordinary bomb while the survivor still carries, then verifies the actual Results transition removes the overlay.
- Final exported-app evidence is recorded in [verification](../verification.md). Headless projection checks establish throw semantics, not GPU visibility.

`reticle` belongs to graphical `all`, not a headless suite. Before/after captures use the normal battle camera. No global gameplay debug labels were added.
