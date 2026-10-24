# Blast Circuit

For development handoffs, start with [AGENTS.md](AGENTS.md) and the [agent guides](docs/agents/README.md): current decisions, debugging, model tuning, tests and delivery pitfalls.

A playable Godot C# arena battle game inspired by Bomberman 64, with original procedural art, 2–8 participants, local play, online private lobbies, matchmaking and server-controlled bots. The featured Sunken Temple arena has a stepped sandstone pyramid, a recessed courtyard, broad stairs and surrounding water. Animated characters, original synthesized action cues, expanding blast clouds, splashes and debris run in the Godot client.

The original game supports four simultaneous players. Eight-player support and the numerical simulation tuning are decisions for this project. See [research and sources](docs/research.md).

## Run the game

Install **Godot 4.7.2 .NET** and the **.NET 10 SDK** (10.0.401 was used for verification). The root project pins `Godot.NET.Sdk/4.7.2` and targets `net10.0`. Use matching Godot .NET export templates when building the standalone desktop app.

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

The game opens fullscreen. **F11** or **Alt+Enter** switches between fullscreen and a window; run `./scripts/client.sh --windowed -- --display-windowed` for a windowed launch.

Choose **LOCAL BATTLE**, then set each of the eight roster slots to **Human**, **CPU**, or **Off** and choose a suit color. The selected character walks on a shared lit 3D stage using the same articulated model seen in battle. Continue to the miniature arena stage and arrow-controlled rules, choose Easy/Medium/Hard CPU difficulty, then start with **2–8 total participants**. **PRACTICE** lets one player explore the map and rehearse bomb handling without CPU opponents. Thirteen expiring training pickups teach bomb capacity, fire, red power, remote detonation, hearts, bouncing bombs, fast bombs Focus and temporary size/speed effects; **RESET PRACTICE** restores the cache. One human can play with up to seven bots. Local play works without a server. Two keyboard seats are available; additional local seats require connected gamepads. With three or more local humans, the first two seats use keyboards and up to six gamepads fill seats 3–8 in connection order. With one or two humans, the corresponding gamepads also work as optional controls.

**Advanced settings** in match configuration controls item drop chance (55% by default), block amount, item lifetime (20 seconds), blast and bomb stack caps (five each), temporary effect duration (10 seconds), and which items may spawn. Every destroyed block rolls independently. Uncollected items expire in battle and practice. Tiny makes you smaller/faster; Big makes you larger/slower; Haste and Slow change only movement speed. A new temporary effect replaces the previous one. Room leaders can set the same rules before starting an online match.


## macOS app

The Apple Silicon app bundles the game, .NET runtime, original assets, and optional Switch 2 USB backend. It runs independently of the Godot editor and source checkout; local play needs no server. The built app is installed at `~/Applications/Blast Circuit.app` on the development Mac. Open it from Finder; normal play starts fullscreen, and **F11** switches to a window.

To remove duplicate builds after verifying the installed version:

```sh
python3 tools/cleanup-macos-builds.py --apply --include-export --expected-installed-version 0.19.1
```

Omit `--apply` to preview the selection. The cleanup keeps the installed app, removes other matching bundles and their Launch Services registrations, and preserves unrelated apps, ZIPs and test receipts.

To rebuild from source, first install the matching **Godot 4.7.2 .NET export templates**, restore the kernel dependency above, and optionally build the USB adapter. Then run:

```bash
python3 tools/package-macos.py
open "artifacts/exports/Blast Circuit.app"
```

The script creates a project-local ARM64 template from the official Universal 2 template, exports the managed dependency graph, adds controller libraries and notices, and verifies the local signature. It rejects exporter errors even if Godot returns exit code zero. The app has a local ad-hoc signature; it is not notarized for public distribution. App bundles and export templates stay outside Git. [Packaging details and verification](docs/macos-app.md).

## Play online

Open **Online Battle** and use `https://blast.burbuja-lab.dev` for Quick Play or private rooms. The shared cluster runs independently of any player's Mac, with the Pi as primary and an idle DiveMac worker for overflow. `Bomber.Server` registers the `blast` game on top of `Burbuja.Hosting`. See [shared-server hosting and releases](docs/online-server.md) and [cluster operations](docs/cluster-hosting.md).

