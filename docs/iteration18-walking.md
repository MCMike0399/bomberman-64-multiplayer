# Iteration eighteen: a coordinated whole-body walk

The user's feedback was that the previous gait looked like moving legs under a rigid toy. The model was not literally frozen: its upper-body motion was too subdued, and all torso geometry shared the hip transform. This pass retains the approved head/suit geometry and short fixed legs while changing the animation rig and phrasing.

The waist now transfers visibly between supporting sides. An independent pivot at the waist lets the shoulders follow the step while the head counterbalances that movement. Arms use the opposite foot's phase with soft lag, rather than unrelated sine waves. A complete left/right stride covers 2.4 world units instead of 1.9, giving each step more time at the same gameplay speed. Boot reach stays bounded, soles counter-rotate gently, and stopping fades the current pose without adding steps. The carried-bomb socket follows the same upper-body transform as the arms.

## Reproduction and measurements

`python3 tools/playtest/run.py --build --scenario movement --headless --output artifacts/iteration18-walk-red` failed the new whole-body weight-transfer check before the animation change. The baseline lateral body travel was .024 world units; final travel is .048. These are explicit art-direction readability bounds, not proof that every player will prefer the animation.

The final focused movement run (`artifacts/iteration18-walk-green/20261022T021855.589562Z-movement`) passes 59 checks. Across 30/60/120 FPS, actual body roll spans about .093–.095 radians, shoulder turn spans .164 radians and head counter-roll spans .080–.081 radians. Maximum leg angle remains .480 radians; the largest body-Y change in a 30 FPS frame falls from .0139 to .0112. Fixed limb length, hip attachment, boot continuity, smooth snapshot interpolation and settling remain required.

`walk-feel` now records side and three-quarter inspection views followed by the normal battle camera. Each leg of the ordinary gameplay journey checks continuous displacement, alternating compact boots, actual lateral body movement and independent shoulder follow-through. It does not edit actor snapshots or animation state to create its footage. [Final exported-app evidence](verification.md).

## Keyboard correction

J is the primary bomb action, K is hold-to-carry/release-to-throw, L holds precision aim, and I detonates remotes. While holding K+L, A/D turn and W/S adjust range. Release L to reposition without dropping the bomb; release K to throw. Shift and R remain aim/remote aliases. Space has no gameplay action. Native checks verify these simultaneous states through the production input adapter, with controller and second-keyboard behavior preserved. The reticle remains shapes only, with no new text labels.

## One installed Mac app

After the v0.18.0 install is verified, the cleanup command removes other bundles with this game's exact identity, including old exports and renamed backup bundles. It checks installed version and every candidate's identity/payload before deletion, preserves unrelated apps/ZIPs/receipts, and unregisters deleted paths from Launch Services. Six tooling regressions cover changed identity, wrong installed version, symlinks, preservation, absent registration and unexpected unregister failures. Renamed backups may have no Launch Services entry; only Apple's explicit [application-not-found status](https://developer.apple.com/documentation/coreservices/3074489-anonymous/klsapplicationnotfounderr) is tolerated, while other failures stop deletion. `--dry-run` lists the selection; deletion requires explicit `--apply` and the expected installed version.
