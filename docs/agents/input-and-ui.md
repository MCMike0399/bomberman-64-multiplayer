# Input, menus, aiming and UI

Read this guide before changing bindings, device sampling, menu navigation,
controller prompts, throw previews or shared UI primitives. It records decisions
that source alone cannot explain. Source and the current README remain the
lookup for exact values; older iteration documents retain historical evidence.

## Round and match announcements (2026-10-28 source work)

The arena stays visible during the authority's three-second Over phase, including
the match-winning round. The HUD shows **Game!** and the accepted winner's name,
distinguishing a round win from a match win; simultaneous elimination shows
**Draw!**, and clock expiry shows **Time's Up!** with the draw/no-point detail.
Draws use an original descending cue instead of the victory fanfare. Winner poses,
the frozen battlefield and updated score remain visible before Results or the
next Ready phase. Final results are captured at elimination and emitted after
the interval by the shared local/server/peer authority.

The [original-game research](../round-ending-research.md) separates the verified
announcer wording/arena hold from our visible Game banner, names, timing and
synthesized sound. `round-ending` covers actual bombs, simultaneous deaths,
timeout, input freeze, next-round cleanup and final Results. `post-match`
additionally checks the on-arena announcement on a real network guest before
the same-room map-selection flow. See the [verification](../round-ending.md).

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
| Bomb, contextual kick, pump | J / left click | N / Enter / Num 0 | A | A / B | Cross |
| Hold carry, release throw | K / right click | M / Num 1 | LB / X | L / Y | L1 / Square |
| Point throw while carrying | Mouse / trackpad | — | Right stick | Right stick | Right stick |
| Precision aim | L / Shift | Ctrl | RT | ZR | R2 |
| Remote detonation | I / R | Comma / Backspace / Num 2 | B | X | Circle |
| Stop kicked bombs | O | Period / Num 3 | RB | R | R1 |
| Menu confirm / back | Enter / Esc | Enter / Esc | A / B | A / B | Cross / Circle |

v0.19.1 adds printed Switch B as a primary/bomb alias so **hold Y + tap B**
readies and pumps without reaching across to A. Both held state and sub-frame
taps use the alias; B remains menu Back, X remains remote, and A still works.

Space has no gameplay action. F11 or Alt+Enter toggles fullscreen and saves the selected mode.
Windows keeps the same L/Shift precision-aim binding. Its launcher owns a
focus-scoped accessibility shortcut guard: only HOTKEYACTIVE/CONFIRMHOTKEY
are cleared, only when the corresponding feature is off, without profile
writes. Focus loss and engine exit restore the saved flags. Already enabled
Sticky/Filter/Toggle Keys and unrelated flags/timings are preserved.
F3 toggles the device-local performance bar and saves the same choice as
Settings → Performance metrics. Repeated key-down events do not toggle it again.
Keep both held-state sampling and short-press detection consistent when changing
bindings: [PlayerControls](../../scripts/input_devices/PlayerControls.cs) owns keyboard states,
`BombKey` and `RemoteKey`; [ControllerProfile](../../scripts/input_devices/ControllerProfile.cs)
owns family semantics and prompt labels. Update the README, controller panel,
HUD hints and native tests together.

Movement axes sample the current physical-key state. A movement tap whose
press and release both occur between render polls can disappear during a long
frame hitch. This is existing shared behavior on all platforms, distinct from
queued button-edge handling. The pinned-engine synthetic down/up control
confirms the next state poll is released; it is not physical-keyboard QA.

The positional action table in early controller research is historical; use
the current table above and `ControllerProfile` for product actions.
Godot `JoyButton` identifiers are canonical physical positions, not printed
Nintendo letters: `A` is south/printed B; `B` is east/printed A; `Y` is
north/printed X. Translate actions through `ControllerProfile`, preserving the
backend's canonical IDs. Nintendo confirms with printed A and backs with B.

## Input path and fairness

