# Bomberman 64 explosion appearance

2026-10-24. The original effect reads as an **expanding, coherent spherical
fireball**, often a dome where terrain hides its lower portion. It is not a
perfectly smooth static ball, and it is not the cluster of separate rounded
lobes currently prominent in Blast Circuit. This is a visual finding; it does
not recover the original damage-volume algorithm.

## Primary imagery inspected

| Reference | Inspected interval / evidence |
| --- | --- |
| [TurflamicGaming420, Top Rules original gameplay](https://www.youtube.com/watch?v=RMV6wCZT63c&t=12s) | Downloaded source 0:10–0:35. Inspected normal-blast onset, growth and fade at clip offsets 2.1–3.3 seconds, sampled at 20 fps. Stream-copy alignment makes source timestamps approximate. |
| [XFactor1111, original multiplayer gameplay](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=4s) | Downloaded source 0:00–0:35. Inspected bomb pumping/carrying around 0:04–0:07, enlarged orange blasts around 0:11.8–0:13.4, and an unambiguous red-bomb detonation around 0:30–0:31.2. Dense sheets use 15 or 20 fps. |
| [Nintendo-hosted original English manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf) | Printed pp. 4–5 describe pumping; pp. 24–25 depict orange blasts on stages. Useful for identity and appearance, not animation timing. |
| [Original official guide scan](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip) | Printed p. 102, `guide-51.JPG`, shows a large cyan blast with angular peripheral flares beneath Top Rules' walkways. A still cannot establish its whole animation. |

Videos are third-party recordings of the original software. Their renderer,
emulator and playback conditions are not established. Observations concern the
game imagery, not uploader commentary. Reference files, decoded frames, contact
sheets and provenance stay in ignored
`artifacts/stage-release/references/explosions/`; none ship with the game.

## What the sequences show

**Normal orange blast.** A compact near-white flash rapidly enlarges into one
orange/yellow body. Its edge is slightly faceted/irregular; bright patches move
within the body. It becomes translucent amber and vanishes. Sampled Top Rules
blasts have no separate persistent thin yellow floor hoop and no detached
balloon-like puffs. The overall round silhouette stays recognizable.
[Normal sequence](https://www.youtube.com/watch?v=RMV6wCZT63c&t=12s).

**Pumped orange blast.** The enlarged blast has a bright white/yellow core,
orange body and sharp triangular flare rays during expansion. It remains one
broad volume, then fades transparently rather than lingering as a large brown
smoke cloud. Ground-level views look dome-like. Multiple nearby bombs can chain,
so the observed compound sequence cannot establish one universal pumped timing
curve. [Pumped sequence](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=11s).

**Red/power blast.** At 0:30 a red bomb sits beside blue in the lower-right
quadrant. Its detonation produces a turquoise/white center and a distinct
expanding white/cyan rim, followed by a translucent teal body and fade. The rim
expands beyond the central glow; it is not evidence for retaining a fixed yellow
ring on every ordinary explosion. Pumped-power animation was not isolated here.
[Red-bomb sequence](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=30s).

**Rendering technique remains uncertain.** Terrain occlusion and rounded shading
suggest volume, but these fixed-camera recordings cannot prove a sphere mesh,
billboard, layered sprites or a hybrid. Do not claim recovered tessellation,
texture animation, particle count, original collision radius or exact lifetime.
The game can show a round fireball without its visible edge defining every hit.

## Pre-integration Blast Circuit comparison

Inspected the tutorial-era `c335037` [ExplosionView](https://github.com/MCMike0399/bomberman-64-multiplayer/blob/c335037/scripts/arena/effects/ExplosionView.cs), its creation
in [ArenaView](../scripts/arena/ArenaView.cs), and actual `blast-small-bloom`,
`blast-large-bloom` and `blast-large-vapor` captures from the tutorial graphical
suite at `artifacts/tutorial/graphical-final/playtest/all/20261025T001044.941514Z-all/`.

The current large bloom visibly resembles a pumpkin/cloud made of overlapping
balls, with a bright thin ground circle. The code builds 6/10/14 offset sphere
lobes (4 under reduced detail), a short-lived center, a boundary sphere and a
torus. UV sine patterns reinforce surface banding. Late lobes rise and turn
brown. Those are deliberate project choices, not established original features.

The entire effect also uses `TempleStageView.HeightScale` (1.35) vertically.
That conversion corresponds to the project's tier presentation; deleting it
without checking the damage-volume mapping could make the displayed danger
misleading. [Blast authority](../src/Bomber.Game/Simulation/BlastSession.Explosions.cs)
and its geometry checks are separate from these meshes.

## Targeted original-procedural direction

1. Make one coherent expanding fireball the dominant silhouette. Use restrained
   faceting/deformation and irregular internal bright patches rather than an
   array of clearly separate round lobes or longitudinal stripes.
2. Give pumped blasts brief sharp flares and a stronger core while preserving
   the same coherent volume. Let terrain naturally occlude the lower portion.
3. Remove or greatly subordinate the ordinary thin torus. Give power bombs their
   own brief expanding cyan rim, central glow and translucent teal decay.
4. Fade the main body cleanly; substantially reduce the current brown rising
   cloud. Match appearance first, without asserting recovered original timings.
5. Preserve authority radius, lifetime, shielding and event deduplication. Keep
   danger readable throughout its accepted active interval, even when cosmetic
   growth/fade differs. Review the tier-height conversion explicitly.

Verify onset/peak/fade for normal, pumped and power variants in the real battle
camera, including airborne bombs, slopes and overlapping chains. Replace tests
that require a lobe count with meaningful checks of active extent, lifetime,
type distinction and bounded rendering cost. Actual visual inspection is the
acceptance evidence; headless sphere counts cannot establish resemblance.

## 2026-10-25 star core and v2 layers

Re-inspected the [XFactor1111 recording](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=11s).
The pumped orange blast was viewed at about 0:11.6–0:13.6, sampled at 8–10 fps
and cropped. The red-bomb blast was viewed at about 0:30.2–0:31.4, sampled at
10 fps. Frames stayed in an ignored temporary directory and none were committed.
Earlier inspection missed three details:

- **Star core.** A white center with a yellow, five-point star outline sits
  inside the orange body during expansion. Its points read as triangular flares
  past the silhouette. The teal power body keeps a pale **star** visible
  through most of its life.
- **Power rim.** A thin white/cyan ring, with a fainter inner line, leads the
  teal body. Small white puffs then pop at the rim before the body dissolves.
- **Fade.** The orange body turns translucent amber and then a darker smoky tint
  before vanishing. No persistent ground mark was seen.

Modern stylized-VFX practice (community breakdowns such as the
[Overwatch-inspired frag explosion](https://realtimevfx.com/t/stylized-frag-launcher-explosion-overwatch-inspiration-breakdown-posted/1894?page=2)
and [its critique thread](https://realtimevfx.com/t/stylized-cartoon-explosion-overwatch-inspired/1767))
contributes the following rules of thumb. These are artist conventions, not
measurements:

- keep the flash to a few frames;
- offset layer starts by a few frames;
- slow particles with heavy drag;
- group sparks into directional jets;
- keep rings soft and subordinate;
- keep late smoke from drawing attention.

Blast Circuit v2 combines both sources: the star as the identity mark, crisp
toon heat bands, and a burning-edge noise dissolve after the hazard window.
Ownership and invariants are in the
[visuals guide](agents/visuals-and-animation.md#v028-reference-fidelity).
Captured before/after sequences are evidence of appearance only. They do not
recover original timing, geometry or damage volume.

## Pending-PR integration review (2026-10-25)

The updated default practice pool reproduced a missing-Red assertion in the
presentation journey. The fixture now requests a Red-only pool and walks to the
actual spawned pickup; it keeps ordinary collection, placement and detonation.
The repaired headless journey passed 49 assertions. Exported captures reviewed
the warm star-core bloom, cyan power ring and later dissolve at the battle camera.

The first full exported graphical run reached 3,242 assertions before the
eight-client adaptive fixture lost a host lease. Seven synthetic sessions had
been polled only inside selected waits. They now poll on every engine frame,
including capture/setup waits, with production lease/qualification policies
unchanged. Its focused rebuilt Metal run passes; the complete combined suite
remains the integration completion gate. Failed receipts are retained under
ignored `artifacts/review/graphical`, alongside the focused replay.

The frame-poll correction alone passed a focused replay but the full suite still
failed. A controlled slow-poll witness and a real HTTP/WebSocket test with a
blocked UI synchronization context isolated the remaining cause: the socket
worker inherited Godot's context. Background transport I/O plus interleaved
authority polling fixes that boundary; peer simulation remains on its polling
thread and the production six-second lease is unchanged. Both red witnesses
are retained beside the corrected receipts.
