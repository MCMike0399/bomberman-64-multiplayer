# Release pipeline

Release identity lives in [`Release.props`](../Release.props): semantic version,
monotonically increasing build number and protocol schema revision. Client and
server assemblies embed these values. `X-Blast-Protocol` is the exact token
`revision/version/build`; every published release invalidates all previous
release tokens, even when the schema revision stays the same. Increase the schema
revision for incompatible peer messages as well. Local play is unaffected.

This source prepares **v0.30.0/build 36/schema 4**; it is not a publication or
deployment receipt. Earlier clients advertise numeric `3` and are rejected by the
new server before identity creation, queue/room admission or WebSocket upgrade.
Authenticated leave/cancel and operator endpoints preserve their existing policy.
Already-connected sessions are not evicted by changing source: deployment drains
and waits for rooms before replacing a process.

## Developer procedure

1. Change `BlastVersion` and increase `BlastBuild` in `Release.props`. Increment
   `BlastProtocolRevision` when the peer schema changes. Run:

   ```sh
   python3 tools/ci/release_identity.py --sync
   python3 tools/qa/run.py --profile public
   python3 tools/qa/run.py --profile managed
   actionlint .github/workflows/ci.yml .github/workflows/promote.yml .github/workflows/release.yml
   ```

2. Complete the changed area's gates in the [delivery guide](agents/testing-and-delivery.md).
   Merge/push through the project's authorized review process. Trusted main
   Verification builds and tests the client, portable server and cluster plugins.
   Both archive metadata receipts must match the source's exact release identity.
   The server's pre-test per-file inventory includes the same plugin files used
   by the real fleet drill; mutation after testing prevents archive creation.
3. Dispatch **Release verified game** with the successful Verification `run_id`
   and version. `publish=false` prepares a signed draft without touching hosts.
   `publish=true` performs the deployment gate and publishes after it succeeds.
   An existing draft can be promoted only when its immutable tag and source
   commit match exactly; published tags cannot be moved or reused.
4. With `AUTO_RELEASE_ENABLED=true`, **Publish verified update** calls the same
   workflow after successful trusted Verification. It preserves existing version
   releases. An unchanged version does not produce a new release. No second
   automatic publication path bypasses deployment.

Publication/deployment requires the verified commit still to be the current main
tip. A superseded queued run fails instead of rolling the fleet back. All manual
and automatic promotions share a single non-cancelling concurrency group.
The deployment job first signs the inspected client with the configured key and
verifies that key against the existing public key. A missing/wrong signing key
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
| `BLAST_DEPLOY_ADAPTER` | Production environment variable | Absolute executable path outside the checkout, e.g. `/opt/blast/bin/deploy-adapter`. |
| `BLAST_RELEASE_SERVER_URLS` | Repository variable | JSON object mapping **every admission route/worker** to its origin, including the public gateway, primary and fallback. Deployment and publication must see the same value. |
| `BLAST_UPDATE_SIGNING_KEY` | Existing release secret | Existing P-256 update signing key; do not rotate it. |
| `AUTO_RELEASE_ENABLED` | Repository variable | Enable only after a complete manual deployment/publish drill succeeds. |

Example target shape (replace all example origins with the configured routes):

```json
{"public":"https://game.example","primary":"https://primary.game.example","fallback":"http://127.0.0.1:18497"}
```

The current gateway exposes `/game` but does **not** expose node-prefixed `/game`
routes (observed 2026-10-25). Use protected direct origins or operator tunnels for
workers. Do not weaken the gateway's operator/API allowlist to make a release
check pass. HTTP is accepted only on loopback; HTTPS certificates are verified.
Origins may include a routing prefix. Redirects, embedded credentials, duplicate
origins, query strings and empty target sets are rejected.

A runner with deployment access must be isolated from public PR workflows. Use
a runner group restricted to this trusted workflow where supported; otherwise
use an isolated deployment runner and require approval for all outside workflow
runs. A main-only job condition alone cannot protect a self-hosted runner from
an edited workflow. See [GitHub environment protection](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)
and the existing [runner restrictions](ci.md#graphical-runner-requirements).

## Adapter contract

The fixed executable receives two arguments: `PHASE /absolute/deployment.json`.
The context contains `schemaVersion: 1`, `runId`, `revision`, `release`,
`artifacts` (a temporary directory containing the inspected ZIPs/metadata) and
`targets` (the configured origins). It contains no credentials. Keep operator
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
stays blocked after any deployment failure. Abrupt runner termination cannot run
Python cleanup: the adapter must journal its state and provide an operator
recovery command; never enable a runner without testing that recovery.

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
redeems its reservation over the actual selected worker WebSocket, requires a
matching handshake and authenticated `sys.welcome`, then leaves and waits for
its seat to disappear. Failure triggers rollback. The managed gate runs the same
probe against the staged server. This proves a real connection and room cleanup;
it does not replace public multi-player gameplay or capacity measurements.

Probe/build failures retain a mode-0600 log inside a private temporary directory
and report its path. These raw diagnostics are never included in uploaded
receipts; inspect them on the runner and remove them under operator retention.

Successful deployments upload `deployment-receipt.json` (30 days), containing
only source/release identity and per-target verdicts. Publication rechecks the
live targets and real room connections immediately before creating/publishing the signed release, even
when invoked directly through the CLI. A publication failure after deployment
keeps the verified server; investigate/retry publication rather than silently
downgrading it. Existing users may temporarily need to wait during this strict
release cutover. Zero-downtime old/new client coexistence is outside this policy.

The current production service and published release remain unchanged until
this source is reviewed, merged, verified and explicitly released. Public QA,
local managed/network results and mocked adapter tests cannot establish a real
host rollout or hosted CI run. Record those receipts separately when activated.
