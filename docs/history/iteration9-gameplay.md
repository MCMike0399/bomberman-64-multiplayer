# Iteration nine: movement, bomb handling and traversal

## Continuous movement

The native Switch publication path already preserved gradual 360-degree direction changes and unchanged held axes. The reproduced visual defect was downstream: exponential catch-up to 15 Hz snapshots made successive rendered steps alternate between about .102 and .198 cells in the fixed-30-FPS test—a 1.95 ratio.

`SnapshotMotion` now interpolates one authoritative snapshot interval behind the newest position, using envelope ticks. The same real-scene test measures approximately .150 cells each frame, with a 1.000003 high/low ratio. This is presentation interpolation, not client prediction or a speed bonus. Discontinuous corrections snap; interpolation stops at the last known state rather than inventing movement during a network gap. Facing turns smoothly and the distance-driven gait has a longer leg stroke, swing-foot lift and less body bob.

The maximum gameplay speed remains equal across devices. Sticks retain continuous direction beyond the .20 radial deadzone. This does not claim measured internet latency or loss tolerance.

## Printed Nintendo buttons

Switch now follows the requested printed labels: **A = primary/confirm, B = carry/back, X or R = remote**. Xbox and PlayStation retain their existing action choices. InputMap entries are per device so regular menus and the configured embedded role popup use the correct buttons. Button-edge buffering, neutral arming, focus suppression and controller seat ownership remain intact.

The successful physical USB observation captured 20,737 real axis reports, a complete circle across all 36 ten-degree sectors, both axes reaching −1/+1, all four face buttons and both shoulders. A sustained three-second diagonal was not established by that trace; sustained unchanged direction is separately covered by synthetic production-bridge tests.

## Front placement and charging

Normal placement uses the continuous facing vector and a .82-cell forward offset, not the player's cell center. A full bomb-circle check rejects walls, incompatible heights, other bombs and players; it never silently falls back underneath the player.

- Tap primary to place.
- Hold it for .25 seconds to lift that same newly placed bomb, if it is still nearby and resting.
- Release primary to keep carrying; each new primary press pumps once.
- Press carry to throw a primary-lifted bomb.
- The older hold-carry pickup / release-to-throw interaction remains available.

Blocked placement and walking away cannot manufacture or remotely lift a bomb. Carry state and candidate fields clear on interruption, reset and respawn. Normal fuses pause while carried and resume after release; remote bombs intentionally wait for the remote command. The HUD distinguishes carried and remote states. No ordinary-fuse failure was reproduced; the suite verifies both timed explosions and command detonation.

Contact kicks use the continuous facing direction at 8 cells/second, above the player's 4.5. Collision substeps are at most .1 cells and check the whole bomb footprint, so a single kick sends it along a line without gradual player pushing or terrain penetration. The existing outgoing-tail guard prevents chasing one's own kick from causing a self-knockdown.

## Drops and ghosts

`CanWalk` permits directed downward transitions while `CanCross` retains strict ramp connectivity. Walking beyond a high ledge starts gravity motion; air steering remains available. A normal drop lands without knockdown. Upward cliff faces, pillars and breakable solids still block passage. Water elimination happens at the falling player's actual height. AI navigation uses directed drop edges.

A latched ghost is rendered above its host. Its stick adds a bounded .3 steering contribution rather than replacing the living player's axes. The living player keeps their own buttons and primary control. Host-following uses the final simulated position; four-second expiry and direction reversals release the latch with explicit events.

## Front-aware bot planning

Changing placement exposed an AI regression: it planned a bomb at its own center, then faced along its escape route before pressing primary. The real bomb appeared in that route and was immediately kicked away. The fixed-seed temple witness produced 265 placements but no explosions, destroyed cover or drops.

Bots now plan a valid forward bomb location, orient through ordinary movement input, use a neutral primary tap, and follow the planned retreat. The same witness produces 31 placements, 30 explosions, 15 destroyed cover objects and seven drops. Six production matches cover three and seven bots on all stock stages, requiring real explosions, destruction and drops; existing AI safety/determinism tests remain.

## Visual refinement and verification

Results now use a dimensional arcade theatre, lit winner podium, physical board, metallic medals and raised action plaques. The final helmet is a continuous near-spherical ellipsoid centered slightly forward; its separate rounded faceplate and body are retained.

The full exported-app suite passed 1,303 assertions with 95 captures. Subsequent head-only refinements were checked in the exported app from five angles, including actual mesh volume, ellipsoid continuity, normals and triangle winding. See [verification](../verification.md) for exact runs and [harness instructions](../../tools/playtest/README.md) for reproduction.
