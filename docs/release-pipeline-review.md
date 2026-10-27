# Release pipeline review — 2026-10-27

The recent healthy pipeline took about **20 minutes after merge**, in addition
to feature validation performed by agents. The latest release took **58m37s**
after merge because publication failed after the servers had already changed.
The delay comes from cold release gates, retries and repeated orchestration;
the normal server rollout itself took roughly one to two minutes.

This review uses GitHub's run/job timestamps, retained QA receipts and release
publication timestamps, read on 2026-10-27. The inspected baseline is
`de68212e02658b2b0bede061167ce0119f39fcdd`. Durations below start when main's
Verification run was created. They exclude local development/testing and do
not establish the total time from an agent's first test to delivery.

## What changed over time

- The [initial release pipeline](https://github.com/MCMike0399/bomberman-64-multiplayer/commit/504cc12)
  introduced exact client/server release admission and gated publication on a
  verified rollout. [Durable recovery and signed delivery](https://github.com/MCMike0399/bomberman-64-multiplayer/commit/a199b23)
  followed, including draining, rollback and signed inventories. Those controls
  protect real compatibility and recovery boundaries.
- Before [PR #35](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/35),
  packaging waited for managed and headless tests. That change ran packaging
  beside them and added archived-client startup, production-updater transactions
  and automatic public-asset verification. The saved-High crash in v0.35.0 is
  the reason archive startup/update tests cannot be replaced by a source or
  default-Medium graphical test; see the [retained regression evidence](verification.md#release-pipeline-gates--source-verification).
- The first v0.38 attempt hit a cold-Metal startup deadline; the
  [next commit](https://github.com/MCMike0399/bomberman-64-multiplayer/commit/bfc7ded)
  separated the initial cold-launch budget from later launches. Cold compilation
  and update retries remained significant in subsequent releases.
- v0.41 deployed successfully, then failed its separate publication room probe
  when a busy fallback declined new rooms. [PR #44](https://github.com/MCMike0399/bomberman-64-multiplayer/pull/44)
  permits only that fallback's explicit `no_capacity` deferral, while retaining
  exact admission/identity checks. The failed release shows why expensive or
  fallible publication preparation belongs before server cutover.

## Measured delivery times

| Candidate | Main Verification | Promotion workflow | Merge to public release | Observation |
| --- | --- | --- | --- | --- |
| v0.39.0 | [16m15s](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40448752051) | [4m35s](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40450245728) | [20m14s](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.39.0) | Updater passed attempt two. |
| v0.39.1 | [14m36s](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40453658530) | [5m12s](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40455060651) | [19m22s](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.39.1) | Updater passed attempt two. |
| v0.40.0 | [15m40s](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40458928369) | [4m47s](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40460479677) | [19m50s](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.40.0) | Updater passed attempt two. |
| v0.41.0 | [11m09s](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40517598808) | [4m25s, failed](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40519338519), then [2m48s, failed](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40520306325) | [58m37s](https://github.com/MCMike0399/bomberman-64-multiplayer/releases/tag/v0.41.0) | Updater passed first attempt; server/client cutover split required operator recovery. |
| Docs-only #42 | [11m07s](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40493308015) | [23s, no new release](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40494730665) | No release intended | Full trusted builds/startup ran despite a documentation-only change. |

Public availability precedes the final public-verification job. Including that
job, the three healthy v0.39–v0.40 journeys took 20m52s, 19m52s and 20m29s.
The v0.41 deployment job finished at 14:17:57Z; publication happened at
15:02:36Z, a **44m39s interval** with the deployed release ahead of the public
client. Existing compatible sessions are drained by policy; older clients
cannot enter the newer release.

## Where the time goes

The [v0.40 run's job records and downloadable receipts](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40458928369)
and the [v0.41 run](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/40517598808)
show the following costs. Nested `owned-server` receipt durations contain
their child tests, so they must not be summed twice.

| Work | Observed cost | Consequence |
| --- | --- | --- |
| Public gate before all trusted jobs | 49–58s in v0.39–v0.41 | Adds a serial minute; actual public checks take 12–13s, with checkout/setup/actionlint installation around them. |
| macOS package job | 3m59s–5m20s | Cold native controller bootstrap alone costs 50–81s; export costs 29–47s. |
| Saved-settings startup job | 5m05s–6m05s | Nine actual launches; a separate runner pays cold Metal compilation. |
| Updater job | 5m50s with first-attempt success; 8m31s–9m25s with a retry | Usually the critical path after packaging. |
| Updater renderer warmup inside that job | 65–88s | Separate from setup and all three transactions. |
| Managed profile | 5m54s–8m27s, plus job setup | Network smoke consistently costs 183–184s; AI soak costs 44–112s. It overlaps packaging/gates today. |
| Deployment command | 56–110s in sampled successful releases | Fleet rollout is much shorter than the overall pipeline. |
| Promotion control jobs | Four serial jobs after candidate discovery | Separate checkouts/setups and two ephemeral deployment-runner allocations add overhead. |

The full self-hosted `Trusted macOS Metal journey` job was **skipped in these
runs**. The hosted startup/update gates did execute Metal. Thus the history
does not support claiming that CI was rerunning the complete local Metal
suite every time; the duplicate work includes additional archive-specific
validation, cold initialization and full managed/network testing.

Updater receipts give more precise evidence:

| Candidate | Updater script total | Renderer warmup | Failed attempts |
| --- | --- | --- | --- |
| v0.39.0 | 523.8s | 87.6s | One; `DirectoryNotFoundException` while recursively deleting the fixture's `failed.app`. |
| v0.39.1 | 473.3s | 71.1s | One; the nominal successful install reported `launch-rollback`, without a native crash report. |
| v0.40.0 | 508.3s | 78.5s | One; the same crash-free `launch-rollback`. |
| v0.41.0 | 291.9s | 65.3s | None. |

These values come from each linked Verification run's `release-gate-update`
artifact. Current receipts do not separate fixture compilation, preparation,
individual transactions and cleanup. Private-kernel fetching took only a few
seconds in the sampled updater job, so removing that fetch alone cannot explain
or solve minutes of latency. The failed cleanup is evidence of competing file
activity; it is not enough to identify which helper operation raced without
further instrumentation.

## Improvements supported by the evidence

1. **Keep routine release work in CI.** Run focused feature/visual/controller
   evidence locally, then build the release once and promote those exact bytes.
   The existing [fast-path policy](agents/testing-and-delivery.md#ci-and-publication-boundaries)
   already says not to repeat a full local Metal suite or successful managed/
   package gates solely for publication. Make it the agent handoff path.
2. **Avoid full release builds for documentation-only changes.** Classify a
   narrow, explicit documentation allowlist; unknown paths, assets, source,
   workflows and release tooling still require the trusted pipeline. Preserve
   public documentation/contracts checks and make manual full verification
   possible. #42 demonstrates an eleven-minute avoidable run.
3. **Cache pinned tools and native dependencies.** Start with the 50–81s
   controller bootstrap, Godot/templates and actionlint installation. Cache
   keys need OS/architecture/toolchain and relevant source/pin hashes; retain
   binary/runtime checks after restoration. Cached dependencies must never
   substitute for tests or artifact identity checks.
4. **Fix updater fixture reliability before reducing coverage.** Preserve
   successful install, deliberate rollback and saved High/fullscreen. Measure
   each phase and ensure owned helpers finish before fixture deletion. Share
   a renderer warmup only when the real user-data/path cache relationship is
   proven; package smoke is headless and does not warm Metal by itself. The
   observed extra attempt costs roughly three minutes.
5. **Prepare and verify the signed draft before deployment.** Upload/download
   equality and signing failures can happen while old servers still serve
   existing clients. Keep cutover, fresh live compatibility/match checks and
   final publication together under the deployment lock, reusing already
   downloaded artifacts within that operation. Preserve rollback/retry
   journals and exact source/artifact validation.
6. **Measure complete lead time.** Report verification, queue/setup, package,
   startup, updater attempts, rollout, publication and public verification
   separately. Compare the next release to these linked baselines. Savings
   are estimates until a new trusted run demonstrates them; faster delivery
   must not be reported as measured solely from local pipeline-tool tests.

No deployment, release publication, runner configuration or service change was
performed to gather this review.

## Implemented changes and verification

The revised [release procedure](releases.md#release-latency) implements the narrow
documentation filter, public controller-library cache, prepared-draft cutover,
one protected deploy/publication job, publication retry from durable artifacts,
and updater installation-path/cleanup fixes. `release.py status` reports job
durations. Full archive startup/update and fleet gameplay checks remain required.

Local verification passed the public QA profile, workflow actionlint and the
updater fixture's .NET build. The CI contracts include upload corruption before
drain, one fresh match cycle before publication, lock ownership at publication,
publication failure/retry without redeploy, stale-fleet rejection, and Git range
tests for documentation, multi-commit changes and source-to-document renames.

The real fixture then updated the authenticated published v0.40.0 archive to
v0.41.0 inside the isolated macOS Metal guest. All three cases passed on the
first attempt: install 47.8s, deliberate rollback 56.4s, saved High/fullscreen
install 33.4s; install-path warmup took 44.2s. Including extraction and setup,
the run took **190.1s**. The VM never became the host foreground app. Local
evidence is retained under ignored `artifacts/qa/cutover-updater-20261027-092126`.
This reused a local VM and tested published apps, not a fresh hosted candidate;
it establishes real installer correctness, not a controlled speed comparison or
production rollout. The first trusted release after adoption must provide the
new end-to-end timing and verify the cache-hit and cutover paths in production.
