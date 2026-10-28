# Six mesh input differences in the retained full-High fixture

**Finding:** the Mac/Linux condition mismatch is real geometry/attribute drift,
not six opaque hashes or unused padding. All six fields belong to the single
Bomber actor: torso/helmet U coordinates, four curved-patch positions, and four
packed tangent components in the left eye. Packed normals and indices match.
The first discriminator should record these two custom C# builders before and
after `SurfaceTool`, preserving every raw hash and the current Mac artwork.

Source and retained-byte research, 2026-10-28. **No game or .NET runtime was executed; no build, capture, desktop, GPU, VM, CI or release
operation was performed.** It does not diagnose the Linux depth fault, Windows
repeat pixels, or shader-output equality. Those remain the coordinated owner's work.

## Evidence boundary and integrity

- Frozen producer: `3da1eac377a9f04323c2f02ce5ce15811d2cec8d`; read using `git show`.
  Read-only source checkout HEAD was `e320b2f9ad1113bb04d019825c6f13c104380697`.
- Engine: official Godot 4.7.2, `ed1daf0bf001b61586d9930840f2f1394092c079`.
  All Godot links below pin that complete commit, including the managed bindings.
- Retained root: `artifacts/qa/native-high-3da/`.
  Inputs: `{macos-arm64,linux-x64,windows-x64}/run-0/parity-base-{a,b}-inventory.{json,bin}`
  and each `parity-base-a-0.manifest.json`. No `run-1` inventories were present there.
- Each inventory has 2,189 nodes, 192 resources, 74,292 binary segment references,
  an 11,360,512-byte blob, and empty `unsupported`. Bounds, lengths and full SHA-256
  were verified for **every referenced segment in all six inventories**, before decoding.
- Within each OS, base-a/base-b inventory JSON differs only in the `binary` filename;
  the complete blobs match. This is within-process/fresh-target evidence: the model
  uses static mesh caches, so base-b does not independently regenerate these meshes.
- Base-a Mac/Linux JSON has exactly the six reported scalar differences, all hashes.
  Mac/Windows and Linux/Windows each have ten, not six; see the bounded note below.

Full base-a blob SHA-256, also matching that OS's base-b:
```text
Mac      cea524fba7e4dd9c99b0483b93d587768f88fe56089719851c87a4bbf4268e53
Linux    a7b96ae54c44b64fd8ac74f2cca69caa1e8ee3a814df598c2bec741d06620b35
Windows  458878b70dedd1505dec87ee46382ccf11f983b403c2e7a8491dc50e15ef832a
```

## Six-field identity and producer mapping

All are pathless runtime `ArrayMesh` resources with **one surface, index 0**,
primitive 3 (triangles). None is an imported/baked mesh or a native `PrimitiveMesh`.
`P = 0/0/0/0/0/0`: ArenaView → dynamic root → BomberModel → body → upperBody →
upperParts. These child-index paths are retained graph locators; friendly names
below come from source, not from excluded instance IDs/names. Each mesh has exactly
one referencing node in this one-human fixture; static caches share it across
other Bomber instances in ordinary use. [Model construction][model], [inventory][inventory].

| Resource / differing field | Node under P / material reference | Original builder and exact arguments | Surface format; vertices / indices |
| --- | --- | --- | --- |
| 1 / `attribute_data` | `/0` Torso / 0, blue `315dc5`, roughness .46 | `CreateHelmet(false)`; torso keeps authored node scale `.395/.854, .38/.79, .37/.73` | `0x800001017`; 985 / 5520 |
| 9 / `attribute_data` | `/5/0` HelmetShell / 8, player `f6f0df`, .46 | `CreateHelmet(true)` | `0x800001017`; 985 / 5520 |
| 11 / `vertex_data` | `/5/1` FaceRim / 10, `8b755c`, .80 | `CreateCurvedFacePatch(.646,.466,0,-.010,.009,.47,true)` | `0x800001007`; 1763 / 9600 |
| 13 / `vertex_data` | `/5/2` FaceSurface / 12, `ffe0b7`, .78 | `CreateCurvedFacePatch(.634,.454,0,-.010,.016,.47,true)` | `0x800001007`; 1763 / 9600 |
| 14 / `vertex_data` | `/5/3` eye at x = −.134 / 3, `272341`, .55 | `CreateCurvedFacePatch(.036,.193,-.134,.010,.022,.8,false)` | `0x800001007`; 1763 / 9600 |
| 15 / `vertex_data` | `/5/4` eye at x = +.134 / 3, same dark material | `CreateCurvedFacePatch(.036,.193,.134,.010,.022,.8,false)` | `0x800001007`; 1763 / 9600 |

