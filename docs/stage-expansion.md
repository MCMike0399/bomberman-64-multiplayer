# Ten battle arenas — expansion in progress

This work completes the battle-stage roster of the **1997 Bomberman 64** in
Blast Circuit's original procedural toy-diorama style. It is not released.
Circuit Garden remains the additional training arena. Original models,
textures, guide scans and footage are not shipped.

The [reference investigation](history/stage-expansion-research-20261115.md)
compares the official guide and inspected gameplay footage against the existing
roster. Eight-player spacing, timing and physical constants are explicit project
adaptations rather than recovered original values.

| Original stage | Blast Circuit arena | Work |
| --- | --- | --- |
| Rock Garden | Rock Garden | Preserve the established meadow and five-column court |
| Up & Down | Up & Down | Refine the offshore rock silhouettes |
| Pyramid | Sunken Temple | Preserve the established golden terraces and lagoon |
| Greedy Trap | Greedy Trap | Preserve the established reservoir and reward chamber |
| Top Rules | Top Rules | Develop a coherent toy-orrery setting around the castle |
| Field of Grass | Field of Grass | New grassy hollow, traversable banks and concealed normal bombs |
| In the Gutter | In the Gutter | New shallow lock, changing currents and closing grates |
| Sea Sick | Sea Sick | New ship deck with coherent cosmetic rocking and barrel cover |
| Blizzard Battle | Blizzard Battle | Preserve the established glacier setting and hazards |
| Lost at Sea | Lost at Sea | New slippery glass with a nonslip outer walkway |

The first candidates did not meet the user's visual-quality bar. An independent
Fable 5.2 review inspected actual native captures and called for stronger
silhouettes, clearer values, deliberate materials and distinct environmental
stories. Test success is not visual approval. The
[initial art review](history/stage-art-review-20261115.md) and
[first shape review](history/stage-shape-review-20261115.md) preserve the
critique rather than presenting rejected candidates as finished work.

| Arena | Art direction | Individual PR |
| --- | --- | --- |
| Field of Grass | **Cutaway Hollow:** ochre earth banks, visible strata, ragged grass and original standing stones | [#107](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/107) |
| In the Gutter | **Copper Lock:** chunky verdigris machinery, cream-capped bollards, jade shallows and connected channels | [#108](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/108) |
| Sea Sick | **Tubby Cutter:** a curved navy hull, clear bow and stern, honey planks and readable slow roll | [#109](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/109) |
| Lost at Sea | **Glass Buoy:** an enamel pontoon, deep violet glass, grippy rim and a submerged world | [#112](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/112) |
| Top Rules | **Orrery Keep:** a visible crag, painted cosmic backdrop and brass-supported toy planets | [#106](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/106) |
| Up & Down | Restrained erosion and layered sea-stack silhouettes | [#110](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/110) |

Gray ship captures are explicitly temporary **shape studies**. The harness
substitutes neutral materials to expose geometry, restores the originals and
keeps this mode outside ordinary gameplay. A shape-review GO permits surface
finishing; it is not a release verdict.

The implementation keeps rules in `Bomber.Game`, transport and session
validation in `Bomber.Client`, and presentation in Godot. Shared stage lists,
menu indices, meteor support elevations and test coverage are reconciled in a
local integration branch. The original six battle maps and training remain
available throughout.

Before release, each changed arena needs actual ordinary-camera High, Low and
Night review, readable players/bombs/aim/hazards, appropriate angled inspection,
and motion evidence where applicable. Native gameplay and local/peer/server
checks accompany those images. The combined source then follows the existing
[release qualification and publication procedure](releases.md); no current
source or art-review result claims publication or server delivery.
