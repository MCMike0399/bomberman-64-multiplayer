# Iteration nineteen: carry ergonomics and aiming

Investigated 2026-10-23. The user reports that holding printed B while pressing A
on a Switch controller is uncomfortable, and that the existing aim mechanic is
awkward. These observations motivate the design; automated checks cannot prove
that the replacement feels fun to a human.

## Evidence and current boundary

The [Nintendo-hosted Bomberman 64 English manual](https://www.nintendo.com/eu/media/downloads/games_8/emanuals/nintendo_8/Manual_Nintendo64_Bomberman64_EN.pdf),
printed pp. 4–5, documents B pickup/throw, B held with A to create a bomb in hand,
A pumping, and variable throw distance from stick displacement or B duration
under the relevant option. The project's [iteration sixteen inspection](iteration16-controls-research.md)
records those pages. The PDF was retrieved again in this investigation, but its
image-only pages were not newly visually verified. These historical controls
establish precedent for variable throws, not the ergonomics of copying an N64
button chord onto a modern Switch controller.

[Microsoft's Xbox input accessibility guidance](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/107)
recommends alternatives to simultaneous presses and prolonged holds, digital
equivalents for analog actions, and accurate prompts when controls change. This
supports treating the complete carry/pump/aim/throw sequence as the design unit.
Moving a hold to a shoulder helps separate fingers, but does not eliminate all
hold-related accessibility barriers. Full remapping and an explicit optional
carry toggle remain possible future improvements.

[Godot's controller documentation](https://docs.godotengine.org/en/stable/tutorials/inputs/controllers_gamepads_joysticks.html)
explains circular deadzones, partial analog strength, and the difference between
held state and press edges. Its focus guidance also matters for a release-to-throw
action. Native sampling must retain right-stick axes and triggers, and the
existing focus/rearm behavior must survive a mapping change.

[Nintendo's Splatoon 3 update history](https://en-americas-support.nintendo.com/app/answers/detail/a_id/61257/~/splatoon-3-update-history)
describes corrections where bomb trajectories differed from the prediction shown
with R pressed, including moving terrain. The transferable lesson is to keep
trajectory previews consistent with flight rules; it does not establish the
best aim controls for this arena. Blast Circuit already shares projection and
flight rules and should preserve that boundary.

Before this change, [ControllerHub](../scripts/Input/ControllerHub.cs) already
accepted L/LB/L1 for carry, but [ControllerProfile](../scripts/Input/ControllerProfile.cs)
presented the face button first. Precision aim used horizontal input to rotate
and vertical input to change distance. The right stick did not select the aim
vector. [The previous aim design](iteration16-aim.md) explains its deliberate
stance and bounded rates. A new shoulder alias alone would therefore neither
add a missing capability nor address the aim interpretation.

## Three viable designs

| Design | Interaction | Strength | Cost |
| --- | --- | --- | --- |
| Refine the existing polar aim | Aim modifier; left/right rotates, up/down changes distance; either stick accepted | Small behavioral change; preserves existing precision tuning | Pushing right can move the marker in an unexpected arena direction; does not directly address the reported awkwardness |
| Move a landing-intent cursor across the arena | Aim modifier; WASD or either stick nudges the cursor in that same arena direction; neutral keeps it | One spatial rule for keyboard, D-pad and sticks; easy fine correction | Requires shared speed/range rules and explicit handling when the cursor crosses the carrier |
| Direct stick direction with timed range | Stick sets bearing, a held action charges distance, release throws; keyboard chooses bearing then times range | Fast arcade flicks; less cursor travel | Keyboard has fewer instantaneous bearings; timing pressure and stick strength can create device advantages; fine corrections require another mode |

These are design inferences, not claims that another title validates them. The
second option is the selected recommendation for this pass. It replaces the
polar interpretation for both devices together and retains the existing planted
stance, release-to-throw, carried-bomb charge, and shared flight physics.

## Selected layout and behavior

| Action | Keyboard 1 | Switch printed labels | Xbox | PlayStation |
| --- | --- | --- | --- | --- |
| Place / pump / contextual kick | J | A / B | A | Cross |
| Hold carry; release to throw | K | L, with Y alternative | LB, with X alternative | L1, with Square alternative |
| Enter precision stance | L / Shift | ZR | RT | R2 |
| Adjust aim while in stance | WASD | Right or left stick / D-pad | Right or left stick / D-pad | Right or left stick / D-pad |
| Remote detonation | I / R | X / R | B / RB | Circle / R1 |

Hands-on follow-up: the user still found Y+A awkward. v0.19.1 adds printed B
as an equivalent bomb/charge input, allowing Y+B with adjacent face buttons.
A remains available and X remains remote detonation; menu B remains Back.

The shoulder route remains available: hold L, tap A or B to create/pump, hold ZR and steer, then
release L to throw. Switch Y replaces B as the face carry alternative; menu
confirm/back retain their Nintendo semantics. The shoulder route avoids asking
one thumb to hold carry while pumping or adjusting the right stick. Y is an
alternative, not a promise that Y+A is comfortable for every hand.

The explicit aim trigger remains required. While aiming, an active right stick
takes priority over the left stick/D-pad; inputs are selected rather than added.
Right-stick motion alone must not enter a stance or hijack ordinary movement.
While carrying without the aim modifier, movement remains ordinary movement.
As in the existing command model, Aim is ignored by authority without a carried
bomb: the selected direction produces ordinary full-speed movement. Thus holding
the aim trigger also selects the right stick when empty-handed; the right stick
alone remains inactive. A future separate aim-intent channel could retain left-stick
movement in that case without relying on delayed client snapshots.

The selected shared cursor policy uses an initial 4.5-cell offset, a maximum
9-cell radius, and a maximum travel speed of 6 cells/second. Fine movement begins
at 25% speed and ramps over 0.30 seconds. Keyboard diagonals are normalized;
analog strength may scale this same bounded rate. Both devices can reach every
allowed direction and range, using the same authority clock. These numbers are
project tuning, not original-game measurements or established competitive
balance results.

The minimum deliberate range becomes zero instead of two cells. Allowing the
cursor through the carrier avoids an inner exclusion ring trapping a straight
cursor movement. It also permits a dangerous vertical lob at the carrier's feet;
this is a gameplay consequence, not just a control remap. Neutral at the exact
center must remain finite and stable, and crossing the center must remain
continuous. Neither device gets self-blast immunity.

Releasing only Aim keeps the bomb and selected offset while movement resumes.
Releasing carry commits the throw. Because the stored intent is relative to the
carrier, repositioning shifts the target with the carrier. Pumping must not
silently change the selected nominal range. The visible endpoint remains the
shared flight prediction: elevation, obstructions, fuse expiry and water can
make it differ from the nominal level-ground intent. It never guarantees a hit
on a moving opponent.

## Verification and remaining questions

Check the complete shoulder-create-pump-aim-release sequence through native
input, including Nintendo labels, canonical physical positions, both sticks,
trigger sampling, focus loss, and menu transitions. Compare keyboard and pad
cursor displacement and diagonal caps at equal authority time. Check center
crossing, zero-distance lobs, full range, retained intent, and ordinary movement
when no bomb is carried. Local and network sessions must consume the same aim
commands; the input adapter must not invent different physics.

Right-stick priority can expose drift if its deadzone is too small, and releasing
the right stick while the left remains deflected intentionally falls back to
left-stick adjustment. Both deserve human testing. Also observe whether the
6-cell/second cap feels responsive at long range without losing close precision.
Synthetic controller checks establish mapping behavior, not physical Switch,
Xbox or PlayStation comfort. The parent implementation records its actual test
receipts separately; this research document does not claim those checks ran or
that the user has approved the feel.
