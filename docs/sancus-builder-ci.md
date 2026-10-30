# Release graphics qualification

User policy, 2026-10-30: a major **or minor** version increase against the latest
published stable release requires real-GPU qualification of the exact source,
client archives, player downloads and server archive. Patch releases use the
free functional and package gates. A changed rendering fingerprint on a patch
prints a warning recommending a minor bump; it does not block promotion.
Visual approval remains separate from machine receipts.

## Deployment status

The approved standing role and boundary were created on 2026-10-30. The existing
OIDC provider remains external. GitHub settings readback confirmed the user as
required reviewer, a main-only branch policy and immutable customized subject
claims; the reviewer rule is removed by the
[2026-10-30 evening decision](#user-decision-2026-10-30-evening-machine-safeguards-replace-the-click). The reusable workflow, campaign controller and exhaustive cleanup audit
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
limited to the existing dated run-schedule ARN clause. It is reviewed and
deployed: 74 simulations pass, the inline/boundary readback matches the reviewed
hashes, and the standing role/policy identities are unchanged. The original S3-v2
identity generator remains unchanged. Inline and boundary Access Analyzer
findings are empty. The unchanged trust has two reviewed, accepted findings:
CONFIRM_AUDIENCE_CLAIM_TYPE (plain StringEquals is correct) and
SPECIFIC_GITHUB_REPO_AND_BRANCH_RECOMMENDED (the immutable custom subject already
pins repository IDs, environment and the exact main workflow). These are retained
with their rationale, rather than reported as zero findings.

Guest bootstrap and dispatch live in `builder_bootstrap.py` and
`builder_guest.py`. Windows uses the AWS SDK already supplied by its pinned AMI
to fetch the checksum-pinned CLI installer from the run bucket. That first
GetObject request also sets ExpectedBucketOwner; no GitHub credential or public
artifact download is needed in the guest. Linux uses its package manager for OS
dependencies. SDKs, engine archives, wheels, client/server files and the public
source snapshot are all staged inputs.

Core results use an immutable checkpoint prefix. The host must verify and retain
that checkpoint before acknowledging it through the bucket; only then can the
guest start the long tours. Later progress cannot overwrite the committed core
files. Missing acknowledgement stops the next phase within a fixed bound and
never extends the campaign expiry.

`builder_preflight.py` performs noncreating account, namespace, network, quota and
pinned-image checks. `builder_recovery.py` maps earlier findings to exact retained
journals before invoking cleanup; unknown or ambiguous ownership blocks a new
launch. Its matching step alone never authorizes deletion. The hosted coordinator
and complete workflow rehearsal remain the next integration boundary.

`builder_controller.py` now exposes separate intent, launch, core, acknowledgement,
final collection and cleanup phases. Hosted execution requires the exact
successful current-main Verification and a clean source checkout. The creation
intent is uploaded durably before submission; a response loss reconciles that
same token. Instance children are recorded during provisioning. SSM dispatch and
termination both verify the exact recorded stack member.

Windows publishes readiness from its standard user's logon task, then waits for
an atomically published descriptor. Both OS wrappers retain early worker errors,
even if source unpacking fails before the normal game reporter starts. A zero
worker exit without the required receipt still fails collection.

The finalizer checks each created physical ID, the complete type audit and the
protected comparison before binding cleanup to the original GPU and graphical
receipt hashes. Its fresh hosted job checks repository variables separately.
AWS key pairs are included in discovery even though this controller creates none.
CloudFormation DELETE_COMPLETE history is retained as an AWS-managed record.

`builder_rehearse.py` runs mocked phase/receipt wiring and live noncreating
preflight under an AWS CLI mutation fence. Development/operator runs identify
that scope explicitly; current-main prepared-input and assumed-role verification
remain required for the hosted rehearsal. Rehearsal never creates native game or
GPU qualification evidence. Asset preparation and workflow phase wiring are implemented in the working
branch. Prepared current-main rehearsal, hosted fault probes and source/main
verification are still pending; this is not a launch-ready qualification receipt.

The Windows desktop helper now has an owned-SSM verification mode. It validates
live task provenance without inventing a GitHub Actions environment, and checks
the ordinary user's display and WASAPI endpoint. Privileged audio service/driver
setup happens in the disposable system bootstrap before the standard-user logon.
The reviewed virtual-audio package is a staged, checksum-pinned QA dependency.

Development rehearsal is runnable without allocating machines:

```sh
python3 tools/ci/builder_rehearse.py --state /absolute/path/to/draft-state \
  --output /absolute/path/to/new-rehearsal-output --operator
```

The hosted form also requires `--require-prepared`, validated frozen inputs and
the actual scoped assumed role. Reports preserve the real caller, source-file
hashes and dirty-tree state; an operator draft run is not that hosted proof.
The workflow on main still performs audit only. This branch implements artifact
and tool preparation, major selection and hosted phase integration; prepared-input
and hosted fault rehearsals plus the source PR/main proof remain unfinished. No
paid run has been launched.

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
The environment's main-only branch policy additionally restricts the caller.
The environment must stay (the subject is bound to it) but has no required
reviewer; `builder_environment.py` readback refuses any reviewer or wait rule.
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

## Cancellation, timeout and recovery boundary

The hosted campaign keeps cleanup in an `if: always()` step in the same approved
job. It re-assumes a fresh one-hour OIDC session before requesting termination. Each
phase checks its real credential expiration; allocation also checks that the fixed
expiry plus the 20-minute cleanup allowance fits before the job timeout and its
15-minute margin. Ordinary phases have earlier bounded subprocess deadlines.

The always path requests termination of exact journaled instances before slow
evidence work. Request timing is journaled before the API call; the first request
and observed cancel-to-request interval are preserved across cleanup retries. It retains the request/cancel timestamps, then recovers available
immutable evidence and uploads the local bytes. Only afterwards does it empty the
bucket, delete the stack and verify every type/ID. Cleanup's internal ordering also
stops journaled writers before evidence work; it does not wait on unrelated IAM/S3
metadata merely to terminate a recorded instance. Cancellation signals are
forwarded through the temporary credential wrapper. A free probe records step
entry and explicitly does not invent a real EC2 termination request.

The EventBridge TTL is **compute termination only**. It does not empty/delete the
bucket or remove the stack, security group, IAM role/profile or schedule group.
A lost runner or hard job termination can prevent always steps from executing.
GitHub documents a five-minute force-termination limit during cancellation;
[the cancellation reference](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-cancellation)
is a platform limit, not a zero-leftover guarantee.

Such a run stays red. An independent reporting job lists recorded IDs as
unverified leftovers if a final cleanup audit is missing. Durable intent/state
artifacts support the next preflight recovery or the identity-checked DiveMac
cleanup command. Promotion and a new paid run remain blocked until recovery and
cost closure are verified. No new cleanup resources or boundary permissions are
introduced by this backstop. A future full-stack TTL extension requires separate
review; a DeleteStack call alone cannot empty the populated staging bucket.

Free hosted rehearsal includes a controlled phase cancellation/timeout and an
actual short job-timeout probe. Mocked signal handling is recorded separately
from observed GitHub behavior. If the hard-timeout probe cannot execute cleanup,
its missing receipt is reported as unverified; it is never called a passing
cleanup test. The probe creates no AWS resources.

### FREE permissions smoke

`gpu-qualification.yml` mode `permissions-smoke` proves the scoped role's real
campaign permissions without allocating compute. It runs in the same protected
`gpu-qualification` campaign job and OIDC assumption as rehearsal, needs no
Verification run and ignores `SANCUS_BUILDER_PAID_HOLD`:

```sh
gh workflow run gpu-qualification.yml --ref main -f mode=permissions-smoke
```

`tools/ci/builder_smoke.py` builds the real template, then removes exactly the
two instances. The expiry role and both schedules keep their real names, tags
and boundary but target never-issued dummy instance IDs, and the schedules are
DISABLED. The paid intent/CreateStack path creates the security group, staging
bucket and policy, roles, instance profile and schedules. The smoke applies the
real `UpdateSchedule` auto-delete and stages one object. It then calls
`ec2 run-instances --dry-run` with each real instance's launch request.
`DryRunOperation` passes; `UnauthorizedOperation` or any other error fails.
An AWS CLI fence and the campaign wrapper refuse non-dry-run launches and every
termination. The paid exact-ID cleanup, type audit and independent
`builder-outcome` report follow. The job is limited to 30 minutes.

Artifacts are `builder-smoke-*`, never `builder-intent-*` or
`builder-approval-plan`, so no ledger receipt is required. Its outcome still
counts as the latest builder outcome for the zero-leftover paid guard.

## Campaign completion and cost

Each campaign has a unique owned stack, a fixed expiry of at most three hours,
at most four instances, and an estimated cost of at most $20. Current allowed
capacity is `g4dn.xlarge`. Concurrency is one. Estimate before launch, include
prior spending, and ask the user before exceeding the cumulative $200 cap.
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
| Total / authorized cumulative cap | **$4.80 / $200.00** |

Estimates are not invoices. `docs/sancus-builder-spend.json` is the enforced
ledger: after each paid run a reviewed PR appends its workflow run ID and
cost-receipt estimate and raises `closedEstimateUsd` by the same amount. Until
then the selector refuses another paid run. Retain timing, evidence and cleanup
receipts.


## Paid approval sequencing

`SANCUS_BUILDER_PAID_HOLD` affects paid qualification selection only; it never
changes release-kind classification or promotion's required GPU receipts.
Missing or any value other than the exact string `false` holds paid selection
before the protected environment job can queue. Audit and rehearsal stay free.
During the initial rehearsal/review boundary the variable is `true`. The workflow
is temporarily disabled as an additional operational hold until this selector is
on main; re-enable it with the variable still true for the free rehearsals.
Do not use `BLAST_RELEASE_KIND_OVERRIDE` to bypass this hold or the major gate.

Before a paid approval is requested, the selector publishes the exact source,
Verification run, both instance types/AMIs, fixed expiry, count, conservative
estimate and remaining cumulative budget in its step summary and the
`builder-approval-plan` artifact. The paid job name includes the cost ceiling and
source; the environment deployment URL opens the run summary. Free jobs are
explicitly labelled FREE/no allocation. The reviewed hourly ceilings are
$0.526 Linux and $0.710 Windows for two us-east-1 g4dn.xlarge instances. The
three-hour estimate including storage/IPv4/transfer/termination allowance is
$5.29. This is a ceiling estimate, not a live quote or invoice: preparation reads
live prices and refuses above either hourly ceiling or the disclosed estimate.

The fixed deadline is never moved after approval. Approval must leave at least
100 minutes for the campaign plus 20 minutes for cleanup. The approved job
checks this before preparation, and the allocation boundary checks it again
after staging. It also checks the approved job's hold value before creation intent and
CreateStack, using GitHub's variable context without a standing admin token. A
late approval, changed source/run/AMI/budget, or held job
cannot allocate resources. Cleanup does not depend on the paid hold.

Only after free rehearsals/observed cancellation and timeout outcomes show
cleanup and audit working on the platform, the release owner sets the paid hold
to `false`. No human approves individual paid runs (see below).

### User decision, 2026-10-30 evening: machine safeguards replace the click

Supersedes the $50/$10 limits and the per-run approval click below. The release
owner dispatches paid qualification without a human click. Hard rule: every run
removes everything it created (instances, volumes, security groups, stacks,
buckets and objects, schedules and roles), with zero leftovers. Budget is $20
estimated per run and $200 cumulative; ask the user before exceeding $200.
Three-hour/four-instance limits, `sancus-builder-*` names, the
`sancus-builder:run` tag key (never `sancus:*`), protected resources and explicit
`--profile sancus --region us-east-1` are unchanged.

`builder_trigger.py` selects a paid run only after `builder_guard.py` passes:

- **Once per (version, render fingerprint).** The plan artifact carries
  `builder-qualification-key.json`. A passing qualification for the key is
  never repeated. An automatic trigger never retries a failed, cancelled or
  running attempt. An operator dispatch after diagnosis must name the latest
  failed run as `diagnosed-failure-run`.
- **Zero leftovers.** The most recent builder run must have an independent
  `builder-outcome` report that passed with verified cleanup, no leftovers, no
  errors and no operator cleanup. A missing, expired or unclean report refuses.
  The next-run preflight sweep is unchanged.
- **Recorded spend.** Every earlier `builder-intent` run must have its cost
  receipt recorded in the ledger. The ledger total must equal the pre-CI
  estimate plus its recorded runs.

If a cleanup audit is ever unclean, stop paid runs, set
`SANCUS_BUILDER_PAID_HOLD=true`, and report. `builder_environment.py --apply
--remove-reviewer` removes only the reviewer rule and reads the result back.

**TTL gap.** The expiry schedules terminate instances only. If the runner is
lost, the stack (roles, security group, schedule group and bucket policy) and
the bucket remain until the next preflight or an operator cleanup. Instances are
the only billed resources. Bucket objects already expire by lifecycle rule.
The other leftovers cost nothing, and the zero-leftover guard blocks further
paid runs until they are gone.

A scheduled `cloudformation:deleteStack` backstop is feasible, but it is not
cheap or safe here. The scheduler role would need delete authority over IAM
roles, security groups, schedules and the bucket, or a stack service role
holding that authority. That widens the boundary of the standing identity. A
non-empty bucket would also leave the stack `DELETE_FAILED`. It is proposed as
a separately reviewed change, not part of this one.

### User budget decision, 2026-10-30 (superseded the same evening)

The user raised the cumulative GPU authorization from $25 to $50 and the per-run
estimate ceiling from $8 to $10, including future sessions. Ask before exceeding
$50 cumulative. Three-hour/four-instance limits, each run's approval click,
reviewed paid hold, ownership-safe cleanup and receipts are unchanged. Historical
plans and receipts retain the amounts authorized at their time; spending remains
$4.80, not an invoice. This decision authorizes no immediate allocation.
