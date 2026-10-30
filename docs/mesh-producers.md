# Render mesh production contract

The source inventory is [mesh_producers.json](../tools/ci/mesh_producers.json).
It records construction, upload, resource duplication and geometry-helper source
entries, including helper declarations. It covers stage geometry, live gameplay
events, effects, pickups/actors, menus/results, tutorial and previews. HUD actor
previews use the same inventoried Bomber library. It is a source contract, not a
runtime or visual qualification receipt.

`python3 tools/ci/mesh_producers.py` rejects new or changed entries. It also binds
the generator/caller files and stage definitions by hash, so a parameter bound
changed outside a constructor cannot silently acquire an unreviewed domain.
After reviewing a deliberate change, regenerate with `--write`; then run the
functional and native consumed-input gates. Merely regenerating this inventory
is not evidence that new requests execute successfully.

| Production | Domain and implementation |
| --- | --- |
| Box | Continuous finite-positive Vector3 dimensions. A portable C# generator scales the lossless XYZ block of the original Mac unit box, retaining vertex order, indices, normals, tangents and UV data. It does not instantiate a native primitive. The shipped test compares every original Mac box reference, including closure walls and results pieces, byte for byte. |
| Sphere | Eight fixed literal configurations for art/menu actors, snow, scenery, stars, explosion particles and burnout; each producing site names its matching captured key. Runtime size changes use node scale. |
| Plane | Two fixed world grids, one unit particle plane, and the two water-menu footprint variants derived from the shipped Sunken Temple/Up & Down dimensions. The exact five sets are recorded. Other shipped stages use dry terrain or their own reservoir geometry. |
| Torus | Fourteen sets: exit/throw/tutorial/meteor and two Greedy rings; three orbital-loop rings; three cabinet rails; two results arches. Loop bounds and source expressions are inventoried, with matching captured keys per site. Meteor radii derive from the shipped game constants. |
| Cylinder/cone/taper | The exact 194 supported tuples are listed, not a numeric tolerance or a cross-product of independently allowed values. They come from literal effect/menu pieces, the fixed seven-stage layouts, and bounded scenery loops/caller constants. Side counts, ring counts, ordered setters, radii and heights remain bound together. Runtime rejects any other tuple. |
| Bomber library | The existing 32 hash-pinned Mac-authored meshes and their editable source builders. Exports consume the shared packed data. |
| Other ArrayMesh geometry | Owned C# generators for rounded/chamfered boxes, terrain, rocks, grass, medals and other custom surfaces. Continuous input dimensions stay with these generators rather than being inferred as a finite template table. Their source entries and callers are inventoried. |

The finite stage domain is the seven layouts delivered by the matching server;
this contract does not promise support for arbitrary modified server geometry.
The explicit tuple list, source-bound inventory, unknown-request refusal and
end-of-round functional coverage work together. Material/color, transforms and
animation do not silently create additional primitive configurations.

The headless functional suite and each native packaged functional lane include
`core-headless`. It exercises actual closing walls, an upper-court wall crush,
meteor warnings/impact and local pause, with normal templates enforced. Missing
geometry must fail these checks and the zero-runtime-error classifier. Mac-only
authoring mode is for capturing actual request bytes; it cannot qualify an
exported client and must not be used to bypass a missing runtime template.

The first preparatory CI run on `5c9ec3d` exposed uncaptured closure/meteor paths
and was cancelled before any seven-stage native-contracts job started. Its raw
failure is retained. The complete Mac core authoring run collected seven more
actual requests. All 402 decoded packed surface arrays agree with their Mac
resources; file UID differences between capture directories are recorded
separately. The original 395 files were retained unchanged. A normal enforced
Mac headless run passes 1,954 assertions; warnings remain in its raw log. This
is source/runtime preparation, not three-OS input or final GPU qualification.


The user approved the completed batch's three-pixel Rock Garden fresh-target
repeat residual on 2026-10-30. Coordinates are `(119,212)`, `(114,223)` and
`(144,230)`, maximum channel delta 104, at the windmill door edge. All seven
representative base-a images remain exact to the approved reference. This
repeat is **not exact**; its cause remains unexplained and raw before/after,
heatmaps and RGBA values are retained. Approval adopts the visually unchanged
Mac reference, without relaxing the strict 21-inventory native input aggregate.
