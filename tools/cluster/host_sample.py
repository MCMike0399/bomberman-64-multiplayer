#!/usr/bin/env python3
"""Read-only host witness. Run locally on each game host; emits no environment or tokens."""
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import time
import urllib.error
import urllib.request


def command(args, timeout=8):
    result = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"{args[0]} exited {result.returncode}: {result.stderr[:160]}")
    return result.stdout


def http(url):
    started = time.monotonic()
    try:
        with urllib.request.urlopen(url, timeout=3) as response:
            status = response.status
    except urllib.error.HTTPError as error:
        status = error.code
    except Exception:
        status = 0
    return {"status": status, "ms": (time.monotonic() - started) * 1000}


def sample():
    mac = platform.system() == "Darwin"
    docker = "/Applications/Docker.app/Contents/Resources/bin/docker" if mac else "docker"
    result = {"at": time.time(), "host": platform.node(), "load": os.getloadavg()}
    ids = command([docker, "ps", "-q"]).split()
    # A narrow format avoids collecting credentials from Docker's full inspect output.
    containers = {}
    for line in command([docker, "inspect", "--format", '{{json .Name}}|{{json .State}}|{{.RestartCount}}', *ids]).splitlines():
        name, state, restarts = line.split("|")
        state = json.loads(state)
        containers[json.loads(name).lstrip("/")] = {"status": state["Status"], "health": state.get("Health", {}).get("Status"), "restarts": int(restarts), "started": state["StartedAt"]}
    result["containers"] = containers
    stats = command([docker, "stats", "--no-stream", "--format", "{{json .}}", "blast-cluster-server-1"])
    result["game"] = json.loads(stats)
    result["cgroup"] = command([docker, "exec", "blast-cluster-server-1", "cat", "/sys/fs/cgroup/cpu.stat"])
    result["http"] = {"game": http("http://127.0.0.1:18496/realtime/v1/games")}
    if mac:
        result["pressure"] = int(command(["sysctl", "-n", "kern.memorystatus_vm_pressure_level"]).strip())
        swap = command(["sysctl", "-n", "vm.swapusage"])
        result["swap_used_mib"] = float(re.search(r"used = ([\d.]+)M", swap)[1])
        result["vm_stat"] = command(["vm_stat"])
        top = command(["top", "-l", "1", "-n", "0"])
        result["cpu_idle"] = float(re.search(r"([\d.]+)% idle", top)[1])
        for name, port, path in [("akos", 18487, "/health"), ("pomnibot", 18081, "/health"), ("umo", 8088, "/")]:
            result["http"][name] = http(f"http://127.0.0.1:{port}{path}")
    else:
        mem = {k: int(v) for k, v in re.findall(r"^(\w+):\s+(\d+)", Path("/proc/meminfo").read_text(), re.M)}
        result["available_mib"] = mem["MemAvailable"] / 1024
        result["swap_used_mib"] = (mem["SwapTotal"] - mem["SwapFree"]) / 1024
        result["cpu_ticks"] = list(map(int, Path("/proc/stat").read_text().splitlines()[0].split()[1:]))
        result["temperature"] = float(Path("/sys/class/thermal/thermal_zone0/temp").read_text()) / 1000
        result["throttled"] = int(command(["vcgencmd", "get_throttled"]).strip().split("=")[1], 16)
        result["units"] = {}
        for name, user in [("pihole-FTL", False), ("chopperbot", True), ("t3code", True), ("service-sentinel", True), ("agentboard", True)]:
            cmd = ["systemctl"] + (["--user"] if user else []) + ["show", name, "-p", "ActiveState", "-p", "MainPID", "-p", "NRestarts"]
            result["units"][name] = dict(line.split("=", 1) for line in command(cmd).splitlines() if "=" in line)
        dns = command(["dig", "@127.0.0.1", "example.com", "+stats", "+tries=1", "+time=2"])
        result["dns"] = {"ok": "status: NOERROR" in dns, "ms": int(re.search(r"Query time: (\d+)", dns)[1])}
        for name, port, path in [("pihole", 8081, "/admin/"), ("seafile", 8377, "/"), ("filebrowser", 8090, "/")]:
            result["http"][name] = http(f"http://127.0.0.1:{port}{path}")
        tailnet = command(["tailscale", "ip", "-4"]).strip().splitlines()[0]
        result["http"]["vaultwarden"] = http(f"http://{tailnet}:8222/")
    return result


if __name__ == "__main__":
    print(json.dumps(sample()))
