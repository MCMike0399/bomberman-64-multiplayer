# Iteration fifteen: a short-legged cartoon trot

The user rejected both the slow contact walk and its added upper-body sway as stiff, robot-like and lacking character. The current pass replaces that approach with three contrasting silhouettes: compressed contact, a gathered passing boot, and a light airborne reach. Visual success remains subject to the next actual-game recording and user review.

## Reference and adaptation boundary

The cached [XFactor1111 Bomberman 64 multiplayer recording, 00:05.60–00:06.40](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=5s) was inspected in consecutive 15-fps samples. Blue traverses the grass with distinct alternating pink boots and continuous travel. The tiny 854×480 silhouettes, compression, perspective and hidden input do not establish an original stride duration, stance percentage, world-space foot trajectory or movement speed. The [Nintendo-hosted English manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf), printed pp. 4–5, describes movement and object actions but gives no animation constants. Reference sheets remain ignored under `artifacts/research/iteration15/`.

This trot is an intentional caricature responding to the user's latest artistic direction, not a claim of reverse-engineered original animation. The earlier 64% support walk and subsequent sway pass were rejected; their recordings document that feedback rather than visual approval.

## Current animation

A complete left/right stride still covers 1.9 rendered horizontal units. Each boot now has a short 38% planted phase followed by a curved recovery with about .125 units of clearance. Front/back reach is larger, the returning boot arcs slightly outward, and the sole rolls from heel contact through toe-off instead of remaining mechanically flat. The half-cycle offset creates a brief interval with both boots light.

The suit compresses briefly at contact, rises about .052 units into the light phase and lands quickly into the next contact. A purposeful forward lean and delayed, asymmetric glove arcs support that same beat. The small white leg stems stretch/turn between the hip and ankle while the support boot stays low, avoiding a detached leg when the suit rises. This is pose deformation of the existing procedural parts; mesh shapes and approved helmet dimensions remain unchanged. The helmet shares most of the bounce, with a restrained follow pose; counter-scale preserves its round proportions during suit compression.

Supporting boot centers are seated against the animated body transform, so the forward lean and bounce do not themselves push a planted boot through the floor. Returning boots share the rise after toe-off. Foot diagnostics now measure actual boot-center height in character-root space, including the animated body, rather than the former body-relative curve. They describe center clearance, not a full sole/terrain inverse-kinematics solution.

Carrying reduces the accent; kick, drop, pump, throw, death, victory and recovery poses suppress it while their existing action motion takes priority. Phase advances from rendered horizontal travel only. Releasing movement freezes phase and settles the pose. No movement pause, collision change, authority mutation, interpolation change or camera change is introduced by the gait.

## Verification and recording

The movement harness samples actual model transforms at 30/60/120 FPS. It checks stride count over a fixed displacement, short planted contacts plus a readable light interval, visible bounded boot clearance without sinking, bounded suit compression/bounce, equal-displacement phase agreement, and settling without extra steps. The ordinary movement checks retain continuous root displacement and no release drift. Existing action/profile scenarios retain carry/kick/drop/recovery and helmet checks.

`walk-feel` routes the actual stock-temple practice actor through the clear courtyard using ordinary controls. A temporary side camera follows about two seconds right, two seconds left and one second of release. Trace begin/release/end events identify movie frames; samples retain authority/render position, pose, clearance and facing. Checks now require planted push-off, gathered recovery and brief light flight. Captures remain `walk-feel-side-ready`, `walk-feel-turn` and `walk-feel-side-settled`.

The roster test observes 24 consecutive real preview poses and an advancing animation clock. Its former two-angle/five-frame comparison could alias opposite sides of a stride and report a frozen preview despite substantial motion between samples.

The new trot compiles cleanly. Runtime measurements, action regressions and fresh video review are pending the root agent's graphical validation; previous-pass recordings and assertions do not establish success of this replacement.

Focused real-engine validation passed: 35 movement assertions and 24 recorded walking assertions with three captures and zero runtime errors. The source recording at `artifacts/iteration15-trot-video` contains 333 decoded frames at 30 FPS. The public 4.7-second normal-speed excerpt starts at movie frame 188 and ends at frame 329, covering the rightward trot, reversal and settle. Consecutive frames were inspected for clear gathered/reaching poses, connected white leg stems and boot clearance; no detached limbs or shell/face clipping was observed. This is a qualitative inspection, not a claim that the user approved the motion.
