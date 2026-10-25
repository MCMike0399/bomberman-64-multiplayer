# Blizzard Battle verification

2026-10-25. Tested implementation: `4ae05e4e1437b488b539367a9880e90b891d81e4`,
an unreleased source build using Godot 4.7.2 .NET / .NET 10. The
[stage contract](blizzard-battle.md) separates original references from project
physics, dimensions and eight-player tuning. [Research](blizzard-battle-research.md)
includes the supplied screenshot, official guidebook and normal/sudden-death footage.

## Verified behavior

- Canonical **public** gate passed all ten steps, including public boundaries,
  documentation links, tooling contracts and audio.
- Canonical **managed** gate passed: **2,918** game integration checks,
  **340** client/session checks, **65** focused AI checks, **19** original audio
  cues and **23** cluster checks. The expanded AI soak passed **60** cases across
  five battle arenas, three difficulties, three/seven CPUs and two seeds.
  Blizzard accounts for twelve cases, with 14–96 bombs observed per case and
  a maximum safe-exit idle window of 6.1 seconds against the existing 12-second limit.
- The managed gate also passed real server WebSocket smoke, including a Blizzard
  room receiving advancing authoritative weather, protocol rejection, advanced
  settings, peer hosting and migration; forced adaptive relay and **36** real
  local fleet checks passed. These were task-owned loopback servers.
- Final **core-headless** completed **1,259** assertions with zero runtime errors.
  The focused final map check separately passed **31** assertions, including
  sixteen-cone budgets, geometry and ice/foundation depth separation.
- The exact signed export passed a fresh **peer-rooms** retry (**19** assertions)
  and **adaptive-rooms** (**9**) against a task-owned server.
- The final **recorded Metal map journey** passed **84** assertions with **12**
  captures, **zero runtime errors and zero runtime warnings**. It covers native
  arena selection, four-player opening, actual snow motion and local pause,
  Low/Medium/High settings, night lighting, ordinary wind-affected aiming and
  throws, real cone destruction/loot collection, bilateral avalanche elimination
  with a center survivor and eight-player CPU combat.
- Its 30 FPS recording is **60.1 seconds / 1,803 fully decoded frames**, at 1×
  playback speed. Full-resolution captures and two contact sheets spanning the
  recording were visually inspected. The clip includes ordinary menu/settings
  transitions. It is fixed-step gameplay evidence, not real-time GPU throughput.

The broader eight-journey `blizzard-regression` graphical run is in progress.
Its final receipt will be recorded here before this PR leaves draft.

## Screenshots from the verified export

These are exact 1280×800 game captures, without image edits. The opening, night,
avalanche and CPU images use the ordinary battle camera. The detail image uses
an explicitly temporary inspection camera. The menu image shows the actual
arena selector and shared miniature.

![Four-player opening](media/blizzard-battle/opening.png)

![Original ice textures and faceted spires — inspection camera](media/blizzard-battle/ice-detail.png)

![Night lighting and snow](media/blizzard-battle/night.png)

![Avalanches and surviving central corridor](media/blizzard-battle/avalanches.png)

![Eight-player CPU combat](media/blizzard-battle/eight-player.png)

![Actual setup miniature](media/blizzard-battle/menu.png)

## Retained failures and limits

The initial missing-stage topology regression failed before implementation.
Native shader compilation then caught the obsolete `CLEARCOAT_GLOSS` identifier;
Godot 4.7 requires `CLEARCOAT_ROUGHNESS`. A separate red geometry check caught
coplanar foundation/ice tops; the foundation now stays below the ice. Both were
corrected before the verified captures above.

A broad graphical attempt stopped at the existing 1600×1000 window-resize check
on a 1600×1000 guest display. A dedicated 1920×1200 guest is used for subsequent
runs. One guest startup exceeded the unchanged 180-second RPC readiness budget;
the next focused recording reached the real Metal renderer and passed. These
failed receipts remain available locally and are not counted as passing runs.

An earlier complete headless profile passed core, peer and adaptive journeys.
A later repeat passed the final core but timed out while a replacement peer host
rebuilt direct channels. The exact-export transport retry passed both journeys;
no timeout or production host-lease policy was weakened. The timeout's cause was
not isolated, and the failed receipt is retained.

Graphical testing used an isolated macOS 26.6.2 desktop with Apple Paravirtual
Metal Forward+, without taking host focus. This establishes rendering and
synthetic native input, not physical-controller coverage, bare-metal GPU timing
or undocumented original-game numerical physics. Visual inspection is not user
approval of the art.

Raw references, traces, screenshots and recordings remain in ignored
`artifacts/blizzard/`. The shared service was not deployed. Online protocol
revision **4** requires a coordinated client/server release.
