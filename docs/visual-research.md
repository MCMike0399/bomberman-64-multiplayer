# Visual reference and playtest evidence

Research date: 2026-10-19. This is a reconstruction of visual principles for original procedural art, not a claim to reproduce Nintendo assets or measured original geometry.

## Source hierarchy

- [Nintendo publisher page](https://www.nintendo.com/en-gb/Games/Nintendo-64/Bomberman-64-1204884.html) identifies the 1997 game, simultaneous 1–4-player mode and links its manual.
- [Original Hudson manual hosted by Nintendo](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_FR.pdf), printed pp. 10, 12–13: battle camera is fixed; setup separates Single/Team mode, human/COM selection, options and stage selection. Options include wins, timer, ghosts and sudden death. These are the basis for focused setup screens rather than one giant form.
- [Japanese original manual hosted by Nintendo](https://m1.nintendo.net/docvc/NUS/JPN/NBMJ/NBMJ_J.pdf) supplies another first-party visual reference for original screen and menu presentation.
- User-supplied gameplay reference, 972×720, inspected directly. It establishes the requested arena composition; exact original map identification was not established in this initial visual study. The reference image is not distributed in this repository.

## Observations from the supplied frame

The arena is a warm gold sandstone island surrounded by bright cyan rippling water. The island fills most of the frame below a full-width pale blue HUD strip. An elevated front camera reveals horizontal floor tops and vertical stair faces, keeping the entire arena visible. The image uses one shared view, without per-player split screens.

The plan reads as an outer rectangular terrace, a dark cobbled recessed court near the front center, side stairs up to the terrace, and two centered stair flights leading to successive rear temple tiers. The summit has a small pink inset structure. Pale ochre stone tops contrast with darker gold walls, engraved repeating wall motifs and dark seams between rectangular blocks. These layout observations are visual interpretation; exact grid dimensions and collision rules are not recoverable from one image.

Characters use round white/black/red/blue heads, warm face panels, distinct eye marks, bright pink hands/feet and top knobs, simple torsos and compact silhouettes. Strong contact shadows anchor them to the floor. The red bomb is an emphatic accent. Characters and bombs are large enough to read against the stone.

The top HUD places a red framed clock icon and pink-red `0:35` at left, then four compact character portraits and large gold score digits. Eliminated portraits have crossed eyes. The scene has no floating developer diagnostics or large opaque score sidebar covering the arena.

## Implementation decisions and visual checks

Use a fixed elevated camera and warm procedural stone materials with subtle tiled variation. Put water outside the play surface. Keep ramps/stairs visually continuous with authoritative walkable cells; decoration must not imply a route that simulation rejects. Preserve the front court and rear stepped silhouette. Use a light HUD band with strong clock and seat colors, and compact hints outside the action area.

Menu flow should expose mode selection, local/practice setup, online create/join/quick play, lobby and match/results separately. This flow is an original adaptation of the manual's separate setup stages. Mouse/keyboard/gamepad focus must remain readable.

Validate with real engine screenshots paired with state traces: a menu image alone cannot prove input, bomb lifting/throwing, ramp traversal or online authority. The playtest harness records observed state and asserts those behaviors; headless runs explicitly report that screenshots are unavailable.
