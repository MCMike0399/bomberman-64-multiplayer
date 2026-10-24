# Godot 4.8 beta evaluation

Investigated 2026-10-24 against the current Godot 4.7.2 .NET baseline. The user
authorized evaluating a complete engine upgrade if it materially improves the
game. Evaluation takes place in an isolated copy; this note does not change the
main SDK pin, installed editor, export templates or installed game.

## Decision boundary

**Retain Godot 4.7.2 for this change.** The isolated 4.8 beta 1 candidate builds,
passes the headless core and native first-use checks, and restores working
internal GPU timings. It contains useful Metal and C# improvements, but the
completed runs do not establish a clear gameplay or visual benefit that justifies
migrating this release to a beta. Apple HUD already supplies GPU measurements
on 4.7.2, and effect warmup addresses first-use stalls on the stable engine.

This is a conditional decision, not a claim that 4.8 offers no benefits. Revisit
it with a matched engine comparison or a demonstrated use for its new rendering
features, plus complete visual/input/export regression coverage. The measured
results and comparison limitations are recorded below.

Godot labels beta snapshots feature-complete test releases intended to find
remaining regressions. Beta 1 was published on 2026-10-15; it is not the stable
4.8 release. [Official release](https://github.com/godotengine/godot-builds/releases/tag/4.8-beta1),
[release notes](https://godotengine.org/article/dev-snapshot-godot-4-8-beta-1/).

## Exact candidate and integrity

| Item | Verified value |
| --- | --- |
| Engine identification | `4.8.beta1.mono.official.4d10419ca` |
| Source commit | `4d10419cacd6d3009976e8f9fd3abd970fd58018` |
| Bundled SDK | `Godot.NET.Sdk/4.8.0-beta.1` |
| Editor asset | `Godot_v4.8-beta1_mono_macos.arm64.zip` |
| Editor size | 91,679,229 bytes |
| Editor SHA-256 | `d39e1a5b370cec019f3ca0b66592f3d4d751133ec455bfe37673f2fe2bea7843` |
| Template asset | `Godot_v4.8-beta1_mono_export_templates.tpz` |
| Template size | 1,194,669,563 bytes |
| Template SHA-256 | `328cdba3375c3bcf732e9c895f235fb3cbb9d0f7c28563903ed5b99edc233eb6` |

Both downloaded archives were hashed and matched the official GitHub release
asset digests before extraction. Bundled NuGet packages supply the matching SDK,
GodotSharp and source generators; there is no mixed 4.7/4.8 managed binding set.
The candidate retains `net10.0`, which already satisfies the engine requirement.
[Official asset metadata](https://api.github.com/repos/godotengine/godot-builds/releases/tags/4.8-beta1),
[C# requirements](https://docs.godotengine.org/en/latest/tutorials/scripting/c_sharp/c_sharp_basics.html).

The public NuGet flat-container indexes also list `4.8.0-beta.1` for
`Godot.NET.Sdk`, `GodotSharp`, `GodotSharpEditor` and `Godot.SourceGenerators`;
the candidate is not dependent on a private or bundled-only package feed.
[SDK index](https://api.nuget.org/v3-flatcontainer/godot.net.sdk/index.json),
[bindings index](https://api.nuget.org/v3-flatcontainer/godotsharp/index.json),
[editor bindings index](https://api.nuget.org/v3-flatcontainer/godotsharpeditor/index.json),
[generator index](https://api.nuget.org/v3-flatcontainer/godot.sourcegenerators/index.json).

## Changes relevant to this game

| Change | Relevance and limits |
| --- | --- |
| Metal synchronization, GPU timestamps and drawable acquisition, [#121392](https://github.com/godotengine/godot/pull/121392) | Directly relevant to the current Metal renderer and unavailable internal GPU timing. Changes default synchronization and delays drawable acquisition to improve pacing. The author's benchmarks use other scenes/hardware; their speedups are not Blast Circuit results. |
| C++ → managed method dispatch, [#116300](https://github.com/godotengine/godot/pull/116300) | Replaces repeated method lookup with cached delegates/hash lookup. Relevant to engine-driven C# callbacks such as `_Process`. It does not optimize pure managed simulation or every C# → native property assignment. Compatibility fallbacks remain. |
| Metal uniform binding cache, [#124341](https://github.com/godotengine/godot/pull/124341) | Fixes stale binding-cache entries after direct push-constant writes. The reported failure involved lighting corruption around opaque/sky/transparent passes on iOS Mobile; it is a correctness fix worth exercising against this game's water/effects, not proof that this project currently exhibits the issue. |
| Multi-bounce AO approximation, [#115426](https://github.com/godotengine/godot/pull/115426) | Can retain saturated surface color in occluded areas. The project setting defaults off. A useful optional art experiment after engine parity, not a reason to silently change established lighting. |
| Directional contact shadows, [#118045](https://github.com/godotengine/godot/pull/118045) | Adds screen-space contact detail alongside existing shadow maps. The global setting defaults off; per-light allowance alone does not enable it. Requires GPU budgeting and close/ordinary camera review. |

GitHub's commit comparison verified that beta 1 includes the merged Metal
timestamp change (1,077 commits ahead, zero behind), managed dispatch change
(484 ahead, zero behind), and binding-cache correction (122 ahead, zero behind).
These checks use the published beta commit rather than assuming a merged PR
belongs to every development build. [Timestamp ancestry](https://github.com/godotengine/godot/compare/20a52a8988b1170a53e0e04a7d71a0cb26f71656...4d10419cacd6d3009976e8f9fd3abd970fd58018),
[dispatch ancestry](https://github.com/godotengine/godot/compare/1d797566ee4f2dafe0a016ef3b7842f97f01da58...4d10419cacd6d3009976e8f9fd3abd970fd58018),
[binding-cache ancestry](https://github.com/godotengine/godot/compare/3f2d69fe8f50eeb2d38f0bf88e15b9d1c0dd5cbf...4d10419cacd6d3009976e8f9fd3abd970fd58018).

Do not attribute [GodotSharp Debug optimization #124391](https://github.com/godotengine/godot/pull/124391)
to beta 1: it merged on October 23, after the October 15 candidate. Likewise,
general rendering optimization articles include work on animated Polygon2D or
D3D12 shader translation that this procedural Metal game may not exercise.
[Renderer optimization methodology](https://godotengine.org/article/rendering-cpu-optimizations/).

## Migration and regression considerations

- macOS editor downloads are architecture-specific starting with beta 1.
  [#123112](https://github.com/godotengine/godot/pull/123112) covers editor bundle
  metadata; [#119708](https://github.com/godotengine/godot/pull/119708) adds official
  export architecture selection and template architecture validation.
- The current [packager](../tools/packaging/package-macos.py) derives template folders by
  joining the first four dot-separated version fields and appending `.mono`.
  With `4.8.beta1.mono.official…` this incorrectly produces
  `4.8.beta1.mono.mono`. Parse the version through the existing `.mono` component
  or use verified template metadata when supporting prereleases. Do not work
  around it by renaming installed global template folders.
- The verified template package still contains `templates/macos.zip`, but its
  nested executables are separate `godot_macos_debug.arm64`,
  `godot_macos_release.arm64`, `godot_macos_debug.x86_64` and
  `godot_macos_release.x86_64` files. There are no `.universal` executables to
  thin. Its `version.txt` is `4.8.beta1.mono`. Use the engine's architecture
  selector and inspect the resulting arm64 export rather than assuming the
  current custom archive transformation still reduces its contents.
- Existing transparent SubViewport compositing deserves explicit inspection.
  [#99715](https://github.com/godotengine/godot/issues/99715) remains open and
  describes premultiplied-alpha edge darkening across multiple renderer versions.
  It is not an established new beta regression. Compare plaque/portrait/medal
  edges over both light and dark backgrounds.
- [#124751](https://github.com/godotengine/godot/issues/124751) reports a Metal
  startup failure when shared-event allocation returns nil. The report lists
  both 4.7.2 and beta 1. It is a shared unresolved robustness issue, not evidence
  that beta 1 newly breaks ordinary startup.
- New default UI focus selection and unfocused joypad behavior are listed in
  the beta release notes. Re-run existing keyboard/gamepad menu, focus and
  native controller checks; compile success is insufficient.
- Keep engine-only A/B comparisons separate from enabling new AO/contact-shadow
  features. Otherwise a different image/workload confounds performance evidence.

## Isolated validation completed

The candidate copy includes all current tracked/nonignored source plus a real
copy of the pinned external dependency. No `bin`, `obj`, `.godot`, Git directory
or shared output symlinks were reused. NuGet and CLI state are isolated. The
editor runs in self-contained mode, with its configuration beside the copied
editor instead of modifying the installed editor configuration.

- C# build passed with zero warnings/errors. The concurrently authored first-use
  test initially assigned `ulong[]` rendering counters to `long[]`; an explicit
  cast was applied in the isolated copy. This is recorded as a temporary test
  correction, not an established engine API migration break.
- Headless asset import completed.
- `core-headless` passed **868 assertions / 12,018 frames** using the harness's
  exact fixed-step flags. Godot reported **20 ObjectDB instances leaked at exit**;
  this warning has not yet been compared with the same 4.7.2 source snapshot.
- Two initial command mistakes were corrected: nonexistent scenario `core`, and
  omission of fixed-step flags causing a frame-bounded countdown timeout. Neither
  is classified as an engine regression.
- This initial phase used no graphical editor or native GPU benchmark. The later
  native phase is recorded below; the headless result itself does not validate
  Metal rendering or physical input.

Local receipts and candidate live under `/private/tmp/blast-godot48-eval/`.
The copy is a timestamped investigation snapshot; compare its retained source
manifest before a final engine A/B test.

### Matching release candidate prepared

The candidate was subsequently refreshed from the main scripts, testing catalog,
playtest tools and native controller libraries after the first-use warmup and
strict repeated-explosion check were added. All 161 captured script/tool/catalog
hashes still matched main at export completion. Only the copied packager changed:
it parses the template version through `.mono`, and accepts a
`BLAST_EXPORT_TEMPLATE` path to the verified isolated `macos.zip`.

The copied preset retains `binary_format/architecture="arm64"`. A headless Release
export succeeded, with no logged export errors or warnings. The resulting
executable is actually arm64; the 217-file app passed strict deep signature
verification and plist validation, includes controller libraries, and retains
the expected application identity. It is an evaluation build retaining the
existing product version, not a published release or installed replacement.

Artifact: `/private/tmp/blast-godot48-eval/export/Blast Circuit.app`.
Receipts beside the candidate include `package.stdout.log`,
`packager-adaptation.diff`, `source-snapshot.json` and `release-inventory.json`.
No graphical launch occurred during this preparation phase.

## Native candidate results and engine decision

The desktop owner subsequently tested the exported candidate on Apple M5 Pro,
macOS 27.0.1, Metal 4.0. The native first-use scenario passed **5 assertions and
2 captures**. Both actual explosion trials added zero Mesh, Surface and Draw
pipeline compilations. Observed worst frame intervals were 24.71 ms and 11.18 ms
respectively; this scenario uses fixed simulation steps and is not the uncapped
GPU throughput benchmark. Receipt:
`artifacts/graphics/godot48-native-startup/20261024T182509.336885Z-first-use-rendering/`.

An earlier VM run timed out before the playtest attached while startup awaited
draw completion. The copied candidate was refreshed with the bounded draw wait
and `ForceDraw` fallback, retained bomb material, and temporary
`[DEBUG-warmup48]` progress logs. Native startup then completed. That earlier
timeout is **not a confirmed engine regression**: the draw/focus behavior and
warmup implementation changed between trials. Diagnostic logs remain confined
to the evaluation copy and are not production changes.

The native graphics benchmark passed **41 assertions and 6 captures** at
1920×1200, seed 64, with eight players, real bot bombs and explosions, VSync
disabled and real-time uncapped processing. Each battle sample lasted about ten
seconds after warmup. Godot's root-viewport GPU timings were positive and
consistent in scale with independently captured Apple Metal HUD timings:

| Preset | Godot root GPU median | Apple HUD GPU median | Process-frame rate | Process-frame p95 |
| --- | --- | --- | --- | --- |
| Low | 2.621 ms | 2.7 ms | 134.6 FPS | 13.06 ms |
| Medium | 4.080 ms | 4.2 ms | 133.8 FPS | 12.38 ms |
| High | 4.979 ms | 5.1 ms | 136.8 FPS | 11.38 ms |

These are different measurement scopes: Godot records root rendering; the HUD
records frame command-buffer duration, and the script measures process-frame
intervals. The process-frame rate is not a claim of that many displayed frames
on the 120 Hz display. HUD analysis uses whole batches inside UTC measurement
windows with boundary guards; duplicate records must be deduplicated before
counting samples. Receipt:
`artifacts/graphics/godot48-native-focused/20261024T183049.738946Z-graphics-benchmark/`
(`graphics-benchmark.json`, `metal-gpu-summary.json`, `summary.json`).

The earlier stable-engine run in
`artifacts/graphics/native-m5-pro-final/20261024T175709.432884Z-graphics-benchmark/`
recorded approximately 120 process FPS at every preset and battle p95 intervals
of 8.81–9.01 ms. **These runs are not a matched speedup or regression comparison:**
HUD instrumentation and effect-preparation state differed. Neither the higher
beta process-frame count nor the lower stable p95 proves an engine advantage.

The confirmed upgrade benefit here is functioning internal GPU profiling.
It improves diagnostics, but GPU timing is already obtainable on 4.7.2 through
the Apple HUD. New multi-bounce AO and screen-space contact shadows were not
enabled in the candidate, so no added artistic benefit from those features was
measured. Native smoke and benchmark success also do not substitute for the
complete graphical/input suite, all stage-lighting views, or Windows/Linux
validation. The evidence therefore supports finishing the presets and warmup
on 4.7.2 and revisiting 4.8 when a meaningful game benefit is established.

## Reproducible next checks

The instrumented native candidate was archived after evaluation as
`artifacts/graphics/godot48-evaluation-instrumented.zip`; its temporary `.app`
was removed to avoid duplicate launchable game bundles. Keep it separate from
the stable-engine graphics preview. Rebuild an uninstrumented current candidate
for any future adoption comparison.

Within an isolated candidate checkout, using its verified matching editor:

```sh
dotnet build Bomberman64Multiplayer.csproj --nologo -m:1
GODOT=/path/to/candidate/Godot python3 tools/playtest/run.py --scenario core-headless --headless
GODOT=/path/to/candidate/Godot python3 tools/playtest/run.py --scenario first-use-rendering
GODOT=/path/to/candidate/Godot python3 tools/playtest/run.py --scenario graphics-settings
GODOT=/path/to/candidate/Godot python3 tools/playtest/run.py --scenario stage-lighting
GODOT=/path/to/candidate/Godot python3 tools/playtest/run.py --scenario presentation
```

Follow [desktop/build ownership](agents/testing-and-delivery.md) and the harness's
documented native benchmark mode for real GPU comparisons. Compare warm and
first-use frame tails, positive internal GPU timer samples against the Apple HUD,
three presets at identical resolution/seed, and screenshots of all stage-lighting
variants. Then validate a matching release export and the complete graphical
suite. Advance the main engine pin only when that concrete candidate is better
and remaining regressions have been assessed.
