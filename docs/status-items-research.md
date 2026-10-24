# Temporary item effects: original Bomberman 64

Researched 2026-10-24 for advanced match settings and reduced arena clutter.
This concerns the 1997 3D game, not the unrelated Japanese 2001 title or
later Super Bomberman games. See also [the gameplay research](history/gameplay-research.md).

## Publisher evidence

The [Nintendo-hosted English manual, printed pp. 26–27](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf)
identifies the following Skull effects:

| Effect | Documented behavior |
| --- | --- |
| Big | Enlarges the character and slows movement. |
| Tiny | Shrinks the character and speeds movement. |
| Disarmed | Prevents placing and pumping bombs. |
| Reverse | Inverts analog movement strength: greater stick deflection means slower movement. |
| Disguise | An ordinary-looking bomb has pumped-bomb power. |
| Instant | Bombs detonate sooner. |
| Fire | A burning state with a fatal consequence if its objective is not met before expiry. |

The separate Evil item creates brief arena-wide events: growth/slowness,
a twister, throwing, shuffled player positions, maximum bomb power, rainbow
lighting or intermittent dizziness. The manual limits Evil to one appearance
per battle. It does not specify numeric Skull duration, scale or speed ratios,
drop probabilities, collision changes, or item despawn timers.

The manual PDF was opened online; the existing local rendering of its item
spread (`artifacts/research/manual-page-15.png`) was visually inspected.
The reference image remains ignored and is not a game asset.

## Suggested Blast Circuit adaptation

These are proposed project rules, not reconstructed original constants:

- Add temporary Tiny and Giant effects first. They directly satisfy the
  remembered combination of size and movement changes. Suggested starting
  tuning: ten seconds, Tiny at 0.65 visual scale and 1.5 movement multiplier;
  Giant at 1.4 visual scale and 0.65 movement multiplier.
- If adding separate Haste and Slow pickups, identify them as project
  extensions. The cited original manual combines speed changes with size.
- Keep authoritative movement modifiers in the shared simulation and publish
  the accepted effect and remaining duration for every client. Expiration,
  death and round reset must restore ordinary behavior.
- Preserve the compact model's proportions through uniform visual scaling.
  Keep collision dimensions unchanged initially to avoid growth trapping
  players in narrow passages; document this explicit adaptation.
- Use one active temporary effect at a time, replaced by a new pickup, with
  bounded duration rather than unbounded stacking. Show the effect and timer.
- Allow each pickup type to be disabled in advanced match settings. Roll among
  enabled outcomes only; turning off all items must produce no drops.
- Item lifetime is independent of effect lifetime. Uncollected pickups should
  expire on the authority clock and disappear from snapshots/rendering in both
  local and online matches. This is an anti-clutter design decision.
- Do not import Reverse as directional inversion while claiming fidelity:
  that differs from the cited 1997 manual. Global Evil events, instant-death
  Fire and contagion need separate scope and balancing if introduced later.

Validate timed expiration at its boundary, replacement and reset, local/network
parity, disabled-drop filtering, restored movement speed and visible model
restoration. Numeric checks alone do not establish readable size effects;
inspect real gameplay captures of both sizes.
