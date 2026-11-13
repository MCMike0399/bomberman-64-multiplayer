# Workstation hosting, deployment and GPU qualification

User decision, 2026-11-13: one owned Linux workstation (Alienware Aurora R11,
Nobara 44, two NVIDIA GeForce RTX 2080 SUPER GPUs) runs the production game
node, the deployment runner and real-GPU release qualification. It replaces the
budgeted Sancus AWS GPU builders and the Raspberry Pi primary / DiveMac fallback
game nodes. Host paths, service names, credentials and network details live in
private operator documentation, not here.

## What runs where

| Role | Before | Now |
| --- | --- | --- |
| Production game node | Pi primary + DiveMac idle fallback | Workstation, sole node `blast-workstation` |
| Public gateway, Valkey coordinator | Edge gateway | Unchanged |
| Deployment runner (`blast-deploy`) | DiveMac autoscaler | Workstation autoscaler, single-use runners |
| Windows/Linux real-GPU qualification | Sancus AWS g4dn campaigns ($200 budget) | Workstation `blast-gpu` runners, no per-run cost |
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
  Ubuntu 24.04 container with the host GPU injected through NVIDIA CDI; Windows
  jobs execute in a VM that owns the second GPU via VFIO passthrough, as the
  interactive standard user `BlastQa`.
- Both service accounts and the VM reach only the public internet. LAN, tailnet,
  Twingate and link-local destinations are dropped by host firewall rules. The VM
  may reach only host DNS/DHCP and the runner broker.
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
and on manual dispatch for any complete current-main Verification run:

```sh
gh workflow run gpu-qualification.yml -f verification-run-id=<Verification run>
```

Each target downloads the exact Verification artifacts, checks the physical
adapter (`EXPECTED_ADAPTER=RTX 2080 SUPER`, never WARP/llvmpipe), runs the
original-High core captures and controls, then the complete graphical tours and
diagnostic repeats. A hosted finalize job binds both staged receipts to
`Blast-Circuit-gpu-host.json`, which records this run's single-use workstation
jobs, runner names and devices. Promotion and published-release verification
require that bound receipt for major/minor releases. Releases qualified on AWS
keep their `Sancus-builder-cleanup.json` and still verify.

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
The second GPU is bound to `vfio-pci` before the NVIDIA driver loads, so the
desktop keeps the first GPU. After a host reboot, verify the public `/realtime/v1/games`
endpoint, the fleet view, both controller heartbeats and one placement probe.
To roll back the game node to the Pi or DiveMac, restore the edge's previous
route/credential backup, start the retained node's relay and container, then
update the production environment targets and placements to match.