On Linux/X11, roster role menus use `PopupMenu.ForceNative`: the official
4.7.2 release runtime leaves embedded popup parent-focus/lifetime subscriptions
behind after closing. A stock three-item OptionButton reproduces the same
errors without game or test-driver logic; the editor does not. Native windows
retain Godot's painted menu and avoid that broken embedded path. Keep the
roster theme and input bindings unchanged when maintaining this workaround.
A native popup window does not stop pad/action events from also reaching the
root window. Without a guard, the A press that selects a role also activates the
parent's focused Back button. `GameRoot._Input` therefore consumes pad and
action events while a native role popup is open and on its closing frame.
Keyboard events target the popup window itself. Windows and Mac keep embedded
popups. A native Windows WARP witness on 2026-10-28 passed `controller-popups`
twice with zero engine errors on the unchanged build, so only Linux/X11 needs
this path.
`controller-popups` asserts that closing each picker restores its parent's
signal-connection counts. Native delivery runs this short journey before the
complete graphical tour, with the same zero-error policy. Native popup captures
read actual X11 screen pixels and save only the app's client rectangle, since
the separate popup window is absent from the main viewport texture. Wayland
and XWayland screen capture need separate evidence.

- [ControllerHub](../../scripts/input_devices/ControllerHub.cs) discovers devices,
  publishes native input, tracks active prompt family/keyboard seat, guards focus,
  and combines held state with bomb/remote/stop press edges through `Read`.
- [MatchControllerBindings](../../src/Bomber.Client/Controls/MatchControllerBindings.cs)
  fixes device seats during a match; [LocalControllerLayout](../../src/Bomber.Client/Controls/LocalControllerLayout.cs)
  handles the keyboard/gamepad layout. Consult these before changing hotplug.
- [InputPolicy](../../src/Bomber.Client/Controls/InputPolicy.cs) resolves D-pad, stick and
  keyboard directions without summing device speed. Ordinary movement normalizes
  nonzero direction after radial stick drift rejection; diagonals gain no speed.
- [GameRoot](../../scripts/app/GameRoot.cs) sends local commands to
  [LocalSession](../../src/Bomber.Client/Sessions/LocalSession.cs) or
  [NetworkSession](../../src/Bomber.Client/Sessions/NetworkSession.cs).
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
   the left stick or D-pad; promote L/LB/L1 for carry so the thumb stays free.
   The right stick no longer drives this rate cursor; it points (below).
4. Release L while retaining K: the bomb stays overhead, the chosen aim persists,
   and WASD moves again. Re-enter L to adjust. Release K to throw.
5. I or R detonates owned remote bombs. Shift is an alternative aim modifier.

Pointing is the faster alternative to steps 2–4: while carrying, move the mouse
or tilt the right stick and the ring goes there while you keep walking. Holding
L/ZR still plants the carrier; a pointer target then wins over the nudge keys.

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

## Pointer aiming: mouse/trackpad and right stick (2026-10-26)

