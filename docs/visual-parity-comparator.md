# Offline exact-pixel comparison

[compare_visual_parity.py](../tools/qa/compare_visual_parity.py) compares two
decoded PNGs without launching the game, using the network or changing inputs.
It measures every pixel first. There is no tolerance, SSIM gate, blur, alignment,
resize, color fitting or masking. Named rectangles add statistics; they cannot
remove pixels from the whole-image result.

This is an offline measurement tool, **not release qualification**, a production
parity certificate or artistic approval. Identical channel bytes do not establish
physical-display equality. Manifests are self-reported consistency evidence, not
cryptographically trusted capture attestation. Their hashes bind declared inputs
to bytes; they do not prove how the game produced those bytes.

## Developer setup and CLI

Pillow is an optional developer dependency. Use a local venv; do not install into
global Python or add Pillow to game/production dependencies. From the repo root:

```sh
python3 -m venv artifacts/qa/parity-venv
artifacts/qa/parity-venv/bin/python -m pip install 'Pillow==13.0.0'
artifacts/qa/parity-venv/bin/python -m unittest discover -s tools/qa -p test_compare_visual_parity.py -v

artifacts/qa/parity-venv/bin/python tools/qa/compare_visual_parity.py \
  captures/mac.png captures/windows.png \
  --reference-manifest captures/mac.json \
  --candidate-manifest captures/windows.json \
  --output artifacts/qa/parity-pair-001
```

The output directory must not exist. This prevents overwriting inputs or mixing
new receipts with stale diagnostics. Output contains `report.json` and, when
dimensions and decoding permit measurement, `amplified-diagnostic-difference.png`.
The actual Pillow version is recorded. No other packages are required.

For historical images without controlled manifests:

```sh
artifacts/qa/parity-venv/bin/python tools/qa/compare_visual_parity.py \
  captures/old-mac.png captures/old-linux.png \
  --diagnostic-only --output artifacts/qa/parity-history-001
```

Diagnostic mode rejects manifest arguments. It always reports
`metadataCompatibility: "unavailable"`, `compatibleInputs: false` and
`evidenceEligible: false`, including when `exactEqual` is true. Never construct
retrospective full manifests to promote uncontrolled screenshots into evidence.

| Exit | Meaning |
| --- | --- |
| `0` | Manifest-checked, compatible inputs with exactly equal decoded pixels. Only this outcome is successful. |
| `1` | Manifest-checked, compatible inputs with measured pixel differences. |
| `2` | Invalid/unsupported/incompatible inputs, missing or invalid manifests, invalid ROIs, encoding mismatch, dimensions mismatch, or output/usage error. Raw statistics may still be available. |
| `3` | Completed diagnostic-only measurement, equal or different; never eligible evidence. |

Even exit `0` is only this pair's declared-input comparison. It cannot verify that
the selected fixture/preset represents the intended product, that effects worked,
or that hardware claims are accurate. Production High witnesses must preserve
2x MSAA, DOF and the intended effects. MSAA-off or no-jitter controls remain
separate diagnostic experiments; the comparator does not choose release policy.

## PNG scope and color contract

Pillow performs PNG validation and decoding. A small header/chunk inspection
checks the original bit depth before Pillow can reduce precision. Only static,
8-bit truecolor RGB (PNG type 2) and RGBA (type 6) are supported. RGB is explicitly
normalized to RGBA with alpha 255 and that normalization is recorded. RGBA uses
all four stored channels, including RGB beneath transparent pixels. No premultiply,
compositing, gamma application, profile conversion or orientation is performed.

The report records IHDR dimensions/format/interlace, chunk identities, sRGB intent,
integer gAMA and cHRM values and a SHA-256 of decompressed ICC profile bytes when
present. Text and compression differences do not affect decoded equality.
For compatibility, color descriptors must match exactly, including absence;
the tool does not infer equivalence between differently tagged files. RGB versus
opaque RGBA, compression and interlace differences are permitted.

