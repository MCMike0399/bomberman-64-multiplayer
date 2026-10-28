# Packaged observable authority replay

The `authority-replay` playtest runs the application's loaded `Bomber.Game`
assembly through `LocalMatch`, directly advancing its 30 Hz authority ticks.
It uses no server, renderer, elapsed-frame approximation or separate test
assembly. This adds empirical package/runtime comparisons to shared-source
tests. It is not a full private-state comparison or proof of every possible
match, input device or rendering outcome.

## Fixed corpus and output

[The bundled recipe](../assets/testing/authority-replay.json) is versioned and
hashed. Its two initial cases are Temple movement/carry/pump/aim/throw and
same-tick button edges, then Blizzard AI with seeded weather/gameplay and
independent math/RNG canaries. Together they execute 836 ticks after the
constructor's two priming ticks. Expanding this corpus should target a measured
gap rather than replace a missing native gameplay witness with more fixtures.

The current `authority-observable-v2` corpus adds the fixed public Top Rules
lower-floor route and early/late kicks:1154 driven ticks/1160 data records across
three cases. Its21 input changes were materialized once on Mac against the actual
3da authority assembly, then replayed without feedback. Positive checks require
the same owned early bomb's drop/windup/kick, a rolling hop followed by lower-floor
rolling, and the same late bomb's positive-height blast during its hop. The actor
must remain alive/non-ghost/non-falling on the lower floor. Events accumulate by
sequence as snapshots arrive; the final snapshot's retention window cannot erase
an earlier failed action. No private state, seed search or custom stage is used.

Removing the late tick307 press causes the actual source journey to fail its
late drop/windup/kick coverage assertion. Restoring it passes20checks in two fresh
source processes with exact observable/canary output equality and zero engine
errors/warnings. These new source receipts are not native package qualification;
the earlier native19c840-record equality remains scoped to its original corpus.
Raw event height exercises the product's kick-hop math, without claiming raw
private operands, a close collision margin or universal math identity.

Every tick delivers the listed input changes in file order, including multiple
edges for one slot. Unchanged slots then receive their held intent in slot order.
The output records the actual calls and ordered `StructuredMessage` emissions,
including the real snapshot tick and ticks with no new snapshot. Object keys are
sorted ordinally; array, input and message/event order is preserved. Numbers keep
their serializer spelling and precision. The comparer adds no rounding and
omits no inconvenient game fields. The PRODUCT snapshot already quantizes player/
bomb x/z to two decimals, y to three and fuse to two; exact replay equality is
therefore equality of that observable wire stream, not proof of raw private float
bits. Events retain their supplied floats. [Source-only bounded trace candidates](authority-replay-recipe-candidates.md)
identify actual operand/decision seams for targeted follow-up without a general
physics or PRNG rewrite.

The constructor runs before an event subscription is possible. Its retained
initial stage and snapshot are included; other constructor emissions are not
claimed. Subsequent observations cover the declared tick range completely.
Internal timers, AI caches, iteration order and authority PRNG checkpoints are
not fully serialized. Equal observable outputs cannot establish that those
unobserved details are equal.

The `MathF` bit probes and separate `System.Random(seed)` canary are explicitly
independent of the authority's RNG. They help classify runtime differences;
they do not expose or reset the authority PRNG. The bot placement Sin/Cos
uses a static32-direction table; it is not a random-angle draw. Blocked steering
has a separate slot/direction-angle fallback, and Blizzard wind uses hash-based
diagonals. Independent canary equality is not a count or checkpoint of those
actual authority paths. The comparer reports canary
and observable-state differences separately. Do not respond to an isolated
canary difference with a fixed-point or PRNG rewrite without establishing its
consequence for the actual game.

Output is `authority-replay.jsonl.gz` plus a small receipt. The stream contains
a schema-1 header, a case-start record, every actual tick, a case-end record,
and completion with record/tick counts and a SHA-256 over the raw data lines.
Gzip storage differences are irrelevant to decoded comparison. Missing,
duplicated, reordered, truncated or hash-inconsistent records are invalid;
a partial matching prefix cannot pass.

## Runtime and package binding

The header separates shared recipe/source/engine-policy/release identity from
platform, OS, architecture, runtime and assembly provenance. Those platform
binary hashes may differ; source, recipe and release must match.

Godot can load managed assemblies from streams, leaving `Assembly.Location`
empty. The entrypoint therefore matches the **loaded module MVID** against
bounded installed/project module locations and hashes the unique matching DLL
bytes. It records the MVID and hash. This is not cryptographic process
attestation: the trusted native runner separately verifies that the DLL hash
belongs to the immutable player archive and that the installed payload matches
its download inventory. The detached updater's separate assemblies are excluded.

[The native runner](../tools/ci/authority_replay.py) installs the real Windows
EXE, launches the Linux AppImage through its normal FUSE path, or installs the
Mac DMG on disposable CI. It runs exactly two fresh processes on that installation,
requires positive complete zero-error receipts, checks source/platform and
module hashes, and compares both outputs. The collector revalidates file hashes,
coverage and repeat equality before comparing Mac with Windows and Linux.

[The diagnostic workflow](../.github/workflows/authority-replay.yml) runs only
on `qa/three-platform-release` when `BLAST_REPLAY_RUNNER_SHA` equals the exact
pushed runner SHA. `BLAST_REPLAY_SHA` names the frozen private `qa-desktop-<SHA>`
payload. This preserves independent Mac stable shipping and works while the
isolated PR has merge conflicts. It does not use periodic polling.

Receipts are currently **diagnostic-only**. Native Windows/Linux downloads built
by this workflow may have different container bytes from another CI run; their
actual archive/download hashes remain recorded. Final promotion must reuse the
final immutable downloads, rerun this corpus after main/version integration,
and connect this verification to the trusted release gates. An old diagnostic
pass is not qualification for different final bytes.

## Comparison and local diagnostics

Source diagnostic, without package qualification:

```sh
dotnet build Bomberman64Multiplayer.csproj --nologo -m:1
python3 tools/playtest/run.py --scenario authority-replay --headless --godot /path/to/Godot
```

Compare two complete streams with the standard-library-only tool:

```sh
python3 tools/qa/compare_authority_replay.py reference.jsonl.gz candidate.jsonl.gz \
  --output artifacts/qa/replay-comparison-new
python3 -m unittest discover -s tools/qa -p test_compare_authority_replay.py
```

The output directory must be new. Exit `0` means compatible shared identities
and equal observable/canary records; `1` means a measured difference; `2` means
invalid/incomplete/incompatible input. None is release approval.

The report retains the first case/tick/field difference for each category, raw
number spelling and its float32/float64 bit interpretations. These bit views
are diagnostic interpretations, not a claim that every JSON number had that
original .NET type. Numeric spelling differences remain visible even when
both bit interpretations happen to agree. No tolerance, dropped field,
event reordering or automatic tick alignment turns a difference into equality.

[Negative controls](../tools/qa/test_compare_authority_replay.py) cover a single
float change, independent canary changes, event order, missing/duplicate/reordered
ticks, missing snapshots, false coverage, source/recipe mismatch, tampering,
truncation and negative zero. The first source diagnostic produced 179 and 239
snapshots; bomb observations count occurrences across snapshots, not unique bombs.
Two fresh source processes matched exactly, including loaded module identities;
that is a narrow source receipt, not a native exported-package result.
