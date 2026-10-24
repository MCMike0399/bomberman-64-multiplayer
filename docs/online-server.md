# Shared online server

Blast Circuit's online service is intended for all users. Players connect to the
same maintained server address for Quick Play or private rooms from any network
that can reach it. A private room is a game feature, independent of the server's
audience. The server runs independently of any player's Mac and owns matches,
rules and CPU players.

The public address is `https://blast.burbuja-lab.dev`. Raspberrypi is the primary
game node; DiveMac keeps a resource-capped idle worker ready for overflow. Both
share matchmaking and room codes. Follow [cluster operations](cluster-hosting.md)
for routing, capacity limits, node updates and the limits of failure recovery.
The [v0.26 delivery receipt](cluster-release-026.md) records the published client,
verified installations, conservative operating caps and measured evidence.

## Release and deploy

1. Run the checks for the changed area in the
   [delivery guide](agents/testing-and-delivery.md). Use a new app version for a
   runtime release; push main and verify CI for that exact source commit.
2. Publish the matching Mac/server artifacts through the
   [signed update pipeline](automatic-updates.md). Existing users get the update
   through Settings; the public release download is the distribution source.
3. Verify the published server ZIP checksum, source/version metadata and runtime
   inventory. Deploy that exact runtime to the established service, preserving
   the current host configuration and a rollback copy.
4. Check for active or waiting rooms immediately before restarting. Restart only
   the owned game service when rooms are empty; retain the previous deployment
   until health and client compatibility checks pass.
5. Verify `/realtime/v1/games`, the deployed runtime hashes and one bounded real
   client lobby/match journey. The managed profile already runs the full
   multi-client/network suite against the staged artifact. Repeat that suite on
   the live host only for a transport, routing or capacity change, or a failure
   that calls for it.

Routine releases finish with the published update and verified shared service.
Local app replacement, Downloads copies, party ZIPs, Wi-Fi peer checks and relay
setup belong only to a task explicitly requesting those operations. A docs-only
correction needs documentation review, with no game build or deployment.

The public lobby-list endpoint excludes private rooms. An empty list alone does
not establish a safe restart. Use the authenticated fleet view, or complete
current-process lifecycle logs covering creation and closure of every room.
If log rotation removed startup/history, fail the idle check rather than assuming
the service is empty. Keep raw operational logs out of Git.

## Hosting and reachability

The portable server requires ASP.NET Core Runtime 10 or .NET SDK 10:

```sh
dotnet Bomber.Server.dll --urls http://127.0.0.1:18496
```

Docker is optional. For the existing single-host Compose deployment,
`tools/prepare-party-server.py` creates a context from the verified portable
artifact; its historical filename does not limit the service to friends.
See [server delivery](ci.md#portable-server-delivery) for the artifact boundary.

The public gateway terminates HTTPS and forwards WebSockets to the owning node
through dedicated game tunnels. Keep host-specific addresses and credentials in
operator configuration. Verify public reachability through that endpoint when
routing changes. An operator SSH tunnel can verify service behavior but cannot
establish public reachability.

The server currently holds rooms in memory. Restarting loses those rooms;
guest reconnect grace does not preserve a process restart. Fleet capacity and
multi-node recovery require their own evidence, as described in the
[architecture](architecture.md#independent-server-and-scaling-limits).

## Player flow

Open **Online Battle**, enter the service's published address and a player name,
then choose **Quick Play**, **Create private room** or **Join room**. Quick Play
needs another compatible queued player. Private rooms wait for their leader;
CPU seats do not start them automatically. The leader can edit Room settings
while waiting, and results return everyone to the same room.

The former same-Wi-Fi deployment is preserved in the
[historical party guide](online-party.md). Consult it only for an explicit LAN
party or relay task; it is not the shared-service release procedure.