All arguments above are source `float` literals. Materials are texture-free
`StandardMaterial3D` from `ArtMaterials.Toy`, with clearcoat/rim enabled and normal
mapping disabled; material storage is equal across these captures. [Toy source][toy].
The exact JSON suffix is `/properties/1/value/0/1/value/data/sha256` for 1/9,
and `/properties/1/value/0/7/value/data/sha256` for 11/13/14/15, beneath `/resources/{id}`.

## Decode and measured differences

The inventory serializes each scalar/packed value using `GD.VarToBytes`; dictionary
keys are sorted before recursive traversal. Resource IDs are traversal references,
not native object IDs. `ArrayMesh._get_surfaces()` supplies actual surface storage,
and RD `mesh_get_surface()` retrieves buffer data. Thus these are retained buffer
readbacks, **not a retained pre-upload builder trace**. [Inventory][inventory],
[ArrayMesh storage][mesh], [RD upload/readback][rd].

The [Variant encoder][variant] and [little-endian helpers][endian] define a four-byte
type header and, for PackedByteArray (type 29), four-byte byte count followed by data
and zero alignment padding. Every selected length is divisible by four: its eight-byte
wrapper matches across OSs and there is **no trailing Variant padding**.
[Format flags][flags] identify format version 2, 3D vertices, normal, tangent and
index; only 1/9 also have UV. Compression, color, UV2, custom, bones and weights are absent.

[Offsets and packing][packing] give these payload layouts (N = vertex_count):

- `vertex_data[0:12N]`: N tightly packed xyz binary32 positions, stride 12.
- `vertex_data[12N:20N]`: N records, stride 8: normal `(uint16,uint16)` then
  tangent `(uint16,uint16)`, octahedral encoding; these words are **not floats**.
- `attribute_data` for 1/9: N binary32 `(U,V)` pairs, stride 8, no other attributes.
- Index payload: unsigned 16-bit indices, exactly 2 × index_count bytes.
  Vertex payloads are exactly 19,700 or 35,260 bytes; no unclassified tail exists.
  RD's special padding branch for normals without tangents does not apply here. [RD][rd].

Offsets below address the original `.bin`, including the eight-byte wrapper.
Lengths and offsets agree on all three OSs. SHA columns are **12-digit prefixes
for lookup only**; verification used all 64 digits from each original JSON.

| ID | Offset | Encoded / payload bytes | SHA prefix Mac / Linux / Windows |
| --- | ---: | ---: | --- |
| 1 | 2088 | 7888 / 7880 | `6685d599583f` / `73d927976a20` / `1d949894cfeb` |
| 9 | 150668 | 7888 / 7880 | `7157fb88e3a45` / `d37bebf27ca7` / `d1af10b2b79e` |
| 11 | 210268 | 35268 / 35260 | `dfe5c06e13a2` / `f947d4ee1a24` / `cee0e3d9c6b0` |
| 13 | 266304 | 35268 / 35260 | `4c204ff7daf3` / `febdbfdf501a` / `e8d7d1d631ac` |
| 14 | 321408 | 35268 / 35260 | `8126930a72bf` / `fb712cbecee2` / `f9ec3edd2e9b` |
| 15 | 376512 | 35268 / 35260 | `ca7765563a02` / `58f62f5c2286` / `9882eb3229e2` |

Mac → Linux measurements; ULP is binary32 representable-step distance, only for
proven floating fields. “Vertices” counts distinct indexed records, not triangles.

| ID | Changed vertices / float components | Components | Max float ULP / absolute delta | Additional packed changes |
| --- | ---: | --- | --- | --- |
| 1 | 77 / 77 | U only | 4 / `5.960464477539063e-8` | None |
| 9 | 45 / 45 | U only | 16 / `5.960464477539063e-8` | None |
| 11 | 27 / 27 | Y only | 1 / `7.450580596923828e-9` | None |
| 13 | 23 / 27 | 23 Y + 4 Z | 2 / `5.960464477539063e-8` | None |
| 14 | 27 / 27 | Y only | 2 / `7.450580596923828e-9` | 4 tangent uint16 components, ±1 |
| 15 | 27 / 27 | Y only | 2 / `7.450580596923828e-9` | None |