User request: keyboard players track the reticle with the mouse/trackpad, controller
players with the right stick, and keyboard diagonal throws must stop snapping to the
last-released key. Both channels produce `PlayerInput.AimTarget`: an absolute arena X/Z point for the
mouse, a carrier-relative offset (`AimRelative`) for the right stick; the authority owns range, height compensation and persistence
([gameplay guide](gameplay-and-ai.md#throws-precision-aim-and-fuse-ownership)).
`GameRoot.PointerTarget` resolves it per seat only while `ArenaView` shows that
local player carrying a bomb.

- **Mouse** (Keyboard 1, seat 0, only). `ArenaView.ArenaPoint` ray-marches the camera ray
  against the drawn terrain so a raised tier is picked where it appears; off-stage
  points use the floor plane. The target is absolute, so the ring stays under a
  still cursor while the carrier walks, and network latency cannot shift it.
  [MouseAimGate](../../src/Bomber.Client/Controls/MouseAimGate.cs) protects
  keyboard-only players: the mouse points only if one continuous motion (pauses over
  .3 s restart the count, so sensor jitter never adds up) covered ≥6 px during the
  carry or within 1.5 s before it began, and L/Shift + WASD hands control back until the
  mouse moves again. Left click aliases J and right click aliases K (held carry); the wheel does nothing;
  both join the focus re-arm check. In matches the cursor is a crosshair and hides
  after 2 s without motion; it is restored outside `Match` before any early return.
- **Right stick** (every pad). [AimStickPointer](../../src/Bomber.Client/Controls/AimStickPointer.cs)
  maps tilt to direction and deflection (radial deadzone .2, full at .92) to 1–9
  cells from the carrier, without ZR. A spring-loaded release sweeps through short
  deflections; an inward sweep faster than 6 deflection/s keeps the previous target,
  and the deadzone stops reporting, so letting go never shortens the throw. Slow
  deliberate pull-backs still shorten it. Samples use real elapsed time per `Read`.
- **Diagonal release grace** is authority-side (`TurnToward`), not an input delay:
  movement stays immediate and only the heading waits ~100 ms when one axis drops.

Alternatives considered: right-stick rate cursor without ZR (no snapback problem,
but slow to cross 9 cells and not what "track" means); relative mouse offsets (latency
moves the landing online); a client-side diagonal latch (changes movement physics).
Tests: `AimedThrowChecks` (grace, pointer range/landing/raised tier), `TutorialChecks`
(pointer completes Place your shot), `AimInputChecks` (filter, gate, wire format,
LocalSession), `NetworkAimChecks` (real WebSocket target), and native `aim-inputs`
(real mouse motion/clicks and right stick on Xbox/Switch native/DualSense). All are
synthetic production-path inputs; physical mouse, trackpad and stick feel still need
the user's hands-on review.

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
Use [MenuStickNavigation](../../src/Bomber.Client/Controls/MenuStickNavigation.cs) for
sampled held-stick navigation, hysteresis and repeat: an axis that gradually
crosses the engage threshold or stops sending changed reports must still work.
D-pad holds use the same repeat timing; their native initial events remain intact
for short taps and popups. D-pad intent takes priority over a held stick.

Menu navigation (2026-10-25 source): each page declares focus columns in
`ArcadeMenu.Navigation`. Up/down stays in a column and reaches its Back/primary
row at the ends; left/right between grid columns preserves the row. Dials and
text fields retain horizontal value/caret editing. L/R, LB/RB or L1/R1 switches
columns and restores the last focused control there. Tab/Shift+Tab traverses all
visible enabled controls in reading order, including sliders and text fields.
Never route through the hidden setup state controls. Native role popups own
navigation/Back until dismissed.

The lobby's Start game / Room settings / Settings controls form a horizontal
action row. Up reaches Copy code, then Leave room; if Copy code is unavailable,
Up reaches Leave directly. The real `network` journey verifies left/right/up
navigation, and `menu-navigation` covers the empty-column fallback.

Navigation QA must account for focus history left by earlier journeys. The
scenario seeds known column selections through real controls and verifies that
shoulders restore a non-first rule. Assuming a fresh page after other setup or
settings journeys incorrectly rejects the intended remembered selection.

Esc/controller Back returns one menu and restores its opener, even while a text
field is editing. Setup keeps its actual origin (roster, Practice or Online),
and Continue/Save must not create a Back loop. Esc from a waiting lobby leaves
for Online; room rules and personal settings return to the same connected room.
During play Esc still opens Pause, and Esc from Pause resumes the same session.

Start/Menu/+/Options **focuses**, without activating: Continue on player select,
Start/Save on setup, Back on Advanced/Settings, Try it on lessons, Resume on Pause,
and Map Select on Results. Online uses Create in the hosting column,
Join from the room-code controls, Play online otherwise, or Cancel while busy.
A room leader gets Start game when enabled; guests get Copy code or Settings.
Controls keeps diagnostic face buttons until Start focuses Back and arms confirm.
A completed/failed tutorial uses Next/Retry; Esc returns to the current lesson
in the browser, with matching on-screen hints. Free practice
continues to use the ordinary pause menu.

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

## Post-match map selection (2026-10-27 source work)

Local Results → Map Select opens Match Setup with the same roster, suits and
rules, focused on the arena selector. The next Start Battle uses that selection.
Online results keep their standings interval, then open the same map/rule picker
inside the existing room and code. Only the room owner can edit rules or start;
guests see accepted settings and wait there. Ownership changes update these
permissions. Public rooms keep their authoritative automatic countdown.
Online Back leaves the room; explicit Mode Select remains available on Results.
Tutorial retry/next and Practice reset retain their existing behavior.

Bomb and flame HUD counters show collected upgrades, starting at 0/0. Base play
still permits two active bombs and ordinary level-one blasts. Pickups raise each
counter by one; round and respawn resets clear the collected upgrades.

## Current online flow (v0.29)

Online offers Play online, Create private room and Join with code. Private hosting
can be Automatic, Server only or Player hosted. Quick Play source policy
(2026-10-27) shows a one-minute countdown with at least two connected humans.
Roster changes restart it; below two, it disappears. Public Start game and Room
settings controls are hidden, and the authority rejects their commands. Shared
rules replace anonymous owner controls; personal Settings stays available.
Expired timers show host/capacity waiting instead of a frozen zero. Private
owners still start manually. Changing simulation host does not
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

On Windows, **Renderer** (Auto, Vulkan, DirectX 12) applies at the next launch. Its
saved/error status replaces the dial caption ("Renderer · on restart" / "not saved",
details in the tooltip) instead of adding a line, so Settings keeps one height and
"Check now" stays inside the minimum 1024 x 640 window under every preset
(Windows GPU run 41214884627 found the old extra line pushed it out).
`graphics-settings` audits that window for each preset. Run it with
`tools/playtest/run.py --qa-windows-settings-rows` to show the Windows rows on
macOS/Linux; the flag only works with `--playtest=` and writes renderer changes to a
scratch launcher folder. It also checks that Tab and controller focus scroll
"Check now" into view. Esc acts in one press while a tooltip is showing: the GUI
spends that press dismissing the tooltip through its own path, and GameRoot then
acts on the next frame. Navigating under a live tooltip tore it down twice and
logged disconnect errors (Linux GPU run 41240998617). `display-settings` covers this.
On Linux X11, Godot 4.7.2's tooltip teardown still logs two "Attempt to disconnect a
nonexistent connection" messages for the root Window (`focus_entered`/`tree_exited`,
object.cpp:1644) after a real tooltip. `tools/playtest/contracts.py` allowlists exactly
those, only on Linux `tour-02`, at most once each per real tooltip cycle (reviewer-approved,
2026-11-01). `display-settings` asserts the root Window connection counts return to
baseline and no tooltip panel survives. An upstream report is drafted, not filed.

**Display mode** selects Windowed or Fullscreen. **Resolution** offers Native
(shown with the screen's pixel size; saved as Automatic) and the common 16:9/16:10
sizes from 1280 x 720 through 3840 x 2400 that fit the current screen. Larger sizes
rendered exactly like Native, so the user reported that resolution "did nothing"
(v0.39.1 hotfix, 2026-10-26); they are no longer offered, though a saved larger
size is kept for a bigger monitor. The hint under the dial states the live 3D
output ("3D renders at W x H"), which changes instantly. Resolution needs no
"Applying graphics" preparation: unlike a preset it compiles no shaders. Changes save
immediately to `video/fullscreen` and `video/resolution` and restore on the next
launch, including shortcut changes. Existing installations default to fullscreen
and Automatic. Missing/malformed values safely retain those defaults.

Windowed resolution sets the native window size, fitting oversized choices to
the current display's usable area. Automatic opens a 1280 x 800 window.
Fullscreen retains the monitor's native mode and aspect ratio; the selected size
caps the arena's 3D resolution while menus stay at output density. Low's existing
85% quality scale multiplies that cap. Automatic uses the native fullscreen 3D
resolution. Moving/resizing the window recomputes rendering without restarting
local/online sessions or effect preparation. `--display-windowed` overrides only
initial presentation for QA and never overwrites a saved fullscreen preference.

**Frame rate cap** is a slider with stops at 30, 45, 60, 75, 90, 120, 144, 165,
240 and Unlimited (default). It saves `video/frameCap` (0 = unlimited; unknown
values fall back to unlimited) and sets `Engine.MaxFps` immediately. Display sync
still applies on top of it. A launch-time `--max-fps` (QA's 30 FPS timing, the
uncapped benchmark) outranks the saved cap. `player-settings` checks the slider,
persistence and that override.

`display-settings` exercises the real controls, both shortcuts, resolution
persistence, fullscreen 3D scaling, oversized-window fitting and paused-match
continuity. It also starts a fresh copy of the executable against an isolated
copy of the saved preferences and retains that process's own rendered restoration
receipt. QA never reads or writes the user's preferences file.

## Performance metrics

The saved `video/performanceMetrics` preference defaults off; malformed values
also fall back to off. `PerformanceBar` is a pointer-transparent, nonfocusable
CanvasLayer overlay shared by menus, local play and online play. Battle and
tutorial HUDs move below the 32-unit strip while it is enabled. Hidden telemetry
does no sampling. Visible counters update twice per second; Godot's FPS monitor
itself updates once per second.

FPS is Godot's actual rendered rate. FRAME averages engine frame deltas over the
sample interval; PROCESS and PHYSICS use Godot's process monitors in milliseconds,
not system CPU utilization. VRAM uses Godot's video-memory counter in MiB and
shows N/A when the renderer reports zero/unsupported. DRAWS covers all rendered
viewports. CPU TEMP explicitly shows N/A: the engine has no portable sensor API.
No temperature is inferred from frame rate, and no privileged sensor helper is
installed. [Godot monitor definitions](https://docs.godotengine.org/en/4.7/classes/class_performance.html).

`performance-metrics` verifies real Settings and F3 inputs, persistence, live
samples, key-repeat suppression, disabled sampling, multiple window sizes,
paused-session continuity, ordinary bomb input, HUD clearance and rendered
before/after pixels. It belongs to `all` and `menu-ux`. Review its actual captures;
fixed-step VM readings establish functionality, not native hardware throughput.

## Arena selection

The native arena dial cycles Sunken Temple, Rock Garden, Up & Down, Top Rules,
Blizzard Battle and Greedy Trap. The real miniatures are built and rendered
during startup preparation, then retained so cycling the dial only changes
visibility. Graphics-preset preparation redraws every retained miniature and
restores the selection before returning focus. See the
[rendering guide](visuals-and-animation.md#first-use-effects).
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
`reticle` for actual marker pixels and Results cleanup; `aim-inputs` also covers mouse and right-stick pointing; `wasd-menu` for native
menu/text-field behavior. `menu-navigation` covers focus routes, Start, nested
Back, grid rows and complete Tab cycles; `menu-ux` combines it with existing
graphical menu, popup, controller, settings, tutorial and real-network journeys. Native scenarios are synthetic production-path tests,
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
temporary effect duration and eleven per-item toggles. Escape/Back restores the
originating setup without disconnecting the room or losing edits. Restore defaults
updates both values and toggles. Status text and remaining seconds appear in the
player HUD; pickups blink for their final three seconds. `advanced-settings`
exercises native navigation, toggles, ordinary pickups, size restoration and
authoritative/rendered expiration.

## Roster CPU level (2026-10-28)

CPU-seat bomb pips are focusable buttons that cycle Easy → Medium → Hard for
all local bots. The name appears below the pips; human/off seats cannot activate
them. This edits the same shared difficulty as Match Setup → CPU level. The
`roster` journey checks focus, activation, mouse cycling and setup persistence;
`network` checks owner editing and accepted server/peer difficulty in a real room.
