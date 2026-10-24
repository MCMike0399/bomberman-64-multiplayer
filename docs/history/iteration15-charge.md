# Iteration fifteen: carrying, charging and deliberate throws

Verified 2026-10-21. Carrying now rewards setup with a larger blast and farther projection, while neutral throws clear the carrier and leave retreat space. The bomb remains carried after releasing A; fresh A presses pump it, and B deliberately throws the A-latched carry. Legacy B-hold/release handling remains supported. Visual bomb scale still caps at 2.3.

## Two reproduced causes

The first failure was authoritative: an ordinary A lift, three fresh A pump presses and neutral B throw knocked down the stationary owner with `falling-bomb` on the fourth simulation tick. The old neutral-stick multiplier produced only 1.2 cells/second of horizontal speed, so descent began inside the owner's contact radius. The regression exercises the actual `LocalMatch` input path, rather than disabling owner collisions.

The second failure was visible release continuity. After the original fix, a real rendered, settled bomb at world Y 2.9964 dropped to 2.8495, 2.7742 and 2.8405 during its first three detached frames. The held bomb exactly matched its actual hand socket before release, ruling out unfinished lift interpolation. The actor's 1.12 scale puts the settled socket about 1.65 world units above its feet; the old .9 gameplay-unit launch height rendered at only 1.215 world units.

## Shared throw rules and reward

`BombThrowRules` supplies both authority and advisory aim prediction. Its horizontal speed in cells/second is:

```text
6 × (0.75 + 0.25 × clamp(stick magnitude, 0, 1)) + 0.65 × clamp(pumps, 0, 3)
```

Neutral speed is 4.5 before pumping and 6.45 at three pumps; full-stick speed ranges from 6 to 7.95. Each pump therefore adds useful projection alongside the existing blast-radius growth. This helps place pressure beyond the throw position and makes retreat practical. Aiming still changes reach continuously. This tuning adds no cooldown or owner immunity: opponents and an owner chasing their own descending bomb can still receive head hits; ordinary blast damage and fuse behavior remain active.

Shared launch height is now 1.22 gameplay units, or 1.647 rendered world units at the terrain's 1.35 height scale. Carried bomb height metadata uses the same constant. Authority and preview share the same 30 Hz ballistic step, gravity and launch velocity. The graphical regression now measures an immediate rise: uncharged Y 2.9964 becomes 3.0598, 3.0923 and 3.2141; charged Y 2.9985 becomes 3.0917, 3.1396 and 3.3297. Carried fuses remain paused and resume after release.

## Advisory aim

Only controlled local humans receive the dotted arc and landing marker. Online identity comes from the connected player's ID; other participants' intentions are not shown. The guide disappears when carrying ends. Aqua-to-gold charge feedback and the enlarged M PLUS label retain the established interface palette and fonts.

The guide samples current static terrain and conservatively truncates at pillar/cover columns and stage bounds. Its stop label describes the guide ending; it does not assert that authoritative flight collides at that point. Player contacts, moving bombs, scenery changes and movement between the displayed snapshot and release can change the result. Existing thrown-bomb collision rules were not rewritten. A short remaining fuse truncates prediction before further flight.

Water and void endpoints now have explicit classifications. The renderer suppresses the resting-landing ring and labels them “sinks here” or “falls away here.” A read-only stage-cell query supplies the classification at terrain contact. Long charged throws can also leave the arena under the existing removal rule. The tested neutral-charge comparison uses the stock temple's clear east-west terrace lane, where both throws land on walkable ground.

## Verification and evidence

The counts below are the completed height/label verification. The subsequent water/void marker follow-up adds a pure ordinary-input regression for both elimination surfaces and a stock-temple graphical shore throw asserting the actual marker visibility, sink label and removal before blast. These extended checks subsequently passed: 113 managed game checks (`artifacts/iteration15-managed-delivery-fixed`) and 105 native graphical charge/aim checks with seven captures and zero runtime errors (`artifacts/iteration15-endpoints`). The earlier height-only recording below remains a separate witness.

- `dotnet run --project tests/Bomber.Game.Tests`: 112 checks pass. Coverage includes sixteen pump/aim combinations, held-state retention, paused/resumed fuses, increased projection, overhead-height continuity, real predicted-versus-authoritative landings, enemy contact and returning owner contact.
- `dotnet build Bomberman64Multiplayer.csproj --no-restore`: zero warnings/errors.
- Windowed native graphical `charge-aim`: 89 checks, six actual viewport captures, zero runtime errors. Ordinary A/B inputs on the stock temple verify held carry, the first three detached render frames, size cap, camera visibility and predicted-versus-actual neutral landing. The recorded MP4 decodes all 407 frames at 30 FPS, 13.566667 seconds, normal speed.

Ignored local evidence:

- Release-dip RED: `artifacts/iteration15-release-red/20261021T152328.987513Z-charge-aim/trace.jsonl` and `failure.png`.
- Height/label GREEN: `artifacts/iteration15-release-green/20261021T152446.169191Z-charge-aim/`; see `charge-aim-3-held.png`, `gameplay.mp4`, `trace.jsonl` and `runner-summary.json`.

Existing [original-gameplay research](gameplay-research.md#pumping-visibly-enlarges-the-carried-bomb) describes inspected [original footage around 00:04–00:07](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=4s): overhead carrying, visible bomb growth and release support the qualitative handling direction. These new launch heights, speed bonuses, input timing, fuse rules and advisory UI are project choices, not measured original-game constants.
