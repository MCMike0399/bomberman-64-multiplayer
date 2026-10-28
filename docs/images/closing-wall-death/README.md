# Closing-wall death review

These are real 1280×800 captures from the isolated macOS QA desktop using Metal
Forward+ and the default Medium preset. They are original Blast Circuit imagery.
[Reference research](../../closing-wall-death-research.md) records inspected N64
footage and separates observations from authored animation choices.

- `before-wall-death.png`: main at `7d090eb`, ordinary four-player Greedy Trap
  closure. The old burning stand-ins are swallowed by the opaque walls.
- `after-greedy-trap-battle.png` and `after-top-rules-battle.png`: real idle
  players eliminated by authoritative closing columns. Both sides leave visible
  physical stand-ins. All four players die together, so the normal Round Over
  banner remains in the captures.
- `after-greedy-trap-squeeze.png`, `after-greedy-trap-flat.png` and
  `after-greedy-trap-puff.png`: inspection camera following one of those real
  eliminations at selected animation ages. Only the cosmetic effect is paused
  to capture its beats; authority is never edited.
- `after-pose-strip.png`: explicitly staged cosmetic stand-ins at 0.08, 0.18,
  0.65, 1.10 and 1.48 seconds under the real arena lighting. The edge-on flat
  silhouette, squeezed eyes, flailing limbs and dust use our existing mishap
  vocabulary. This is a pose comparison, not five simultaneous gameplay deaths.

The flatten pose and 1.95-second effect are an original adaptation. Footage did
not isolate a definitive original crush pose. Simulation and network rules are
unchanged. Visual inspection establishes visible composition; it is not user
approval of the art or a physical-GPU performance measurement.

Reproduce with `python3 tools/playtest/run.py --scenario wall-death-regression`.
[Capture and source hashes](manifest.json) tie the pictures to the tested source.
