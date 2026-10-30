# Release graphics qualification

User policy, 2026-10-30: a major **or minor** version increase against the latest
published stable release requires real-GPU qualification of the exact source,
client archives, player downloads and server archive. Patch releases use the
free functional and package gates. A changed rendering fingerprint on a patch
prints a warning recommending a minor bump; it does not block promotion.
Visual approval remains separate from machine receipts.

## Deployment status

The approved standing role and boundary were created on 2026-10-30. The existing
OIDC provider remains external. GitHub settings readback confirms the user as
required reviewer, a main-only branch policy and immutable customized subject
claims. The reusable workflow, campaign controller and exhaustive cleanup audit
are still under preparation. Live OIDC assumption and a free audit must pass
before any paid campaign. The initial `gpu-qualification.yml` dispatch invokes
only the read-only reusable controller; it cannot launch instances. Paid
orchestration and automatic major-release queuing are a subsequent boundary.
Policy simulation is not GPU qualification.

The [per-run S3 staging update](sancus-builder-staging.md) is deployed and read
back against the reviewed policy hashes. It supports SSM execution without
placing a GitHub token in AWS. The second free OIDC audit still needs the user's
environment approval. No paid run or staging bucket has been created by this
controller work.

### SSM controller implementation boundary

The source modules under `tools/ci/builder_*` now contain the journal, exact
creation-intent recovery, immutable source payload preparation, per-run template,
SSM dispatch, result retention and cleanup stages. These are preparation work;
the paid workflow remains disabled until guest bootstrap, the complete dry run
and the reviewed campaign plan are ready. Unit tests do not qualify graphics or
prove live cleanup.

SSM dispatch uses a fixed installed dispatcher and a data-only descriptor bound
to the source, instance, target and deadline. A lost SendCommand response leaves
an unresolved durable intent; reconciliation never blindly sends a second
command. Windows graphics must run in the disposable standard user's interactive
session, not SSM's LocalSystem session. No GitHub runner is registered on AWS.
Native collectors record a distinct SSM execution context, checking the task's
fixed expiry, source and target against the guest's live IMDS identity and exact
Git snapshot. They do not set `GITHUB_ACTIONS` to impersonate a hosted runner.
Official engine archives can be staged in advance and installed with the same
checksum pins using `install_godot.py --archives` without a guest download.

The current CloudFormation `AWS::Scheduler::Schedule` resource schema does not
accept `ActionAfterCompletion`, although the Scheduler API supports it. The
controller therefore sets `DELETE` through UpdateSchedule and reads both exact
targets back before writing readiness. The corresponding permission delta is
limited to the existing dated run-schedule ARN clause. It is **not deployed**;
it requires simulation, Access Analyzer and review before an identity update.
The original S3-v2 identity generator remains unchanged.

The cleanup-only entry point never provisions or retries a game test:

```sh
python3 tools/ci/builder_supervisor.py --state /absolute/path/to/owned-campaign-state
```

Its state must retain the exact creation journal, plan and protected baseline.
Evidence collection errors do not bypass deletion ownership checks or prevent
later independently authorized cleanup stages. Missing creation responses remain
unresolved until exact-intent reconciliation succeeds. Stopped instances and
DELETE_FAILED stacks are never clean. Final checks cover recorded physical IDs
and the discovery audit; unknown ownership is reported rather than deleted.

## Promotion and fingerprint

Promotion and deployment derive the release kind from the latest published
stable version before signing, uploading or changing servers. A major candidate
requires an explicit `--gpu-run-id` (the promotion workflow has the corresponding
input). Its final controller run must match the current main source; raw
`gpu-once.yml` collector output cannot qualify it. Receipts bind both native
client archives, downloads, server and rendering fingerprint. They also bind a
successful cleanup receipt: every required type absent, protected inventory
verified, runner removal and opt-in disarming complete. Preliminary game results
without that cleanup audit are refused.

The signed delivery includes `Blast-Circuit-graphics-policy.json`. A patch
does not need GPU or graphical-tour files, but still needs every functional,
startup, update and crossplay receipt. Historical releases retain their original
signed asset contracts. An explicit user release-kind override, if needed, is
version-scoped JSON in `BLAST_RELEASE_KIND_OVERRIDE`.

The non-blocking fingerprint selects `project.godot`, `export_presets.cfg`,
`icon.svg`, the Godot project and shared build properties, the engine installer,
`scripts/{app,arena,rendering,ui}`, `scenes`, all non-testing assets, the Windows
launcher and stage definitions. Only the four export package-version fields are
normalized; driver, quality and engine settings remain hashed. The exact resolved
file list and per-file hashes are retained as `render-fingerprint.json` beside
the CI package. Generate the same list locally:

