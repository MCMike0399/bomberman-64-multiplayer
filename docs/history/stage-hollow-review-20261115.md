# Cutaway Hollow first redesign: REVISE (substantial)

The hue split between ochre banks and green floor is a real gain, but the stage now reads as a carved wooden planter floating in a pale void. It does not read as a slice of earth, and the source shows why.

**Inspected (all opened with Read):**
- `HOLLOW-STUDY-cutaway-hollow-shape-high-midday-combat`
- `HOLLOW-STUDY-cutaway-hollow-shape-high-night-combat`
- `HOLLOW-STUDY-cutaway-hollow-shape-low-midday-combat`
- `HOLLOW-STUDY-cutaway-hollow-shape-oblique-inspection`
- `HOLLOW-STUDY-cutaway-hollow-shape-cut-face-inspection`
- `HOLLOW-SOURCE-TempleStageView.CutawayHollow.cs`
- `HOLLOW-SOURCE-TempleStageView.FieldOfGrass.cs`
- `HOLLOW-SOURCE-TempleStageView.HollowStones.cs`
- `HOLLOW-SOURCE-TempleStageView.FieldOfGrassExterior.cs`

## What reads at battle scale

- **Banks: engineered, not earth.**
  - The side ridges are uniform half-cylinders (radius about 0.88, swell ±9%) running the full length, with vertical outer walls. They read as rolled wooden gunwales.
  - The soil shader's fine horizontal lines read as wood grain.
  - The front coping is another roll, 0.83 high, across the whole open end.
- **Ledges and ramps: flat strips.** The ledge is a flat felt-green strip and the ramp a flat brown plane, meeting on ruler lines.
- **Grass boundary: still a rectangle.** The side wobble of ±0.3 is invisible, and the front and back edges are straight.
- **Stones: authored but repetitive.** They are no longer Rock Garden boulders. But three round variants at one height, in one grey, read as rows of teeth or headstones.
- **Cross-section: does not communicate.**
  - No strata are visible.
  - The roots are symmetric three-prong shapes that read as propellers.
  - The pebbles are about 12 px.
  - The burrow is a black rectangle.
  - Much of the face sits behind the status banner.
- **Setting: weakest in the game.** A flat khaki plane with one hay rick and stile on a floating shoulder block. There is no countryside.
- **Actors: fine.** Players on the ledges read clearly. The half-hidden bots and the bomb in the grass are intended concealment, not a defect.
- **Low and Night:** Low keeps the same forms. Night has no warm accent.

**Rendering defects:**
- **Burrow hole:** the cut face skips whole 0.25 quads by their centres, so the hole is blocky while the tunnel behind is round. The gaps probably explain the black and teal see-through.
- **Seam lines:** thin dark outline seams show at both ends of the front lip, where the coping tube meets the ridge end caps.

**Retain:**
- The gameplay support sheets exactly as they are.
- The blade mesh and its density.
- The dry-stone idea at the far end.
- The stone moss caps.
- The stile and hay vignette.

## Corrections for the next pass

The support sheets stay planar and exact. Natural appearance comes from everything around them, plus shader blending on them.

**1. Make the banks the land itself.**
- Delete the vertical outer walls and the floating shoulder block.
- Build the exterior terrain at bank-top height (about 2.6, just under the 2.7 ledge) on the left, right and far sides, extending past the frame.
- The hollow then reads as a dip in a meadow, cut open only at the front.
- Run the front cut face past both frame edges at the same z, instead of ending at the arena corners.
- At the far end, raise a turfed slope from the valley to the plateau. Keep the dry-stone as a retaining course at its foot.

**2. Replace the rolled ridges with low turfed shoulders.**
- Vary the height between 0.3 and 0.9 along the length, as two or three distinct hummocks per side, not a sine.
- Slope the outer side into the plateau.
- Give them a turf top. Soil shows only where the front cut slices them.

**3. Delete the front coping roll.**
- The open end gets a thin sod lip no more than 0.2 high, with an overhang of 0.15 to 0.4 that varies along its length.
- Add three to five notches where clods have dropped, with the clods lying at the base.
- This green-over-brown turf line following the ledge, slope and valley profile is the main cue for "slice of earth".

**4. Rebuild the material and value structure.**
- **Soil shader:** remove the line grain.
- **Strata, by world height on the cut face:**

| Layer | Thickness | Colour |
| --- | --- | --- |
| Dark topsoil | about 0.3 | `#4a2f22` |
| Red-ochre loam | main body | `#a8623c` |
| Pale clay band, with pebbles | about 0.5 | `#d2a373` |
| Dark base | to the bottom | `#6b4a3a` |

- **Ledges:** a worn ochre footpath about 1.2 wide with ragged edges, and turf either side. Actors keep a clean ground value.
- **Ramps:** a noise-masked turf-to-soil blend, so turf fingers spill 0.3 to 0.8 down the slope and scuffed soil reaches onto the ledge. This is shader only, so support is untouched.
- **Plateau turf:** cooler and lighter than the valley grass.
- **Valley grass:** it needs the wheat-gold tips from the original brief. It is currently a uniform green.

**5. Break the grass rectangle.**
- **Sides:** raise the edge wobble to about ±0.6 at low frequency.
- **Slope foot:** let sparse tufts no taller than 0.35 climb the first half-unit of each slope. They must stay short so they never imply concealment outside the valley.
- **Front and back:** taper density and height over the last 0.8 units, with ±0.35 of edge wobble.

**6. Scale the cut-face features and fix the burrow.**
- **Face depth:** lower the bottom from −1.15 to about −1.8, so the valley section has readable height above the hint row.
- **Burrow:** do not skip quads. Cut the opening with a ring that matches the tunnel, or set a dark inset ellipse behind a raised soil lip. Make the mouth about 1.2 × 0.8.
- **Roots:** two or three tapering curved roots per side, hanging from the turf line. Width 0.3 down to 0.08, up to 2.5 long, built as chained segments on a curve, with no symmetric fork.
- **Pebbles:** clusters of three, each at least 0.8 wide, in the clay band.

**Bounded, same pass if cheap:**
- **Stones:** five variants at the same peak height but with different mass. Include a flat slab with an elliptical footprint, a leaning needle, and a tall-and-small pair. Add per-instance width variation of ±15% and warm or cool grey variation.
- **Seams:** close the lines at the lip ends.
- **Night:** a lantern on the stile.

## Evidence needed for the next review

- High Midday at the battle camera with eight players in combat.
- The same on Low and at Night.
- The oblique view.
- The cut-face inspection with the burrow visible.
- A frame with bombs in the grass and a player on each ledge and each slope.
- A frame without the status banner, so the cut face is unobstructed.