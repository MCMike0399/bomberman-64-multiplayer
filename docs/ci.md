# Verification and delivery

The canonical entry point is `python3 tools/qa/run.py --profile PROFILE`. CI calls the same commands used locally. Each profile writes receipts beneath `artifacts/qa`; gameplay evidence keeps the existing per-run playtest summaries and traces. A successful public PR check does not imply that private builds, Metal rendering or physical controllers were tested.

| Profile/job | Environment | Evidence and boundary |
| --- | --- | --- |
| `public` | Every PR and main push, hosted Ubuntu | Python syntax, tracked private-file boundary, CI credential/publication contract tests, deterministic PCM tests. No kernel credentials. |
| `managed` | Trusted main, hosted Ubuntu | Managed gameplay/client/AI/audio checks, portable Release server publish, and real HTTP/WebSocket smoke against the exact staged server delivery. Produces the server ZIP. |
| `headless` | Trusted main, hosted Ubuntu, Godot .NET | Resource import/build and simulation scenarios. No screenshot/rendering claim. |
| `graphical` | Trusted main, isolated macOS ARM64 Metal runner | Windowed real Godot journeys and actual viewport captures. No physical-controller claim. |
| `package` | Trusted main, hosted macOS ARM64 | Self-contained Apple Silicon export, bundle/signature checks and exported-app headless smoke. Artifact is ad-hoc signed. |

## Enabling trusted builds

Trusted private CI is configured with a dedicated read-only dependency deploy key and KERNEL_CI_ENABLED. The pinned checkout bootstrap was tested with that scoped key. The native graphical runner remains disabled and unregistered; skipped graphical checks are not evidence of a passing rendering suite. The workflow describes these capabilities in the public job summary.

An administrator must configure these repository Actions settings:

| Setting | Type | Value |
| --- | --- | --- |
| `KERNEL_REPOSITORY` | Secret | Authorized GitHub `owner/repository`, supplied by the owner. Never commit its private origin. |
| `KERNEL_DEPLOY_KEY` | Secret | Preferred: a repository-scoped read-only SSH deploy key, without a passphrase. |
| `KERNEL_READ_TOKEN` | Secret | Alternative: fine-grained token with **Contents: read** only on that dependency repository, or equivalent short-lived GitHub App installation token. |
| `KERNEL_CI_ENABLED` | Variable | `true` after the repository and one authentication secret exist. |
| `MACOS_QA_ENABLED` | Variable | `true` only after the graphical runner meets the isolation and session requirements below. |

The pinned dependency revision is `e3c46ab795a8095d55b75790d76ea2786559be7f`, matching `scripts/bootstrap-kernel.sh`. The upstream history rewrite replaced the original pin; the mapped commit was verified byte-identical for all runtime/source/build files, with only five Markdown documentation changes. `tools/ci/bootstrap_kernel.py` fetches that exact revision into the ignored `burbuja-kernel/` directory. It prefers the scoped SSH deploy key and uses GitHub host keys obtained over HTTPS from the official metadata API. Alternatively, its temporary askpass helper reads the token from the environment; it never writes the token into a URL or Git config, and it suppresses private-origin diagnostics. Temporary deploy-key/known-hosts/askpass files are removed on exit. Credentials are scoped to that bootstrap step, with no credential cache. There is no private-checkout cache or workspace artifact.

These jobs accept only main pushes and manual dispatches of main. There is no `pull_request_target` workflow, and no private dependency checkout for any PR, including same-repository PRs. To run full verification for a contribution before merging, review it and run the canonical profiles in an authorized isolated checkout. Do not change the trusted workflow to fetch arbitrary PR refs with private credentials. Protect main and review changes to workflow/tooling before merging.

## Graphical runner requirements

The optional job selects `[self-hosted, macOS, ARM64, blast-metal]`. It needs a logged-in Aqua session with an active display and working Metal, the exact Godot 4.7.2 .NET editor at `/Applications/Godot_mono.app`, Python 3.11+, and .NET 10. Use a dedicated disposable runner, not a developer's everyday login. Avoid concurrent graphical sessions and provide enough disk space for captures; runner job timeout is 30 minutes. The optional USB controller libraries and physical controllers are separate capabilities and are not silently inferred from simulated controller checks.