All six index streams and packed normal streams match across **all three** OSs.
Positions of 1/9, all V coordinates, and the other five tangent streams also match.
Examples below give original zero-based vertex/component and binary32 bits Mac → Linux:

- 1, v5 U: `3f16a4b7 → 3f16a4b8` (`.5884508490562439 → .5884509086608887`).
- 9, v67 U: `3cef8550 → 3cef8560` (`.029238373041152954 → .029238402843475342`).
- 11, v39 Y: `bc01cb4f → bc01cb4e` (`-.007922007702291012 → -.007922006770968437`).
- 13, v39 Y: `bbfce757 → bbfce756`; v690 Z: `3ebe8e53 → 3ebe8e55`.
- 14 and 15, v39 Y: `bb533d59 → bb533d57` (`-.003223261097446084 → -.0032232606317847967`).
- 14 tangent: v626/v1605 `(39633,36206) → (39633,36207)`;
  v689/v1668 `(39581,36545) → (39581,36544)`. Linux and Windows agree on these four.
  These interior octahedral coordinates are not the encoder's equivalent-edge
  special case; the decoded tangent direction changes. No float ULP is assigned. [Octahedral source][oct].

Windows comparison, IDs ordered 1,9,11,13,14,15: Mac/Windows changed float-vertex
counts are **33,11,52,50,54,54**, maximum ULP **4,8,1,2,3,3**; maximum absolute
values follow the same six-row maxima above. Linux/Windows counts are
**72,42,25,27,27,27**, maximum ULP **4,16,1,2,3,3**; eye maxima are `5.587935447692871e-9`.
All six field hashes are distinct on each OS. The extra Windows scalar differences
are node 21 `transform`, resource 16 surface `aabb`/`vertex_data`, and resource 18
surface `vertex_data`; recorded as outside this six-field investigation, not diagnosed.
Manifests identify Mac ARM64/Metal (Apple Paravirtual), Linux x64/Vulkan (llvmpipe),
Windows x64/D3D12 (Microsoft Basic Render Driver), all .NET 10.0.11. OS, architecture,
native math implementation and backend are confounded; even the two x64 targets differ.

## Cause hypotheses constrained by source

**Torso/helmet:** `CreateHelmet` uses fixed 24 × 40 loops and C# `Mathf.Sin/Cos`,
then explicitly supplies analytic normals and `U = .5f + Atan2(p.Z,p.X)/Tau`.
Positions match exactly, while U alone differs: the Atan2/divide/add chain is the
narrowest arithmetic candidate. The pinned managed `Mathf.Atan2(float,float)` calls
`MathF.Atan2`, not a GPU intrinsic. [Builder][model], [managed math][math].
Native UV packing copies the two floats unchanged. This supports an upstream
calculation hypothesis but does not prove which operation first diverged.

**Curved patches:** fixed 12-ring/64-segment caps and three lip profiles use
`Outline` → Sin/Cos → max/clamp → Vector2 normalization → width/height products;
Z adds `FaceHemisphereZ` (Pow/Sqrt), radius-squared terms and Lerp. Y-only drift
shared by four patches implicates that common outline/scale path; four face Z
changes are compatible with propagation into depth calculation. v39 first occurs
in cap triangle 35, at the outer point for segment 36, providing a bounded probe.
The managed Vector2 normalization uses squared length, Sqrt and division. [Model][model], [Vector2][vec2].

**Normals/tangents:** patches call native `GenerateNormals()` before `Index()`.
Godot accumulates triangle-plane normals in smoothing groups and normalizes them.
`Index()` deduplicates complete vertices in encounter order; `Commit()` copies
arrays into ArrayMesh. This path does not invoke meshoptimizer/cache optimization
or MikkTSpace tangent generation. [SurfaceTool][surface]. RenderingServer adds a
missing tangent when normals exist, generates it from the *unpacked* normal via
cross/normalize, then quantizes both streams separately. Identical packed normals
therefore do not establish identical pre-quantization normals; either upstream
normal drift or native tangent arithmetic can explain the eye's ±1 steps. [Packing][packing].
No RNG/seed participates in these builders; fixture match seed 64 governs the
scene, not their geometry. Static caches also exclude animation-time regeneration.
Exact runtime cause remains unproven without the missing boundary observations.

