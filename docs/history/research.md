# Bomberman 64-inspired multiplayer: implementation research

Verified 2026-10-19 against publisher manuals and engine documentation. This project is an original implementation inspired by the 1997 game; eight-player support and numerical tuning are project decisions.

## Original mechanics verified by the publisher

Nintendo hosts the original Hudson instruction booklet. Page references below are printed pages, with PDF page numbers in parentheses. [Original manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_FR.pdf).

- Controls, pp. 6–7 (PDF 4): analog stick controls direction and speed; A places/kicks bombs and pumps a held bomb into a mega bomb; B lifts/throws bombs or stunned enemies; holding B while pressing A creates a held bomb. R stops a kicked bomb; Z detonates remote bombs.
- Battle, pp. 12–13 (PDF 7): mix human and computer players, with three computer difficulty levels. Single Battle rewards the last survivor. Original simultaneous player count is four. Round count, time limit, ghosts and sudden death are configurable. Ghosts cannot place bombs but can push living players. Sudden death starts one minute before time expires.
- Camera, p. 10 (PDF 6): battle camera is fixed.
- Items, pp. 14–15 (PDF 8): flame upgrades blast size; bomb upgrades simultaneous capacity, capped at eight. Normal bombs have three size levels, mega bombs six. A heart absorbs one explosion and cannot stack. Remote bombs detonate on command.

Source: [Nintendo-hosted manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_FR.pdf). The publisher also lists simultaneous multiplayer for 1–4 players on its [product page](https://www.nintendo.com/en-gb/Games/Nintendo-64/Bomberman-64-1204884.html).

The manual does not define fuse duration, collision dimensions, blast propagation equations, occlusion, movement acceleration or network behavior. Continuous planar movement in a 3D arena, spherical distance-based blast volumes, chain reactions, destructible-cover behavior and all numerical constants must be treated as deliberate implementation choices here. Do not describe tuned values as reverse-engineered originals.

## Godot and C# baseline

Godot 4.7.2 is the latest stable release listed by the official archive on the verification date; 4.8 is beta. Pin editor, export templates and Godot.NET.Sdk to the same release. The existing project declares Godot 4.7. [Official archive](https://godotengine.org/download/archive/), [4.7.2 release download](https://godotengine.org/download/archive/4.7.2-stable/).

Use the .NET-enabled editor and separately installed .NET SDK. The 4.7 C# prerequisites still state .NET 8 or later, and .NET 9 or later for Android export. Desktop net8.0 is a conservative project target; an installed newer SDK may build it. C# Godot 4 projects cannot export to the web; native desktop is the appropriate deliverable. [Godot C# prerequisites and limitations](https://docs.godotengine.org/en/4.7/tutorials/scripting/c_sharp/c_sharp_basics.html).

## Authoritative kernel integration

Godot explicitly states its high-level MultiplayerAPI protocol is an implementation detail, can change without notice, and is not intended for non-Godot servers. Therefore use an explicit application protocol over raw WebSocketPeer for the ASP.NET burbuja-kernel server. Do not assume WebSocketMultiplayerPeer/Godot RPC interoperates with the kernel. [MultiplayerPeer protocol warning](https://docs.godotengine.org/en/4.7/classes/class_multiplayerpeer.html).

The raw client calls ConnectToUrl, Poll each frame, checks GetReadyState, drains GetAvailablePacketCount/GetPacket in the open state, and sends JSON with SendText. Continue polling during closing for a clean shutdown. [WebSocketPeer API](https://docs.godotengine.org/en/4.7/classes/class_websocketpeer.html).

WebSockets use TCP; reliable ordered delivery can increase latency in fast games. Godot normally recommends transport choices that permit disposable position updates. Since the kernel uses WebSockets, the practical project recommendation is bounded queues and latest-state snapshots instead of growing input backlogs. [WebSocket transport characteristics](https://docs.godotengine.org/en/4.7/tutorials/networking/websocket.html), [Godot networking overview](https://docs.godotengine.org/en/4.7/tutorials/networking/high_level_multiplayer.html).

Recommended project architecture (design decisions, not claims about the original): share a pure C# fixed-step simulation between local practice and server; run bot decisions inside the authority; accept sequenced movement/actions rather than client positions; let the authority own bomb creation, fuses, blast hits, pickups, eliminations, round wins and participant ownership. Send periodic versioned snapshots to clients and interpolate visual positions. Configure 2–8 total slots with a mixture of humans and bots; local play runs the same simulation without a remote transport.
