# Shape-study review: ship REVISE, lock REVISE (narrow), Top Rules brief

Neither study is ready for surface finishing. The ship still reads as a platter around a rectangle. The lock's court is good, but its gates and setting are not.

**New images inspected (all opened with Read):**
- **Ship:** `STUDY-ship-sea-sick-shape-high-battle`, `-low-battle`, `-high-maximum-roll`, `-oblique-inspection`, `-near-profile-inspection`.
- **Lock:** `STUDY-lock-copper-lock-shapes-high`, `-low`, `-oblique`, `-night`.
- **Corrected:** `CORRECTED-glass-before-redesign`, `CORRECTED-grass-before-redesign`.

## Sea Sick, "Tubby Cutter": REVISE

The near-profile inspection view shows a real hull, with sheer, a wale stripe and a dark belly. The battle camera does not. There the outline is a near-symmetric rounded lozenge with an even flat shelf all round, which reads as a serving platter.

**Retain:**
- The left-to-right axis.
- Long staggered planks.
- The capstan and the bitts, the best new forms and readable at battle scale.
- The round banded mast.
- The stairs.
- The wale stripe and two-tone hull.

**Geometry changes before any materials:**

1. **Remove the shelf.**
   - The flat band between the gunwale rail and the hull edge is about 0.5 to 1 cell wide on all sides, and it reads as walkable deck beyond authority bounds.
   - Along both long sides, make the rail the top of the hull, with an outboard ledge of 0.2 cell at most.
   - Flare the hull outward below the rail, widest about 0.3 cell outboard at the wale, so the belly is seen from above.

2. **Make bow and stern different shapes.** They are currently mirror images, with a peg for a bowsprit.
   - **Bow (right):** the two rails converge from the forecastle corners to a rounded stem. The surface between them should be a cambered, rising whaleback, not a flat floor, ending about 0.8 player-height above the forecastle.
   - **Bowsprit:** half the mast-base diameter, at least 1.5 cells long, raked up about 20°.
   - **Stern (left):** a broad, nearly flat transom, with a curved taffrail bulwark about one player-height tall.
   - Do not make the ends more distinct by adding props.

3. **Exaggerate the sheer.**
   - It is invisible from the battle camera.
   - Raise the rail line about 1.0 player-height at the bow and 0.7 at the stern, relative to the waist.
   - Keep the near-side waist rail at its current low height.

4. **Earn margin with the camera.**
   - The hull spans roughly x=10 to 1250 of 1280, and the near belly sits under the control-hint row.
   - Pull the fit back so the playable rectangle is about 78% of frame width, and lift the arena so the wale clears the hints.
   - Without this, no hull shape can read.

5. **Re-rig.**
   - The two fat stays land on the near rail and cross the centre of the court, and their shadows add two more diagonals. On Low those shadows are jagged dashes.
   - Remove all near-side rigging.
   - Keep far-side shrouds landing on the far rail.
   - Run one forestay to the bowsprit and one backstay to the taffrail, leaving the mast above the HUD line so they pass only over the raised end decks.
   - Rigging should cast no shadows.

6. **Mast top.** Delete the stub yard that pokes out beside the timer, and let the mast exit the frame cleanly.

7. **Roll.** The maximum-roll capture is indistinguishable from the level one, so the roll cannot be judged from it. Recapture with a water or horizon reference.

**Low:** the massing is identical to High, which is good. Confirm the hull belongs to the arena body and not to the hidden scenery node.

## In the Gutter, "Copper Lock": REVISE (narrow)

**GO for the court.** These are a clear step up and on-brief:
- Bollard posts with rounded cream caps and brass mooring rings.
- Jade shallows over a mosaic floor.
- Chunky pale blocks.
- A rounded burgundy plinth.

The gates and the setting fail.

**Geometry changes:**

1. **Gates read as green railings, not machines.**
   - Keep the bar count (about six thick bars is right).
   - Double the depth of the top beam.
   - Widen the corner pillars to 1.6× into gantry towers, the far tower one player-height above the beam and the near tower lower.

