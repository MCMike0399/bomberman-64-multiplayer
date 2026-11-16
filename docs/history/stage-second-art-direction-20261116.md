Recorded 2026-11-16 from the requested plain-Claude Fable 5.2 medium creative advisor. This is intermediate advice, not user approval or release qualification. Fresh primary-reference reconciliation follows separately.

# Blast Circuit: second creative pass (intermediate, not a release approval)

This is creative direction from 16 stills with mixed provenance. I opened all 16 required images and five local references. I did no new web research: `/private/tmp/blast-second-pass-research.md` does not exist yet, so everything rests on `prior-primary-research.md` and the local images. Stills prove no motion.

## 1. Blizzard landmark: what the user's crop shows

From `USER-blizzard-clipping.png`, `blizzard-full-night.png`, `blizzard-battle-high.png` and `blizzard-low.png`:

- **The arch sinks into the berm.** The right leg and lintel end abruptly against the curved white rim. There is no snow fillet, drift or contact shadow at the seam, so the hard blue block reads as cut by the berm.
- **A detached flat slab sits under and in front of the arch.** It is a paler, untextured polygon with a hard straight edge. It does not read as ice shelf, shadow or path, and its lower end is sliced off by the berm's dark outline band. This is the most "clipped" shape in the crop.
- **The left leg ends in a small white wedge.** It looks like a stray drift or the corner of another mesh poking out, not a footing.
- **The snow cap is a flat ellipse** with three small pyramids laid on top like a decal. The cap edge overhangs the lintel with no thickness.
- **A pine stands directly behind the lintel**, so in the crop it appears to grow out of the cap.
- **Material mismatch.** The arch is saturated matte cyan with sharp bevels, while the berm is soft white. Even with clean geometry they would look like two kits.
- **Same shape at Low, High and Night.** It is a persistent shape or placement issue, not a one-frame shader glitch.

What I cannot tell from stills: whether the slab is an intended shelf mesh, a misplaced shadow-catcher or a z-ordering issue, and whether the berm is truly intersecting or only overlapping in projection. That is root's mesh diagnosis.

**Replacement shape: Glacier Grotto.** Do not reseat the arch; make it part of the berm.

- Swell the berm's north-west corner into a rounded hump about 2.5× wall height, the same white snow material as the berm, as one continuous surface.
- Carve a shallow grotto mouth into the hump's outward face, lined with translucent cyan-violet ice. The ice is seen only inside the opening, so there is no free-standing blue block.
- Hang 3–5 fat rounded icicles from the grotto lip and leave one fallen ice chunk at the mouth.
- Replace the three decal pyramids with one tilted, chunky crystal on the crown, in the cone family (violet, bevelled). This is the "crown" silhouette and echoes the White Glacier hero crystal.
- Remove the detached slab. If a shelf is wanted, make it a frozen tongue that flows out of the grotto mouth and fades into the snow, with a rounded edge and no straight cut.
- Move the pine so it does not overlap the hump from the battle camera.
- Put the single warm lantern inside the grotto so Night gets a glowing ice mouth. Remove the lantern or signpost currently sitting on the south-east corner wall.

## 2. Defects visible in this packet

