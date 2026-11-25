# PortDebug: observing the player

Inert unless the player is started with `-portObserve <dir>`. `pipeline/run_player.py` passes it.

| Argument | Effect |
| --- | --- |
| `-portObserve <dir>` | Logs `[PortObserve] scene loaded: ...` for every scene load, saves `<dir>/<seq>_<scene>_<reason>.png` every 2 s (`timer`) and on every scene load (`scene`), writes `<dir>/log.jsonl` (scene loads, screenshots, key presses, every Error/Assert/Exception). |
| `-portKeys "<t>:<key>[@<hold>],..."` | Scripted input. `t` is seconds since player start, `hold` defaults to 0.1 s. |

Keys:

- Pad aliases (PS4 button numbers from the game's InputManager): `cross`=0, `circle`=1, `square`=2, `triangle`=3, `l1`=4, `r1`=5, `touchpad`=6, `options`=7, `l3`=8, `r3`=9. They press `Joystick1ButtonN`, which also counts as `JoystickButtonN`.
- Directions `up`, `down`, `left`, `right`: the arrow key plus the `PS3DPadVert` / `PS3DPadHoriz` axis at +1/-1.
- Any `KeyCode` name (`Return`, `Escape`, `Joystick1Button7`, `A`) or input-manager button name (`joystick button 3`, `[2]`).
- `axis:<name>=<value>` holds a named axis, for example `axis:PS4LeftStickY=-1`.

Example: `--keys "8:Return,20:cross,24:down,26:cross@0.3"`.

## How the input gets in

The game reads the legacy input manager in its own code: `MultiControllerInput` (a thin wrapper over `Input.GetKey*`/`GetAxis`) for the front end, `Menu` and the game flow; NGUI's `UICamera` key delegates (bound to `Input.GetKeyDown` and friends); the uGUI `PS4InputModule` on the language select screen (`GetButtonDown("Submit")`, `GetAxisRaw("Horizontal"/"Vertical")`); and PlayMaker actions. Unity cannot inject events into `UnityEngine.Input`, so `InputShim.cs` declares a global-namespace `Input` class. C# resolves an unqualified `Input` in Assembly-CSharp to it before the `UnityEngine.Input` brought in by `using UnityEngine;`, with no game file changed. Every member forwards to `UnityEngine.Input` and ORs in `PortInput`'s injected state.

`GetButton*` and `GetAxis` for injected keys use the InputManager axis table that `PortBuild` writes to `StreamingAssets/PortInputAxes.json`, so `Return` also counts as `Submit`, `UpArrow` as positive `Vertical`, and so on.

Limits: Assembly-CSharp-firstpass (Plugins, Standard Assets) still reads `UnityEngine.Input` directly. Mouse and touch are not injected.
