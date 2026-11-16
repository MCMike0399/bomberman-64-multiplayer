# Round 2 shape review: ship REVISE, lock GO

The ship still fails at the battle camera, and the source explains why. The lock's major forms are a GO, with one bounded change to fold into finishing. Neither verdict is a release endorsement.

**Inspected (all opened with Read):**
- **Ship:** `ROUND2-ship-sea-sick-shape-high-battle`, `-low-battle`, `-high-maximum-roll`, `-high-opposite-roll`, `-low-maximum-roll`, `-oblique-inspection`, `-near-profile-inspection`.
- **Lock:** `ROUND2-lock-copper-lock-shapes-high`, `-low`, `-oblique`, `-night`.
- **Source:** `SOURCE-ship-shape.cs`, `SOURCE-ship-deck.cs`, `SOURCE-ship-sections.txt`.

## Sea Sick: REVISE

**Resolved since round 1:**
- No rigging crosses the court.
- The stays pass only over the galleries.
- The mast exits the frame cleanly.
- The side shelf is gone.
- The bowsprit is a real spar.
- Low and High match.

**What the battle camera still shows:** a court with two large blank pads bolted to its ends, a near edge that steps like a tray lip, and almost no visible hull side.

### Blocker 1: the cross-section is a bowl, so the camera sees no hull side

- **Cause:** `TubbyPoint` puts the widest point at the rail and curves inward immediately below it. The `belly` term is only a 0.355 lip in the top few degrees.
- **Effect at battle camera:** the near side is a thin stripe.
- **Seam it creates:** at the galleries the hull is about z=18.55 at waist height, but the gallery riser boxes reach z=18.6 (ship-deck.cs line 42). I believe these are the pale rectangles under each gallery in the battle and profile views. This is my reading of the numbers, not something I could test.
- **Fix:** replace the angle-based U with an explicit station profile. With B the half-beam at the rail, each side runs:

| Point | Half-breadth | Height |
| --- | --- | --- |
| Rail | B | sheer |
| Wale | B + 0.35 | sheer − 0.9 |
| Waterline | B + 0.45 | −2.1 |
| Bilge | B − 0.6 | −3.4 |
| Keel | 0 | keel |

The topsides then flare outward down to the waterline. That shows about 2.5 units of side from above and encloses the riser boxes.

### Blocker 2: the sheer is stepped, so the wale wiggles

- **Cause:** the hull sheer jumps from 1.795 to 0.445 across 0.3 units at x≈3 and x≈16, and the wale follows it.
- **Effect:** the S-kinks on the near edge make the dog-bone outline.
- **Fix:**
  - Give the hull one smooth sheer (values in the table below), with the wale following it unbroken.
  - Build the gallery sides as separate castle bulwark panels standing on the hull, from the hull sheer up to 1.795.
  - End each panel at x=3.0 and x=16.0 with a vertical edge and a round post of radius 0.28.
  - Do not run a spline through the step.

### Blocker 3: both ends are fake walkable pads, and the bow is a semicircle

- **Bow, cause:** it tapers from a 9.15 half-beam to nothing in 3.8 units, with a 0.40 hump. That is a flat round pad larger than the forecastle.
- **Bow, fix:**
  - Lengthen it to about 6.5 units with an ogive plan (table below).
  - Raise it as a turtleback: the cap edge rises from 1.8 at x=19 to 3.5 at the stem.
  - Give it a centreline crown of 0.6 growing to 1.6, using a sharper profile such as `1−|v|^1.5`, so a stem ridge reads.
  - Slopes over 25° stop it reading as floor.
- **Stern, cause:** a 3.2-unit flat poop with a 0.10 hump.
- **Stern, fix:**
  - Cut the overhang to 1.6 or less.
  - Bring the taffrail forward to x≈−1.2, so it backs the gallery rail directly.
  - Leave only a cambered strip of 1 unit or less.

**Proposed stations (x, half-beam, hull sheer, keel):**

| x | Half-beam | Sheer | Keel |
| --- | --- | --- | --- |
| −1.6 | 8.6 | 1.10 | −2.6 |
| 0 | 9.15 | 0.95 | −3.6 |
| 3 | 9.15 | 0.60 | −4.4 |
| 9.5 | 9.15 | 0.445 | −4.9 |
| 16 | 9.15 | 0.60 | −4.6 |
| 19 | 9.15 | 1.00 | −4.0 |
| 20.5 | 8.2 | 1.5 | −3.5 |
| 22 | 6.3 | 2.1 | −2.9 |
| 23.5 | 3.9 | 2.7 | −2.0 |
| 24.8 | 1.6 | 3.2 | −1.0 |
| 25.5 | 0.35 | 3.5 | −0.3 |

The hull is about 1.1 units longer overall. Keep the court centred if actor framing needs it. The bow tip and bowsprit heel must stay in frame, though the sprit end may crop at the screen edge.

### Blocker 4: roll is not visible

The maximum-roll and opposite-roll captures match the level frame almost exactly. Only the sea texture differs. Either the amplitude is imperceptible at this camera or the captures are not at the extrema. I cannot approve a rocking ship on these.

### Optional polish, not blocking

- A solid cap rail on the bow in place of the thin rope.
- Rudder visibility.
- Water contact and foam.

**Retain:** the axis, capstan and bitts, planking, stairs, mast, the current rigging layout, and the two-tone hull with its wale.

## Copper Lock: GO on major forms

**What now works:**
- **Gates:** they read as gates, with a deep beam, about six fat bars and domed towers.
- **Gears:** solid toothed discs, fully visible in both margins.
- **Vane:** readable at battle scale.
- **Coping:** the front and back rows no longer look like walkways.
- **HUD:** the far towers stop just below the eight-player HUD.
- **Low:** it keeps outer water and the same massing as High.

**One bounded geometry change, to fold into finishing (no new grey study needed):**

The channel does not read as connected at the battle camera. Both stubs hide behind the gates. The gears and counterweight arms hang on bare shafts over open sea. The left culvert still sits beside a separate green islet.

- Extend each stub's two side walls to about 3 cells beyond the gate, as wide as the gate opening.
- Seat each gear's axle and counterweight on a stub wall top.
- Build the culvert arch into the left stub's end wall.
- Delete the islet.

That grounds the machinery and breaks the rectangular outline in one move. I will check it in the coloured review.

**Also check:** the oblique view shows sharp white pointed fins at the right spillway. If they are intended cutwaters, round them. If not, remove them.

**Finishing list (materials, not shape):**
- **Wakes:** the wedges behind blocks are still spiky and bright at Night and on Low. Make them softer, shorter V shapes.
- **Low water:** the court water and the sea are the same teal. Separate them by value.
- **Reflection artefact:** there is a hatched patch below the near-left plinth corner on High and Night.
- **Mosaic:** it is now quiet enough. Keep it at this level.

## Next steps

1. **Ship:** apply blockers 1 to 3 as one rebuild of the section function and end caps. Then re-capture battle High and Low, the near profile, and both roll extrema with a visibly tilted deck.
2. **Lock:** make the stub and gear-mount change, then go to the full coloured gameplay review.