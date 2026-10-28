# Avalanche crush fix — 2026-10-28

Real local four-human battles, no authority edits, isolated macOS QA VM,
Metal Forward+, Medium preset, 1280×800 captures. Blizzard wind pushes the
right-side players into the avalanche first; their real deaths create the
pictured crush stand-ins. Greedy Trap and Top Rules retain simultaneous
left/right closures. Inspection captures pause only cosmetic stand-ins at
selected animation ages and move the camera; the battle capture uses the
ordinary game camera. The flat pose keeps its suit color and squeezed eyes,
then disappears in dust. These are agent-inspected captures, not user art approval.

- [Blizzard battle](wall-death-blizzard-battle-battle.png)
- [Blizzard flattened victim](wall-death-blizzard-battle-flat.png)
- [Blizzard dust puff](wall-death-blizzard-battle-puff.png)
- [Greedy Trap](wall-death-greedy-trap-flat.png)
- [Top Rules](wall-death-top-rules-flat.png)

Reproduce: `python3 tools/playtest/run.py --scenario wall-death`.
The exported graphical run passed 32 assertions with 13 screenshots and zero
runtime errors/warnings. The source headless run passed 31 assertions; its
existing ObjectDB shutdown warning remains. Before the fix, the extended
journey failed at the missing Blizzard crush stand-ins. The cause was the
selector accepting only `columns`, while Blizzard emits `avalanche`.
[Source and image hashes](manifest.json).