| File | Defect |
|---|---|
| `USER-blizzard-clipping.png`, `blizzard-full-night.png`, `blizzard-low.png`, `blizzard-battle-high.png` | Arch, slab and berm issues above. The berm also carries a dark navy outline band on the south and east edges that reads as a drop to void, not snow. |
| `blizzard-low.png` | Beyond the berm is blank white, so the berm reads as a floating tray. The grounding problem has only moved outward. |
| `circuit-high-night-before-shadow-fix.png` | Fine corrugation over the whole deck (known, fix pending). Also in the day frame: the deck's front edge is jagged or aliased. |
| `circuit-garden-high.png` | The deck is now a featureless cream slab. The earlier stone and paving character was lost and it reads as unfinished. The central stair is nearly invisible, white on white. |
| `field-of-grass-high.png` | Paths are grey-beige flanked by dark maroon and saturated green stripes, so each bank reads as a striped ribbon, not an earth mass. The turf lips are sharp creases. The surround is mottled dark olive. |
| `grass-oblique.png` | Hard chamfered planes on the cutaway and a visible triangular facet at the front-right corner cap. The roots are good. The rounded outer hills are right but do not show from the battle camera. |
| `greedy-trap-high.png` | The camouflage ground was replaced by flat beige that almost matches the coping and paving. The wall now blends into the ground and the world reads as blank paper. |
| `rock-garden-high.png` | The camera or crop is tighter than the other maps and the bottom wall is cut by the legend bar. The boulders are now uniform putty spheres. The coping is very large and bright and pulls the eye from the lawn. |
| `top-rules-high.png` | The orrery arms are much better. The crag is still a faint smooth grey dish. |
| `in-the-gutter-high.png` | Water now reads. Blocky reflection residue remains under the front pylons. |
| `lost-at-sea-high.png` | The rim's front lip is a thin flat plate with a hard edge. Nothing is visible under the glass. |
| `up-and-down-high.png` | The sea-stack shading is slightly stair-stepped. |

## 3. Concepts per map

The shared rule: High is the designed place. Medium keeps every mass and every motion and drops real reflections, soft shadows and particles. Low keeps the masses, landmark and palette, plus one cheap life cue (vertex or UV animation, or an emissive), with no lights and no particles.

### Tier A: substantial design work

**Blizzard Battle, "Glacier Grotto."** A snowbound bowl with one glowing ice cave.
- **Add:**
  - The grotto hump described in section 1.
  - An outer field of 3–4 broad rounded drifts with blue shadow sides and a darker blue-grey far ring. Replace the navy outline with a soft blue ambient-occlusion skirt.
  - A wind sock on a short pole on the berm's far side, with a striped red and cream cloth.
- **Motion:** the sock and snow streaks point with the accepted wind state and go limp in calm.
- **Keep:** cones, wall, penguin pond.
- **Remove:** the free-standing arch, the slab, the igloo (the grotto replaces it) and the corner signpost.
- **High:** translucent ice with rim light, lantern glow, flakes.
- **Medium:** opaque gradient ice, emissive lantern, fewer flakes.
- **Low:** hump, crystal and drifts in the core. The sock rotates only, with no cloth simulation. The lantern is an emissive quad.

**Field of Grass, "Cutaway Hollow."** A slice of hillside lifted out of a meadow.
- **Add:**
  - Path colour back to warm ochre-red loam, as a single mass from path to bank face. The turf is a thick rounded overhanging lip (a fat bevel), not a stripe.
  - The oblique shot's rounded hills pulled in, so two soft knolls crest above the bank tops in the battle camera. One carries the hay rick, stile and lamp; the other carries a single wind-bent tree.
  - A burrow: use the existing front culvert hole as a rabbit or mole hole with a small dirt mound.
- **Motion:** one gust band sweeps across the grass, the tree and the knoll tufts together. The hazard remains meteors only.
- **Keep:** roots, front strata, standing stones (add moss caps).
- **Remove:** the dark maroon stripe and the olive mottling.
- **High:** blade detail, soil speckle.
- **Medium:** same forms, simpler grass shading.
- **Low:** knolls and lips in the core, gust as a UV-scrolled tint band, tree static.

**Circuit Garden, "Harbour Workshop."** A shipwright's drafting table floating in a harbour.
- **Add:**
  - Texture back with purpose: pale timber planking, or large pale stone slabs with broad seams. Inlaid teal range lines (keep these, they are good), a brass compass rose at the centre, and a rounded dark-teal bumper rail round the plinth, like a dock fender.
  - Stair treads with teal nosing so the stair reads.
  - A small rounded crane or davit on the side platform holding a spare target. This is the "workshop" landmark.
