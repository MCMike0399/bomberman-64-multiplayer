# Iteration fifteen: deliberate walking contacts

Inspected 2026-10-21. The user requested less rapid, tiny stepping and a more deliberate walking rhythm resembling the 1997 Bomberman 64. This changes presentation only: authoritative movement, continuous direction, collision, and snapshot interpolation retain their existing behavior.

## Direct reference and limits

The cached [XFactor1111 multiplayer recording, 00:05.60–00:06.40](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=5s) was inspected in consecutive 15-fps contact-sheet samples. Blue walks upward and right on the left grass, with alternating pink boots visible below the body and relatively restrained helmet movement. The changing foot silhouette reads as separate contacts beneath continuous travel. White is carrying a large bomb nearby; the visible carry does not turn into a large rhythmic arm swing.

Ignored research evidence is `artifacts/research/iteration15/walk-5-7.jpg` and the enlarged nearest-neighbor `blue-walk-detail.jpg`, extracted from the previously cached original recording. These are reference frames, not distributable game assets. The recording is 854×480 at 29.97 fps, with very small character silhouettes, perspective, hidden inputs, and compressed/repeated poses. It supports qualitative contact rhythm and restrained bob. It does not establish an exact original stride duration, stance percentage, world-space foot trajectory, or walking speed. No chosen animation constant below is represented as reverse-engineered.

The [original Nintendo-hosted English manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf), printed pp. 4–5, documents stick movement and separate object handling actions; it does not prescribe walking animation cadence. Existing [iteration eleven](iteration11-kick-research.md) and [iteration twelve](iteration12-bombs-research.md) retain the separate kick/drop/carry evidence and its limits.

## Diagnosis and implementation

The previous animation used a uniform `sin(distance * 5.8)`, completing a left/right cycle every 1.083 world units. At the project's ordinary 4.5-unit/second movement speed that produces about 8.3 individual steps per second. Both feet continuously rotate around their leg pivots, which also lifts a supporting boot as the pivot turns.

The revised full stride spans 1.9 units, approximately 4.7 individual steps per second at that same speed. Each foot spends 64% of its cycle in low support and 36% returning forward on an eased swing. The feet are offset by half a cycle, giving a brief both-feet-down interval around each contact. Maximum local clearance is .045 units. Boot counterrotation and pivot-height compensation keep the supporting boot low, with forward reach distributed between the leg and its animation translation. Bob follows the returning foot and peaks around .014 units, with the existing head counter-motion retained.

These are deliberate project tuning values. This is a stylized contact gait, not world-space inverse kinematics: the short cartoon limbs do not pin a boot to one exact ground coordinate over the complete long stride. The important comparison is longer, readable support and fewer steps beneath uninterrupted movement.

Phase advances only from horizontal rendered distance, wrapping each stride. Releasing input freezes phase and lets the existing speed envelope settle the pose. No authority pause is introduced to suggest weight. Carry, pump, drop, kick, throw, recovery, ghost and menu-preview animation continue to compose through the existing action system. The mesh dimensions, materials, face/head shape and resting boot orientation are unchanged.

## Verification

The movement playtest samples real `BomberModel` transforms at 30, 60 and 120 FPS. It checks bounded stride count over 18 units, sustained supporting-foot height, both-feet-down intervals, bounded returning-foot clearance, equal-displacement end-pose agreement, and settling without taking further steps after stopping. Foot-height diagnostics read the actual boot-center transforms rather than reporting the gait curve directly. Existing movement checks also verify continuous rendered displacement and release without drift. Graphical recording and integrated action/menu verification are recorded with the final iteration evidence.

## Recorded walking witness

The focused `walk-feel` scenario uses the actual stock-temple practice actor and ordinary routed movement to reach the clear courtyard row eighteen. A temporary side camera follows that actor for roughly two seconds right, two seconds left, and one second of release/settling. The camera is restored afterward. No character transform, animation phase, stage geometry, authority state or movement speed is rewritten for the recording.

`walking-showcase-begin`, `walking-showcase-release` and `walking-showcase-end` trace events identify movie frames for a normal-speed clip. Per-frame samples retain authority/render position, foot pose/clearance and facing through the reversal and stop; leg summaries report measured travel, frame count, displacement and clearance. The real-scene checks require uninterrupted horizontal travel beneath supporting-foot contacts, alternating foot reach, a turn on the same clear path and drift-free settling. Captures are `walk-feel-side-ready`, `walk-feel-turn` and `walk-feel-side-settled`. This inspection camera is a presentation witness of real gameplay, not the usual battle camera.

## Personality follow-up

The user's review of the first recorded gait was that it still felt stiff and needed more caricature, life and style. Slower feet alone did not satisfy that request. The second pass keeps the same 1.9-unit stride and measured low-foot support, but phrases the upper body around contact: .032-unit support-side hip shift, .075-radian weight roll, .07-radian shoulder twist, stronger slightly asymmetric arm swings, and a helmet pose that follows with an approximately .1-second soft lag. A restrained .006-unit spring and 1.5% suit compression mark toe-off/contact. The helmet counter-scales that suit compression to retain its approved round proportions. Carry, kick, placement, throw and recovery damp these extra accents so their action poses retain priority.

The actual second-pass side recording is `artifacts/iteration15-personality/20261021T153300.913369Z-walk-feel/gameplay.mp4`, 340 decoded frames at 30 FPS, normal speed. The short review clip is `artifacts/iteration15-personality/walk-feel-personality.mp4`, starting at source 6.0 seconds and lasting 5.25 seconds; it was decoded in full without errors. The movement regression still passes all 33 checks; the actual recorded walking witness passes 24 checks with zero engine errors. Build completed with zero warnings/errors. Pure profile footage emphasizes the glove swing and helmet follow; the ordinary battle/three-quarter view additionally reveals the lateral hip weight shift.

The full exported suite exposed an older roster test that compared only two leg angles five frames apart. With contact/swing phrasing, opposite sides of a contact can have nearly equal endpoint angles while the animation between them remains lively. A phase sweep at the roster's 2.1-unit/second preview speed reproduced false negatives in about 2.9% of starting phases. The roster check now observes 24 consecutive actual preview poses, requires substantial forward/backward excursion and an advancing animation clock, and records the samples. This catches a frozen preview without depending on which point in its stride the preceding menu interactions leave it.

## Small helmet follow-up

The user initially requested a little more spherical fullness, then clarified the desired silhouette: slightly shorter vertically and softly oval for a cartoon feel. Final full shell dimensions are .854 × .750 × .810, compared with the previous .854 × .760 × .790. Width, face patch width/height and eye spacing stay the same; front-to-back depth retains the small increase, while vertical height is lower. The crown charm retains its size and follows the crown. This geometry refinement is independent of the gait; front/side/three-quarter README captures come from the final exported app.

The final public walking clip was refreshed from the exported app after the softly oval helmet adjustment: `artifacts/iteration15-final-walk/20261021T160936.232215Z-walk-feel` (24 checks, three captures, 340 frames). The 5.25-second excerpt starts at 6.0 seconds and decodes in full at normal speed.