ICC profiles are recorded but **rejected**, even if their digests match, because
this tool does not establish their interpretation. Also rejected: 16-bit,
grayscale/indexed images, APNG, tRNS transparency, sBIT precision, EXIF orientation
metadata, cICP/HDR metadata (mDCV/cLLI), unknown critical chunks, invalid sRGB/gamma,
conflicting sRGB/gAMA, and non-sRGB cHRM primaries/white point. No unsupported
interpretation silently becomes a successful comparison. Pillow's decompression
bomb warnings are treated as errors; its limits are never disabled. Other PNG
text decompression/validation safeguards remain in effect.

Untagged RGB/RGBA is supported only as raw channels. In default mode the shared
capture color contract must explicitly describe what those bytes mean (for
example, SDR sRGB after tone mapping with straight alpha). The tool checks that
the declarations and embedded metadata match between inputs, but cannot prove
that an emitter followed its declared color contract. Matching gamma-only tags
are likewise compared without applying gamma. Desktop/OS/HDR captures and viewport
readbacks must not be mislabeled as the same capture surface.

## Capture manifest schema 1

The following is a structural example, not evidence. Replace every illustrative
identity and hash with actual capture values. Hash fields are lowercase SHA-256
hex strings of exactly 64 characters. `pngSha256` hashes the original PNG file,
not its decoded pixels. Only `rois` is optional. Unknown fields, missing fields,
wrong types, duplicate JSON keys and unknown schema versions are rejected.

```json
{
  "schemaVersion": 1,
  "pngSha256": "0000000000000000000000000000000000000000000000000000000000000000",
  "conditions": {
    "sceneId": "water-high-v1",
    "assetsSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "stateId": "fixture-v1:seed=17:snapshot=recorded-state-identity",
    "tick": 90,
    "width": 1280,
    "height": 800,
    "render": {
      "preset": "High",
      "msaaSamples": 2,
      "dof": true,
      "settingsSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
    },
    "cameraSha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
    "shaderTimeSeconds": 3.0,
    "animationTimeSeconds": 3.0,
    "temporal": {
      "historyId": "reset-at-frame-0:draw-sequence-v1",
      "warmupFrames": 16,
      "jitterId": "sequence-v1:index=16"
    },
    "capture": {
      "surface": "viewport-post-tonemap:RGBA8:post-draw:no-fallback",
      "colorContract": "SDR-sRGB-straight-alpha-v1"
    }
  },
  "provenance": {
    "platform": "macOS:record-OS-build:arm64",
    "api": "Metal",
    "device": "record-actual-device-and-hardware-or-software-status",
    "driver": "record-driver-version",
    "engineIdentity": "Godot-4.7.2-stable:official:record-shared-source-and-build-policy",
    "templateSha256": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
    "packageSha256": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
    "sourceIdentity": "git:record-source-revision-and-local-patch-identity",
    "runtimeIdentity": "record-runtime-patch-and-architecture"
  },
  "rois": [{"name": "water", "x": 200, "y": 450, "width": 500, "height": 180}]
}
```

All `conditions` values must match. Width/height must also match the decoded PNG.
Tick, dimensions and warmup are nonnegative integers (booleans are not integers);
times are finite nonnegative numbers, and strings must be nonempty. MSAA samples
are `0`, `2`, `4` or `8`; DOF is a boolean. Both time fields are required even for
static fixtures. Use an explicitly frozen value rather than an omitted clock.

Keep the emitter small by hashing versioned, reproducible fixture descriptions:

- `assetsSha256`: actual selected resource inventory, imported texture/mip formats
  and bytes. Same source names alone are insufficient.
- `stateId` plus `tick`: deterministic fixture/state identity, seed, authority and
  presentation state/pose/event ages; not merely the stage name. A versioned hash
  of a saved state/fixture recipe is suitable.
- `settingsSha256`: full resolved render settings, including render scale,
  filtering, shadows, lighting, tone mapping, exposure and DOF parameters. Preset,
  MSAA and DOF remain readable required fields.
