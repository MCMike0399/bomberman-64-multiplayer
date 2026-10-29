# Round and match ending reference research

Investigated 2026-10-28 for Blast Circuit. Scope: **Bomberman 64 (1997)**,
Single Battle. This does not use the 2001 Japanese game, The Second Attack or
Super Bomberman R's unrelated Battle 64 mode as references.

## Sources actually inspected

| Source | Inspection |
| --- | --- |
| [Hudson/Nintendo English instruction booklet](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf#page=13) | Rendered printed pp. 22–23, PDF page 13. Establishes Single Battle's elimination objective and configurable battle count/time; does not specify announcement timing. |
| [XFactor1111, original multiplayer gameplay](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=46s) | Existing research recording, sampled every 0.25 seconds from 0:40–0:58. Examined elimination, arena hold, fade and coin results. Also sampled 6:20–6:50 every 0.5 seconds for a winning point followed by a new arena. |
| [GameFace, four-player Battle Part 1](https://www.youtube.com/watch?v=TM25Xa0DgCI&t=516s) | Existing research recording, sampled every 0.25 seconds from 8:32–8:47. Examined simultaneous final deaths and the visible Draw announcement. Commentary portraits and checked sidebars are the recording's presentation, not game UI. |
| [MechaFungus, Battle Announcer Voice Clips](https://www.youtube.com/watch?v=ckaYS8cVH9g&t=3s) | Downloaded the original-game audio compilation (7.12 seconds). Local whisper.cpp with unprompted English base.en transcription identifies Game, Draw and Time's up; tiny.en independently agrees on Game and Draw. This is machine transcription, not a claim of a human listening review. |

Reference recordings, audio and extracted frames remain outside tracked source.
No Nintendo/Hudson voice, art or music becomes a Blast Circuit asset.

## Observations

The original has separate round-end callouts. The inspected audio compilation
contains **Game!** at approximately 0:03.14–0:04.44, **Draw!** at
0:04.44–0:05.78 and **Time's up!** at 0:05.78–0:06.94. Ready, Go and Hurry up
precede these. These are transcript segment boundaries in the compilation,
not original game's cue durations or event triggers. **Finish!** is not the
word identified in this original-game sample.
[Original announcer recording](https://www.youtube.com/watch?v=ckaYS8cVH9g&t=3s).

A winning round retains the battlefield briefly before showing results.
Around 0:47 in XFactor1111's recording, the final elimination leaves the HUD
clock fixed at 1:27. The arena and elimination aftermath remain visible until
the fade near 0:49. The sky-background results then animate a large gold
star/coin into the winner's score row. The next round follows around 0:56.
The inspected winning sequence does **not** establish an on-arena written
Game banner; its useful evidence is the breathing space before the coin
results, rather than an immediate screen replacement.
[Winning round, approximately 0:46–0:57](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=46s).

A draw has an explicit announcement over the arena. In GameFace's Pyramid
sequence, the last combatants die in the same blast around 8:37–8:38; the
clock remains at 1:32 and all four portraits have elimination marks. The
large yellow-green **Draw** word grows over the still-visible Pyramid around
8:41–8:43. The following round has zero wins for every player. The uploader
cuts rapidly into that next round, so this passage cannot establish a full,
uninterrupted original draw-transition duration.
[Simultaneous elimination and Draw](https://www.youtube.com/watch?v=TM25Xa0DgCI&t=516s).

The multi-round score is separate from the individual round ending.
XFactor1111's sequence around 6:34–6:42 retains the arena after an elimination,
then awards another gold coin to red on the sky scoreboard; a new Pyramid
game follows with scores reset. This supports keeping the outcome beat
before results even on the final point. It does not prove a distinct final
match voice line, a mandatory winner-podium sequence or an exact match-win
delay; the inspected audio compilation has no separate match-winner callout.
[Score award and next arena](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=394s).

The manual defines the winner of Single Battle as the player who eliminates
all opponents and exposes battle-count and timer settings. It does not
document how simultaneous deaths are ordered, whether inputs remain live
during the finishing beat, or the transition duration. Those details must
come from this project's shared rules, rather than inferred original constants.
[Original booklet, printed pp. 22–23](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf#page=13).

## Adaptation for Blast Circuit

Preserve a readable finishing beat in the arena for ordinary rounds **and**
the match-winning round, then let the existing round/match results proceed.
Use the original-inspired wording **Game!**, **Draw!** and **Time's Up!**,
with an explicit player name and round-versus-match detail where there is a
winner. An on-arena Game banner is a deliberate legibility adaptation, not
a claim that the inspected original winning frames display that word.

The hold duration, animation, text styling and synthesized audio cues are
Blast Circuit choices. A three-second authority-owned Over phase gives local
and online players the same observable outcome beat without making network
clients invent a delayed result. Exact original timing and voice reproduction
are not requirements supported by these references. Outcome handling must
still prevent late bombs, movement or disconnects from changing an accepted
winner, award each point once, and retain the correct draw/time-up reason.
