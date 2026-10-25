# Tutorial arena and teaching references

Researched 2026-10-24 for the requested original tutorial map and a dedicated
tutorial section. These are design references, not a claim that Blast Circuit
reproduces either game's rules or interface.

## What the original sources show

The Nintendo-hosted **Bomberman 64 English instruction booklet** was downloaded
and its scanned spreads inspected directly: printed pages 4–5, 12–15 and 24–27
(PDF pages 4, 8, 9, 14 and 15).

- **Green Garden**, pp. 12–13: green ground, ruined stone terraces, bridges and
  channels; its description emphasizes spatial puzzles and using bombs to cross
  gaps. This provides a garden/ruin reference distinct from Pyramid's silhouette.
- **Blue Resort**, pp. 14–15: a town threaded by water, with bridges and changing
  water levels. This supports making shorelines and elevation visually legible.
- **Battle stages**, pp. 24–25: Rock Garden combines nature and ruins in an open
  court; Pyramid emphasizes many steps and narrow areas; Up & Down separates
  height levels. Each has a recognizable spatial idea.
- **Controls and items**, pp. 4–5 and 26–27: movement, placement, kicking/stopping,
  carrying/throwing, pumping, recovery and remote detonation are separate ideas;
  item illustrations pair one object with one effect. These are useful teaching
  categories, but current Blast Circuit bindings and rules must own the lessons.

Source: [original Nintendo-hosted manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf).
Reference scans stay outside the repository; no original assets are reused.

**Super Smash Bros. Ultimate's official teaching pages** introduce the main loop
as short visual cause-and-effect beats, then place individual advanced techniques
under their own titles with brief explanations. Their separate basic/advanced
organization supports progressive lessons and quick revisiting of a specific
move. This is an observation of the official web teaching material, not a claim
that its in-game tutorial has the same navigation.
[Gameplay basics](https://www.smashbros.com/en_US/howtoplay/basic.html),
[advanced techniques](https://www.smashbros.com/en_US/howtoplay/technique.html).

**Brawl's official Training introduction** describes a quiet gameplay screen,
settings shown on pause, spawned items and CPU opponents, measurable combo damage
and slow motion. **Ultimate's official modes page** describes a dedicated training
stage that makes launch distance measurable. The transferable principle is an
uncluttered place to try one action repeatedly and see its outcome.
[Brawl Training](https://www.smashbros.com/wii/en_us/gamemode/various/various13.html),
[Ultimate Training](https://www.smashbros.com/en_US/howtoplay/mode.html).

## Proposed original direction

The following is our design inference, not a description of either reference.

Use an original **garden academy**: a generous central practice court, green
insets, pale stone paths, a low side terrace and a clearly bounded water channel.
Prefer an asymmetric collection of teaching areas over another concentric
pyramid. Quiet range marks and colored landing pads can reveal throw distance
without turning the whole environment into a technical grid. Keep monuments,
vegetation and banners outside movement and camera sightlines.

Give each area a job: an open movement circuit, a replaceable-cover blast lane,
a carry/throw target, a low elevation route, a controlled item station and a
shoreline for hazard/ghost lessons. Spawn only the props and opponents required
by the active exercise. Teach only mechanics implemented in Blast Circuit; the
original manual's bomb-jump puzzles are visual/layout inspiration unless that
mechanic is independently implemented and verified here.

Present the section as **Tutorial**, with selectable short lessons grouped from
fundamentals to advanced play. Each lesson should give one clear title, the
current keyboard/controller prompt, a concise explanation and a visible task.
After the real action succeeds, show an unmistakable completion cue and offer
the next lesson or replay. Keep retry and lesson selection accessible, retain
completion progress and allow free practice. These interaction choices adapt
the references rather than copying Smash branding, art, audio or exact screens.

Coverage should follow the shipped mechanics: movement/terrain, placement and
blast safety, cover and chain reactions, kicking/stopping, carry/throw and aim,
pumping/charged throws, impacts and player handling, equipment and temporary
effects, remote bombs, hazards/ghosts and the round's win/flood rules. An item
reference should explain every current item rather than leave lesser-used
effects undocumented. Current gameplay authority and input profiles determine
the exact content and acceptance conditions.

## Implementation and verification constraints

- Keep training setup separate from battle-stage selection and online rules.
- Check completed actions against accepted simulation state; a button press
  alone does not prove that a bomb was placed, carried, thrown or detonated.
- Provide deterministic retry/reset for an exercise after a failed attempt.
- Use real rendering review for map readability and tutorial overlay placement;
  passing geometry tests cannot establish the visual result.
- Verify keyboard and controller navigation, restarting/revisiting a lesson,
  returning to the main menu, and ordinary battle after leaving training.

The proposed flow is a product design recommendation. This research note is
not a delivery receipt; implementation and test evidence belong with the change.