- **Motion:** the pennants on the four posts and the target raft's bob share one breeze and swell.
- **Keep:** raft, jetty, boat, buoys.
- **Remove:** the palm islet (it repeats Sunken Temple).
- **High:** reflections, soft shadows.
- **Medium:** no reflections.
- **Low:** the rail, rose and crane are core. Pennants flap by vertex animation. The raft is retained.

**Greedy Trap, "Reservoir Keep."** A brick cistern-fort sitting in its own reservoir.
- **Add:**
  - The four canal strips turned into one continuous still reservoir around the fort. The houses stand on short brick quays, giving a dark teal surround under pale coping and the value separation that is now missing.
  - The existing waterwheel made the single machine: bigger, on the west wall, with a visible sluice trickling into the reservoir.
  - Warm lit windows in the blue-roof houses and brass gear plaques that pick up the light.
- **Motion:** the wheel turns slowly. Optionally, the wall gear plaques rotate a quarter-turn when the accepted closing-wall phase advances.
- **Keep:** brick, cream coping (good now), chamber.
- **Remove:** the beige ground, the round tree trays (repeated tray decoration) and one house per side.
- **High:** reflections outside the court.
- **Medium:** flat tinted water with a foam edge.
- **Low:** opaque teal, wheel rotating, windows emissive.

### Tier B: moderate

**Rock Garden, "Stone Orchard."** A walled monastery garden.
- **Add:**
  - Boulder variety: two sizes and two tones (warm putty and mossy grey-green), slightly squashed, a few with a lichen cap.
  - One espaliered or blossom tree leaning over the north wall from outside, its canopy outside the play area, with a few petals drifting on the outside only.
  - Column caps as carved rounded finials (Medieval round masonry).
- **Motion:** the windmill sails and tree canopy share the breeze. The stream already exists.
- **Keep:** windmill, stream, bridge, lanterns.
- **Remove:** half the shrubs and the flower-bed discs. Tone the coping down by about 15% in value and width.
- **Camera:** restore the standard framing.
- **High:** petals, lawn relief.
- **Medium:** no petals.
- **Low:** boulder tones and finials in the core, the windmill silhouette retained as a core landmark, sails rotating.

**Top Rules, "Orrery Keep."** A castle riding the hub of a brass planetarium.
- **Add:**
  - A crag built as a rounded, layered dark-violet rock with a brass ring gear embedded round its waist, from which the arms visibly spring. This is the load-bearing machine the theme needs.
  - Warm arched window glow on the inner walls and turrets.
  - One small comet or shooting star on a long slow loop in the far background.
- **Motion:** the arms orbit slowly and the ring gear turns. Optionally, tie a gear tick to the accepted closing-wall step.
- **Keep:** court, banners, the thicker arms.
- **Remove:** two of the three floating mini-islands, which are repeated island decoration.
- **High:** lacquer specular, glow.
- **Medium:** matte finish.
- **Low:** crag, ring, one planet and turrets in the core; emissive windows; the orbit kept, since it is a transform only.

**In the Gutter, "Copper Lock."** A canal lock chamber.
- **Add:**
  - A spillway stub outside each gate, a short curved channel with a foam lip, so the lock connects to something.
  - A lock-keeper's hut with a lit window and a brass bell on one corner pylon.
  - Wakes behind the bollards.
- **Motion:** the fish vane, the wakes, the spillway foam direction and the gears all follow the accepted current. The gears reverse when the current flips.
- **Keep:** everything else.
- **Fix:** the pylon reflection residue.
- **High:** caustics, reflections.
- **Medium:** tint plus foam rings.
- **Low:** opaque water, vane and gears animated, hut emissive.

**Lost at Sea, "Glass Buoy."** A research float over a drowned wreck.
- **Add:**
  - Chunky rounded pontoons with an orange stripe and a ladder under the rim, and a thicker rounded front lip.
  - The wreck silhouette faintly present from the start, with one slow manta shadow.
  - A companion buoy with a blinking beacon synced to the four pylons.