There is **no padding-only explanation** for these six fields. The toy materials
currently have no textures/normal maps, so changed U/tangents may be visually inert;
that is not identical semantic mesh input, nor a reason to remove their hashes.
Actual positions differ too. A future semantic inventory should decode typed
positions/UVs/normal/tangent directions alongside unchanged raw storage hashes;
keep any representation-only finding explicit and versioned, never round or mask inputs.

## Two smallest owner-run discriminators; no implementation here

1. **Instrument only the two existing builders at the frozen parameters.** Retain
   raw float bits before `SetUV`/`AddVertex`, `CommitToArrays` before/after normal
   generation and indexing, then the committed `_surfaces` bytes. For 1/9 record
   exact p.X/p.Z, Atan2 result, quotient and final U at the cited vertices; for the
   patches record segment 36 Outline intermediates, Point Y/Z, and normals feeding
   eye vertices 626/689. Include full ordered streams to correlate deduplicated IDs.
   If initial arrays differ, investigate only their first divergent expression;
   if those agree but packed/readback buffers differ, isolate that native boundary.
   This adds observation without changing production qualities, artwork or hashes.
2. **If a controlled common-input trial is needed, propose a reviewed six-mesh
   Mac-authored resource bundle.** Preserve exact existing Mac surface bytes,
   ordering, normals, generated tangents, bounds and UVs; load the same binary
   resources on each target and verify post-load raw hashes. Rebuilding from decoded
   arrays could requantize normals/tangents, so loading must demonstrably preserve
   storage. Keep the C# authoring functions and parameters as the editable source,
   with explicit regeneration/review and a Mac before/after identity check. This is
   a narrow experiment, not approval to bake everything or change shipped art.

Comparable future conditions require genuinely equal inventories plus unchanged
state/camera/history/capture/High policy, fresh manifests bound to actual new bytes,
and resolution of any remaining Windows fields. The fixture includes its inventory
in `assetsSha256`, `stateId` and `settingsSha256`: matching one label cannot suffice.
Historical hashes/manifests remain untouched. Subsequent controlled images must
retain native scale, 2×MSAA, Circle/Low jitter flag, SSIL, DOF and mirror water. [Fixture][fixture].
Neither experiment establishes GPU depth correctness or cross-API shader parity.

[model]: https://github.com/MCMike0399/bomberman-64-multiplayer/blob/3da1eac377a9f04323c2f02ce5ce15811d2cec8d/scripts/arena/models/BomberModel.cs#L104-L266
[toy]: https://github.com/MCMike0399/bomberman-64-multiplayer/blob/3da1eac377a9f04323c2f02ce5ce15811d2cec8d/scripts/rendering/ArtMaterials.cs#L15-L30
[inventory]: https://github.com/MCMike0399/bomberman-64-multiplayer/blob/3da1eac377a9f04323c2f02ce5ce15811d2cec8d/scripts/testing/journeys/rendering/VisualParityInventoryPlaytest.cs#L23-L87
[fixture]: https://github.com/MCMike0399/bomberman-64-multiplayer/blob/3da1eac377a9f04323c2f02ce5ce15811d2cec8d/scripts/testing/journeys/rendering/VisualParityPlaytest.cs#L63-L161
[variant]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/core/io/marshalls.cpp#L1904-L1931
[endian]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/core/io/marshalls.h#L76-L85
[flags]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/servers/rendering/rendering_server_enums.h#L124-L202
[packing]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/servers/rendering/rendering_server.cpp#L600-L1278
[mesh]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/scene/resources/mesh.cpp#L1512-L1580
[rd]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/servers/rendering/renderer_rd/storage_rd/mesh_storage.cpp#L353-L679
[oct]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/core/math/vector3.cpp#L90-L131
[surface]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/scene/resources/surface_tool.cpp#L419-L1263
[math]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/Mathf.cs#L273-L276
[vec2]: https://github.com/godotengine/godot/blob/ed1daf0bf001b61586d9930840f2f1394092c079/modules/mono/glue/GodotSharp/GodotSharp/Core/Vector2.cs#L92-L106
