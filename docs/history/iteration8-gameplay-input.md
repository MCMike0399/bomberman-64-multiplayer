# Iteration eight: stick navigation and recoverable knockdowns

## Reproduced input failures

A production-event test gradually pushed the stick through .35, .42, .48 and .65. The old menu stayed on Local Battle even though the configured .45 threshold was crossed. Changed-only native axis reports also supplied no reliable repeat while the stick stayed still.

`MenuStickNavigation` now reads held state rather than relying on fresh motion reports: .45 to engage, .30 to release, .4 seconds before repeating, then .12-second repeats. Direction reversal responds immediately; near-diagonal input retains its chosen axis instead of alternating rows. Gameplay retains the existing .20 radial deadzone and equal movement speed across keyboard and gamepads.

A second regression reproduced nonworking stick navigation on Results: match bindings still existed after play ended, and the hub treated their presence as “in gameplay.” `MenuNavigationEnabled` now follows the visible screen independently of binding lifetime. Lobby and Results can retain session state while accepting menu navigation.

Switch-specific synthetic tests travel through the same axis/button publication functions as the native SDL polling path. Actual Switch 2 USB observation separately captured 8,998 axis reports, both left-stick axes reaching −1/+1, gradual intermediate values and a return below deadzone. This confirms live USB input and full-range normalization; it does not claim every physical button or Bluetooth was tested in this iteration.

## Reproduced knockdown failure

The player runs at 4.5 cells/second and a kicked bomb rolls at 4. A player continuing behind their outgoing kick could catch its trailing collision radius, knock themselves down, and stop the bomb before it reached an opponent. The fixture reproduced exactly that state.

Rolling impacts now require the bomb to be approaching the victim. A knockdown event identifies the bomb, victim, reason (`rolling-bomb` or `falling-bomb`) and last player to propel it. This drives the visible hit cue, downed pose, stars and HUD status. Invulnerability time advances while downed rather than freezing behind the early stun return.

## Recovery and player carries

`BlastSession.Grapple` owns the state transitions for local and network play. The timings below are project tuning, not claimed original-game constants:

- Rolling hits start a 1.5-second knockdown; falling bomb hits start 2 seconds.
- New action-button presses or deliberate movement direction changes remove .24 seconds of recovery, capped at one effort per .10 seconds. Holding an input does not repeatedly count.
- Holding carry near a downed opponent picks them up. Resting bombs retain pickup priority; already-held, airborne and ghost players cannot be picked up.
- A paired hold follows the carrier's position and raises the victim. It lasts at most 3.2 seconds, shortened by the victim's own inputs.
- Releasing carry throws the victim with continuous horizontal motion and gravity. Landing begins a short .65-second recovery. Terrain and water still own elimination.
- Recovery consumes its button edges, including the get-up tick, preventing buffered actions from unexpectedly dropping bombs. Fresh presses after recovery remain ordinary actions.
- Carrier/victim departure, elimination and reset detach paired state. A connection loss drops a hold safely rather than interpreting the lost button as a throw.
- Bots use the same input-driven recovery mechanism; they do not bypass timers.

Snapshots add `knockdownReason`, `recoveryRemaining`, `recoveryTotal`, `carriedBy`, `carryingPlayerId` and `airborne`. New presentation kinds include `knockdown`, `recover`, `player-grab`, `player-release`, `player-throw` and `player-land`. The existing bounded presentation cursor still deduplicates effects. The client and independent server should be rebuilt together.

## Verification

The game suite covers real rolling contact, no outgoing self-hit, accelerated versus passive recovery, held-input rejection, no latent bomb, hold-following, escape, timeout, throw continuity/landing, carrier disconnect and both leave paths. It uses ordinary session messages and assembly-internal stage fixtures; there is no network debug-position command.

The graphical `grapple` scenario uses the normal temple map and three real local seats. Players walk to the training remote pickup, position themselves, kick a bomb into a victim, recover, carry/escape and throw/land through ordinary inputs. It checks actual renderer/audio events and captures visible behavior.

The `controller-popups` scenario tests the configured embedded Godot role popup and setup dials with gradual Switch bridge input and south-button acceptance. The setup scenario also verifies exact room settings, persistence and practice launch. [Final exported-app results](../verification.md).
