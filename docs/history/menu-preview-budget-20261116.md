# Packaged menu-preview budget correction — 2026-11-16

The packaged Mac Verification on source `4958b866919ea91fe74c79e47fae87e7e66ad9ea`
completed the graphical regression (6,009 assertions, 598 captures), then its
separate `menu-preview` process exceeded the 420-second outer catalog budget.
[Failed job](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/43899148379/job/133420230931),
[retained artifact](https://github.com/MCMike0399/bomberman-64-multiplayer/actions/runs/43899148379/artifacts/14247618363).

The original artifact establishes healthy slow progression, rather than a
render-preparation deadlock. Its last progress receipt records 637 assertions,
frame 6,240, Match, 390.834 seconds. Circuit Garden Low, Medium and High captures
were saved at 16:57:00, 16:57:12 and 16:57:24 UTC; the outer timeout followed at
16:57:26. All 34 recorded preparations finished, with zero pending effects:
241.694 seconds total, 6.999-second median, 8.797-second maximum. The runtime log
contains only the engine/device header, with no runtime error. The actual device
was Apple Paravirtual device (Apple5), Metal, Forward+.

Graphics Settings alone spans at least 314 seconds from its default-menu capture
to Circuit Garden High. The preceding successful `all` process shows the same
span taking 280 seconds. The standalone graphics-settings budget was 180 seconds;
the nearby 1,200-second catalog value belonged to a different regression suite.
Both outer budgets were therefore smaller than observed healthy work.

`MiniaturePreviewRegression` still runs FirstUseRendering, GraphicsSettings,
AssetAudit, then the explicit ArcadeMenu configuration reset and SetupMenus in
one process. The failure had reached the end of Graphics Settings, but had not
reached Asset Audit or Setup. No phase, live per-stage preset transition,
assertion, capture or quality feature is removed.

The broader ten-battle-map plus training candidate `3270d5c` completed the same
native scenario on the physical M5 Pro: 1,163 assertions, 57 captures, 69.817
seconds, no runtime errors. It recorded 48 completed preparations, 12.234 seconds
total and a 0.235-second median. This demonstrates the substantial host-scale
difference; it does not retroactively pass the failed hosted/package receipt.
The retained package files were obtained with bounded HTTP ranges and ZIP CRC
validation. The stalled partial full-archive download was not treated as a valid
archive or given a whole-archive digest.

The release owner approved only these catalog budgets for the expanded roster:

| Scenario | Previous outer limit | Current outer limit |
| --- | ---: | ---: |
| graphics-settings | 180s | 900s |
| menu-preview | 420s | 1,200s |

The complete packaged Mac envelope also accounts for the larger catalog:

| Mac-only limit | Previous | Current |
| --- | ---: | ---: |
| `all` child process | 2,400s | 3,600s |
| parent wait for `all` | 2,700s | 3,900s |
| exported Mac regression CI job | 60min | 90min |

The old `all` completed in 2,182.072s, using 90.92% of its limit and leaving only
217.928s. The final integration expands 21→25 tours and 63→75 leaves (+19.05%),
before considering added maps inside existing graphics, asset and setup leaves.
Scaling the old elapsed time only by that leaf count gives 2,597.705s, already
197.705s above the old limit. This is a workload estimate, not a claim that
different tours have equal cost. The new 3,600s bound leaves 1,002.295s above that
estimate; the parent retains its existing 300s cleanup/reporting allowance.

The old job reached the partial menu timeout after 2,864s (47min 44s), before the
remaining menu phases and water-surface case. Its pre-`all` work, including setup,
headless/network cases and controller-popups, had already taken about 261s. Four
more tours, expanded per-stage loops, four additional network cases and completion
of menu-preview make the old 60min job ceiling poor headroom for healthy work.
The 90min ceiling applies only to the exported Mac regression job; every other
job and the global `all` catalog timeout retain their existing limits.

These conservative whole-scenario allowances account for the measured hosted
cost and larger roster. The inner 20-second graphics-preparation deadline,
software-renderer policy, all other catalog budgets and product/runtime behavior
remain unchanged. An actual hang still fails its inner wait
or final outer bound. This change supplies time to finish existing work; exact
current-source packaged Verification remains a separate release gate.
