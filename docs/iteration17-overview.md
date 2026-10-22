# Iteration seventeen: keyboard play, visible aiming and useful openings

> Historical evidence: this document records its iteration. Read the [current maintenance guide](agents/README.md) before reusing controls, animation tuning or compatibility claims.

## Current behavior

- **Both hands on the keyboard:** WASD moves; Space holds carry and releases to throw; J places, kicks contextually and pumps; K holds precision aim; L detonates remotes. Shift remains an Aim alias and R a remote alias. K no longer carries. Controller actions and bounded precision rates remain shared. [Persona 5 Royal screenshot observations, primary sources and inference limits](iteration17-keyboard-research.md).
- **Clean endpoint reticle:** an outlined ring/crosshair marks safe landing, an X marks blocked or unsafe outcomes, and an edge arrow indicates an endpoint outside the frame. The world arc still uses the shared static throw projection. There is no reticle player/range/status text; `EndpointLabel` is diagnostic metadata only. Two local carriers receive independent markers. [Normal-match failure reproduction and rendering change](iteration17-reticle.md).
- **Useful Sunken Temple openings:** terrain-distance placement supplies nearby cover opportunities around each spawn; the first destroyed crate assigned to each spawn area gives a fire or bomb reward. Later crates use the weighted pool with a 60% drop chance. Revealed rewards survive the revealing blast. These are project tuning choices, not claimed original-game constants. Pickup icons are 1.30× their prior size above the existing plates; collision and inventory caps remain separate rules.
- **Compact gait:** short rigid legs retain their hip attachments and fixed length. Eased bounded reaches remove recovery overshoot, boot counter-rotation moderates sole rotation, and gloves/head follow through the body motion. The stride remains distance-driven; stopping settles without extra steps or a gameplay speed change. The current appearance has not been declared user-approved.

## Focused evidence

The table preserves focused source-build receipts. Final exported-app, installation and full-suite evidence is in [verification](verification.md). Graphical checks use real rendered pixels; synthetic keyboard/controller events do not certify physical hardware.

| Scenario | Result | Evidence under `artifacts/` |
| --- | --- | --- |
| Keyboard and controller aiming | 107 checks, 4 captures, no runtime errors | `iteration17-keys/20261021T235731.196095Z-aim-inputs` |
| Final text-free reticle | 34 checks, 13 captures, no runtime errors | `iteration17-reticle-clean/20261021T235725.667131Z-reticle` |
| Movement and gait | 47 headless checks, no runtime errors | `iteration17-walk-green/20261021T235302.360200Z-movement` |
| Actual walking showcase | 36 checks, 4 captures, no runtime errors; 470 decoded frames, 30 FPS, 15.6667 seconds at 1× | `iteration17-walk/20261021T235716.518529Z-walk-feel` |
| Normal-match opening loot | 26 checks, 2 captures, no runtime errors | `playtest/20261022T000503.111765Z-loot-opening` |
| Opening loot state contract | 25 headless checks, no runtime errors | `playtest/20261022T000541.214125Z-loot-opening` |

The reticle journey uses an ordinary two-human seed64 Sunken Temple match and native Space+J carry inputs. It reproduces a short-range blocked endpoint, checks actual marker/outline pixels, verifies two-carrier ownership and release cleanup, and separately tests renderer contracts for all five endpoint types plus an offscreen arrow. Earlier labeled reticle captures and recordings are historical; the text-free receipt above supersedes their appearance.

The loot journey starts a normal two-human match without a practice item cache. Ordinary movement approaches cover, a primary tap plants a bomb, and retreat survives the full fuse and hazard. The test checks that the revealed reward persists, its actual enlarged icon renders, and walking onto it increases the matching inventory/pickup counter and removes the real item model. Its focused captures use a temporary inspection camera; they are not presented as normal battle-camera framing evidence.

Walking verification checks fixed limb length and hip attachment, boot orientation continuity and stopping at multiple frame rates. The recorded journey uses ordinary practice movement with a temporary following inspection camera. These measurements establish motion continuity and compact geometry; perceived charm and comfort still need human judgment.

## AI status and limits

AI work addresses post-blast reengagement and a stock-stage route choice that can favor a lower-court position over a ramp detour into actual blast range. Reached goals now expire, and reachable positions within visible blast range earn pressure utility so bots can take the necessary ramp detour. In the matched stock-Pyramid Medium seed64 case, human-pressure bombs rose from 4 to 7 over 30 seconds and the longest gap fell from 12.27 to 6.93 seconds, with no bot death. The other Medium case stayed at 8; Hard cases produced slightly fewer bombs while retaining short pressure gaps. All 52 AI checks and 36 soak scenarios pass. Normalized whole-soak pressure rose 1.83% overall (Medium +1.20%, Hard +26.47%, Easy −14.51%). These bounded observations do not establish universal aggression, survival or fun improvements. See [AI fixture definitions and limits](../tests/Bomber.Ai.Tests/README.md) before interpreting those results.