For local development, start that independent server in another terminal:

```bash
./scripts/server.sh
```

It listens at `http://127.0.0.1:18496`. Enter that URL in the game and choose **ONLINE BATTLE → CREATE PRIVATE ROOM**, then use **Copy code** to share the lobby’s join code. Another game instance can enter the same URL and use **JOIN ROOM**. The room waits until its leader chooses **Start game**, including when CPU rivals are enabled. For a human-only match, set CPU players to zero. Each online game instance controls one human seat. After the results, everyone returns to the same room and code; the leader starts the next match.

**QUICK PLAY** uses the kernel matchmaking queue. It needs another compatible queued player; use a private lobby for solo bot practice online. The setup screen configures stage, CPU count and difficulty, win target, round duration, sudden death and ghosts. The private lobby displays the real server roster and its leader. The leader can open **Room settings** while waiting to change the match rules without leaving the room; guests receive the accepted rules. If the leader disconnects, leadership transfers to another connected player. Type your name in **Your name — click to edit**. The name and server URL are saved automatically between launches. Your chosen name appears above your bomber and on the HUD; unnamed players use P1, P2, and so on. Choose your suit in the online setup; the authority preserves that appearance through rounds and reconnects.

For a LAN server, bind to a reachable interface and enter the server machine's address on each client:

```bash
./scripts/server.sh --urls http://0.0.0.0:18496
```

The development server binds to loopback; the shared public service is available at `https://blast.burbuja-lab.dev`. Native desktop C# is the target; this project does not provide a browser build.

Normal battle defaults to **one minute of normal play + one minute of sudden death**, with **first to two round wins** taking the match. Room settings remain configurable. Temple water starts rising as soon as sudden death begins. Before sudden death, falling into water returns you to dry ground as a ghost when ghost comeback is enabled.

## Controls

| Action | Keyboard 1 | Keyboard 2 | Xbox | Switch 2 | PS5 |
|---|---|---|---|---|---|
| Move | WASD | Arrows | Stick / D-pad | Stick / D-pad | Stick / D-pad |
| Bomb / kick / pump | J | N or Enter | A | A or B | Cross |
| Hold carry / release throw | K | M or Numpad 1 | LB or X | L or Y | L1 or Square |
| Precision aim while carrying | L (Shift alias) | Ctrl | RT | ZR | R2 |
| Remote detonation | I or R | Comma or Backspace | B | X | Circle |
| Stop kicked bombs | O | Period or Numpad 3 | RB | R | R1 |
| Game menu | Esc | Esc | Menu | + | Options |

**Ready, aim, throw:** keep your left hand on WASD and your right hand around J/K/L. Hold K near a bomb to lift it, or hold K and tap J to create one overhead. Keep K held and tap J to pump. Hold L and use WASD to move the landing target in the same direction on the arena. Small nudges start slowly; sustained input sweeps faster. Release K to throw; I detonates remote bombs. Shift also aims and R also detonates. On controller, hold L/LB/L1 to carry and tap A/A/Cross to ready or pump. Hold ZR/RT/R2 and use either stick or the D-pad to move the target; release carry to throw. Switch Y, Xbox X and PlayStation Square remain face-button carry alternatives. On Switch you can hold Y and tap the adjacent B to ready or pump a bomb; A also works, and X stays remote detonation. Releasing the aim modifier lets you reposition while keeping the selected direction and range. [Controller and keyboard design research](docs/iteration19-controls-research.md).

