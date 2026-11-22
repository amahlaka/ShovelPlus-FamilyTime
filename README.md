# ShovelPlus

Shovel upgrades for **Family Time (Episode 4)**: an adjustable brush size, an instant slope tool and a smoothing tool.

## Description
ShovelPlus adds three modes to the shovel. Press **Home** to switch between them.

- **Brush:** digs (left click) or raises (right click) a whole circle of ground at once. **PgUp/PgDn** sets the radius. Radius 0 is the normal one-point shovel. It also works with the game's flatten-to-height stroke, so you can level large areas in one go.
- **Slope:** one click makes a straight ramp in the ground. It starts at the point you aim at and runs in the direction you're facing, snapped to 45° steps. Left click makes a slope going down, right click a slope going up. You can set its length, width and angle.
- **Smooth:** each click evens out bumps in the circle, pulling every point toward the average of its neighbours. Click a few times for a stronger effect.

An outline on the ground shows the area that will change: a circle for Brush and Smooth, and the up-ramp (green) and down-ramp (red) for Slope. While the shovel is equipped, the current mode and its controls appear in the bottom-left corner, in the game's own hint text style.

The mod keeps the normal shovel rules: trees still block digging, saplings in the area get uprooted, and dug ground gets the game's usual dirt marks.

### Controls (only while the shovel is equipped)
| Key | Brush | Slope | Smooth |
|---|---|---|---|
| PgUp / PgDn | radius (0 = normal shovel) | slope length | radius |
| Shift + PgUp / PgDn | – | slope width | – |
| Ctrl + PgUp / PgDn | – | slope angle (5° steps) | strength |
| Home | next mode | next mode | next mode |
| Left / right click | dig / raise the area | slope down / up | smooth |

## Installation

### Requirements
- Family Time **Episode 4**.
- The **BepInEx base pack for Episode 4**

### Install
1. Extract the zip into your game folder, the folder that contains `FamilyTime.exe`. The zip already has the right layout, so the mod ends up at `BepInEx\plugins\ShovelPlus.dll`. If your unzip tool creates an extra folder, move its `BepInEx` folder into the game folder.
2. Start the game.
3. Check that `BepInEx\LogOutput.log` contains `ShovelPlus 1.0.0 loaded.`

The first start creates the settings file `BepInEx\config\familytime.shovelplus.cfg`.

### Uninstall
Delete `BepInEx\plugins\ShovelPlus.dll`. The mod doesn't change the save format: terrain you shaped with it stays shaped, like any shovel work.

## Settings (`BepInEx\config\familytime.shovelplus.cfg`)
- `[General]`
  - `Mode`: the current mode.
  - `ShowHud`: shows the info text.
  - `ShowPreview`: draws the ground outline.
- `[Brush]`
  - `Radius`: the current brush radius.
  - `MaxRadius`: the largest radius or slope length allowed (default 24).
- `[Slope]`
  - `Length`, `Width`, `Angle`: the current slope settings.
  - `AngleStep`: degrees per key press (default 5).
  - `DirectionSnapDegrees`: 45 by default. 0 follows the camera exactly, 90 snaps to the grid axes only.
- `[Smooth]`
  - `Strength`: how strong each click is (0.1 to 1).
- `[Keys]`
  - `Bigger`, `Smaller`, `CycleMode`: the key bindings, using Unity Input System key names such as `PageUp`, `Home` or `F5`.
