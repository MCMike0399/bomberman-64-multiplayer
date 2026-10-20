# Iteration seven: a living 3D game menu

Researched 2026-10-20. This note distinguishes inspected reference observations, documented engine capabilities, and proposed art direction. Reference images are retained only in ignored `artifacts/research/iteration7/`; they are not game assets.

## Intent and principles

The user rejected the flat, static, webpage-like menu. The target is a native party-game presentation with dimensional characters, physical space, animation and immediate controller feedback. TUNIC and Pokémon Brilliant Diamond/Shining Pearl are visual references for readable stylized volume, not specifications for copying assets or exact rendering techniques.

1. Build one coherent stage: characters belong to a lit world with a ground plane, visible platform sides, overlap and contact shadows.
2. Let the cast sell the game. Full-body characters, expressive silhouettes and independently phased idle movement deserve more space than settings.
3. Treat selection as an action: a cursor, changed pose, brief pop and short sound acknowledge input immediately.
4. Keep navigation legible. Spatial presentation must preserve clear current selection, readable labels and reliable back/confirm behavior.
5. Establish depth through geometry, camera and light before adding post-processing.

## First-party visual inspection

### TUNIC

The [official game website](https://tunicgame.com/) identifies the game as isometric and links its [publisher-supplied Steam page](https://store.steampowered.com/app/553420/TUNIC/). Its first store screenshot was downloaded from [Steam's game asset CDN](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/553420/ss_9c0184ba83022786f74f4a70ebae5c47d2a3c35c.1920x1080.jpg) and visually inspected (`tunic.jpg`).

Observed: raised land has substantial exposed side faces; stairs and trees overlap the terrain; warm illuminated tops contrast with violet shaded faces; tree and character shadows anchor forms to the floor. Simplified geometry remains strongly dimensional. The main character reads through silhouette and value/color separation. A screenshot cannot establish the underlying shader, precise camera projection, light setup or animation behavior.

Design inference: use a small arena island as the menu's stage, with chunky sidewalls and an elevated front camera. Separate warm highlights from cool shadow tones, rather than applying uniform ambient brightness. Preserve silhouette and depth even with simple procedural meshes.

### Pokémon Brilliant Diamond/Shining Pearl

The [official story page](https://diamondpearl.pokemon.com/en-us/story/) supplies a [forest overworld frame](https://diamondpearl.pokemon.com/assets/story/story_loop.jpg), [battle frame](https://diamondpearl.pokemon.com/assets/story/turtwig_screen.jpg), and [character-photo selection frame](https://diamondpearl.pokemon.com/en-us/assets/screens/p21_05.jpg), all downloaded and visually inspected. The files are `bdsp-town.jpg` (despite that local name, this is a forest), `bdsp-forest.jpg` (a battle), and `bdsp-overworld.jpg` (photo selection).

Observed: forest trees have modeled layered crowns with bright upper surfaces and dark underside/trunk regions. Small chibi characters remain readable against that environment. The battle frame uses foreground subject, trainer and distant scenery as distinct depth layers. The photo selector has modeled portraits, a strong coral focus border and a restrained prompt area; it is a grid and is not evidence that cards should be universally avoided. The [official homepage](https://diamondpearl.pokemon.com/en-us/) notes that its screenshots/footage are from development.

Design inference: use rounded helmets, pronounced face recess/rim, separate limbs and restrained glossy highlights to make the existing cast feel like physical toys. Borrow the clarity of one selected state; retain Blast Circuit's own stage and layout. Do not claim these images prove a particular tilt-shift, SSAO or PBR implementation.

## Skills research and applicability

Read the local `frontend-design` skill from the locally installed Claude plugin marketplace. Its useful transferable practices are subject-specific art direction, deliberate palette/type hierarchy, critique before implementation and avoidance of generic cards. Web-specific hero/section patterns do not define this game's UI. Its preference for sparing ambient motion should not suppress appropriate character idle animation in a game scene.

Read these public skill files directly as research; nothing was installed or executed:

- [game-ui-ux](https://github.com/gamedev-skills/awesome-gamedev-agent-skills/blob/main/skills/disciplines/game-ui-ux/SKILL.md): resolution scaling, safe areas, initial gamepad focus, explicit navigation and screen flow. Relevant as a behavior checklist, not an art-direction prescription.
- [game-feel](https://github.com/gamedev-skills/awesome-gamedev-agent-skills/blob/main/skills/disciplines/game-feel/SKILL.md): combine a few short feedback channels, ease motion, return feedback to rest and keep presentation separate from simulation. Do not carry its impact-heavy shake/hit-stop examples into routine menu input.
- [godot-ui-control](https://github.com/gamedev-skills/awesome-gamedev-agent-skills/blob/main/skills/godot/godot-ui-control/SKILL.md): Control/Container layout, inherited Theme resources, focus and decorative mouse filtering. These instructions support the readable 2D control layer over the 3D stage.
- [studio-game-director](https://github.com/bgrenat/godot-game-dev-studio/blob/main/skills/studio-game-director/SKILL.md): articulate player fantasy, success conditions, scope and touched systems before implementation. This is useful process framing; the brief above already makes that direction concrete.

These are community-authored instructions, not Godot project guidance or proof that their versioned code examples suit this repository. Validate APIs against the project's actual Godot version and official docs. Their primary value here is a small set of design/review questions, rather than adding dependencies.

## Concrete proposed composition

This section is an original proposal, not a reconstruction of either reference game.

- Give the 3D stage roughly 55–65% of the menu image. Put primary mode actions in a compact left stack, or short bottom ribbon, leaving the cast unobscured. Title, controls and a single current description need clear hierarchy; dense explanatory text should not dominate the scene.
- Build a raised gold-stone dais on blue water or a softly receding blue environment. Show its top and front/side thickness. Use a few staggered heights and a rear architectural silhouette to establish layers, without obscuring characters.
- Present animated full-body characters on that shared platform. Eight local seats may use one broad arc or two staggered rows; keep faces visible and put short seat labels beneath their positions. Selection should illuminate a floor ring/arrow and prompt a small character response.
- Keep graphic elements crisp in a CanvasLayer. A strong outline, offset dark extrusion, colored selection fill and one triangular cursor give the type a playful game identity. Avoid turning each setting into an equally weighted isolated panel.
- Suggested original palette: lagoon `#39B8D0`, deep water `#246BB3`, sandstone `#E4B66C`, shadow violet `#62527F`, glove pink `#F278B5`, warm highlight `#FFF0C6`. Preserve player identity colors; these palette tokens are tuning starting points.

## Proposed motion and rendering budget

All timings and amplitudes below are tuning suggestions:

| Trigger | Visual response | Initial tuning |
| --- | --- | --- |
| Menu idle | Character breath/weight shift; separate arm/helmet pivots | 2–3 second cycles, different phase per seat, about 2–3% body movement |
| Selection changes | Cursor moves; label emphasis; selected character leans or bobs | 150–220 ms ease-out, modest 1.03–1.05 scale peak |
| Confirm | Brief compression/rebound plus short sound | 100–180 ms, always return to rest |
| Screen changes | Small camera settle and coordinated UI reveal | 300–450 ms; navigation continues during the visual transition |
| Ready | Character celebratory gesture and persistent ready marker | Short gesture followed by readable stable state |

Use a warm directional key with real shadows and a restrained cool sky/ambient contribution. Character material roughness should allow broad highlights; sandstone can remain matte. Helmet edges need enough bevel/curvature to catch light. Avoid lighting every face equally. Treat water movement and small environmental accents as background motion with a slower cadence than selection feedback.

The repository's `project.godot` currently requests Godot 4.7 with Forward+; its mobile override is Compatibility. The [official environment guide](https://docs.godotengine.org/en/stable/tutorials/3d/environment_and_post_processing.html) documents environment/sky lighting, tonemapping and camera-based depth of field. It also documents renderer-specific restrictions, including SSR requiring Forward+. Use restrained glow only where it helps emissive accents; keep all gameplay/menu essentials clear without it. Reserve shallow focus for background props, if used, and keep every selectable character/label sharp.

The [official Tween reference](https://docs.godotengine.org/en/stable/classes/class_tween.html) supports eased property animation and warns against competing tweens for the same property and reusing completed tweens. Kill an interrupted selection tween before starting its replacement. The [animation introduction](https://docs.godotengine.org/en/stable/tutorials/animation/introduction.html) documents animating node transforms, material properties and UI with AnimationPlayer. Use stable authored loops or bounded per-frame visual pivots for idle animation; keep presentation transforms separate from simulation.

The [official focus guide](https://docs.godotengine.org/en/stable/tutorials/ui/gui_navigation.html) requires assigning an initial focused control and explains explicit focus neighbors and focus loss when controls are hidden. Every screen transition must restore valid focus; match actions should remain separate from Godot's UI navigation actions.

## Review evidence required

Capture the actual native game menu at its supported window size and a second aspect ratio. Record several seconds rather than relying on a single still: independently moving characters, selection response, stage/camera settling and controller navigation must be visible. Inspect a close-up of helmet/face depth and a wide shot showing platform side faces and contact shadows. Test fast repeated navigation and back/confirm during motion to catch stale tweens or lost focus. The work is successful when the scene reads as a playable party-game world before reading any setting labels.