- **Motion:** the manta pass, a slow beacon pulse and an occasional fish school under the glass.
- **Keep:** the glass and rim palette.
- **High:** transparency, reflections.
- **Medium:** fake depth, with the wreck as a dark decal layer.
- **Low:** diagonal sheen bands plus the wreck as a darker tint shape, and beacons pulsing by emissive.

### Tier C: finishing only

**Sunken Temple, "Drowned Sun Temple."**
- **Add:** a carved sun disc of gold with a teal inlay on the apex. Tint Night stone warm. Give the reeds and palms one shared breeze and add a turtle or fish shadow circling the west ruin.
- **Remove:** one of the two palm islets, which duplicate each other.
- **Low:** keep the gateway islet and the disc.

**Up & Down, "Lighthouse Piers."**
- **Add:** a soft sweeping beam at Night, a striped channel marker echoing the lighthouse, and gulls that perch on the sea stack.
- **Fix:** the stack shading.
- **Low:** the lighthouse is core and the beam is a rotating emissive cone.

**Sea Sick, "Tubby Cutter": KEEP.**
- **Add (optional):** a pennant at the masthead that streams opposite the roll, a foam line at the waterline, and one dolphin or flying-fish arc far off the bow.
- **Low:** the pennant by vertex flap.

## 4. Implementation briefs

**P0, defects**
1. **Blizzard:** remove the arch and slab and build the grotto hump as part of the berm mesh. Replace the navy outline with a soft blue skirt. Add the outer drifts to the core.
2. **Circuit:** land the pending shadow fix. Fix the front-edge jaggies. Restore the surface material, stair nosing and bumper rail.
3. **Greedy Trap:** replace the beige ground with a continuous reservoir and quays.
4. **Field of Grass:** recolour the paths and banks as a single loam mass, give the turf rounded lips, soften the corner cap and bring the knolls into the battle view.

**P1, character**
5. **Top Rules:** crag, ring gear, window glow.
6. **Rock Garden:** boulder variants, finials, overhanging tree, calmer coping, camera restored.
7. **Gutter:** spillways, hut, current-tied gears.
8. **Lost at Sea:** pontoons and lip, early wreck, manta.

**P2, polish**
9. **Sunken Temple:** sun disc and warm Night.
10. **Up & Down:** beam and marker.
11. **Sea Sick:** pennant and foam.

**Bounds for every brief:**
- No collision, route or hazard changes.
- Nothing taller than the walls within one tile of the play area.
- At most three new mesh types per map.
- No new real lights on Low.
- Each addition must name its Low form before it is built.

## 5. What root should capture and judge

- **Standard battle camera**, Low, Medium and High, midday and Night, with eight players in combat, for all eleven maps on one matched run. The current packet mixes matrix01, matrix02 and one-off captures; resolve the preparation timeout first.
- **Blizzard:** a close three-quarter view of the grotto from the battle-camera direction, plus one from behind, to prove a continuous surface with no slab.
- **Field of Grass:** the oblique again, plus the battle camera, to confirm the knolls are visible.
- **Circuit:** High Night after the shadow fix.
- **Motion clips**, 6–8 frames or about 3 seconds each:
  - Blizzard wind sock across a wind change.
  - Gutter gears, vane and wakes across a current flip.
  - Greedy Trap wheel.
  - Top Rules orbit.
  - Field of Grass gust band.
  - Lost at Sea manta.
  - Sea Sick roll with pennant.
  - Circuit pennants with raft.
- **Hazard-phase frames:** flood (Temple, Up & Down), closing walls (Top Rules, Greedy Trap, Gutter), avalanche, and a meteor warning, each with the new scenery present.
- **320 px grayscale thumbnail per map** to check that the landmark and the fight still separate.

Until that gallery exists on one clean run, I am not approving release.