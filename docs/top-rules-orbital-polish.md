# Top Rules: painted orbital surroundings

Source work, 2026-11-15. The ordinary battle-camera captures in
[living stage worlds](living-stage-worlds.md#high-ordinary-battle-camera) are the
baseline. The floating castle, crimson court, green stairs, gray masonry,
lighting profiles, camera and playable geometry are retained.

## Art intent

The existing planets had bright, nearly regular stripes and thin luminous rings.
The surroundings should read as a small painted astronomical diorama around the
castle: varied soft cloud bands, a rounded shadow side, restrained atmospheric
limbs and a broad layered ring. The blue world is smaller and quieter so the
castle and combat remain the visual focus. The moon has fewer broader craters
and softly mottled plains, rather than dense stippling. Slow motion remains
ambient, with no attack trails, warning circles or paths across the court.

The two ring annuli have a genuinely empty center and division. Opaque,
double-sided triangles give a stable silhouette and let the globe obscure the
rear half without transparency sorting. A subdued painted shadow connects the
ring to the globe. Violet and blue nebula folds add depth in the existing dark
surround, with a radial fade before the backdrop ends. Planet cloud bands and
nebula motion use the existing pausable scenery clock.

## Reference boundary

The [Nintendo-hosted Bomberman 64 manual](https://m1.nintendo.net/docvc/NUS/JPN/NBMJ/NBMJ_J.pdf)
and the earlier [inspected reference notes](living-stage-worlds-research.md)
establish the dark floating-fortress and readable battle-stage context.
[Konami's Super Bomberman R 2 story](https://www.konami.com/games/bomberman/r2/us/en/character/)
was rechecked for this pass: its black moon threatens planetary cities, and its
characters visit planetary ruins. That supports a celestial extension within the
series vocabulary; it does not establish planets as an original Top Rules
feature. These planetary models, shaders and ring geometry are original work.
No reference media or recovered assets are distributed.

## Review and verification

Root verification owns the build, graphical desktop and export. Review the
`stage-scenery` journey's `top-rules` ordinary Medium/High Midday and Night
captures against the baseline, followed by its motion capture. Establishing
views inspect the full ring, planetary shadow and nebula but do not replace
ordinary battle-camera approval. Check both 16:10 and 16:9 framing, where the
large worlds approach the image edges.

The existing journey checks finite textured geometry, absence of collision
bodies, all props outside standing space before/after motion, local pause,
Low visibility and miniature omission. Its mesh winding audit also covers the
new ring faces. These checks establish safety and renderer contracts; actual
captures establish the visual result. No simulation, collision or rules changed.