- `cameraSha256`: transform, projection, near/far planes and offsets.
- `historyId` and `jitterId`: actual reset/replay/render sequence and jitter policy,
  seeds/index; use an explicit `disabled` identity where appropriate. Capture
  surface includes the fence/readback policy and any extra/fallback draw.

Retain the hashed descriptions beside the captures for human audit. The comparer
validates digest syntax, not those descriptions. Matching arbitrary labels cannot
prove temporal repeatability; measure same-device repeats before cross-target
comparison. A reported unknown state must not be treated as a controlled fixture.

Provenance is retained in full. `sourceIdentity` and `engineIdentity` must match;
they identify shared source/build policy, not platform binary hashes. Platform,
API, device/driver, template/package hashes and runtime identity may differ and
are listed under `permittedProvenanceDifferences`. In particular, Windows, Linux
and Mac package hashes are **not** required to equal. Runtime patch/architecture
differences remain visible for review, not silently asserted equivalent.

## ROIs and report schema 1

Supply identical ordered `rois` arrays in both manifests, or use `--rois rois.json`
with the array alone. These sources cannot be combined when manifest ROIs are
nonempty. Diagnostic mode can use `--rois`. Rectangles have exactly `name`, `x`,
`y`, `width`, `height`; names are unique/nonempty, coordinates are integers,
width/height are positive, and the rectangle must fit within the entire image.
Coordinates start at the top left. Overlapping ROIs are permitted and independently
measured; they are never summed into a replacement whole-image score.

`report.json` separates `compatibleInputs`/`evidenceEligible` from `exactEqual`.
Eligibility means only that this tool's input-consistency checks passed, not that
the product passed. `exactEqual` is null if pixels could not be compared; true/false
always describes decoded RGBA bytes, even when later manifest checks fail.
`encodingCompatible` separately compares supported PNG color descriptors.

Each input contains original-file and decoded-pixel SHA-256 hashes, PNG metadata,
normalization and (when read) its manifest. Decoded hashes cover tightly packed,
row-major RGBA8 bytes; dimensions are separate and must also match. `wholeImage`
and each ROI contain area, differing-pixel count/fraction, equality, a bounding box
and per-channel absolute-delta max/mean/sparse histogram. Histogram keys are integer
deltas `0..255`, values are counts, and omitted bins mean zero. Mean includes all
pixels in the region. Bounding boxes use global `[left, top, right, bottom]` pixel
coordinates, with right/bottom exclusive, or null if equal.

The diagnostic PNG is opaque grayscale `min(255, 16 * max(abs(RGBA delta)))` at
original dimensions. Alpha-only defects are visible. It is deliberately amplified
and clipped, so use JSON statistics for exact magnitudes. It is never a replacement
reference or evidence of rendered appearance. Output pixels/JSON are deterministic
for fixed inputs, paths and Pillow version; compressed output bytes may vary across
Pillow versions. There are no timestamps in the report.

Dimensions mismatch produces no cropped/partial statistics or diagnostic PNG.
Malformed inputs normally produce an error report and exit `2`; invalid CLI usage
or an unwritable/existing output directory fails before a report can be written.

## Verification scope

[Focused unittest witnesses](../tools/qa/test_compare_visual_parity.py) create tiny
PNGs and manifests in temporary folders. They cover compression/text independence,
RGBA normalization, alpha-only and removed-feature defects, equal-mean pixel
rearrangements, malformed/unsupported PNGs, decompression safeguards, cross-platform
provenance, stale/invalid manifests, diagnostic ineligibility, ROIs and CLI exits.
When Pillow is absent, these tests are explicitly skipped to preserve the optional
dependency; run the venv command above for actual comparator verification.

Follow [testing and delivery](agents/testing-and-delivery.md) for repository
checks. This standalone addition has no runtime, capture-fixture, CI gate or release
integration. Numeric test passes are not visual approval.
