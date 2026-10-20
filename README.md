# Blast Circuit

A playable Godot C# arena battle game inspired by Bomberman 64, with original procedural art, 2–8 participants, local play, online private lobbies, matchmaking and server-controlled bots. The featured Sunken Temple arena has a stepped sandstone pyramid, a recessed courtyard, broad stairs and surrounding water. Animated characters, original synthesized audio, debris, and bomb effects run in the Godot client.

The original game supports four simultaneous players. Eight-player support and the numerical simulation tuning are decisions for this project. See [research and sources](docs/research.md).

## Run the game

Install **Godot 4.7.2 .NET** and the **.NET 10 SDK** (10.0.401 was used for verification). The root project pins `Godot.NET.Sdk/4.7.2` and targets `net10.0`. Use matching Godot export templates for desktop exports; a standalone export has not been produced here.

Both local and online builds currently require the kernel realtime contracts. A clean checkout does not build until that dependency is supplied. The kernel checkout is an external dependency ignored by this game's Git repository. It remains locally at `burbuja-kernel/`. After a fresh checkout, restore the pinned source using your normal Git access (the kernel source is not included in this public repository):

```bash
KERNEL_REPOSITORY="<your-authorized-kernel-git-url>" ./scripts/bootstrap-kernel.sh
```

From the repository root:

```bash
dotnet build Bomberman64Multiplayer.csproj
```

Import `project.godot` into the .NET editor. Open `Scenes/Main.tscn` and press **F6**, or press **F5** to run the configured main scene.

For a one-command build and launch, run `./scripts/client.sh`. Set `GODOT_BIN` if the .NET editor is in a custom location.

On macOS, the installed editor can also launch it directly:

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path .
```

Choose **LOCAL BATTLE**, configure **2–8 total participants** counting local humans and CPU players, then start the match. **PRACTICE** lets one player explore the map and rehearse bomb handling without CPU opponents. One human can play with up to seven bots. Local play works without a server. Two keyboard seats are available; additional local seats require connected gamepads. With three or more local humans, the first two seats use keyboards and up to six gamepads fill seats 3–8 in connection order. With one or two humans, the corresponding gamepads also work as optional controls.

## Play online

Godot is a client of a separate authoritative kernel process. The intended deployment is the kernel on your own server hardware, with clients entering that server's reachable address in **Online Battle**. The server must register the `blast` game; `Bomber.Server` provides that registration on top of `Burbuja.Hosting`. No Docker or Compose stack is required.

For local development, start that independent server in another terminal:

```bash
./scripts/server.sh
```

It listens at `http://127.0.0.1:18496`. Enter that URL in the game and choose **ONLINE BATTLE → CREATE PRIVATE ROOM**, then share the lobby’s displayed join code. Another game instance can enter the same URL and use **JOIN ROOM**. For a human-only match, set CPU players to zero; with bots enabled a lobby can start with one connected human. Each online game instance controls one human seat.

**QUICK PLAY** uses the kernel matchmaking queue. It needs another compatible queued player; use a private lobby for solo bot practice online. The setup screen configures stage, CPU count, win target, round duration, sudden death and ghosts. The lobby displays the real server roster and countdown.

For a LAN server, bind to a reachable interface and enter the server machine's address on each client:

```bash
./scripts/server.sh --urls http://0.0.0.0:18496
```

The default is loopback. No external hosting, domain, TLS, firewall changes or production deployment was performed. Public hosting needs its own deployment and security configuration. Native desktop C# is the target; this project does not provide a browser build.

## Controls

| Action | Keyboard seat 1 | Keyboard seat 2 | Gamepad |
|---|---|---|---|
| Move | WASD | Arrow keys | Left stick |
| Place bomb / pump held bomb | Space | Enter | A |
| Lift / release to throw | Shift | Ctrl | X |
| Remote detonate after pickup | R | Backspace | B |
| Leave match / cancel connection | Escape | Escape | Start in battle; B backs out of menus |

Walking into a resting bomb kicks it. Hold lift to carry a nearby bomb, press bomb to pump it, then release lift to throw. Explosions use spherical distance and cover occlusion rather than a classic grid cross. Pickups upgrade blast size, bomb capacity, remote detonation, red piercing bombs or a protective heart. Ghosts and closing sudden-death columns are enabled by default. Last survivor earns a round win; first to the win target wins the match.

## Repeatable playtests and triage

The agent harness plays through the actual Godot scene using inputs and product sessions:

```bash
python3 tools/playtest/run.py --build --start-server --scenario all
python3 tools/playtest/run.py --scenario ramp --headless
python3 tools/playtest/run.py --scenario script --script tools/playtest/temple-walk.json
```

Runs save screenshots, authoritative state traces, assertion results, engine logs and failure summaries in unique `artifacts/playtest/` folders. Failed assertions, timeouts and missing reports return a failing exit code. Graphical captures show real viewport pixels; headless captures are explicitly disabled. See [harness instructions](tools/playtest/README.md) for scenario scripting and diagnosis.

## Verify

[Verification results and captured evidence](docs/verification.md) record the checks completed for this iteration.

The checks below are executable integration tests, run with `dotnet run`:

```bash
dotnet run --project tests/Bomber.Game.Tests/Bomber.Game.Tests.csproj
dotnet run --project tests/Bomber.Client.Tests/Bomber.Client.Tests.csproj
```

With the server running, exercise real HTTP and WebSocket clients:

```bash
dotnet run --project tests/Bomber.Smoke/Bomber.Smoke.csproj -- http://127.0.0.1:18496
```

The game suite checks limits, eight unique human seats/spawns, raised terrain, deterministic seven-bot replay, bomb fuse/explosion/ghost behavior, safe input acknowledgements and round reset. Client checks cover settings, protocol encoding, local input edges and bounded snapshots. Network smoke covers product session adapters, private create/code join, 2–8 simultaneous human clients, remote movement, reconnect and one human with seven server bots.

Godot also has a short scene smoke mode:

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . -- --smoke
```

It starts a local match and exits after eight seconds with `GODOT_SMOKE_OK`. `--smoke-tier`, `--smoke-menu`, and `--smoke-online` exercise the second stage, menu, and running server. Add `--smoke-seconds=90` for a longer run. A graphical run with `--smoke` also writes `artifacts/gameplay.png` when that directory exists. These automated checks do not establish controller coverage, internet latency tolerance or final gameplay balance.

## Code and dependencies

[Architecture](docs/architecture.md) describes the boundaries, protocol and scaling path. The repository consumes a separately supplied burbuja-kernel checkout at `burbuja-kernel/`, revision `b0758339984bbeead1b3d1236f31e980aaa5f20e`. Build and runtime use source project references for the kernel. The product now owns its adapted simulation in `src/Bomber.Game/Simulation`, retaining MIT provenance, so map and collision changes do not mutate the generic kernel. Preserve the kernel checkout and its license.


## Screenshots

![Sunken Temple battle](docs/images/temple-battle.png)

![Mode selection](docs/images/mode-select.png)

The game uses original procedural visuals. No original game ROM, Nintendo/Hudson artwork, footage, kernel source, credentials, or playtest traces are distributed here. The adapted simulation retains its upstream MIT notice in `src/Bomber.Game/Simulation/LICENSE`.
