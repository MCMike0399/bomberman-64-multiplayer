# Working on Blast Circuit

Read [the handoff index](docs/agents/README.md) before changing this project.
It identifies current decisions, superseded experiments and the appropriate
source/test entrypoints. The latest user instruction takes precedence over
these recorded preferences. Treat older iteration notes as evidence, not defaults.

## Route the task

Read the relevant guide before editing its area:

| Task | Required guide |
| --- | --- |
| Rules, bombs, collision, stages, bots, local/network parity | [Gameplay and AI](docs/agents/gameplay-and-ai.md) |
| Character geometry, walking, camera, shaders, effects or audio | [Visuals and animation](docs/agents/visuals-and-animation.md) |
| Keyboard/gamepads, menus, HUD, prompts or reticle | [Input and UI](docs/agents/input-and-ui.md) |
| Bug reproduction, harness, test selection, recording, packaging or CI | [Testing and delivery](docs/agents/testing-and-delivery.md) |
| Adding, moving or renaming files; folder, namespace and naming rules | [Codebase map](docs/agents/codebase-map.md) |

## Working rules

1. Check branch/worktree and applicable instructions. Preserve unrelated changes.
   State the concrete symptom, session type and expected observable result.
2. Trace the owning boundary. `Bomber.Game` is authority, `Bomber.Client` is
   transport/session, and Godot scripts are presentation/input. Local and online
   play share rules; an animation change must not silently change physics.
3. For a bug, retain a failing test or real gameplay witness before fixing it.
   Distinguish authority state, rendered state and native device/focus evidence.
4. Make a focused change. Keep short fixed legs and the current model proportions
   unless the user asks to change them. Art tests need actual visual review;
   green numeric checks are not evidence of charm or user approval.
5. Run the guide's checks for the changed branch. Build before source playtests;
   test the rebuilt export when shipping. Report exact evidence and limitations.
6. Update the relevant current guide when a decision changes. Keep historical
   notes clearly dated and avoid copying stale bindings or tuning into new work.

## Operational boundaries

- QA/debug is **windowed**. Ordinary launch restores saved display settings;
  first launch is fullscreen. One owner at a time for shared builds/imports/exports
  and one for the graphical desktop.
  Parallel agents may own disjoint files; coordinate before shared builds.
- Kernel is an ignored external dependency; game rules belong in this repo.
  Preserve the checked-in pin, MIT notices and public/private boundary.
  Keep private source, credentials, raw traces and reference media out of git.
- The server is an independent process on the user's chosen hardware. Docker
  is not required. Verify ownership before restarting a local listener;
  packaging is not authorization to deploy or alter external hosts.
- After a verified Mac installation, use the documented identity-checked cleanup
  so only `~/Applications/Blast Circuit.app` remains launchable. Inspect CI app
  ZIPs in place rather than recreating duplicate bundles for Spotlight.
- A docs-only task ends with documentation/link/command review. It does not
  require launching the game, rebuilding packages or reinstalling the app.
- Commit/push only within the user's authorized scope; omit generated-by and
  co-author attribution. Keep personal machine/account memory in its canonical
  local store; repository guides contain only shareable project knowledge.
