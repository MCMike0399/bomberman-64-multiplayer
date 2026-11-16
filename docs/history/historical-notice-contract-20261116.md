# Signed historical package notices — 2026-11-16

The network interruption preparation job on candidate `9352433` failed before
building or measuring the candidate. [Original failed job](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/43975176786/job/133677205614)
reported `Desktop runtime is incomplete: Blast-Circuit-GPL-3.0.txt`.

`network_blackout_prepare.py` deliberately pins the shipped v0.45.0 source
`af93089f385290de2f30954ef2822bd4051324da`. Its signed delivery/source and asset
hash checks completed, then the current desktop validator applied the new GPL
notice basename to that immutable MIT-era archive. The published desktop and
server metadata both name `Notices/Blast-Circuit-MIT.txt`; their downloaded bytes
match the signed delivery's hashes. The current server allowlist would also
reject that old filename immediately after the desktop check was repaired.

The validators keep GPL as the default. Historical MIT selection is explicit and
accepted only for these complete published identities:

| Source | Version | Build | Protocol | Admission token |
| --- | --- | --- | --- | --- |
| `af93089f385290de2f30954ef2822bd4051324da` | 0.45.0 | 56 | 6 | `6/0.45.0/56` |
| `68002f2c28314d2ed91301cf37088bea1d408d1a` | 0.45.1 | 57 | 6 | `6/0.45.1/57` |

The pinned fault baseline selects its expected notice only after its existing
published-source, signature and signed-asset checks. Published-release inspection
does the same after authenticating its delivery and full inventory. Server
validation substitutes the expected project notice in its exact allowlist;
it does not accept both filenames. Current/unknown sources cannot select MIT,
even with an old version number. Archive paths, per-file inventories/hashes,
source/release identities and all other runtime checks remain intact.

Caller audit: candidate packaging, smoke/startup, promotion, current updater
fixtures and generic online/peer diagnostics retain the GPL default. Mac
`inspect_package` has no project-notice basename assumption: it validates its
existing checksum, source/release/plist identity and controller backend. Mac
`update_from_published.fetch_previous` authenticates the published updater
manifest without either filename assumption. Both flows remain unchanged; a
miniature MIT-era Mac archive retains their structural inspection regression.

The retained regression drives the real baseline-preparation function over
runtime-only desktop/server ZIP fixtures and reproduces the original GPL-name
failure before the fix. It then passes both exact historical identities and
retains negative signature, source, signed-asset hash, file-inventory, wrong
build/protocol/admission-token and unknown/new-source cases. No license content,
baseline pin, release identity, product/runtime behavior, workflow execution or
published archive is changed by this compatibility correction.
