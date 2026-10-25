# Input, menus, aiming and UI

Read this guide before changing bindings, device sampling, menu navigation,
controller prompts, throw previews or shared UI primitives. It records decisions
that source alone cannot explain. Source and the current README remain the
lookup for exact values; older iteration documents retain historical evidence.

## Current controls and compatibility

The main menu now includes Tutorial. [Tutorial navigation](../tutorial.md) owns device-aware lesson copy, saved stars, explicit retry/next, pause actions and returning to lessons. Keep Practice available for battle arenas.

The current right shoulder stops kicked bombs; it no longer aliases remote detonation.
Keyboard R remains a remote alias. The iteration19 controller mapping supersedes printed-B carry and promotes the
left shoulder; keyboard remains v0.18. The v0.18 keyboard mapping supersedes the Space-carry/K-aim/L-remote mapping in v0.17
and earlier positional Nintendo mappings. The user requested keyboard-only play
with both hands on keys and restored K carry because Space felt awkward.

| Action | Keyboard 1 | Keyboard 2 | Xbox | Switch printed labels | PlayStation |
|---|---|---|---|---|---|
| Move | WASD | Arrows | Stick / D-pad | Stick / D-pad | Stick / D-pad |
| Bomb, contextual kick, pump | J | N / Enter / Num 0 | A | A / B | Cross |
| Hold carry, release throw | K | M / Num 1 | LB / X | L / Y | L1 / Square |
| Precision aim | L / Shift | Ctrl | RT | ZR | R2 |
| Remote detonation | I / R | Comma / Backspace / Num 2 | B | X | Circle |
| Stop kicked bombs | O | Period / Num 3 | RB | R | R1 |
| Menu confirm / back | Enter / Esc | Enter / Esc | A / B | A / B | Cross / Circle |

v0.19.1 adds printed Switch B as a primary/bomb alias so **hold Y + tap B**
readies and pumps without reaching across to A. Both held state and sub-frame
taps use the alias; B remains menu Back, X remains remote, and A still works.

Space has no gameplay action. F11 or Alt+Enter toggles fullscreen.
Keep both held-state sampling and short-press detection consistent when changing
bindings: [PlayerControls](../../scripts/input_devices/PlayerControls.cs) owns keyboard states,
`BombKey` and `RemoteKey`; [ControllerProfile](../../scripts/input_devices/ControllerProfile.cs)
owns family semantics and prompt labels. Update the README, controller panel,
HUD hints and native tests together.

The positional action table in early controller research is historical; use
the current table above and `ControllerProfile` for product actions.
Godot `JoyButton` identifiers are canonical physical positions, not printed
Nintendo letters: `A` is south/printed B; `B` is east/printed A; `Y` is
north/printed X. Translate actions through `ControllerProfile`, preserving the
backend's canonical IDs. Nintendo confirms with printed A and backs with B.

## Input path and fairness

- [ControllerHub](../../scripts/input_devices/ControllerHub.cs) discovers devices,
  publishes native input, tracks active prompt family/keyboard seat, guards focus,
  and combines held state with bomb/remote/stop press edges through `Read`.
- [MatchControllerBindings](../../src/Bomber.Client/MatchControllerBindings.cs)
  fixes device seats during a match; [LocalControllerLayout](../../src/Bomber.Client/LocalControllerLayout.cs)
  handles the keyboard/gamepad layout. Consult these before changing hotplug.
- [InputPolicy](../../src/Bomber.Client/InputPolicy.cs) resolves D-pad, stick and
  keyboard directions without summing device speed. Ordinary movement normalizes
  nonzero direction after radial stick drift rejection; diagonals gain no speed.
- [GameRoot](../../scripts/app/GameRoot.cs) sends local commands to
  [LocalSession](../../src/Bomber.Client/LocalSession.cs) or
  [NetworkSession](../../src/Bomber.Client/NetworkSession.cs).
  Both adapters retain action edges, including Aim, for authority consumption.

Precision aim preserves partial analog strength; ordinary running stays equally
fast for keyboard, D-pad and partial/full sticks. If Aim is held without a bomb,
the ignored stance must still normalize any nonzero movement at the authority.
Preserve speed, aim caps and command semantics across local/network/device paths.

## Carry and aim recipe

1. Start Practice or a normal local match. Hold K near a ground bomb to lift it,
   or hold K and tap J to create one overhead. Keep K down throughout this recipe.
