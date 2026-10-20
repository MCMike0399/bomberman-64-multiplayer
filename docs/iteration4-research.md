# Iteration four: diorama polish and an animated battle roster

Research verified 2026-10-19. The current original Bomber models, temple layout and cream/gold interface remain the starting point. This research informs readability and motion; it does not authorize importing TUNIC or Nintendo assets.

## TUNIC: primary developer observations

In [Andrew Shouldice's developer interview](https://www.thexboxhub.com/the-tunic-interview-an-exclusive-chat-with-polymath-designer-andrew-shouldice/), he describes starting with low-poly geometry and flat colors, with lighting supplying depth. Additional detail required restraint: visual density could become inconsistent with the simpler geometric world. This supports improving material/light relationships before adding more surface detail.

In [Shouldice's Game Developer/IGF Q&A](https://www.gamedeveloper.com/business/how-tunic-weaves-wondrous-unknowable-worlds-inspired-by-inscrutable-nes-manuals), he explains experimenting with bounced light on isometric geometry, richer shadows through color correction and a gentle additive screen-space glow. Noise, ambient occlusion and authored texture detail were introduced gradually. These are the developer's own descriptions; they do not establish a single shader recipe that can be copied into another engine.

The [official GDC catalog entry for “TUNIC: This Was Here the Whole Time”](https://www.gdcvault.com/play/1029384/-TU) identifies Shouldice's 2023 talk as design material about secrets and exploration. Its catalog was checked; the talk is not cited as evidence for unobserved rendering settings. A formerly linked Unity technical article returned 404 during this research, so copied versions are not treated as primary technical evidence.

Project application, rather than claims about TUNIC's exact renderer:

- Keep large, readable masses and the existing silhouettes. Reduce contrast and repetition in stone seams and water patterns so characters, bombs and pickups remain dominant.
- Use a warm key light, restrained cool fill, grounded contact shadows and subtle ambient occlusion. Preserve readable colored helmets and face panels in shadow.
- Use modest roughness/color variation and localized emissive effects. Strong full-screen glow or busy procedural noise would work against the eight-player arena.
- Fit the entire playable island within the HUD-safe region at each viewport aspect ratio. A more intimate diorama camera is useful only if front walkways, court and summit remain visible.

## Bomberman 64: selection footage actually inspected

The existing research copy of [GameFace's original multiplayer recording](https://www.youtube.com/watch?v=TM25Xa0DgCI&t=9s) was sampled densely from 00:00–00:24 at half-second intervals. Original footage remains under ignored research artifacts; no frames or audio from it become project assets.

At approximately 00:09–00:16, Player Select presents four full-body white, black, red and blue characters in one row above MAN/COM/OFF choices and CPU-level rows. Across adjacent frames, feet and arms change position and the heads bob. Green column highlights and small red directional cursors identify selection. This is a living character presentation rather than a sheet of static faces. The frames establish animation, not the original internal mesh/sprite implementation.

Around 00:17, Stage Select shows a large arena thumbnail and red side arrows. Around 00:18, the players return to the character screen. The publisher's [original English manual, printed pp. 22–25](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf) independently documents player type/CPU level selection followed by stage selection.

An eight-seat roster, arbitrary appearance changes and separate ready feedback are project adaptations. The inspected footage does not prove that the original screen offered an unrestricted color palette. Reusing our actual articulated Bomber model for previews gives immediate consistency with the match: selected characters can walk or turn, while other active seats retain subtle idle motion and disabled seats clearly read as absent.

## Verification targets

Use real button activation to enter the roster, change seat type/appearance, continue to rules and start a match. Confirm both the recorded selections and actual preview poses evolve; an incrementing UI counter alone is insufficient. Verify that chosen appearances reach local match presentation, with sensible human/CPU totals.

Fullscreen should be the normal product launch, with a deliberate windowed override for automation. Test actual window modes and keyboard toggles in a graphical run, and test 16:9 and 16:10 layouts independently. Camera checks should project actual stage geometry into the viewport and enforce the HUD-safe area, followed by screenshot inspection. Existing bomb, powerup, flood, results and real-network checks remain required.

## Original character closeups

The same publisher-hosted English manual was also rendered at higher resolution for a direct look at the large background character art on PDF pages 3–4 (printed pp. 2–5). The helmet has a broad front with a rounded crown and softened corners; the peach face panel sits inside a visible white shell rim. It is a flattened, rounded helmet shape rather than a sharp box or a perfect sphere. Narrow vertical black eyes, a pink spherical antenna knob, round mittens and rounded boots preserve the silhouette.

For our original procedural model, a smooth squircle helmet and face inset are appropriate. Keep the face panel slightly narrower than the shell, with rounded corners and enough depth separation to avoid intersecting edges. A head width around 1.5–1.8 times the torso width and roughly one third of the full silhouette height are useful visual starting points, not measurements recovered from the original assets. Face readability from the arena camera matters more than adding bevel or texture detail. The reference is the [Nintendo original manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf), not a later game's model or fan remake.