2. **Rebuild the gear.**
   - The current thin spoked wheel reads as a ship's helm, which confuses it with Sea Sick, and it is half hidden behind the gate.
   - Make it a solid disc about 2 cells in diameter and at least 0.35 cell thick, with 8 to 10 blocky teeth and four round holes.
   - Mount it fully outboard at mid-depth, so the whole gear is visible from the battle camera on both sides.
   - Add one fat counterweight block per gate.

3. **Fix the setting.**
   - A lock floating as a raft in open sea makes no sense, and the culvert is a doghouse on a separate islet.
   - Add a channel stub about 1.5 cells long outside each gate, as part of the arena body. The silhouette then stops being a rectangle and survives Low.
   - Build the culvert arch into the upstream stub's end wall, about 2 cells wide and on the gate axis.
   - Make the downstream end a smooth curved spillway. The current steps (visible in the oblique) look like stairs.

4. **Front and back cream rows.**
   - They read as paved walkways.
   - If they are not walkable, make them half-round coping about 0.5 cell wide.
   - If they are walkable, keep them.

5. **Centre vane.**
   - It is about 30 px and unreadable.
   - Double it: fish about 1 cell long on a pole about 1.2 player-heights tall.

6. **HUD.**
   - The far gate ends reach y≈100.
   - With the eight-player HUD they will sit under the outer tokens.
   - Check, then shorten the far towers or shift the camera fit.

**Low:** the court keeps its identity, but the exterior is a flat pale void with no shaded water. That needs a fix or an explanation.

**Note for the later surface pass:**
- At Night and on Low, the white wake bars behind every block are too heavy and too straight.
- On High, the mosaic lines are too contrasty against the blocks.

## Corrected captures: what changes in my first critique

**Glass:**
- I withdraw "reads as a dark hole" and the crate-shadow point.
- It now reads as matte lilac flooring, not glass. Its value is too close to the white rim, and the fish are barely visible.
- Everything else stands: the yellow band, the coral pads, the ambiguous sea level, the box pylons.
- The brief stands too, with one emphasis: the glass must go to a deep ultramarine-violet for both the value structure and the see-through depth.

**Grass:**
- I withdraw "dark board-like face". That was the culling bug.
- The slopes are now continuous, but they are flat ramps with a diagonal grain that reads as wooden planks.
- The rest holds: flat lawn terraces that merge with the exterior, a ruler-straight grass edge, reused boulders and meadow, the hairline fence.

## Top Rules: brief for a stronger pass

A stronger pass is warranted, since the user named this stage. The problem is the setting, not the stripes: a pure black void, cumulus clouds in space, planets cropped at the frame edge, and a castle whose base fades to nothing.

**Direction: "Orrery Keep".** Castle geometry and court reading are unchanged.

- **Plinth.** Give the castle a visible rounded crag under the near wall, as part of the arena body so Low keeps it.
- **Backdrop.** Replace black with a deep indigo-to-violet painted gradient.
- **Stars.** A few chunky four-point stars in two sizes, not pinpoint noise.
- **Clouds.** Delete them.
- **Planets.** Two or three small lacquered toy spheres, each no more than 12% of frame width and fully in frame in the side margins, with one as the hero.
- **Orrery.** Carry the planets on thin brass arcs so the whole thing reads as a tabletop model.
- **Islets.** Keep the turret islets.
- **Light.** One coloured rim light from the hero planet on the castle walls.

This is one pass, after the four new maps.

## Next changes, in order

1. **Ship:** shelf removal, bow and stern asymmetry, camera pull-back, re-rig. Then a new grey study from the battle camera.
2. **Lock:** gantry and gear rebuild, channel stubs, vane scale. Then new High, Low and eight-player HUD captures.
3. **Lost at Sea and Field of Grass:** continue the redesigns to the original briefs.