# Iteration sixteen: compact chibi limbs and continuous recovery

> Historical evidence: this document records its iteration. Read the [current maintenance guide](agents/visuals-and-animation.md) before reusing controls, animation tuning or compatibility claims.

The latest feedback rejects the visibly extending legs in the first iteration-sixteen recording. Earlier continuity checks passed, but did not guard the character's compact proportions. The final source removes the extension entirely: short white leg stems and pink boots rotate together from fixed hips. Head and face geometry remain unchanged. Artistic success still requires the new actual-game recording and user review.

## References and interpretation

The user-provided original Bomberman 64 player-select images were inspected directly. They show a large head over a small blue suit, very little visible white leg between belt and broad pink boots, and rounded gloves. Their scale and still poses do not establish stride timing or world-space foot planting.

The official [Konami character page](https://www.konami.com/games/bomberman/online/asia/en/character/) and [Super Bomberman R 2 illustration gallery](https://www.konami.com/games/bomberman/r2/asia/cns/guideline_img/) were consulted. The [White Bomber illustration](https://www.konami.com/games/bomberman/r2/s/img/guideline_img/main_white.png) is **2D action artwork**, not a 3D model render: its expressive extended limbs should not be mistaken for evidence that this game's walking legs should stretch. The useful common cues are oversized helmet, gloves and boots around a small body. No external character assets were imported.

An actual primary **3D render** was also inspected on Nintendo's official [Super Smash Bros. Ultimate Assist Trophies page](https://www.smashbros.com/en_GB/item/assistfigure.html). Its HTML identifies Bomberman with the transparent [character render](https://www.smashbros.com/assets_v2/img/item/assistfigure_sub03.png) and two gameplay images, including a [close in-game view](https://www.smashbros.com/assets_v2/img/item/assistfigure_main03_01.jpg). This rendition has a large rounded helmet, inset cream face, small tapered blue suit and rounded pink extremities. Its visible white legs are appreciably **longer and slimmer** than the user's original Bomberman 64 player-select reference. It is useful for face/suit/material reference, but not a reason to reintroduce long legs against the user's explicit direction. The second screenshot is an effects/action pose, not a gait reference; `assistfigure_list03.png` is a pixel icon, not a render. The downloaded HTML and four images remain ignored under `artifacts/research/iteration16/`; none are shipped assets.

The actual first-pass `walk-feel-turn.png` in `artifacts/iteration16-walk/20261021T214215.201869Z-walk-feel` shows a long exposed rear white leg while the suit rises. The user's objection is visible there despite the first pass's passing numeric checks.

## Two separate reproducible problems

The original foot curve had a pitch jump at toe-off and touchdown, then a velocity corner when linear supporting reach changed to cosine recovery. `python3 tools/playtest/run.py --scenario movement --headless --output artifacts/iteration16-character-red` failed against the original model: boot angular speed rose from 7.099 rad/s at 30 FPS to 16.221 at 60 FPS. Fixing only pitch brought 60 FPS down to 8.600, while boot-center acceleration still failed at 120 FPS with 585.997 units/s². Recovery reach now matches the preceding and following backwards velocity; pitch carries continuously from toe-off into heel contact.

The user's later stretch objection required a separate regression. `python3 tools/playtest/run.py --scenario movement --headless --output artifacts/iteration16-shortlegs-red` measured white stems reaching **2.4897 times** their rest length, with the leg pivot **.26986 units** from its nominal hip. That is an actual change of silhouette, not a frame-cadence problem. It reproduced the exact unwanted extension before the final fix.

## Final geometry and motion

The torso is .395 wide by .380 tall by .370 deep, compared with .430/.380/.400 before this iteration. Belt and shoulders follow the modest slimming. The helmet remains exactly .854 wide, .750 tall and .810 deep, with its separate curved cream face and existing eyes/crown ornament. Pink glove and boot mesh sizes remain unchanged.

`SeatWalkingFoot` and `TrotLegStem` are removed. Each .190-high white stem remains at its constructor position and scale, attached to its own hip; the boot shares that rigid limb transform. No moving hip pivot or per-frame stem scale/orientation can produce an elongated recovery leg. Boot centers sit slightly farther back under the suit. The grounded inverse-motion correction is gone; a stylized amount of foot glide is preferable to stretching the silhouette to chase a stationary contact.

Leg swing is reduced from .78 to .48 times the same continuous reach curve. Body hop falls from .052 to .025, suit compression from .030 to .018 and forward lean from .13 to .08 radians. Glove and head follow-through still share the existing beat. Full stride distance remains 1.9 units, driven by actual rendered travel. Input, authoritative movement, collision and root interpolation are unaffected.

The movement and walking tests now require readable alternating compact boots and fixed leg attachment. Their old demand for a particular support/flight fraction was removed because it would force the rejected extended-leg solution back into the implementation. Support/flight center-height metrics remain diagnostic only; they are not sole contact or terrain IK measurements.

## Final source validation

`dotnet build Bomberman64Multiplayer.csproj --no-restore -v quiet` passed with zero warnings/errors. The same movement command with output `artifacts/iteration16-shortlegs-green` passed **45 assertions, zero runtime errors**, in .98 seconds. The exact report is `artifacts/iteration16-shortlegs-green/20261021T214645.328693Z-movement`.

The actual animated stem ratio is now **1.000**, and hip separation **0.000** throughout four seconds of movement. The same composed-transform continuity probe remains green:

| Samples per second | Maximum boot angular speed (rad/s) | Maximum boot-center acceleration (units/s²) |
| --- | --- | --- |
| 30 | 8.306 | 65.081 |
| 60 | 8.649 | 78.365 |
| 120 | 8.722 | 95.233 |
| 1200 diagnostic | 8.770 | 123.959 |

The 1200 Hz probe exposes curve boundaries; it is not a display-rate or performance claim. At 30/60/120 FPS, maximum boot-center clearance is .0961–.0964, alternating height separation .0728–.0734, and minimum startup clearance -.0130 or better. Equal-displacement phase, continuous real-scene root travel and release settling checks remain in place. These bounds catch motion defects, not charm or subjective fluidity.

The walking showcase now logs and checks fixed stem length, fixed hip attachment and actual boot angular speed through both travel directions and reversal. The root agent owns the new graphical recording and full action/profile regression reports; the preceding stretchy-leg recording is explicitly superseded.

An independent inspection of the root agent's new `artifacts/iteration16-shortlegs-contact.jpg` compared twelve real-game front, turning, side and recovery samples. The white stems remain short and meet the suit and boot throughout those samples; the long diagonal exposed stem from the preceding recording is absent. Broad pink boots and the large helmet remain readable. This checks silhouette and attachment in sampled images only; normal-speed motion and artistic approval are separate judgments.

## Recorded compact-leg witness

The final compact-leg source recording is `artifacts/iteration16-shortlegs-video/20261021T214719.259257Z-walk-feel`: 26 assertions, three captures, 341 fully decoded frames at 30 FPS, zero runtime errors. The refreshed public clip is the normal-speed interval from movie frame 196 to 337 (4.7 seconds). Twelve consecutive samples were inspected for fixed short stems, hip attachment and the absence of the previous elongated rear leg. This verifies that visible defect; artistic appeal remains a human judgment. No original-game media is included in the public clip.
