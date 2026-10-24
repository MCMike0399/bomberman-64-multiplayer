#!/usr/bin/env python3
"""Collect host baselines and stop only a child load generator if safety limits trip."""
import argparse
import concurrent.futures
import json
from pathlib import Path
import signal
import subprocess
import time

SAMPLE = Path(__file__).with_name("host_sample.py")


def collect(host):
    response = subprocess.run(["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=8", "-o", "ControlPath=/private/tmp/blast-monitor-%h-%p-%r", "-o", "ControlMaster=auto", "-o", "ControlPersist=60", host, "python3 -"],
                              input=SAMPLE.read_text(), capture_output=True, text=True, timeout=30)
    if response.returncode:
        raise RuntimeError(f"{host} sampling failed: {response.stderr[:200]}")
    return json.loads(response.stdout)


def violations(before, now):
    failures = []
    for name, previous in before["containers"].items():
        if name.startswith("blast-"):
            continue
        current = now["containers"].get(name)
        if current != previous:
            failures.append(f"existing container changed: {name}")
    for name, previous in before.get("units", {}).items():
        if now.get("units", {}).get(name) != previous:
            failures.append(f"existing service changed: {name}")
        if now.get("units", {}).get(name, {}).get("ActiveState") != "active":
            failures.append(f"existing service not active: {name}")
    for name, previous in before["http"].items():
        current = now["http"].get(name, {"status": 0, "ms": 9999})
        if current["status"] != previous["status"] or current["status"] == 0:
            failures.append(f"HTTP health changed: {name}")
        if current["status"] not in (200, 301, 302, 307, 308, 401, 403):
            failures.append(f"HTTP service unhealthy: {name}")
        if current["ms"] > max(500, previous["ms"] * 3 + 100):
            failures.append(f"HTTP latency degraded: {name}")
    if now.get("pressure", 0) >= 4:
        failures.append("critical macOS memory pressure")
    if now.get("available_mib", 99999) < 1536:
        failures.append("Pi available memory below 1.5 GiB")
    if now["swap_used_mib"] > before["swap_used_mib"] + 256:
        failures.append("swap grew by more than 256 MiB")
    if now.get("temperature", 0) >= 75 or now.get("throttled", 0) & 15:
        failures.append("Pi temperature/throttling limit")
    if not now.get("dns", {"ok": True})["ok"] or now.get("dns", {"ms": 0})["ms"] > 250:
        failures.append("DNS probe failed or slowed")
    if now.get("cpu_idle", 100) < 10:
        failures.append("host CPU idle below 10%")
    if "cpu_ticks" in before and "cpu_ticks" in now:
        delta = [b - a for a, b in zip(before["cpu_ticks"][:8], now["cpu_ticks"][:8])]
        total = sum(delta)
        if total > 0 and (delta[3] + delta[4]) / total < .15:
            failures.append("Pi CPU idle below 15% over the observation interval")
    game = now["containers"].get("blast-cluster-server-1", {})
    if game.get("health") != "healthy" or game.get("restarts") != before["containers"].get("blast-cluster-server-1", {}).get("restarts"):
        failures.append("game node unhealthy or restarted")
    return failures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--hosts", nargs="+", default=["pi", "DiveMac"])
    parser.add_argument("--baseline-seconds", type=int, default=30)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if args.baseline_seconds < 5:
        parser.error("A baseline of at least five seconds is required")
    args.output.mkdir(parents=True, exist_ok=False)
    child = None
    baseline = {}
    failures = []
    result = {"passed": False}
    try:
        with concurrent.futures.ThreadPoolExecutor(max_workers=len(args.hosts)) as pool, (args.output / "hosts.jsonl").open("w") as log:
            start = time.monotonic()
            while True:
                futures = {host: pool.submit(collect, host) for host in args.hosts}
                samples = {host: future.result() for host, future in futures.items()}
                phase = "load" if child else "baseline"
                log.write(json.dumps({"phase": phase, "samples": samples}) + "\n")
                log.flush()
                if not baseline:
                    baseline = samples
                failures = [f"{host}: {message}" for host, sample in samples.items() for message in violations(baseline[host], sample)]
                if failures:
                    raise RuntimeError("; ".join(failures))
                if not child and time.monotonic() - start >= args.baseline_seconds:
                    command = args.command[1:] if args.command[:1] == ["--"] else args.command
                    if not command:
                        result = {"passed": True, "baselineOnly": True}
                        break
                    child_log = (args.output / "probe.log").open("w")
                    child = subprocess.Popen(command, stdout=child_log, stderr=subprocess.STDOUT)
                    print("Baseline accepted; started bounded probe", flush=True)
                if child and child.poll() is not None:
                    result = {"passed": child.returncode == 0, "probeExit": child.returncode}
                    break
                time.sleep(5)
    except Exception as error:
        result = {"passed": False, "abort": str(error), "violations": failures}
        print("STOP: " + str(error), flush=True)
    finally:
        if child and child.poll() is None:
            child.send_signal(signal.SIGINT)
            try:
                child.wait(timeout=30)
            except subprocess.TimeoutExpired:
                child.terminate()
                child.wait(timeout=10)
        (args.output / "guard.json").write_text(json.dumps(result, indent=2))
    print(json.dumps(result), flush=True)
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