```sh
python3 tools/ci/render_fingerprint.py --output /tmp/render-fingerprint.json
```

## AWS namespace and ownership

All future AWS names use plain `sancus-builder`. Repository-local workflow,
environment, variable and runner labels may still mention GPU qualification.
Historical `blast-gpu-*` receipts keep their original names and hashes.

- Standing identity stack: `sancus-builder-ci-identity`.
- Controller role: `sancus-builder-ci`; boundary: `sancus-builder-boundary`.
- Run stack: `sancus-builder-<YYYYMMDD>-<shortsha>-<nonce>`.
- Child role, profile, security group and schedule: the complete run name plus
  an explicit suffix.
- EC2 Name tags: `sancus-builder-windows` and `sancus-builder-linux`.
- Ownership and expiry keys: `sancus-builder:run` and `sancus-builder:expires`.

Never use `sancus:` tag keys or a broad `sancus-*` ownership search. Those belong
to unrelated services and their watchdog. Discovery of an apparent leftover is
not deletion authorization: match the exact journal, account, region, tags and
CloudFormation physical membership before cleanup. Unknown ownership stops that
cleanup item and is reported.

The standing stack contains only the role and boundary. It references the
account's existing GitHub OIDC provider and never manages that provider.
Child IAM permissions use the dated run prefix, excluding the standing role
and boundary from self-modification or PassRole. New child roles must carry
the same permissions boundary.

## OIDC approval boundary

The trust policy accepts only this repository's `gpu-qualification` environment,
immutable owner/repository IDs, STS audience and exact reusable workflow
`.github/workflows/builder-controller.yml@refs/heads/main`. Repository OIDC
customization requires `repo`, `context` and `job_workflow_ref` claims. Preserve
the immutable subject format; name recycling must not authorize another repo.
The environment's main-only branch policy additionally restricts the caller,
and the user is its required reviewer before any paid job can run.
See [GitHub's reusable-workflow OIDC guidance](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-with-reusable-workflows).

RunInstances is limited to reviewed exact AMI IDs, `g4dn.xlarge`, required
IMDSv2 tokens, encrypted volumes and a security group with the run ownership tag.
Current SSM agents use modern `ssmmessages` channels; legacy `ec2messages` and
State Manager association privileges are not granted. Check that agent readiness
before a campaign. Log-group creation is not granted; the read-only audit still
enumerates log groups and schedules and must report any unexpected footprint.

Prepare a review packet without creating resources:

```sh
python3 tools/ci/builder_identity.py --output /tmp/builder-identity.json
python3 -m unittest discover -s tools/ci -p 'test_builder*.py'
python3 tools/ci/builder_policy_simulation.py \
  --protected-inventory /absolute/path/to/protected-instances.json \
  --output /absolute/path/to/new-simulation-directory
```

The simulation command pins the Sancus profile and `us-east-1`, checks STS
account identity and makes only read-only STS/IAM calls. Its retained results
cover tagged/untagged/protected instance actions, the small instance type,
child-role boundary and protection of the standing controller. Simulation is
not live OIDC, provisioning, desktop, cleanup or hardware proof.

The operator removes the approved **standing identity only**
with the command below, then waits for deletion and checks its two exact
physical IDs. This does not remove the referenced OIDC provider. Clean any
owned run stacks first so they do not still need the boundary.

```sh
aws cloudformation delete-stack --profile sancus --region us-east-1 \
  --stack-name sancus-builder-ci-identity
aws cloudformation wait stack-delete-complete --profile sancus --region us-east-1 \
  --stack-name sancus-builder-ci-identity
```

## Campaign completion and cost

Each campaign has a unique owned stack, a fixed expiry of at most three hours,
at most four instances, and an estimated cost of at most $8. Current allowed
capacity is `g4dn.xlarge`. Concurrency is one. Estimate before launch, include
prior spending, and request a decision before exceeding the cumulative $25 cap.
The standing identity is the only intended persistent AWS footprint.

Every result must include an exact-ID/type audit, tagged-resource discovery,
protected-inventory comparison, ephemeral runner removal and opt-in disarming.
Terminate instances and verify storage/network/IAM/schedule/log/S3/SSM resources
are absent. Download and hash evidence before teardown. CloudTrail events, SSM
command history and billing records are AWS-managed non-resource records;
list them honestly rather than claiming to delete them.

| Spending ledger | Conservative elapsed-cost estimate |
| --- | ---: |
| Closed campaigns before the CI/OIDC follow-up | $4.80 |
| New CI/OIDC campaigns launched | $0.00 |
| Total / authorized cumulative cap | **$4.80 / $25.00** |

Estimates are not invoices or an enforced dollar cutoff. Update the ledger
after each campaign and retain its timing, evidence and cleanup receipts.
