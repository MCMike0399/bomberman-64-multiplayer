# Workstation hosting, deployment and GPU qualification

User decision, 2026-11-13: one owned Linux workstation (Alienware Aurora R11,
Nobara 44, two NVIDIA GeForce RTX 2080 SUPER GPUs) runs the production game
node, the deployment runner and Linux real-GPU release qualification. It replaces
the Raspberry Pi primary / DiveMac fallback game nodes and the Linux Sancus AWS
builder. The planned Windows VM (VFIO passthrough of the second GPU) was abandoned
after passthrough hung the host kernel; Windows real-GPU qualification stays on
the paid [Sancus builder](sancus-builder-ci.md), Windows only. Host paths,
service names, credentials and network details live in private operator
documentation, not here.

## What runs where

| Role | Before | Now |
| --- | --- | --- |
| Production game node | Pi primary + DiveMac idle fallback | Workstation, sole node `blast-workstation` |
| Public gateway, Valkey coordinator | Edge gateway | Unchanged |
| Deployment runner (`blast-deploy`) | DiveMac autoscaler | Workstation autoscaler, single-use runners |
| Linux real-GPU qualification | Sancus AWS g4dn campaign ($200 budget) | Workstation `blast-gpu` Linux runner, no per-run cost |
| Windows real-GPU qualification | Sancus AWS g4dn campaign (two instances) | Sancus AWS, one Windows instance `sancus-builder` ($20/run, $200 total) |
| Hosted functional, package, Mac and crossplay lanes | GitHub-hosted | Unchanged |

The Pi and DiveMac game containers and relays are stopped, not deleted; their
configuration and images remain for rollback. The edge gateway now has one
origin credential and one node route (`/nodes/workstation`). Capacity started at
the previous two-node total (9 rooms, 3 simulations, 1 peer relay) with a 2-CPU,
768 MiB container ceiling. That is a conservative starting point, not a measured
workstation limit; run the [capacity probe](cluster-hosting.md#capacity-and-failure-checks)
before raising it.

## Isolation boundaries

- Production and deployment run as an unprivileged service account with rootless
  Podman. The game container binds only loopback; its wstunnel relay is the only
  public exposure.
- GPU CI runs as a separate unprivileged account. Linux jobs execute in a fresh
  Ubuntu 24.04 container with the host GPU injected through NVIDIA CDI. There is
  no workstation Windows GPU runner.
- Both service accounts reach only the public internet. LAN, tailnet, Twingate
  and link-local destinations are dropped by host firewall rules.
- Every runner is single use (GitHub JIT configuration or `--ephemeral`). An
  operator job-start guard, outside the checkout, rejects anything other than the
  allowed workflow on trusted `main` before any step runs: `promote.yml`/`release.yml`
  for `blast-deploy`, `gpu-qualification.yml` for `blast-gpu`.
- The workstation also hosts developer accounts with `sudo`. Anyone with root on
  the host can read production configuration and runner credentials; grant that
  access accordingly. The runner credential is a fine-grained token limited to
  this repository.

## GPU qualification

`gpu-qualification.yml` runs automatically after a successful trusted main
Verification when the release policy requires real GPUs (major/minor versions),
and on manual dispatch with `mode=qualify` for any complete current-main
Verification run (other modes are free AWS audits/rehearsals with no Linux job):

```sh
gh workflow run gpu-qualification.yml -f mode=qualify -f verification-run-id=<Verification run>
```

One run is hybrid. The `select` job runs the AWS selector (paid hold, dedupe,
cleanup and spend guards) and the workstation selector. Windows runs on the
[Sancus builder](sancus-builder-ci.md) through the reusable `builder-controller.yml`
and uploads `release-gate-gpu-windows-x64` bound to `Sancus-builder-cleanup.json`.
The Linux job downloads the exact Verification artifacts, checks the physical
adapter (`EXPECTED_ADAPTER=RTX 2080 SUPER`, never llvmpipe), runs the
original-High core captures and controls, then the complete graphical tours and
diagnostic repeats. A hosted finalize job binds the staged Linux receipt to
`Blast-Circuit-gpu-host.json`, which records this run's single-use workstation
job, runner name and devices, and uploads `release-gate-gpu-linux-x64`. The two
artifacts never share a file name. Promotion and published-release verification
require every bound receipt the target receipts reference: both for a hybrid
release; only the shared cleanup receipt for earlier AWS-only releases, which
still verify.

Pass `--gpu-run-id` (or the dispatch input) with the successful
`gpu-qualification.yml` run when promoting a major/minor release, as before.
Visual sign-off remains a separate human judgment.

## Deployment

The `production` environment's adapter path, state directory, target origins
and placements point at the workstation. Targets are `public` and `primary`,
both expecting `blast-workstation`. `python3 tools/ci/release.py doctor --publish`
must pass before dispatch. The adapter drains to zero rooms, stages and verifies
the server ZIP, swaps the runtime into a fenced container, verifies mounted
hashes and live identity, resumes and plays a real match through every target.
It rolls back on failure.

The 2026-11-13 activation drill reinstalled the published v0.45.1 server through
that adapter transaction on the live node without publishing anything: drain,
fenced apply, identity `6/0.45.1/57` and real connections on both targets passed
in 28 seconds.

## Reboot and recovery

Production, the relay, both runner controllers and the firewall start at boot.
Both GPUs stay on the NVIDIA driver; the host policy forbids runtime GPU
rebinds, resets or passthrough, and reboots need the owner's approval with
Blast drained first. The relay uses public DNS resolvers because the Twingate
client owns the system resolver and needs an interactive login after boot.
After a host reboot, verify the public `/realtime/v1/games`
endpoint, the fleet view, both controller heartbeats and one placement probe.
To roll back the game node to the Pi or DiveMac, restore the edge's previous
route/credential backup, start the retained node's relay and container, then
update the production environment targets and placements to match.
