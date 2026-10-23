# A party with a remote Docker server

Historical same-Wi-Fi setup, superseded as the default on 2026-10-23. The current
goal is a shared online service for all users; use the
[shared-server guide](online-server.md) for routine releases and deployments.
Read this page only for an explicit LAN party or relay task.

The server runs `Bomber.Server`, which registers Blast Circuit on the Burbuja
kernel and owns all match rules and CPU players. Each Mac app controls one human.
There are at most eight participants per room. Use the current v0.23 client and server for leader-controlled rooms and persistent
parties. Older clients use the legacy automatic-match protocol.

For friends together on one Wi-Fi, an organizer's Mac can relay the game port
over its existing SSH connection to the remote server. Friends need only the
game; their Tailscale accounts, routes and other VPN configurations stay intact.
The remote server binds loopback, and the relay binds the organizer's specific
Wi-Fi address. This grants access to the game endpoint, not general home-network
membership. The organizer must stay awake and on that network. A Wi-Fi network
that isolates clients can prevent this route; check from a second Mac before
the party. A VPN that blocks local LAN traffic may also prevent access.

## Prepare and run the server

On the development machine, with the pinned external dependency available:

```sh
python3 tools/qa/run.py --profile managed
python3 tools/prepare-party-server.py --output artifacts/party-server
```

The second command verifies the ZIP checksum and each inventoried runtime file,
then creates a new Docker context. It refuses to overwrite an existing context.
Copy that directory to a new deployment directory on the server. It contains
runtime binaries, public defaults, notices, image configuration and a build
receipt; no private source or credentials. On the destination host:

```sh
docker compose config --quiet
docker compose up -d --build --wait
docker compose ps
curl --fail http://127.0.0.1:18496/realtime/v1/games
```

Use the host's existing local Docker context. This is an independent Compose
project named `blast-circuit`; it does not require Swarm. `BLAST_PORT` and
`BLAST_BIND` can override its default loopback publication. The image runs as
the .NET `app` user and the health check requires registration of `blast`.
Docker restarts the service unless explicitly stopped. The server holds rooms
in memory: restarting it ends matches and invalidates guest sessions.
The single-node deployment intentionally issues relative socket URLs so the
client uses the same reachable address for HTTP and WebSocket traffic.

Only this project's service is managed by the following commands:

```sh
docker compose logs --tail 50 server
docker compose stop server
docker compose start server
```

See the [Compose service reference](https://docs.docker.com/reference/compose-file/services/)
and [.NET container guide](https://docs.docker.com/guides/dotnet) for the underlying
runtime and lifecycle options.

## Start the organizer's Mac relay

The remote host must already have working SSH authentication. Set its existing
SSH alias with `BLAST_SSH_HOST` (the local helper defaults to `DiveMac`):

```sh
BLAST_SSH_HOST=your-server bash tools/party-relay.sh start
bash tools/party-relay.sh status
bash tools/party-relay.sh stop
```

Use the same `BLAST_SSH_HOST` for status if overriding the default. Start detects
the Wi-Fi address on `en0`; supply another LAN IPv4 address as its second argument
when needed. It prints the game URL, normally `http://LAN_IP:18497`. All players,
including the organizer, enter that URL. The relay is a dedicated current-login
launchd job, survives the launching terminal, and retries SSH after a drop. It
does not edit SSH configuration or start on a later login. After changing Wi-Fi,
stop and start it again. A long outage can still exceed the game's 15-second
reconnect grace; rejoin or create a new room if necessary.

## Give friends the game

Share a verified Apple Silicon app ZIP by AirDrop or another file transfer.
macOS 14 or newer is required; Godot, .NET, Docker and Tailscale are not required
on the players' Macs. Unzip, move `Blast Circuit.app` into Applications, and open
it. This build is ad-hoc signed, not notarized. If macOS blocks the first launch,
use System Settings → Privacy & Security → Open Anyway for this app, as described
in [Apple's instructions](https://support.apple.com/102445). Allow local-network
access if macOS asks. No global Gatekeeper change is needed.

1. Everyone opens **Online Battle**, enters their own name and the shared server URL.
2. One person chooses **Create private room**, selects CPU count and rules, and shares the room code.
3. Everyone else chooses **Join room** and enters that code.
4. The leader can open **Room settings** to edit the match rules in the same room, then chooses **Start game** once everyone is ready. Start / + focuses that button; A / Enter confirms.

Choose zero CPUs for humans only. Private rooms wait for an explicit leader start,
including solo rooms with CPUs. At least two total participants are required.
Late joiners wait until the next round and can replace bot seats. After the
results, everyone returns to the same room and code. The leader starts the next
match; if the leader disconnects, another connected player becomes leader.

Esc / Start opens the game menu. Settings contains sound volume and controller
vibration (off by default); these choices are stored on each player's Mac.
Local play pauses in this menu. Online authority continues, and the player's
movement and action inputs are released while the menu is open.

Keyboard: WASD moves, J places/pumps, K holds carry (release to throw), L aims,
and I detonates remotes. Gamepads work with the controls shown in the game.

## Verify the actual remote path

Substitute the relay URL printed by the helper:

```sh
dotnet run --project tests/Bomber.Smoke -- http://LAN_IP:18497
python3 tools/playtest/run.py --app "$HOME/Applications/Blast Circuit.app" --scenario network --server http://LAN_IP:18497
```

The smoke tests exercise 2–8 simultaneous clients, shared state, actions,
reconnect and seven server bots. The native journey verifies the installed
app's lobby and rendered game. These checks from the organizer establish the
remote server route; a friend's Mac is still needed to confirm Wi-Fi peer access
and its first-time macOS installation. No public Internet address, DNS change or
router port forwarding is required by this setup.

## Verified deployment, 2026-10-23

The v0.19.1 Release runtime from revision `2a23a5bd9810` passed the managed
profile (139 game, 123 client, 52 AI, 19 audio checks and 36 AI soak scenarios).
Its separate Docker Desktop deployment passed the complete remote network smoke
in 53.8 seconds through the LAN relay: 2–8 clients, reconnect, appearances,
precision aim, movement, bombs and one human plus seven server bots.
The installed Mac app passed the remote native `network` journey with 18
assertions, two visually reviewed captures and zero runtime errors.

The organizer confirmed a friend's Mac could fetch the game's registration
endpoint through the Wi-Fi relay. This is peer-network evidence, not yet a
completed friends' match. The shared ZIP matched all 217 installed app files,
and a download through the local file server matched its SHA-256. Public checks
passed, including four Docker-context inventory/tamper tests. Receipts are under
ignored `artifacts/online-party/`; the party-specific address and download
instructions are in its `share/JOIN.txt`.

An initial sandboxed public check was denied `ps`; the final outside-sandbox run
passed. The first daemonized SSH attempt did not survive its tool launcher;
the helper now uses a dedicated launchd job, verified across separate calls.
The existing installed v0.19.1 app was tested and archived while unrelated
rendering work continued in source; that unfinished work is not in this ZIP.

Later the same day, the party client was rebuilt from `af388bf` as **v0.20.0**
with the completed walking changes. Package checks, the remote native network
journey (18 assertions) and movement journey (70 assertions) passed. The installed
copy matched all 217 rebuilt files and passed Launch Services smoke with 58
snapshots. The shared `Blast-Circuit-Mac.zip` now points to that v0.20.0 archive;
the old ZIP was moved out of the download directory. Download checksum and
installation receipts are in `artifacts/online-party/rebuild-020`. Server rules
and protocol were unchanged, so the existing DiveMac service kept running.