2. Tap J while carrying to pump. Hold L to plant the carrier in precision stance.
3. Use WASD to move the target in arena directions. Short nudges begin slowly;
   sustained same-direction input accelerates. Controllers hold ZR/RT/R2 and use
   either stick or D-pad; promote L/LB/L1 for carry so the thumb stays free.
   Active right-stick intent wins while aiming, then D-pad/left stick/keyboard.
4. Release L while retaining K: the bomb stays overhead, the chosen aim persists,
   and WASD moves again. Re-enter L to adjust. Release K to throw.
5. I or R detonates owned remote bombs. Shift is an alternative aim modifier.

A separate primary-button flow exists: tap J to place, keep the original press
held while stationary to lift that bomb, release J, then tap J to pump. This
primary-lifted carry is latched; pressing K throws it. Distinguish that from
hold-K carry, which throws on release. Controllers follow the same two paths.
Walking into a resting bomb and a deliberate second primary press near it both
support contextual kicking; no extra dedicated kick key is required.

Aim is authority-owned, with no target lock or auto-aim. The cursor has a shared
6-cell/second limit, a 25% fine start and 0.3-second ramp; analog input retains
partial strength. Deliberate range is 0–9 cells so the cursor crosses the carrier
to aim behind them. Close self-lobs are dangerous, not protected.
[Design alternatives and primary sources](../history/iteration19-controls-research.md). New carry, drop or throw
resets chosen intent. Pumping changes blast strength while deliberate aim keeps
its selected range. Read [precision throw research](../history/iteration16-aim.md)
when changing the underlying throw policy.

## Switch 2 USB: preserve the real sampling path

Read [controller research](../history/controller-research.md) before touching
[SdlGamepadBackend](../../scripts/input_devices/SdlGamepadBackend.cs) or
[controller bootstrap/patches](../../tools/controllers/). Godot's bundled backend
at the time of implementation did not expose Switch 2 Pro USB reliably.
The optional adapter uses pinned SDL/libusb and a narrowly patched hybrid reader:
USB bulk interface 1 initializes the pad; a separate nonexclusive macOS HID reader
receives live reports from interface 0. Successful enumeration alone proves
neither motion nor button delivery. Preserve both patches and their cleanup.
The reader requires one uniquely identified USB controller and rejects ambiguous
same-model devices rather than borrowing the wrong stream.

`ControllerHub.PollNative` must sample all six canonical axes, including both
triggers, through `PublishNativeAxes`. An earlier implementation sampled only
axes 0/1: synthetic injected right-trigger tests passed while real ZR aiming
would fail. `NativeAxesForPlaytest` deliberately shares the production sampler;
retain this seam when refactoring, rather than testing only event publication.
Aim uses canonical right trigger above its threshold in `ControllerHub.Read`.

The installed adapter implements USB, not Switch 2 Bluetooth. Third-party BLE
bridges exist; their claims are not this project's hardware evidence. Preserve
that boundary in docs and do not infer wireless support from USB detection.
Physical Xbox/Switch stick and button observations exist in controller research;
DualSense and newer trigger paths have synthetic coverage unless a separate
physical observation explicitly records them. Label synthetic results as such.

## Menus, focus and hotplug

WASD and arrows belong in native Godot `ui_*` actions. Keep ordinary text entry
working; global key interception can steal W/A/S/D from text fields. Install
per-device face-button confirm/cancel actions for embedded popup menus as well.
Use [MenuStickNavigation](../../src/Bomber.Client/MenuStickNavigation.cs) for
sampled held-stick navigation, hysteresis and repeat: an axis that gradually
crosses the engage threshold or stops sending changed reports must still work.

`ScreenName` controls menu-navigation enablement independently of match binding
lifetime; Lobby and Results can retain bindings while requiring menu behavior.
Actual window/application focus gates gameplay. Focus loss releases commands;
restoration requires a fresh neutral/rearm interval before held actions resume.
Hotplug must preserve unaffected seats and clear disconnected-device state.
P2 Ctrl/Enter/Backspace presses must select P2 prompts, not P1 labels.

