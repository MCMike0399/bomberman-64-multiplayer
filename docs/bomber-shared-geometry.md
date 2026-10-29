# Shared Bomber presentation inputs

Exports load 32 Mac-authored packed meshes from
`assets/models/bomber-meshes.res`: torso, helmet, face/rim, two eyes, two squints
and both twelve-frame spiral sequences. The editable procedural builders remain
in `BomberModel` and its mishap partial. The Mac SDK uses those builders by
default for fast authoring; `--bomber-meshes-shared` previews the packaged inputs.
Other hosts and exported applications use the resource by default.

The binary resource preserves the original packed surfaces, including normals,
tangents, UVs and ordering. It does not rebuild from unpacked vertex arrays,
round coordinates or discard hidden eyes. Each loaded mesh is checked against
the manifest's packed-surface hash. Public QA rejects stale source hashes, a
changed binary or an incomplete mesh roster.

After an intentional builder change, build the source project, then regenerate
with the pinned Mac SDK in a fresh process:

```sh
dotnet build Bomberman64Multiplayer.csproj --nologo -m:1
GODOT --headless --path . -- --generate-bomber-meshes
python3 tools/ci/bomber_mesh_library.py
```

Replace `GODOT` with the installed 4.7.2 Mono SDK executable. Generation requires
Mac editor tooling and checks every packed surface after actual resource reload.
Commit the resource and manifest with the builder change. The producer revision
records the loaded authoring assembly; the manifest's source-file hashes bind
the actual builder contents, including an uncommitted authoring change. It is
not a final release qualification receipt.

The narrow antenna correction uses common managed sine arithmetic for the two
idle waves feeding its springs, plus fixed arithmetic for its soft limit and YXZ
basis. It only writes the antenna node's visual basis during presentation.
Authority and networking assemblies do not reference either model class; no
input, player position, collision, tick, mechanics or network message changes.
It avoids platform libm differences in that proven rendering consumer rather
than changing simulation math.

## Verification contracts

The `visual-inputs` diagnostic constructs the original High scene and records
actual CPU scene/storage inputs in a headless process, without drawing frames,
capturing images or claiming effective GPU sample support. The separate
`visual-parity` scenario still requires a renderer and retains the original
High/2xMSAA/SSIL/Circle/DOF/mirror captures and controls. Headless and rendered inventories can differ in generated font-cache state;
headless equality cannot establish GPU input equality. Both complete inventories
remain visible. No inventory field or pixel is masked.

Fresh procedural builder traces remain intact on every platform. Their strict
comparison may fail when the platform math differs; that result stays visible.
The captured-input comparison separately requires canonical mesh hashes and
exact complete scene inventories and binary storage. Corrupted/unclassified
segments or unsupported inputs fail. A headless input pass is not GPU evidence;
the real-GPU campaign also checks the rendered scene's inventories.

Initial M5 diagnostics retained all five before/after images as exact full-image
matches for both the packed geometry and antenna correction. Raw reports live
under `artifacts/qa/mesh-shared-prototype/`. They are local source measurements,
not cross-platform parity or release approval. Native package inputs, matched
Mac-to-Mac measurements, feature controls and user visual sign-off remain required.

The controlled fixture retains actual idle-wave and antenna-target samples in
`pose-wave-*.json`. Native sine evaluations are recorded as diagnostic canaries;
they do not feed the corrected idle waves. This adds no production observer work
when the callback is absent.
