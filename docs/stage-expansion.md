# Ten battle arenas — reviewed release candidate

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
| Rock Garden | Rock Garden | Orchard, working mill and planted borders around the five-column court |
| Up & Down | Up & Down | Lighthouse, sea arch and bell buoy around the offshore terraces |
| Pyramid | Sunken Temple | Sun shrine, lotus ruin and tide float around the golden terraces |
| Greedy Trap | Greedy Trap | Connected waterwheel and gauge house beside the reservoir |
| Top Rules | Top Rules | Develop a coherent toy-orrery setting around the castle |
| Field of Grass | Field of Grass | New grassy hollow, traversable banks and concealed normal bombs |
| In the Gutter | In the Gutter | New shallow lock, changing currents and closing grates |
| Sea Sick | Sea Sick | New ship deck with coherent cosmetic rocking and barrel cover |
| Blizzard Battle | Blizzard Battle | Continuous snow grotto, clear lantern, penguins and pond |
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
| Up & Down | Lighthouse, sea arch, bell buoy and layered sea stacks | [#110](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/110) |
| Greedy Trap | Reservoir gauge house and connected waterwheel | [#115](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/115) |
| Rock Garden | Orchard mill, fruit branch and planted borders | [#116](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/116) |
| Blizzard Battle | Continuous snow grotto with an open vault and seated icicles | [#117](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/117) |
| Circuit Garden | Timber workshop, teal rails and a target raft around the lessons | [#118](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/118) |
| Sunken Temple | Sun-disc gateway shrine, lotus ruin and copper tide float | [#119](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/119) |

The second pass uses High as the designed world. Medium and Low retain the core
landmarks and inexpensive ambient motion, while reducing costly lighting and
water effects. The [renewed references](history/stage-second-creative-research-20261116.md)
and [creative brief](history/stage-second-art-reference-addendum-20261116.md)
record the inspiration and its translation into original procedural models.
Shared Night readability is tracked separately in
[#114](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/114).

A fresh plain-Claude Fable 5.2 medium advisor inspected all 83 required images
and returned a visual GO. Root reviewed the native results and corrected the
Field foreground gap and Rock Garden miniature bounds. The reported Blizzard
clipping is absent in the final front, rear and Low vault views. The
[final review](history/stage-final-visual-review-20261116.md) records the findings,
optional notes and evidence limits; it is not a claim of personal user approval.

Gray ship captures are explicitly temporary **shape studies**. The harness
substitutes neutral materials to expose geometry, restores the originals and
keeps this mode outside ordinary gameplay. A shape-review GO permits surface
finishing; it is not a release verdict.

The implementation keeps rules in `Bomber.Game`, transport and session
validation in `Bomber.Client`, and presentation in Godot. Shared stage lists,
menu indices, meteor support elevations and test coverage are reconciled in a
local integration branch. The original six battle maps and training remain
available throughout.

The final native preset matrix passed 911 assertions with 126 captures. The
complete scenery regression passed 5,239 assertions with 420 captures, and all
four new arenas passed their real server/peer journeys. These checks complement
the visual judgment; they do not measure frame rate. The combined source follows the existing
[release qualification and publication procedure](releases.md); no current
source or art-review result claims publication or server delivery.
