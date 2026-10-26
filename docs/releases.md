# Release pipeline

Release identity lives in [`Release.props`](../Release.props): semantic version,
monotonically increasing build number and protocol schema revision. Client and
server assemblies embed these values. `X-Blast-Protocol` is the exact token
`revision/version/build`; every published release invalidates all previous
release tokens, even when the schema revision stays the same. Increase the schema
revision for incompatible peer messages as well. Local play is unaffected.

**v0.32.0/build 38/schema 6** is delivered. The [delivery receipt](verification.md#v0320-delivered)
records the exact CI artifacts, production rollout and signed update. Earlier clients advertise numeric `3`, `4` or branch-specific stage tokens and are rejected by the
new server before identity creation, queue/room admission or WebSocket upgrade.
Authenticated leave/cancel and operator endpoints preserve their existing policy.
Already-connected sessions are not evicted by changing source: deployment drains
and waits for rooms before replacing a process.

## Developer procedure

1. Run `python3 tools/ci/release.py doctor --publish` first. This read-only audit
   reports missing repository/environment settings, credentials by name only,
   a live deployment runner or fresh autoscaler heartbeat, explicit worker placement and persistent state.
   Presence is not proof that a key matches, a host is reachable or runner
   isolation is correct; the protected workflow and operator drill establish those.
2. Prepare a new identity with `python3 tools/ci/release.py prepare 0.33.0`
   (use the intended next version). It increases the build and synchronizes
   exports. Add `--protocol N` for an incompatible schema. The preparation
   command rejects local downgrades; promotion independently compares published
   versions and builds (including releases later marked prerelease), including legacy ZIP plists. Run:

   ```sh
   python3 tools/ci/release_identity.py --sync
   python3 tools/qa/run.py --profile public
   python3 tools/qa/run.py --profile managed
   actionlint .github/workflows/ci.yml .github/workflows/promote.yml .github/workflows/release.yml
   ```

3. Complete the changed area's gates in the [delivery guide](agents/testing-and-delivery.md).
   Merge/push through the project's authorized review process. Trusted main
   Verification builds and tests the client, portable server and cluster plugins.
   Both archive metadata receipts must match the source's exact release identity.
   The server's pre-test per-file inventory includes the same plugin files used
   by the real fleet drill; mutation after testing prevents archive creation.
4. Dispatch **Release verified game** with the successful Verification `run_id`
   and version. `publish=false` prepares a signed draft without touching hosts.
   `publish=true` performs the deployment gate and publishes after it succeeds.
   An existing draft can be promoted only when its immutable tag and source
   commit match exactly; published tags cannot be moved or reused.
5. With `AUTO_RELEASE_ENABLED=true`, **Publish verified update** calls the same
   workflow after successful trusted Verification. It preserves existing version
   releases. An unchanged version does not produce a new release. No second
   automatic publication path bypasses deployment.

From a clean checkout of the verified commit:

```sh
python3 tools/ci/release.py dispatch --run-id 123456
python3 tools/ci/release.py dispatch --run-id 123456 --publish
```

The first command prepares a signed draft. The second explicitly deploys and
publishes. Use a real successful Verification run ID. Build once in trusted CI;
reuse its exact artifacts for both commands. Do not repeat package/managed runs
locally after their required gates pass unless source, failures or unresolved
risks changed. Keep the source SHA, run ID and release identity together.

An initial deployment requires the verified commit still to be the current main
tip. A superseded queued rollout fails instead of rolling the fleet back. All manual
and automatic promotions share a single non-cancelling concurrency group.
The deployment job first signs the inspected client with the configured key and
verifies that key against the existing public key and verifies each generated signature. A missing/wrong signing key
therefore fails before draining or changing servers. Release credentials are
removed from the host adapter's subprocess environment.

## Operator setup (one time)

The repository supplies orchestration, artifact validation and live verification.
Host-specific SSH, service supervision, installation and credentials belong to
an **operator-installed deployment adapter**, not to arbitrary PR code. This
change does not install that adapter, register a runner, change GitHub settings,
expose worker routes or deploy the existing fleet.

Configure these settings before enabling automatic releases:

| Setting | Scope | Purpose |
| --- | --- | --- |
| `production` environment | GitHub Actions | Restrict deployment to trusted main; apply the organization's deployment protection rules. |
| `blast-deploy` runner label | Dedicated trusted runner | Python 3.11+, `gh`, network access to the public route and every worker, and the installed adapter. The workflow installs .NET SDK 10 for the dependency-free connection probe. Publishing uses this runner too, so private node origins remain private. |
| `BLAST_DEPLOY_AUTOSCALER_HEARTBEAT` | Repository variable | Operator controller refreshes this Unix timestamp while healthy. Doctor accepts a heartbeat no older than 90 seconds, so on-demand runners can start after dispatch. Stale/far-future/malformed values fail readiness. |
| `BLAST_DEPLOY_ADAPTER` | Production environment variable | Absolute executable path outside the checkout, e.g. `/opt/blast/bin/deploy-adapter`. |
| `BLAST_RELEASE_SERVER_URLS` | Repository or production environment secret | JSON object mapping **every admission route/worker** to its origin, including the public gateway, primary and fallback. Deployment and publication must see the same value. |
| `BLAST_DEPLOY_STATE_DIR` | Production environment variable | Absolute persistent directory outside checkout, owned by the runner user with mode 0700; journals, inspected artifacts and operator backups survive cleanup/reboots. |
| `BLAST_RELEASE_PLACEMENTS` | Repository or production environment secret | JSON mapping every target name to its expected `serverId` and placement `region`; probes fail if the selected worker differs. |
| `BLAST_UPDATE_SIGNING_KEY` | Existing release secret | Existing P-256 update signing key; do not rotate it. |
| `AUTO_RELEASE_ENABLED` | Repository variable | Enable only after a complete manual deployment/publish drill succeeds. |

Keep target origins and placement configuration in Actions secrets, not ordinary
variables: Actions prints step environments. The workflow additionally masks each
origin and hostname before use, because structured-secret redaction alone is not
sufficient ([GitHub guidance](https://docs.github.com/en/actions/reference/security/secure-use)).
Transport and subprocess exceptions retain diagnostics in private files and suppress
underlying exceptions in public tracebacks. Doctor can inspect secret names only;
shape/URL/placement validation occurs inside the protected job before host changes.

Example target secret shape (replace all example origins with configured routes):

```json
{"public":"https://game.example","primary":"https://primary.game.example","fallback":"http://127.0.0.1:18497"}
```

Placement configuration must have exactly the target keys, for example:

```json
{"public":{"serverId":"primary-worker","region":"primary"},"primary":{"serverId":"primary-worker","region":"primary"},"fallback":{"serverId":"fallback-worker","region":"fallback"}}
```

Give each independently verified worker an effective placement region. The probe
requests that region and asserts the returned server ID; fallback placement can
never masquerade as successful verification of the requested worker. Preserve
operator drains and capacity policies; an intentionally unavailable worker blocks
publication until its release policy is explicitly resolved.

The current gateway exposes `/game` but does **not** expose node-prefixed `/game`
routes (observed 2026-10-25). Use protected direct origins or operator tunnels for
workers. Do not weaken the gateway's operator/API allowlist to make a release
check pass. HTTP is accepted only on loopback; HTTPS certificates are verified.
Origins may include a routing prefix. Redirects, embedded credentials, duplicate
origins, query strings and empty target sets are rejected.

The activated operator controller starts an ephemeral runner only when a trusted
main release job is queued. Its operator-owned pre-job hook rejects other refs,
PR events and non-release workflows before their steps. Each worker unregisters
after its job; its private node tunnel is also stopped. A fresh controller heartbeat
allows manual dispatch while no privileged worker is idle. Controller programs,
SSH configuration and service locations remain in private operator documentation.

Runner job hooks must use a supported script extension. If the guard is Python,
point `ACTIONS_RUNNER_HOOK_JOB_STARTED` at an operator-owned `.sh` launcher that
executes it; a direct `.py` path fails during runner setup before checkout. Keep
the guard's allowlist intact and verify trusted release acceptance plus rejection
of PR, feature and unrelated workflow contexts. [Supported hook scripts](https://docs.github.com/en/enterprise-cloud%40latest/actions/how-tos/manage-runners/self-hosted-runners/run-scripts).

A runner with deployment access must be isolated from public PR workflows. Use
a runner group restricted to this trusted workflow where supported; otherwise
use an isolated deployment runner and require approval for all outside workflow
runs. A main-only job condition alone cannot protect a self-hosted runner from
an edited workflow. See [GitHub environment protection](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)
and the existing [runner restrictions](ci.md#graphical-runner-requirements).

## Adapter contract

The fixed executable receives two arguments: `PHASE /absolute/deployment.json`.
The context contains `schemaVersion: 1`, `runId`, `revision`, `release`,
`artifacts` (a persistent private directory containing inspected ZIPs/metadata),
`targets` (the configured origins) and `placements` (expected worker/region pairs). It contains no credentials. Keep operator
configuration/credentials outside the checkout. Commands are invoked as argv,
without shell interpolation. Each phase has a ten-minute timeout and must be
idempotent; use shorter bounded sub-operation timeouts and private operator logs.

| Phase | Required behavior |
| --- | --- |
| `drain` | Save previous state/runtime/config/plugin inventories. Drain **all** fleet servers through authenticated operator APIs before replacing any node. Wait for all public/private/waiting rooms to empty. Missing/unavailable fleet data fails closed. Return only the JSON evidence below on stdout. |
| `apply` | Verify the archive inventory while extracting safely into new versioned directories. Install the exact server **and both packaged plugins**. Preserve host secrets, mounts, resource limits and supervisor configuration. Replace/restart only the game services; retain previous runtimes. Do not rebuild on hosts or fetch another artifact. Keep the fleet drained until verification. |
| `rollback` | Drain any partially resumed nodes. Restore the saved runtime, plugins, configuration and service state on every changed target. Verify prior hashes and readiness before returning zero. A failed rollback must return nonzero and keep admission drained for operator recovery. |
| `resume` | Restore each target's original admission state, preserving any pre-existing operator drains. Must handle a partial drain or partially successful resume. |

Example drain evidence, with exactly the configured target keys:

```json
{"public":{"drained":true,"rooms":0},"primary":{"drained":true,"rooms":0},"fallback":{"drained":true,"rooms":0}}
```

Gateway aliases can share their owning worker's authenticated evidence. A public
lobby list is insufficient: it excludes private rooms. The adapter should use
authenticated `/realtime/v1/servers` plus `/servers/{id}/drain` and
`/servers/{id}/activate`, following [cluster operations](cluster-hosting.md).
Service startup must retain the drain (or enforce equivalent admission fencing)
across process replacement; draining an old process alone is insufficient.
The adapter owns this hardware/supervisor-specific behavior.

The orchestrator rejects malformed/partial/positive-room drain evidence. An apply
failure or any stale worker invokes rollback before resume. A failed rollback
leaves the fleet drained. A failed drain resumes the unchanged fleet. Publication
stays blocked after any deployment failure. Abrupt runner termination cannot run Python cleanup. The orchestrator now
fsyncs atomic mode-0600 context/journal files before and after every adapter
phase and retains inspected artifacts. A local advisory lock serializes rollout
and recovery; an incomplete journal blocks every subsequent rollout. Run all
fleet deployment jobs on the same dedicated runner/state volume; this is not
a distributed lock. The adapter must also persist its host-specific backups and
make rollback safe after partial `apply` or `resume`.

```sh
python3 tools/ci/deployment_state.py --state-dir /operator/blast-state --run-id 123456
python3 tools/ci/deployment_state.py --state-dir /operator/blast-state --run-id 123456 --recover
```

The first command prints phase/status only. Explicit recovery rolls back when
apply started, resumes only after rollback succeeds, and refuses to undo a
completed deployment. After successful recovery, retry the same verified run; its previous attempt is
archived privately under `history` before restaging. Never delete an incomplete
journal to bypass recovery.
Retain completed journals/artifacts until the following release is stable; prune
only completed directories under the operator's retention policy. Neither tool
prints target URLs, credentials or adapter output. These are private state files,
not CI upload artifacts.

## Live checks and evidence

`/game` advertises `release` with version, build, protocol revision, admission
token and the compiled source commit. After apply, **every configured target**
must report that exact identity and a single matching response header. Checks
also require a nonempty `/realtime/v1/games`, reject an obsolete release token
at both identity admission and the socket door, and actually create one bounded
current guest identity. Tokens never enter receipts; probes create no rooms.
The managed gate separately proves real HTTP/WebSocket reservation/welcome and
gameplay against the exact staged server, including old/future/malformed tokens.
The live readiness probe is not a full public match or performance test.

After resume, a dependency-free .NET client creates a private `blast-room`,
asserts the expected owning worker, redeems its reservation over the actual
selected worker WebSocket, requires a matching handshake and authenticated
`sys.welcome`, starts a server-hosted match with one CPU and observes advancing
fight snapshots. It then leaves and waits for both seat removal and actual room
retirement (up to 85 seconds), so a one-room fallback has capacity for the next
probe. The full probe is bounded by 125 seconds per target. Failure triggers rollback. The managed gate runs the same
probe against the staged server. This proves worker placement, real simulation and room-capacity cleanup;
it does not replace public multi-player gameplay or capacity measurements.

Probe/build failures retain a mode-0600 log inside a private temporary directory
and report its path. These raw diagnostics are never included in uploaded
receipts; inspect them on the runner and remove them under operator retention.

Successful deployments upload `deployment-receipt.json` (30 days), containing
only source/release identity and per-target verdicts. Publication stages all assets as a draft, downloads them and compares exact
bytes, then rechecks live identities and matches immediately before publishing.
An interrupted upload stays a draft. Promotion runs the expensive match/retirement
probe once after upload, avoiding a redundant pre-upload match cycle. In addition to the existing updater manifest,
`Blast-Circuit-release.json` and its P-256 signature bind client/server ZIPs,
source/release metadata and the updater manifest as one delivery. The existing
updater format and public key stay unchanged. Ad-hoc app signing remains the
chosen policy; no Apple Developer ID or notarization is claimed. A publication failure after deployment
keeps the verified server. Rerun failed workflow jobs or use
`python3 tools/ci/release.py dispatch --run-id 123456 --publish --retry-deployed`
from the original clean verified checkout. The deployment job recognizes only a
matching durable success context and rechecks the live fleet without applying
again. Publication may finish after main advances because its authority is the
exact verified deployed candidate, subject to monotonic published version/build
checks, including releases marked prerelease after distribution. A newer deployed fleet fails the old candidate's live checks. Never
silently downgrade servers while retrying publication. Existing users may temporarily need to wait during this strict
release cutover. Zero-downtime old/new client coexistence is outside this policy.

The v0.31 activation is complete; future candidates still require review, merge,
trusted verification and explicit or opt-in automatic release. Public QA,
local managed/network results and mocked adapter tests cannot establish a real
host rollout or hosted CI run. Record those receipts separately when activated.

## Activation drill and completion receipt

Before enabling automatic publication, the operator must install and audit the
adapter outside the checkout, register the restricted runner, configure the
protected environment/targets/placements, and run a manual release drill. Exercise
a partial apply failure, failed rollback, interrupted runner and recovery against
an isolated equivalent fleet first. The repository tests use real fixture adapter
processes and fixture signing keys; they do not certify the production adapter.

A release is complete only with the exact Verification run, final client/server
archive hashes, signature verification, successful per-worker match/retirement
receipt, draft-download byte verification and published release URL. Record these
in a dated repository verification note. Separate those from installation or
physical-device evidence. Failed setup ends with the missing prerequisite names,
not an improvised manual deployment. Automatic releases stay disabled until the
manual production drill has a retained receipt.

## Source verification — 2026-10-25 expansion

The expanded source passed the complete managed profile: 2,834 game checks,
343 client checks, 78 AI checks, 48 AI soak cases, real multiplayer/forced relay,
36 fleet checks and tested server/plugin packaging. The updated live-probe code
passed 78 checks against the staged server, including real match progress and
room retirement. Public contracts pass; 60 CI-tool tests include real temporary
P-256 signatures/tampering and fixture adapter recovery, plus malformed evidence,
version/build regression, upload interruption, private subprocess/TLS failures and
Actions masking regressions. The isolated server-staging CLI also has a fresh-process
contract that excludes host configuration.
All three workflows pass actionlint. Receipts remain in ignored
`artifacts/qa/review-expanded-managed` and `artifacts/qa/review-final-public`.

The read-only GitHub audit found no `production` environment, registered Actions
deployment runner, adapter path, persistent state setting or target/placement
configuration. The existing update-signing secret and trusted-build credentials
are configured by name. This is implemented and locally tested source, not a
production rollout, published v0.31 release or installed-app replacement.

## Production activation — v0.31

The first manual production release passed the protected workflow, per-worker
gameplay/retirement gates, signed draft download comparison and final publication.
Automatic publication is enabled after that drill. The operator controller starts
ephemeral workers on demand and refreshes its repository heartbeat; `doctor`
therefore accepts a healthy controller without requiring an idle privileged worker.
An operator-owned pre-job hook additionally limits those workers to the main
release workflows. Fork workflows require approval from all outside contributors.
The [delivery receipt](verification.md#v0310-delivered) supersedes the earlier
source-only setup audit above. Installed scripts, private host configuration and
recovery logs remain in operator storage.
