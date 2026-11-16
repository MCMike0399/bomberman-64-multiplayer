# Final stage visual review — 2026-11-16

A new plain-Claude Fable5.2 medium advisor reviewed combined candidate6ef981f
in session158ba9c5-87dc-46f1-8c95-5515c0663e96. All83 required images were
opened;84 image results returned with zero tool errors. This is independent
visual advice, not user approval or deployment evidence.

Root reviewed the actual native results and accepts the visual GO. The noted
Field foreground hairline was a0.1-unit decorative gap, closed in d4731be and
verified in a120-check/20-capture native rerun. A separate full regression
caught outboard orchard ground on the menu stand; c8d69a9 excludes only those
bands from miniatures, and the unchanged setup bounds now pass117 checks.
The Low glacier window already uses unshaded emissive paint; its real light pool
is intentionally omitted on Low. No extra Low light or structural crag rewrite
was added for the optional notes.

The complete native scenery regression after those small corrections passed
5239 assertions and420 captures without engine errors. Source and exported
release checks remain separate gates.

---

# Blast Circuit: independent final visual review (candidate 6ef981f)

**Visual verdict: GO.** I found no intersections, floating supports or clipped landmarks in any frame. Three small corrections are worth applying, but none gates release.

I opened all 83 required images (66 base, 17 supplements), plus one non-required base frame (`review-blizzard-battle-low-night-opening.png`) to cross-check a Low finding. I did not open the reference JPGs and did no web browsing.

## Map table

| Map | Low | Medium | High | Basis |
|---|---|---|---|---|
| Sunken Temple | Pass (sparse) | Pass | Pass | Sun-disc shrine reads at every preset; Low drops lotus ruin, palm, columns and far island, leaving a lopsided empty sea. |
| Rock Garden | Pass | Pass | Pass | Mill, mossy columns, two-tone stones and cream coping hold; fruit branch is barely legible (see corrections). |
| Up & Down | Pass | Pass | Pass | Lighthouse and sea arch at all presets; purple/cream/turquoise split is strong; beam visible at Medium/High Night. |
| Top Rules | Pass (note) | Pass (note) | Pass (note) | Hero moon, hoop with seats, turrets and warm lamps present; the crag does not read (see corrections). |
| Blizzard | Pass (note) | Pass | Pass | Grotto is one continuous mound with an open vault; Low Night lantern is unlit (see corrections). |
| Greedy Trap | Pass | Pass | Pass | Strongest result: cream coping against teal, gauge house, wheel, lit gauge at Night on all presets. |
| Field of Grass | Pass (note) | Pass (note) | Pass (note) | Bank, turf lip, roots, burrow and concealment canopy intact; white hairline across the frame (see corrections). |
| In the Gutter | Pass | Pass | Pass | Gantries, gears, dial, fish vane and burgundy bollards read as one object; Low opaque jade keeps the place. |
| Sea Sick | Pass | Pass | Pass | Hull, lantern and pennant unchanged across presets; lantern is warm at Night. |
| Lost at Sea | Pass | Pass | Pass | Tube pontoon, orange caps, pane sheens; wreck ribs visible under glass at Medium/High, faint painted cue at Low. |
| Circuit Garden | Pass (plain) | Pass (plain) | Pass (plain) | Timber body, teal rail, inlays, target raft and lit posts present; plainest map, but the lesson stays dominant. |

## Glacier check

- **Original crop:** `USER-original-blizzard-clipping.png` shows a separate cap, two leg blocks and a shelf with crown points sunk into a flat cap.
- **Front:** `grotto-front-three-quarter.png` shows one snow shoulder growing from the berm, a through-vault, seated icicles on the lip, and a pond with a raised snow rim.
- **Rear:** `grotto-rear-support-inspection.png` shows the vault opening clean through and the mound meeting the ground on a continuous line, with no post or overhang.
- **Low vault:** `grotto-low-vault-inspection.png` shows the lantern standing clear of the vault wall and icicles, with no face ending inside another.
- **Gameplay camera:** the grotto, penguins and pond sit fully inside the frame at all three presets.

The reported defect is not present in any current frame. These three are inspection cameras; only the base gallery shows the product view.

## Visual blockers

None located.

## Limited high-value corrections (non-gating)

1. **Blizzard Low Night lantern is unlit.** In `review-blizzard-battle-low-night-combat.png` and `-low-night-opening.png` (around x140, y340) it is a dull brown box. Medium and High Night show a clear amber glow, and the brief names an emissive lantern for Low. Fix: unlit emissive material on the panel, no light or bloom.
2. **Field of Grass has a white hairline across the full frame width.** It sits at y≈688 in all six required frames (bluish at Night) and runs past the bank to both screen edges. `hollow-bank-inspection.png` shows it is the pale lower edge of the loam strip. Fix: tint that edge to loam or turf, or stop it at the bank ends.
3. **Top Rules crag does not read.** The brief calls for a layered rounded crag with an undercut; in all six base frames the castle floats in flat violet. `orrery-hoop-inspection.png` shows only a flat hard-edged violet plate under it. Fix, if cheap: one rounded dark-violet mass under the footprint, visible below the south wall. The hoop is also matte cream, so it reads more as hose than brass; a darker, warmer, glossier material would help.

## Optional taste notes

- **Rock Garden:** the fruit branch sits behind the north wall directly under the HUD timer (around x560–680, y130) and is nearly invisible; moving it toward a corner would make it count.
- **Sunken Temple:** keeping the lotus ruin at Low would balance the single shrine islet. Night stone goes grey-tan rather than warm.
- **Field of Grass:** Night is only a darker green; at Low there is no lamp glow at the stile and the haystack is gone.
- **Circuit Garden:** the worktop turns concrete grey at Night and loses its cream identity. No brass corner fittings or gull are visible; I would not add them for their own sake.
- **Up & Down:** the smooth arch sits beside faceted hex stacks, a slight vocabulary mismatch.
- **Dither pattern:** a fine screen-door texture shows on the arch, the stacks and the grotto snow in the close inspection shots only, not at gameplay distance.
- **Blizzard:** the lantern is a boxy flat panel, the least rounded object on the map, though only about 20 px in play.
- **Lost at Sea:** at this scale the manta is a faint pale wedge near the lower beacon and does not read as a manta; the wreck ribs carry the under-glass identity.

## Evidence limits

- **Manta control:** I could not tell `DEPTH-MantaRay-visible.png` from `DEPTH-MantaRay-hidden-control.png` by eye, so I cannot confirm the manta from that pair. The wreck pair is clear: the ribs vanish in the hidden control.
- **Motion:** the three `glass-motion` frames show small changes near the lower beacon and the buoy. That is consistent with slow movement but establishes neither continuity nor frame rate.
- **Not observed:** mill, wheel, vane, beacon, pennant and globe motion; night colour beyond one combat frame per preset.
- **Supplement provenance:** `hollow-burrow-peek.png` predates a subpixel cap correction, and `workshop-real-lesson-night.png` predates a wording change; per the index the art is unchanged in both.
- **Not claimed:** user approval, performance, gameplay correctness, hazard behaviour or deployment.

**Visual GO.** Required images I could not inspect: none.