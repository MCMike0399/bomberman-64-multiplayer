# Iteration sixteen: original controls and deliberate landing aim

Verified 2026-10-21 for the **1997** Bomberman 64. This note separates publisher-established controls from Blast Circuit design. Reference media remain outside the distributed game assets.

## Confirmed original controls

The [Nintendo-hosted original English manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf), printed pp. 4–5 / PDF page 4, was directly inspected again:

| Action | Original instruction |
| --- | --- |
| Place | A sets a bomb. |
| Ground kick | Run into a planted bomb; alternatively, stand still, place with A, then press A again. Both routes exist. |
| Stop a kicked bomb | R. |
| Lift | B picks up a planted bomb or paralyzed enemy. |
| Throw | B again throws the held object in the facing direction. |
| Create directly in hands | Hold B and press A. |
| Pump | Repeated A presses after picking up a bomb. |
| Choose throw distance | Greater stick displacement throws farther; when throwing is assigned to B in Options, longer B duration throws farther. |
| Remote detonation | Z. |

These are general controls, not a separately verified multiplayer-specific remapping. The booklet's explicit distance sentence means variable throw reach is original behavior; a new landing cursor is a project interface choice. It does not establish charging a **kick** or pumping as the distance selector.

## Original gameplay cross-check

Previously cached original-game contact sheets were actually viewed again during this investigation:

- [XFactor1111 multiplayer, 00:13.50–00:15.05](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=13s), `artifacts/research/iteration11/white-red-kick.jpg`, 0.05-second samples: white makes a brief foot-level action around 14.00–14.10 and a red bomb travels down/right without an overhead carry. Black is already prone before contact. This corroborates a distinct ground kick, but hidden inputs cannot identify whether A or contact initiated it.
- [Same multiplayer recording, approximately 00:04–00:07](https://www.youtube.com/watch?v=SPjnYY_Gomo&t=4s), `artifacts/research/iteration10/kick-detail.jpg`, 0.2-second samples: white carries a bomb overhead; its silhouette enlarges before release. Overlap and sampling prevent deriving exact pump count, throw-input timing or an original range curve.

This is targeted frame inspection, not a claim of watching the full recordings or measuring original simulation constants. Original screenshots/video stay ignored and are not game assets. See [iteration eleven](iteration11-kick-research.md) for longer-travel and collision witnesses.

## Implemented project design

An explicit precision stance plants the carrier while the player adjusts direction and range. Keyboard Space now holds a bomb and J places/pumps; Shift enters precision aim. On controllers, the existing carry button or left shoulder holds the bomb, the primary button pumps, and the right trigger enters aim. Left/right turns and up/down changes range on both keyboard and controller. Release carry to commit the throw; releasing only Aim restores movement while retaining the chosen direction/range.

Both devices share range bounds and turn/range speed limits. The first directional nudge is slow and sustained input ramps up; partial stick displacement scales this same adjustment. Keyboard players can make fine taps without an additional chord. There is no instant mouse targeting, target snap or automatic opponent tracking. Pumping changes aimed blast charge without changing the selected range; ordinary non-aim throws retain their existing behavior.

The guide and server share static flight, swept collision and landing rules. Elevation, fuse expiry, water and void affect the actual endpoint. Moving opponents/bombs and future map changes remain possible interruptions, so a marker is a prediction rather than a guaranteed hit. The high-lob shape, 2–9 cell range, precision modifier, retained intent and ramp rates are new project tuning, not claims about the original game's implementation. [Authority and verification details](iteration16-aim.md).
