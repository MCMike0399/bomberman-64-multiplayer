# Iteration six: character, interface, blast and sound reference

Verified 2026-10-20 against original-game footage and Nintendo's scanned instruction booklet. This is reference research for original procedural assets. Reference images, recordings and extracted audio remain in ignored `artifacts/research/iteration6/` and are not product assets.

## Sources and inspection

- [Nintendo-hosted original English booklet](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf): the high-resolution character illustrations on PDF pages 3–4, printed pages 2–5, were inspected directly. The controller spread documents repeated A presses while carrying for pumping, with B for pickup/throw. The item spread distinguishes regular bomb power capped at 3 from pumped power capped at 6. This establishes a gameplay distinction, without establishing our exact pump count or dimensions.
- [XFactor1111's original multiplayer recording](https://www.youtube.com/watch?v=SPjnYY_Gomo): inspected frames at 4.9, 6.5, 6.7, 7.0, 7.4, 7.7, 8.0, 8.4, 8.8, 9.2, 12, 1489, 1523 and 1527 seconds. These provide carrying, pumping, compact explosions, Pyramid HUD and flooding references.
- [GameFace's original four-player recording](https://www.youtube.com/watch?v=TM25Xa0DgCI): inspected full frames at 9.5, 10.5, 13, 16, 17, 28, 32, 36, 45, 60, 90, 110, 113 and 116 seconds. Additional frames every 0.3 seconds across 31.7–32.6 and 44.0–46.1 establish blast growth. Commentary portraits and red checked video sidebars belong to this recording's presentation; they are not the game's UI.

The existing cached MP4 files contain video streams only. A separate original audio stream for the first 25 seconds of the XFactor recording was downloaded and a 3–15-second clip and spectrogram extracted. The available model interface explicitly rejected audio perception, so no claim below represents an audition or an exact identification of original sound effects. Spectral inspection cannot isolate overlapping music and action sounds into reliable kick/pump/throw labels. Exact original timbre and cadence remain unverified.

## Character observations and implementation guidance

The [player-select closeup at 00:09.5](https://www.youtube.com/watch?v=TM25Xa0DgCI&t=9s) shows a deep, rounded-box helmet with a broad, almost square front and strongly softened corners. The face is warm cream, nearly fills that front, and retains a visible helmet rim. It has two narrow upright black eyes, no nose and no mouth. A spherical pink knob sits above a short stalk. The head should not read as a featureless ball or a sharp cube.

That same frame shows the white character's blue shirt, black belt and small gold buckle, white limbs, pink mittens and large pink oval boots. Black, red and blue characters have matching dark/red/blue helmet and torso colors; the cream face and pink accents remain shared. Legs are thin and separated, with outward-facing rounded boots. The whole pose reads clearly at small size. Across adjacent roster frames, arms and feet change position: a walking/idle presentation is visible, rather than a static face tile. The booklet's large rendered illustration independently supports a deep helmet shell and recessed face opening.

Suggested procedural proportions are design inference, not extracted model measurements: make helmet width around 1.7 times shirt width, let it account for roughly one third of total height, keep white legs visually separate, and make the face taller and broader than the current narrow inset. Preserve a readable blue torso and yellow buckle on the white character. At the match camera distance, face silhouette, glove/boot placement and body colors matter more than surface detail.

## Interface observations and adaptation

[Player Select around 00:09–00:16](https://www.youtube.com/watch?v=TM25Xa0DgCI&t=9s) is one shared stage with animated full-body characters in a row. Short MAN/COM/OFF choices sit beneath characters; a brighter vertical highlight and red triangular arrows show selection. [Stage Select around 00:17–00:23](https://www.youtube.com/watch?v=TM25Xa0DgCI&t=17s) uses one large arena preview with side arrows. The observed personality comes from expressive characters, bright lettering, arrows and patterned space. It does not require separate rounded cards per setting.

The [match at 00:28](https://www.youtube.com/watch?v=TM25Xa0DgCI&t=28s) and [Pyramid at 25:23](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=1523s) put the clock at the upper left, followed by compact character-face and gold win-number pairs across the top. Eliminated portraits show crossed eyes while the win number remains visible. The arena occupies almost the entire remaining image. A [Pyramid warning at 24:49](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=1489s) is large pink text over the playfield.

Project adaptation: use a flat eight-seat stage with short seat controls, full-body previews and a clear selection arrow. Keep concise mode/rule choices on shared rows. Use a narrow upper HUD with a red clock at left, face/win groups, and alive/eliminated status expressed in each portrait. Eight seats, controller connection/ready status and explicit keyboard hints are our extensions. Preserve player identity during death and round transitions; a changing survival count does not replace persistent wins.

## Small and large explosions

In [the first recording around 00:07.4–00:08.4](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=7s), compact explosions begin pale and hot, become orange globular bursts and clear quickly. They occupy only a few character widths. Carried bombs earlier in that same sequence visibly enlarge beyond helmet width before release.

The denser [second-recording sequence around 00:44.3–00:46.1](https://www.youtube.com/watch?v=TM25Xa0DgCI&t=44s) shows a pale star-like ignition, a growing orange/yellow sphere, a large translucent billowing cloud, an expanding pale ring and an overlapping cyan/white sphere. Large bursts occupy several times the footprint of the compact early explosions. Color differences may indicate bomb type or overlapping effects; these frames do not prove that pumping alone causes a cyan blast, nor isolate the exact cause of each chained explosion.

Project adaptation: scale both floor footprint and vertical volume from authoritative blast radius and pump/power metadata. Give small blasts a tight flash, compact orange core and short fade; let large blasts develop a taller cloud, stronger pale center and broader fading ring. Keep the dangerous area legible under translucency. A larger explosion must affect the visible volume, rather than merely extending the same thin ground effect. Suggested timings and size multipliers remain project tuning, not reverse-engineered constants.

## Original sound design guidance

These are proposed synthesized effects for this project, informed by the observed action timing. They are **not verified transcriptions of original sounds**. Separate effects should identify actions without needing the player to watch a HUD message.

| Event | Proposed original cue | Trigger and mix guidance |
| --- | --- | --- |
| Kick | Short low knock with a quick pitch drop | Once when authoritative bomb motion starts; avoid retriggering every moving snapshot. |
| Accepted pump | Rounded rising squeak/chirp with a soft air transient | Once per accepted growth step; successively raise pitch slightly. No cue for rejected input. |
| Throw | Brief airy upward/downward sweep | On carry-to-flight transition; shorter and lighter than an explosion. |
| Small explosion | Sharp click/pop into a short filtered noise body | Tight attack; compact low-frequency tail. |
| Large explosion | Deeper attack plus longer layered noise and a low thump | Increase body/tail more than raw peak loudness; limit simultaneous peaks in chain reactions. |
| Elimination | Descending two/three-note cartoon fall | Once per alive-to-dead transition, distinguishable from the blast that caused it. |
| Water | Short splash with a low plop for drowning, quiet intermittent wash for the hazard | Drowning cue once; ambient wash should not mask movement and pump cues. |

Visual action evidence is sufficient to place these cues relative to actual gameplay transitions. Ear-based comparison with the extracted original stream is still needed before claiming similar timbre. All samples and synthesis parameters shipped by this project should remain original.

## Review targets

Inspect actual roster and match screenshots at native UI size: white-character shirt/belt/limbs, rounded front face, readable timer, persistent wins and distinct eliminated status. Exercise each accepted kick, pump, throw, blast and death through real gameplay and verify one cue per transition. Compare small and pumped blasts in the same camera and location so perspective does not fake a size difference. Check overlapping blasts with eight participants and ensure the clock and survival state remain legible.
