# README screenshot gallery

## Current map and character views

The README's seven High-preset arena views come from the 2026-10-27 living
stage worlds `scenery-regression` capture: real local sessions in a macOS VM
using Metal Forward+, at 1280×800 with the ordinary battle camera. The four
images in `images/release-042/` are the `scenery-<stage>-high.png` captures
from that run; the other three are recorded in the [living scenery manifest](media/living-stage-worlds/manifest.json).
The shipped v0.42.0 includes this scenery pass; see its [delivery receipt](verification.md#v0420-delivered).

The two character views in `images/release-042/` were captured on 2026-10-27
from the installed v0.42.0 app using `model-profile` in the isolated macOS VM
with Metal Forward+. The journey passed 13 assertions with five captures and
zero runtime errors or warnings. It uses an inspection camera in practice;
these are unedited game viewport images, not the ordinary battle camera.

The README’s remaining `docs/images/current/` gallery was refreshed on 2026-10-25 from
v0.31.0/build 37 game content, with a documentation-only capture journey added
to the source build. These are unedited PNGs from a freshly built game’s real
Metal Forward+ viewport, rendered directly on an Apple M5 Pro.

## Capture settings

- **High**, the highest built-in graphics preset, selected through the actual
  Settings UI and checked against the saved preference.
- **3840×2400** output, 100% 3D resolution and 4× MSAA; Midday lighting.
- The ordinary battle camera, without inspection-camera repositioning.
- Seed 64. Battle arenas use one human and seven real CPU players; Circuit Garden
  uses free practice. Tutorial images come from the lesson browser and actual
  movement lesson.
- Explosion images come from the existing accepted-input `presentation` journey:
  actual placement, pumping, throwing and a collected red upgrade. The results
  screen comes from a completed real duel.

The runtime banner identifies **Metal 4.0 / Forward+ / Apple M5 Pro (Apple9)**.
These fixed-step images establish rendered appearance; they do not measure
real-time GPU performance or physical gamepads.
The capture journey uses a borderless window because Cocoa constrains decorated
windows to the desktop height. The GPU renders the full 3840×2400 target even
where the window extends beyond the physical desktop; PNG readback checks the
actual output dimensions. The images are not enlarged from a smaller render.
Captures use the complete viewport, including the current HUD and prompts.
No AI-generated artwork, compositing, sharpening, upscaling or invented match
states are used. Historical screenshots remain at their original paths for
older documentation; historical videos retain their original capture settings.

## Reproduce

With the pinned dependency and Godot .NET available, run from the repository
root on the native Mac desktop:

```sh
python3 tools/playtest/run.py --scenario readme-gallery --desktop shared --build --timeout 360
```

The runner builds current C# sources and runs the windowed game on the native
Mac desktop. Announce the desktop use and stop owned QA VMs/rendering jobs first.
It leaves the installed application and its preferences alone. Read the
[testing guide](agents/testing-and-delivery.md#own-the-build-desktop-and-server)
for desktop ownership. Routine graphical regression still uses the isolated VM.

Require a passing `runner-summary.json`, zero runtime errors, and inspect each
selected image at full size before copying it into the gallery. Raw logs,
per-frame traces and app inventories stay in ignored artifacts.

## Verified capture

The native run passed **111 assertions**, saved **36 captures** and reported
**zero runtime errors**. Godot reported two ObjectDB instances at process exit;
that warning remains in the [image manifest](images/current/manifest.json).
All 19 selected PNGs match their original capture bytes. The manifest records
image dimensions, SHA-256 hashes, renderer and source revision.

## Selected captures

| Source PNG | Gallery PNG |
| --- | --- |
| `gallery-mode-select.png` | `mode-select.png` |
| `gallery-settings.png` | `settings.png` |
| `gallery-roster.png` | `roster.png` |
| `gallery-setup.png` | `setup.png` |
| `gallery-online.png` | `online.png` |
| `gallery-controls.png` | `controls.png` |
| `gallery-tutorial.png` | `tutorial.png` |
| `gallery-tutorial-lesson.png` | `tutorial-lesson.png` |
| `gallery-<stage>.png` | `<stage>.png` for the six battle arenas and Circuit Garden |
| `blast-small-bloom.png` | `blast-small.png` |
| `blast-large-bloom.png` | `blast-large.png` |
| `blast-power-bloom.png` | `blast-power.png` |
| `results-cpu-victory.png` | `results.png` |

The capture journey also retains arena action frames and additional effect
phases in its local receipt for selection and comparison.