**Stop a kick:** O (keyboard 1), period / numpad 3 (keyboard 2), or the right shoulder stops bombs you last kicked. Fuses keep running; a falling bomb still falls. Remote detonation stays on its face button. The original N64 R stop control is confirmed in the [Nintendo manual](https://manuals.plus/m/a8e88edc86a9ce47e982bda2351d4c7d9fe8c079505b31257b5768b4c5e9907e).

Deliberate aim covers 0–9 cells on level ground, with a high lob that can reach upper terraces. The target can cross through your position to aim behind you; a close self-lob can hit you. The outlined reticle stays visible over scenery: a ring marks a landing, an X marks a blocked or unsafe endpoint, and an edge arrow points toward an endpoint outside the frame. It has no added text labels. The prediction accounts for terrain height, cover, walls and fuse expiry; moving opponents can still interrupt the flight. Pumping increases blast power without shifting an aimed landing. The Focus ability charges faster and gently nudges a neutral aiming cursor toward a nearby visible opponent. Any deliberate stick/key adjustment takes priority; it never hard-locks a target. [Reticle diagnosis](docs/iteration17-reticle.md); [original manual controls and project choices](docs/iteration16-controls-research.md).

Menus accept **WASD or arrow keys**, with Enter to confirm and Esc to go back; text fields still accept letters normally. **Start / Menu / + / Options** focuses the primary action in menus, including **Start game** for the lobby leader. During play, Esc or Start opens **Resume game / Settings / Leave game**. Local play pauses; online play continues with your inputs released.

**Settings** is available from the main menu, room and game menu. Sound volume and controller vibration are saved on this Mac. Vibration is **off by default**; enable it and use **Test vibration** to check your controller. Supported controllers receive brief feedback for accepted bomb placement, pumping, kicks, impacts and deaths.

From v0.21, **Settings → Game updates** checks for signed GitHub releases automatically. When a new version is ready, leave your room and choose **Update and restart**. A failed launch restores the previous app. Older versions need one manual upgrade to get this feature. [Update and release details](docs/automatic-updates.md).

Keyboard 2 also supports numpad 0 / 1 / 2 / 3 for bomb / carry / remote / stop. Switch uses its **printed A to confirm and B to go back**. Xbox/PlayStation retain bottom-button confirm and right-button back, with matching prompts. F11 or Alt+Enter toggles fullscreen.

Choose **CONTROLS & CONTROLLER TEST** on the main menu to inspect connected pads and live inputs. Face buttons stay in the tester; Menu / + / Options or Esc leaves it. The roster shows which pad accompanies each human seat. Two-human matches allow one pad per player alongside the two keyboard layouts; larger matches reserve the first two humans for keyboards and assign pads to the remaining humans.

All devices run at the same maximum running speed, including diagonal keyboard movement. Stick direction is continuous through 360 degrees. Character rendering now interpolates between authoritative snapshots rather than repeatedly catching up to each one, and the stride/turn animation follows that smooth motion. Sticks retain their analog direction but use full running speed beyond the **20% radial deadzone**. Precision aim preserves stick magnitude for fine adjustment; keyboard taps use the same slow-start ramp and all devices share a six-cell/second cursor limit. While aiming, an active right stick takes priority; otherwise D-pad input takes priority over the left stick, then keyboard. Directions are never added together. Short bomb, remote and stop taps are buffered, holding bomb does not auto-repeat, and actions must be released after entering a match or returning focus. Disconnecting a pad reserves its seat without moving another player's controls. Identical controllers that both reconnect under ambiguous identities require restarting the match to assign them safely.

### Switch 2 USB on macOS

The optional adapter is built locally for the Switch 2 Pro controller that Godot's bundled backend cannot expose:

```bash
./tools/controllers/bootstrap-sdl.sh
./scripts/client.sh
```

It downloads checksum-pinned SDL 3.4.18 and libusb 1.0.29, builds inside ignored project directories, and places the runtime libraries in `native/macos/`. Xcode command-line build tools and Python 3 are required. It does not install a system driver. This adapter currently supports **one Switch 2 Pro over USB** at a time; additional players can use Xbox, DualSense, other mapped pads or keyboards. Xbox and DualSense continue through Godot's normal backend. Rebuild/restart the game after installing the adapter. The macOS packaging script includes these native libraries and their licenses when the adapter is installed locally. The exported app loads them directly from its bundle; physical-input evidence remains separate from device enumeration.

**Switch 2 Bluetooth is not implemented by this adapter.** Wireless software bridges exist, but none was installed or validated in this iteration. [Controller research](docs/controller-research.md) explains the USB issue, positional button labels, wireless distinction and dependency provenance. Detection and synthetic tests are reported separately from physical button observation in [verification](docs/verification.md).

Left-stick menu navigation uses deliberate threshold crossings and repeat while held, including setup, lobbies and results. It accepts gradual pushes, stabilizes diagonal choices, and changes direction immediately on reversal. The Switch 2 USB path is tested through the same native event bridge used during play; [physical versus synthetic evidence](docs/verification.md) remains explicit.

Tap the bomb button to drop a bomb **near your feet**. You can walk away and leave it stationary, including while laying a trail with separate taps. A fresh second press while beside the bomb deliberately kicks it. Hold the original placement press while standing still for a quarter-second to lift that same bomb; release, then tap again to pump. Press carry to throw the A-lifted bomb. On Switch, hold A or B after placement to lift; tap the primary button to pump, then press L or Y to throw. The older hold-carry pickup and release-to-throw path also works, as does carry + bomb to create a bomb overhead. Once clear of your own new placement, walking back into a resting bomb triggers one kick and sends it down the chosen continuous direction faster than running speed; it is not a gradual push. Normal fuses resume after throwing. Remote bombs wait for the remote command, with an explicit HUD hint.

Explosions use spherical distance and cover occlusion rather than a classic grid cross. Pickups increase blast size and bomb capacity, equip a bomb type, or grant one player ability. Ghosts and sudden death are enabled by default. Ghosts pass through bombs and cannot place, carry, kick, stop, charge, throw or detonate them. A latched ghost rides visibly on top and adds limited steering interference; the living player retains control and can shake it off. Walking off a high terrace starts a gravity fall to lower terrain; upward cliffs and solid walls still block movement. With one minute left, the Pyramid-inspired Sunken Temple warns of rising water; its lower court floods before the upper terraces. Other arenas retain closing columns. Permanent stone and terrain block blasts; red bombs penetrate breakable cover. Last survivor earns a round win; first to the win target wins the match.

## Equipment and charging

Bomb capacity and normal blast strength stack to five by default; both caps are configurable.
One bomb-type slot holds red, remote, bouncing or fast bombs; a separate ability
slot holds a protective heart or Focus. Picking a replacement drops the previous
equipment for other players. The old holder cannot immediately reclaim it, and
the dropped item disappears after the configured lifetime (20 seconds by default).
Duplicate equipment and capped upgrades remain available for others until expiry.

Red bombs produce blue explosions with 25% more radius and penetrate breakable
cover. Blue remote bombs use command indicators with no burning fuse. Purple
bouncing bombs roll normally, ricochet off walls, and rebound on the ground after
a charged throw. Stop still halts kicked bombs. Mint fast bombs travel 50% faster. Their
travel never resets the fuse. Normal charging takes 0.45 seconds per level;
Focus takes 0.15. Each tap queues one level, up to three. Throwing early uses only
completed levels. Larger charged bombs have stronger gravity and a much smaller
head-hit rebound; the reticle uses the same flight rules.

Sunken Temple now spans 25×27 cells, with wider platforms, a fifth raised tier,
a larger courtyard, more breakable cover and open water pitfalls behind the
summit. Most of its cover is concentrated in lower farming pockets, with clear
stair and crossing lanes. Rock Garden is 23×15 with pillar-area clusters; Up &
Down is 23×19 with cover on broad platforms and outer pockets. Layouts vary by
seed while preserving those areas and safe openings. Fire and Bomb upgrades each
make up 30% of the default drop pool; equipment and temporary effects share the remaining 40%.
[Original-game layout observations and project tuning](docs/iteration23-block-distribution-research.md).

## Knockdowns and player grabs

Contact starts a short planted kick: about 0.13 seconds of anticipation, then a strike and about 0.17 seconds of gradual return to running. The bomb travels at 12 cells/second, with no short distance cap, and keeps momentum when falling from a terrace onto lower ground. Walls still block it. A hit causes a low hop and short stumble before finite landing recovery; following your own outgoing kick no longer hits you from behind. Falling bombs can also knock players down. A visible hit message, stars and **DOWN / HELD** status explain what happened. Bomb impacts grant only a brief 0.35-second contact cooldown: a later hit can chain another knockdown, and explosions remain lethal during knockdown, holding and recovery. Hearts and practice respawns retain their separate damage protection.

Alternate movement directions or tap the action buttons to recover faster. Holding one direction/button does not repeatedly count as effort. Hold **carry** near a downed opponent to pick them up, walk with them, and release carry to throw them through an arc. The victim can wiggle/tap to escape; an inactive hold also times out. Both controller and keyboard inputs use the same authoritative recovery rules, and bots recover through those inputs too. Disconnects release held players safely.

These mechanics run in the shared local/server simulation. Rebuild the independent game server along with the client for the new state and presentation events. [Rules, failure reproductions and input fixes](docs/iteration8-gameplay-input.md).

Players now meet through height-aware body contacts: opposing runners block, glancing contact preserves sideways movement, and idle opponents yield slowly instead of becoming an immovable barrier. Movement uses a 100 ms snapshot buffer to absorb uneven arrivals without changing input speed or direction. This adds roughly 33 ms of visual delay over the previous interpolation; it does not predict network movement.

Normal and pumped bombs keep their timed fuse when stopped in a corner. Active remote bombs still wait for the owner’s command, including while moving. If the owner leaves, dies or loses the ability to detonate, the abandoned remote resumes its remaining ordinary fuse. [Kick research, timing policy and regression evidence](docs/iteration10-kick-research.md).

Water contact uses the actual visible water height. Merely grazing the shoreline with the side of the player’s body is safe while the feet remain on land; stepping off starts a fall, and contact with the water remains lethal. [Original footage comparisons and iteration-eleven tuning](docs/iteration11-kick-research.md).

[Earlier movement, bombs, traversal and AI fixes](docs/iteration9-gameplay.md).

## Battle progression

Breakable stone objects can drop weighted upgrades. Fire and bomb upgrades stack to five by default; advanced settings controls both caps. Red power penetrates breakable cover. Blue remote bombs with green indicators detonate on command. Picking up either special upgrade replaces the other for newly placed bombs. Existing red bombs retain their original fuse; an existing remote resumes its normal fuse if its owner switches to red and can no longer command it. One heart absorbs one blast without stacking. Water remains lethal. Equipment resets between rounds.

Three accepted pump presses add three effective blast levels, reaching level 8 with the default maximum of five fire stacks. The default bomb is larger for readability, but pumped visual growth is restrained: three pumps are about 1.8× the default size, with a visual target cap of 2.3. This does not change blast strength. Pickups are also larger and the camera fits the stage more closely. Timed bombs pause their fuse while carried and resume it after release. Pumping adds reach to ordinary throws; deliberate aim keeps the chosen range. While a local player carries a bomb, a dotted arc shows the shared static trajectory and landing point; water and void endpoints instead show where it sinks or falls away. Moving players and bombs can interrupt that forecast. Neutral throws now clear the carrier, and the released model starts at hand height instead of dipping toward their helmet. [Carry/throw diagnosis and evidence](docs/iteration15-charge.md). These numerical choices and the flood curve are project tuning; [manual and gameplay-video research](docs/gameplay-research.md) distinguishes confirmed observations from assumptions.

The original manual establishes increased destructive power for red bombs, but the inspected sources do not establish a separate radius multiplier at identical fire and pump levels. This build keeps radius tied to those levels and gives red its cover penetration. [Placement, color and red-power research](docs/iteration12-bombs-research.md).

Sunken Temple gives each spawn area nearby cover to open and a useful fire or bomb reward from its first assigned crate. Later crates use the varied drop pool. Newly revealed rewards survive their revealing explosion, and their icons are larger above the existing pickup plates. [Normal-match opening journey and verification](docs/iteration17-overview.md).

Bots pursue useful upgrades, pressure reachable opponents, predict visible fuse/chain danger, plan an escape before placing bombs, and seek high ground before flooding. Easy, Medium, and Hard change reaction time, search depth, spacing, bomb cadence, and escape-route awareness. Easy gives more room but still periodically attacks; Hard applies more sustained pressure and avoids contested dead ends more strongly. Every level shares the same speed, damage and visible information. Progress tracking and body-aware movement prevent repeated blocked routes from becoming permanent freezes. See the [AI behavior tests and measured tradeoffs](tests/Bomber.Ai.Tests/README.md).

## Verification and CI delivery

Local development and CI use the same orchestrator:

```bash
python3 tools/qa/run.py --profile public
python3 tools/qa/run.py --profile managed
python3 tools/qa/run.py --profile headless
python3 tools/qa/run.py --profile graphical
python3 tools/qa/run.py --profile package
```

`public` works without private kernel access; the other profiles require the pinned dependency. Managed verification tests the exact staged portable server before archiving it. Graphical verification exercises the real windowed game and records viewport evidence. Package verification builds the Apple Silicon app with the Switch 2 USB backend and runs its embedded game through headless smoke checks. JSON/JUnit receipts, failure triage and scenario provenance make incomplete or stale runs fail explicitly.

[GitHub Verification](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/workflows/ci.yml) runs public contracts on PRs and protected dependency builds on trusted main. Successful builds deliver separate Mac client and portable server artifacts. A manual workflow validates both artifacts before preparing a draft release. Native Metal CI requires an isolated graphical runner and is currently disabled; local graphical QA remains required for visual changes. [Pipeline configuration, artifacts and deployment boundaries](docs/ci.md).

## Repeatable playtests and triage

The agent harness plays through the actual Godot scene using inputs and product sessions. Its shared [scenario catalog](assets/testing/scenarios.json) drives both C# dispatch and Python validation; `--list` shows every scenario and capability:

```bash
python3 tools/playtest/run.py --build --start-server --scenario all
python3 tools/playtest/run.py --scenario fullscreen
python3 tools/playtest/run.py --scenario controllers
python3 tools/playtest/run.py --scenario controller-observe
python3 tools/playtest/run.py --scenario ramp --headless
python3 tools/playtest/run.py --scenario script --script tools/playtest/temple-walk.json
```

Graphical harness runs and `./scripts/debug.sh` default to **windowed mode**. Normal `./scripts/client.sh` play still opens fullscreen. The suite exercises real menu controls, enters matches, moves through stairs, handles bombs, collects upgrades, survives flooding, reaches results and connects to the real server. Use `--fullscreen` to explicitly test fullscreen; `--scenario fullscreen` requests it directly. The normal windowed suite stays windowed through gameplay and aspect-ratio checks.

Runs save screenshots, authoritative state traces, assertion results, engine logs and failure summaries in unique `artifacts/playtest/` folders. Failed assertions, timeouts and missing reports return a failing exit code. Graphical captures show real viewport pixels; headless captures are explicitly disabled. See [harness instructions](tools/playtest/README.md) for scenario scripting and diagnosis.

## Verify

[Current walking, keyboard and installation changes](docs/iteration18-walking.md) cover this iteration. [Earlier reticle and opening-reward changes](docs/iteration17-overview.md) remain documented. [Verification results and captured evidence](docs/verification.md) record the broader checks.

The checks below are executable integration tests, run with `dotnet run`:

```bash
dotnet run --project tests/Bomber.Game.Tests/Bomber.Game.Tests.csproj
dotnet run --project tests/Bomber.Client.Tests/Bomber.Client.Tests.csproj
dotnet run -c Release --project tests/Bomber.Ai.Tests
dotnet run --project tests/Bomber.Audio.Tests
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

It starts a local match and exits after eight seconds with `GODOT_SMOKE_OK`. `--smoke-tier`, `--smoke-menu`, and `--smoke-online` exercise the second stage, menu, and running server. Add `--smoke-seconds=90` for a longer run. A graphical `--smoke` run writes `artifacts/gameplay.png` in the editor or `user://captures/gameplay.png` in the exported app. Capture failures fail the smoke run. These automated checks do not establish controller coverage, internet latency tolerance or final gameplay balance.

## Code and dependencies

[Architecture](docs/architecture.md) describes the boundaries, protocol and scaling path. The repository consumes a separately supplied burbuja-kernel checkout at `burbuja-kernel/`, revision `64ec8db830bb058de8ba2aa92b6f195165cd06d4`. Build and runtime use source project references for the kernel. The product now owns its adapted simulation in `src/Bomber.Game/Simulation`, retaining MIT provenance, so map and collision changes do not mutate the generic kernel. Preserve the kernel checkout and its license.


## macOS Game Mode

The game already uses native Metal. The `macOS Metal` export preset declares Apple’s `LSSupportsGameMode` opt-in and Games bundle category. Game Mode is controlled by macOS during eligible fullscreen play; it is not enabled by windowed debug tests, and no performance boost is claimed without measurement. [Setup, primary sources and verification limits](docs/macos-game-mode.md).

## Character, UI and effects

The current menu is a live 3D theatre: characters share lighting and floor shadows, mode plaques have solid bevels and selection motion, and the camera has restrained parallax. The eight-seat roster uses a green stage with short MAN/COM/OFF controls; the winner celebrates on a lit podium in a dimensional arcade theatre, with a physical standings board, metallic medals and raised replay buttons. The helmet is softly oval (slightly shorter vertically, with fuller front-to-back roundness and the same width), centered slightly forward to balance its rear volume, with a separate rounded faceplate and a softer neck connection. The compact pear torso, short relaxed arms and full pink boots retain the approved proportions. The suit is a little slimmer while retaining the large helmet, gloves and boots. Short, fixed-length legs stay attached at the hips. Bounded eased steps remove the earlier recovery overshoot, and boot counter-rotation keeps the soles rolling gently through the stride. The waist transfers weight, shoulders turn independently, opposite arms follow each foot, and the head counterbalances the motion. A slower distance-driven cadence leaves more time for each step without slowing the player. [Current gait checks and recording](docs/iteration18-walking.md). The head retains its softly oval proportions. [Character references and measured corrections](docs/iteration16-character.md). Stopping settles the pose without extra steps or changing gameplay speed. [Walk tuning, original-footage observations and regression tests](docs/iteration15-walk.md).

Match setup now displays an actual lit miniature of the selected arena and a native arrow-controlled rules rail.

The 2D interface shares one stamped-key language: ink outlines, a solid ink lip under each key face that collapses when pressed, a gold ring for keyboard and gamepad focus, and drawn chevrons instead of glyph arrows. Dela Gothic One sets titles, buttons, values and the timer; M PLUS Rounded 1c sets labels and running text. Input prompts render as keycaps. In battle, the round timer is a fuse clock at top centre: its fuse burns toward the bomb as time runs out, and during Hurry Up the bomb turns red and shakes. Participant tokens flank it with helmet portraits, win stars, bomb/fire counts and power-up icons, and show Dizzy, Held, Ghost and Out states. Icons are original vector drawings. The Pyramid-inspired arena now has a centered lower camera, weathered sandstone with responsive relief, engraved fascia, preserved irregular courtyard cobbles, a submerged bedrock foundation, and lower-intensity lighting. Water combines four modulated directional waves, analytical normals, reconstructed depth tint and shoreline foam; [rendering techniques and limits](docs/iteration8-rendering.md). TUNIC and Pokémon BDSP informed the separation of lit tops, shaded sides and readable toy-like forms. [Design study, inspected official images, and skills research](docs/iteration7-design.md); [original-game footage and timestamps](docs/iteration6-research.md).

Small, medium and large blasts have distinct billow counts, growth/fade timing and sound bodies. A full-radius boundary marks the active blast; the longer vapor tail is cosmetic. Water eliminations produce ripples and a splash plume, while accepted actions drive the character's kick, lift, pump and throw reactions.

**19 original synthesized clips** cover bomb placement, kicks, lifts, three pump charges, throws, three blast sizes, pickups, deaths, water, hurry, round transitions and menu confirmation/back. A bounded authoritative event history and client cursor prevent repeated snapshots from replaying cues; joining or reconnecting establishes a silent baseline. Sound effects are new compositions, not ripped samples or a claimed exact audio reproduction.

[Download a captured game-engine action mix](docs/audio/actions-mixed.wav). This is the automated input sequence, recorded from the game's output bus without microphone input. Individual generated WAVs can be exported locally:

```bash
dotnet run --project tests/Bomber.Audio.Tests -- --export artifacts/audio/iteration6
python3 tools/playtest/run.py --scenario presentation
```

## Screenshots

The gallery contains real exported-app captures. Controls and aiming/reticle images are refreshed for v0.19.0; model, other gameplay images and walking clips retain their v0.18.0 receipts. The walking clip shows side and three-quarter inspection views followed by the ordinary gameplay camera. Other historical clips retain their documented versions. The bomb-trail closeup uses a temporary inspection camera on actual actors. The controller tester uses explicitly labeled synthetic QA devices; it does not establish new physical button testing.

[Historical precision-aim and charged-throw demo](docs/videos/precision-aim.mp4) — v0.16 practice gameplay at normal speed with the ordinary battle camera. It uses the earlier turn/range controls; v0.19 moves the target in arena directions. Pumping and release-to-throw behavior remain the same.

![Choosing a charged bomb's landing point](docs/images/precision-aim.png)

[Watch the clean reticle checks](docs/videos/throw-reticle.mp4) — ordinary local keyboard play, separate renderer outcome checks, then a real match ending while the winner still carries. No reticle text labels.

![Visible endpoint over stock temple cover](docs/images/throw-reticle.png)

![Larger reward icon after breaking temple cover](docs/images/opening-loot.png)

[Watch the compact chibi walking animation](docs/videos/walk-personality.mp4) — normal-speed movement, with side and three-quarter inspection views followed by the normal battle camera. [References and fixed-leg continuity checks](docs/iteration16-character.md); [current gait and focused checks](docs/iteration18-walking.md).

[Watch Easy, Medium and Hard in actual gameplay](docs/videos/ai-difficulties.mp4) — three labeled six-second excerpts, normal simulation speed and game audio. [AI behavior and test results](docs/iteration13-ai.md).

[Watch near-foot placement and leaving bombs behind](docs/videos/near-foot-placement.mp4) — actual v0.12.0 gameplay at normal simulation speed, with game audio. [Recording receipt and measurements](docs/verification.md). The [earlier kick/stumble clip](docs/videos/kick-and-stumble.mp4) documents v0.11.0.

![Sunken Temple battle](docs/images/temple-battle.png)

![Leaving a stationary bomb trail](docs/images/bomb-trail.png)

![Blue remote bomb after walking clear](docs/images/blue-remote.png)

![Mode selection with WASD navigation](docs/images/mode-select.png)

![Easy Medium Hard selection](docs/images/ai-difficulty-menu.png)

![Easy CPU match](docs/images/ai-easy.png)

![Medium CPU match](docs/images/ai-medium.png)

![Hard CPU match](docs/images/ai-hard.png)

![Animated eight-seat roster](docs/images/battle-roster.png)

![Native arena and rules setup](docs/images/match-setup.png)

![Private room settings](docs/images/room-settings.png)

![Controller tester](docs/images/controller-test.png)

![Small blast](docs/images/blast-small.png)

![Charged blast](docs/images/blast-large.png)

![Actual winning character](docs/images/battle-results.png)

The v0.16.0 model inspections below show the softly oval helmet on the actual in-game character with a temporary close-up camera:

![Character front](docs/images/model-front.png)

![Character side profile](docs/images/model-side.png)

![Curved separate faceplate](docs/images/model-three-quarter.png)

![Carrying a downed opponent](docs/images/player-grab.png)

![Player throw arc](docs/images/player-throw.png)

![Planted anticipation before the strike](docs/images/kick-windup.png)

![Contact kick launching a bomb](docs/images/kick-launch.png)

![Low nearby stumble after a kicked-bomb hit](docs/images/kick-impact.png)

![Kicked bomb continuing across the lower courtyard](docs/images/kick-courtyard.png)

![Falling below the shore before water contact](docs/images/shore-fall.png)

![Player bodies meeting on the temple walkway](docs/images/body-contact.png)

![Falling from an upper terrace](docs/images/ledge-fall.png)

![Ghost riding a living player](docs/images/ghost-rider.png)

The game uses original procedural visuals. No original game ROM, Nintendo/Hudson artwork, footage, kernel source, credentials, or playtest traces are distributed here. The adapted simulation retains its upstream MIT notice in `src/Bomber.Game/Simulation/LICENSE`.


Current bomb and flood checks:

![Charged throw with an advisory landing arc](docs/images/charge-aim.png)

![Pyramid-inspired lower courtyard flooding](docs/images/rising-water.png)


## License

The game is licensed under the [MIT License](LICENSE), copyright 2026 MCMike0399. The adapted simulation retains its upstream MIT notice in `src/Bomber.Game/Simulation/LICENSE`. The bundled interface fonts remain under the SIL Open Font License; see [docs/licenses](docs/licenses/README.md). The separately supplied kernel dependency and reference media are not bundled or relicensed by this repository.