GitHub warns against self-hosted runners in public repositories because PRs can alter workflow code. A job's main-only `if` condition is not a security boundary against a modified workflow. Before registering this runner, require approval for **all** outside-contributor workflow runs and use an isolated, ephemeral machine with no personal files, persistent credentials or access to production/private networks. Where available, restrict an organization runner group to the selected trusted workflow. If those controls cannot be enforced, keep `MACOS_QA_ENABLED` unset and run graphical QA locally instead. This change does not register or provision a runner. See [GitHub's secure-use guidance](https://docs.github.com/en/actions/reference/security/secure-use) and [runner setup constraints](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners).

Standard hosted macOS ARM64 is used for export, not as evidence that native Metal rendering has been exercised. GitHub documents the [hosted runner architectures and limitations](https://docs.github.com/en/actions/reference/runners/github-hosted-runners); an active graphical session must be verified for the graphical lane.

## Toolchain and artifacts

The editor and .NET export templates come from the official Godot 4.7.2 release. `tools/ci/install_godot.py` verifies checked-in SHA-512 hashes before extracting downloads; update the project SDK, the installer version/hashes and export-template version together. .NET uses the current supported `10.0.x` servicing SDK. Managed builds run with `-m:1` to avoid competing Godot/MSBuild workers. Godot's [command-line import and export interface](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html) supports headless CI; headless execution does not provide rendered-image evidence.

Actions are pinned to immutable commits. Public PRs receive read-only repository permissions; checkout never persists GitHub credentials. Build caches are intentionally absent: private dependency outputs never enter a cache readable from untrusted PRs. Package jobs depend on passing managed and headless gates. The separate optional Metal lane must also pass when rendering approval is required; a package artifact alone does not establish that.

`tools/ci/stage_reports.py` stages only JSON/JSONL receipts, `junit.xml`, and PNG/MP4/WAV evidence beneath the canonical QA output. It excludes source trees, compiled build directories, hidden paths, symlinks and raw logs. Workflows upload only that staging directory (seven-day retention) and the explicitly named app/server ZIPs and identity receipts (14-day retention), never `.` or the whole `artifacts/` tree. Raw logs remain available locally for diagnosis. Do not put credentials into scenario scripts, trace state or report text.

The macOS ZIP preserves bundle permissions and symlinks via `ditto` and ships with a SHA-256 checksum receipt plus source/bundle identity metadata. It contains the exported game, managed runtime dependencies and required notices; it does not contain the kernel source checkout. The package profile includes the SDL/USB backend required for Switch 2 USB support. On Darwin ARM64 it builds `tools/controllers/bootstrap-sdl.sh` from checksum-pinned archives when any required library/license is missing, then audits all four files in the exported bundle. Existing complete local binaries can be reused. The artifact feature manifest records this backend, and draft promotion rejects missing manifests or missing archived libraries/licenses. Inclusion does not establish fresh physical-controller validation.

## Portable server delivery

The `managed` profile publishes `src/Bomber.Server` in Release with `--self-contained false -p:UseAppHost=false`, stages compiled runtime dependencies plus the entrypoint/deps/runtimeconfig files, and excludes private source, symbols, kernel documents, schemas and local/production host configuration. It copies only the game’s tracked public `appsettings.json` defaults directly from product source; the realtime worker role serves the game protocol without mounting unused kernel portal assets. It starts that exact staged `Bomber.Server.dll` on a fresh Kestrel-assigned loopback port, runs the real network smoke, stops only its owned process group, then archives the same delivery. A file/hash inventory is captured before smoke; runtime-created data or configuration is excluded, changes to staged binaries fail packaging, and an independent runtime-file allowlist is enforced again at archive/promotion time. A failure prevents the server artifact upload. `tools/ci/package_server.py` owns staging/ZIP/metadata; the Actions artifact is `Blast-Circuit-server-dotnet10`.

On the intended hardware, install ASP.NET Core Runtime 10 (or .NET SDK 10), verify the ZIP checksum, extract the server directory, then run:

```sh
dotnet Bomber.Server.dll --urls http://127.0.0.1:18496
```

Service supervision, external binding, reverse proxy/TLS, authentication policy, storage and rollback belong to the host operator. The artifact supplies no host credentials/configuration and makes no remote-deployment claim. Framework dependence means it needs the matching runtime on the host; this is separate from the self-contained macOS game client.

## Deployment boundary

Verification delivers a downloadable CI artifact. The separate **Prepare draft release** workflow accepts a successful Verification run ID and bundle version on main. Its read-only job validates repository, branch, workflow, required successful jobs and artifact availability; its write-permission job repeats validation, checks source metadata, SHA-256 and the archived bundle version, then creates a draft release targeting exactly that run commit. Existing release tags are rejected. It uses only the workflow token and downloads only the named macOS and server artifacts from the same run. Both must match the requested version and exact source commit; server runtime files are also checked against their manifest. A draft attaches both delivery ZIPs and their checksum/identity receipts; it is not publicly published automatically. See the [GitHub CLI draft/target options](https://cli.github.com/manual/gh_release_create).

This pipeline does not install an app on another machine, sign with an Apple Developer identity, notarize, or deploy/restart an authoritative server. The current ad-hoc signature is appropriate for development and is not a Gatekeeper/notarization claim. Production release work needs an explicit destination, signing identity and protected approval environment. Server delivery likewise needs an authorized host, service lifecycle and rollback plan; no Docker Compose or production credentials are assumed.

## Extending verification

Add gameplay scenarios to the canonical playtest manifest and assign them to the appropriate QA profile. Keep graphical-only behavior out of the headless profile. Prefer actual input and observable assertions; scene startup, a nonzero screenshot count, or a missing optional capability does not independently prove the requested behavior. A timeout, missing receipt, runtime error or failed assertion must fail the corresponding profile. Keep build/server ownership in the shared orchestrator rather than adding background server processes to YAML.

For workflow changes run `actionlint .github/workflows/ci.yml`; `.github/actionlint.yaml` declares the custom runner label. `python3 tools/ci/public_checks.py` and the standalone audio suite are safe from a clean public clone. Validate private profiles in an authorized checkout before enabling the trusted jobs.