Native harnesses require real foreground focus in their test desktop. The
default macOS VM provides that desktop independently of the user's active apps;
see [isolated QA](../../tools/playtest/README.md#isolated-macos-desktop).
Their bounded focus retries
restart the whole interrupted scenario from released controls and a fresh
session; functional failures remain failures. Keep invalid-attempt evidence.
For popup tests, reach a known endpoint with real keys before testing the next
movement: prior mouse hover can leave the last item selected. See the
[native focus guidance](../../tools/playtest/README.md#native-focus-interruptions).

## Current online flow (v0.29)

Online offers Play online, Create private room and Join with code. Private hosting
can be Automatic, Server only or Player hosted. Public rooms count down with two
humans; private owners still start manually. Changing simulation host does not
transfer the room-owner controls. Reconnects rearm physical input. See
[adaptive rooms](../smart-multiplayer.md) for privacy and recovery boundaries.

## Historical party menus and player settings (v0.21)

Private rooms created by the current client opt into product-owned `PartySession`.
Only the authenticated leader can start. A completed match produces results,
then returns everyone to the same room/code after five seconds; no auto-rematch.
Disconnect transfers leadership to a connected member. Random lobby now enters
a persistent cluster room with a two-human minimum and explicit host start.
Host peer room runs authority on a player device and displays the actual direct
or relay transport. See [peer hosting](../peer-hosting.md). Both client and server must be updated for this flow.

Esc/Start during gameplay opens the game menu. Leave is an explicit button.
Start in menus focuses the primary action (the leader's Start game in a room);
confirm activates it. Settings is reachable from Modes, Lobby and the game menu.
Local authority pauses in the menu; online authority continues. Clear queued
inputs and rearm controller actions when opening/resuming so held confirmation
buttons do not plant bombs. Tests may call `Back()` to explicitly exit a match;
that helper does not model the new Esc key path.

`PlayerPreferences` stores volume, vibration, update choice, online name and server URL
in `user://preferences.cfg`. Name/server edits save immediately through native TextChanged;
startup restores both without connecting automatically. Programmatic LineEdit.Text assignments
do not emit TextChanged, so persistence tests must type through the real input path.
The online screen labels the name field explicitly; blank/legacy generic guest names
resolve to P1, P2, etc. Authority names feed character labels, HUD and results.
Long world labels scale to a bounded width; HUD names ellipsize. Copy code uses the
actual current join code and disables itself for matchmaking/no-code lobbies.
Vibration defaults OFF. QA uses a temporary per-process file and must not alter
personal preferences. Feedback follows already-deduplicated accepted arena
presentation events for each local seat. Godot owns ordinary pad rumble; the
Switch 2 adapter uses SDL_RumbleGamepad. Stop on disable, focus loss, leaving and
menu entry; pulses are bounded. Test requests on synthetic pads prove routing,
not physical motor behavior. `player-settings` and `network` cover native menus,
persistence, pause behavior and manual room start.

Settings also contains **Local lighting**: Morning, Midday (default), Afternoon
and Night. It saves `video/stageLighting`, applies immediately to local arenas,
and is retained while online rounds use the server's snapshot choice. The dial
uses the existing arrow/A-D/controller navigation. See the
[rendering guide](visuals-and-animation.md#four-stage-lighting-variants-2026-10-23)
for the palettes, older-server fallback and `stage-lighting` graphical witness.

**Graphics** selects Low, Medium (default) or High and saves `video/graphicsPreset`.
Changes apply in place without restarting the session. A brief “Applying graphics…”
screen prepares effect pipelines and restores the previous focus; changing only
audio, vibration or lighting does not trigger that preparation. Missing, malformed
or unknown values fall back to Medium. The choice is
device-local in online games too; it never overrides server lighting or room rules.
Use the existing arrow/A-D/controller dial navigation. `graphics-settings` checks
native selection, persistence and paused local continuity; `network` checks online
continuity. [Rendering budgets and rationale](../graphics-research.md).

## Arena selection

The native arena dial cycles Sunken Temple, Rock Garden, Up & Down, Top Rules and Greedy Trap.
Use `StageIds.Length` for wraparound. Local battle, Practice and waiting-room
settings use the same validated ids; the menu miniature includes thin castle
bridges and their lower passages. `setup` checks both wrap directions;
`top-rules` launches the castle through the normal Practice menu.

## Reticle rendering and lifecycle

The user explicitly requested shape-only endpoints with no extra labels.
[ThrowEndpointReticle](../../scripts/arena/effects/ThrowEndpointReticle.cs) draws an
outlined ring/crosshair, an unsafe gold cross or an offscreen edge arrow.
`EndpointLabel` is diagnostic metadata, never visible copy.

The stock Temple can produce `Obstructed` even on a short ordinary throw.
Rendering only `Landing` previously made the carried-bomb reticle disappear.
[BombThrowPreview](../../scripts/arena/effects/BombThrowPreview.cs) uses a screen-space
CanvasLayer at layer 2 so terrain cannot hide the marker. Keep its world arc and
shared static projection, semantic endpoint colors and local-player ownership.
Moving actors, bombs and future water changes can interrupt the forecast.

[ArenaView](../../scripts/arena/ArenaView.cs) gates previews to active Match Fight/Hurry
and valid local carriers. Call `HidePreview` before deferred `QueueFree`:
CanvasLayer descendants can otherwise survive visibly above Results even when
an ancestor's 3D visibility changes. The normal-duel lifecycle regression reaches
actual victory with a survivor carrying; it does not forge a Results snapshot.

## UI identity and verification lookup

Extend [ArcadeTheme](../../scripts/ui/ArcadeTheme.cs), its `PromptBar` keycaps and
[BattleHud](../../scripts/ui/hud/BattleHud.cs) primitives. Dela Gothic One carries
headings/buttons/values; M PLUS Rounded 1c carries body labels. Retain their OFL
notices. Stamped keys use ink outlines/lips and gold focus; the fuse clock and
player tokens are the HUD's visual identity. Favor readable mixed-case copy,
drawn icons and clear control focus over decorative uppercase labels or glyph
arrows. Copy changes can also require exact native harness lookup updates.

Layout polish (2026-10-24): settings toggles use `ArcadeToggle`, a native
CheckButton with one key height in both states, a tinted off face and a vector
switch centered on the label line (the stock icon centered on the whole key and
the pressed style dropped the text). Secondary copy uses muted `Hint` text.
Results standings are positioned every frame from the 3D board's projected face
(`ArcadeBackdrop.ResultsBoardFace`), with rows sized to the player count, because
the backdrop camera has mouse parallax. Screens whose first tree button is Back
or Leave declare a `DefaultFocus` instead. The bundled Japanese fonts draw U+2026
as centered dots, so visible copy uses three periods. `ui-layout` captures the
online, advanced, settings, eight-seat lobby and four/eight-player results pages
and fails when a control leaves its card, a field cannot show its text, or
standings leave the visible board (the stage floor hides its lower edge, so the
face is clipped there and large fields cap the board height). Its lobby and
results data are presentation fixtures; `network` and `results` cover the real paths.

Find scenario membership/headless support in the single
[catalog](../../assets/testing/scenarios.json), dispatched by
[ScenarioCatalog](../../scripts/testing/ScenarioCatalog.cs). Use `controllers`
for mappings, seats, focus/hotplug and menus; `aim-inputs` for native K/J/L and
canonical pad parity; `precision-aim` for authority range/turn/landing;
`reticle` for actual marker pixels and Results cleanup; `wasd-menu` for native
menu/text-field behavior. Native scenarios are synthetic production-path tests,
not physical certification. Read [runner usage](../../tools/playtest/README.md)
for commands, recording and timeout behavior before running them.

## Iteration23 equipment and lobby editing

Room settings is available to the waiting lobby's leader and reuses HostSetup
without disconnecting or changing the room code. General Settings remains each
player's own audio, vibration and update preferences. Start still requires the
leader's explicit action.

Each primary tap queues one charge level; the progress hint shows the active
level and percent. Normal charge is .45 seconds/level; Focus is .15, with gentle
assist only while the aimed cursor is neutral. The bomb slot and ability slot
have separate HUD symbols. Ghosts have no bomb controls or bomb collision.

## Advanced match page (v0.25)

Match/Practice/Room settings → Advanced settings provides independent item drop
chance, authored block budget percentage, item lifetime, blast/bomb stack caps,
temporary effect duration and twelve per-item toggles. Escape/Back restores the
originating setup without disconnecting the room or losing edits. Restore defaults
updates both values and toggles. Status text and remaining seconds appear in the
player HUD; pickups blink for their final three seconds. `advanced-settings`
exercises native navigation, toggles, ordinary pickups, size restoration and
authoritative/rendered expiration.
