# PortDebug: observing the player

Inert unless the player is started with `-portObserve <dir>`. `pipeline/run_player.py` passes it.

| Argument | Effect |
| --- | --- |
| `-portObserve <dir>` | Logs `[PortObserve] scene loaded: ...` for every scene load, saves `<dir>/<seq>_<scene>_<reason>.png` every 2 s (`timer`) and on every scene load (`scene`), writes `<dir>/log.jsonl` (scene loads, screenshots, key presses, game-state probes, every Error/Assert/Exception). |
| `-portKeys "<t>:<key>[@<hold>],..."` | Scripted input. `t` is seconds since player start, `hold` defaults to 0.1 s. |
| `-portNoImageEffects` | Diagnostic only, default off: disables the camera MonoBehaviours that have `OnRenderImage`, except the ones the game needs to work (see [Render probe](#render-probe)). Works with or without `-portObserve`. |
| `-portNoImageEffectsOnly <Type,Type>` | With `-portNoImageEffects`: disable only these types. A default-kept type named here is disabled too. |
| `-portKeepImageEffects <Type,Type>` | With `-portNoImageEffects`: never disable these types, in addition to the default-kept ones. |
| `-portSaveDir <dir>` | The save block goes to `<dir>[/<dirName>]/<fileName>` instead of `<Application.persistentDataPath>/SavedGame/...` (see [Fresh save](#fresh-save)). |

Keys:

- Pad aliases (PS4 button numbers from the game's InputManager): `cross`=0, `circle`=1, `square`=2, `triangle`=3, `l1`=4, `r1`=5, `touchpad`=6, `options`=7, `l3`=8, `r3`=9. They press `Joystick1ButtonN`, which also counts as `JoystickButtonN`.
- Directions `up`, `down`, `left`, `right`: the arrow key plus the `PS3DPadVert` / `PS3DPadHoriz` axis at +1/-1.
- Any `KeyCode` name (`Return`, `Escape`, `Joystick1Button7`, `A`) or input-manager button name (`joystick button 3`, `[2]`).
- `axis:<name>=<value>` holds a named axis, for example `axis:PS4LeftStickY=-1`.

Example: `--keys "8:Return,20:cross,24:down,26:cross@0.3"`.

## Keyboard map

Always on, with or without `-portKeys` (`Runtime/Input/PortPadKeyboard.cs`, switch off with `PortPad.PortPadKeyboard.Enabled = false`). A key counts as the pad button it is listed under, in every unqualified `Input.GetKey*` call in Assembly-CSharp.

| Key | Pad button | Home screen (menu closed) |
| --- | --- | --- |
| `Return`, `KeypadEnter`, `Space` | Cross (0) | confirm |
| `Escape`, `Backspace` | Circle (1) | back |
| `H` | Square (2) | Help (opens the instructions pages) |
| `P` | Triangle (3) | Play Online |
| `K` | L1 (4) | Game Length |
| `L` | R1 (5) | Question Set |
| `O`, `Tab` | Options (7) | Settings menu |
| arrow keys | D-pad / left stick (`PS3DPadVert`, `PS3DPadHoriz`, `PS4LeftStickX/Y`) | move selection |

`K` and `L` were already read by the game's own home bar code; the alias adds them to screens that only read the pad button (scenario browser, PlayMaker input events). The arrow keys are added to the axes only where `MultiControllerInput.GetAxis` reads them; screens that read `PS3DPad*` straight from `Input` already check the arrow keys themselves. Mouse, touch pad (6), L3 and R3 have no keyboard alias.

With `-portKeys`, both the pad names (`square`, `triangle`) and the keyboard keys (`H`, `P`) can be scripted.

## State probe

The front end renders black while the NGUI and image-effect shaders are placeholders, so screenshots cannot show whether the menu opened or the selection moved. With `-portObserve`, `PortStateProbe.cs` writes a `probe` event to `log.jsonl` on every scene load (`reason` `scene <name>`), with every timer screenshot (`timer`) and 0.5 s after each scripted key is released (`key <spec>`):

```
{"t":12.300,"kind":"probe","msg":"menu=on page=MainMenu(kOptions) sel=1:'Options' ...","reason":"key down","state":{...}}
```

`state` fields:

| Field | Source |
| --- | --- |
| `scene` | active scene name |
| `menu.active`, `menu.transition`, `menu.ignoreInput`, `menu.loading` | `Menu.MenuActive`, `Menu.MenuInTransition`, `Menu.IgnoreInput`, `Menu.m_Loading` |
| `menu.page` | `Menu.CurrentMenuPage`: `name` (MenuPageName), `object` (GameObject), `class`, `type` (MenuPageType), `index` (highlighted item, MenuPagePosition), `count`, `item` (its MenuObjectName), `itemObject`, `itemClass`; null when no page |
| `display` | `MenuDisplay.s`: `page` and `button` it is showing, `title` text and `titleShown`, slide `panel` active, `arrowScrolling`, `buttonPressed`; null when there is no MenuDisplay |
| `frontend` | `FrontEndFlow.s_InFrontend`, `FrontEndFlow.IsStarting`, the home camera `home` position and `transition` |
| `logo` | `IWYKLogo.Enabled` (title logo) |
| `pause` | `PauseGame.s.CanPause` and `Paused`; null without a PauseGame |
| `labels` | text of enabled, visible NGUI `UILabel`s whose centre is inside their camera's viewport, top to bottom then left to right, markup stripped, at most 48 of 80 characters |

Private members are read through reflection and come out null if they are renamed. A section that throws is written as `{"error":"..."}`. The one-line `msg` digest also goes to player.log as `[PortObserve] probe (<reason>): ...` whenever it changes. `pipeline/run_player.py` prints the first probe and then every change between consecutive probes under `probe changes`.

## Render probe

While the front end renders black, screenshots cannot tell which camera, image effect or shader fails. With `-portObserve <dir>`, `PortRenderProbe.cs` appends a `render` event to `<dir>/render.jsonl` 1 s after every scene load (`reason` `scene <name>`, so the scene has rendered once) and every 10 s (`timer`). It is a separate file because `PortObserver` keeps `log.jsonl` open with its own writer and has no append hook. A digest line `[PortObserve] render (<reason>): cams=.. effects=.. visible=.. shaders=.. unsupported=..` goes to player.log when it changes.

```
{"t":31.2,"kind":"render","msg":"cams=3 effects=5 ...","reason":"timer","scene":"Home","noImageEffects":false,
 "noImageEffectsOnly":null,"keepImageEffects":[],
 "cameras":[...],"ngui":{...},"shaders":[...]}
```

| Field | Content |
| --- | --- |
| `noImageEffects` | `-portNoImageEffects` was given |
| `noImageEffectsOnly` | the `-portNoImageEffectsOnly` list, or null when every effect outside the keep set is disabled |
| `keepImageEffects` | the effective keep set: the default-kept types plus `-portKeepImageEffects`, minus the types named in `-portNoImageEffectsOnly`; empty without `-portNoImageEffects` |
| `cameras[]` | every enabled camera, sorted by depth: `name`, `path`, `depth`, `clearFlags`, `cullingMask` (and its `layers` by name), `targetTexture` (`"<name> <w>x<h>"` or null for the screen), `rect`, `hdr`, `renderingPath` (actual), `depthTextureMode`, `visibleRenderers` |
| `cameras[].effects[]` | MonoBehaviours on the camera that define `OnRenderImage` (found by reflection), enabled or disabled by `-portNoImageEffects`: `type`, `enabled`, `probeDisabled`, `probeKept` (left on by `-portNoImageEffects` because of the keep set or the `-Only` list), and `shaders` read from the component's `Shader` and `Material` fields |
| `cameras[].shaders` | distinct shaders of the enabled renderers with `Renderer.isVisible` whose layer is in the culling mask. `isVisible` is true when any camera renders the renderer, so with overlapping masks this is an upper bound per camera |
| `ngui` | NGUI draw calls, found by the type name `UIDrawCall` through reflection: `drawCalls` (active, with an enabled renderer) and per shader `name`, `drawCalls`, `visible` |
| `shaders[]` | every shader named above: `name`, `supported` (`Shader.isSupported`), `passCount`, `renderQueue` |

A capture that throws is written with an `error` field instead.

`-portNoImageEffects` disables the image effects on every scene load (all cameras, including disabled ones) and in every `LateUpdate` (enabled cameras), so effects added or re-enabled by the game are switched off before they render; each one is logged once as `[PortObserve] -portNoImageEffects: disabled <type> on camera <name>`.

Some `OnRenderImage` behaviours are part of how the game works, not post effects, and are kept on by default. Types are matched by name (`Name` or `FullName`, case-insensitive), with no compile-time reference to game code:

| Kept type | Why |
| --- | --- |
| `MenuRenderer` | Snapshots the menu for page transitions. `MenuDisplay` waits for the snapshot (`NeedSnapShot`), so without it a submenu never finishes opening (`menu.transition` stays true) and the menu image disappears. |
| `ScreenCopier` | Copies the frame into a texture the game shows later. |
| `TransitionBorderOverlay` | Draws the scenario transition borders; passes the image through otherwise. |
| `ExcludedEffectGrab`, `ExcludedEffectMerge` | Grab the frame before the effect chain and blend the objects excluded from it back on top. |

Each one left on is logged once as `[PortObserve] -portNoImageEffects: kept <type> on camera <name> (keep set)`, or `(not in -portNoImageEffectsOnly)` when an `-Only` list leaves it out. `-portNoImageEffectsOnly UltimateBloom,DeluxeTonemapper` switches off just those (to isolate one effect), and naming a kept type there (`-portNoImageEffectsOnly MenuRenderer`) switches it off on purpose. `-portKeepImageEffects AmplifyColorEffect` keeps more types on. Both lists are ignored, with a warning, without `-portNoImageEffects`.

Use `-portNoImageEffects` to see whether the scene renders underneath the effect chain (UltimateBloom, CinematicEffects DepthOfField, Amplify Occlusion, Amplify Color, Deluxe tonemapper). It changes what the game draws, so never leave it on for behaviour checks.

`pipeline/run_player.py --no-image-effects` passes the switch, `--fx-only <Type,Type>` adds `-portNoImageEffectsOnly` and `--fx-keep <Type,Type>` adds `-portKeepImageEffects` (both need `--no-image-effects`). The summary shows kept effects as `probe-kept` and the lists in its first line. `--render-summary` reads `render.jsonl` after the run and prints, for the last `Home` event, the cameras with their effects, then every shader seen in any `Home` event with where it was used (`cam:<name>`, `fx:<type>`, `ngui`). Shaders are flagged `DUMMY <file>` when their name matches a file under `work/project/Assets/Resources` whose text contains `DummyShaderTextExporter` (the placeholder shaders the export wrote), and `UNSUPPORTED` when `isSupported` was false.

## Fresh save

`PortSaveStore` (`Platform/SavedGame`) normally keeps the save block in `<Application.persistentDataPath>/SavedGame/unitySaveData`, on macOS `~/Library/Application Support/unity.Wish Studios Ltd.That's You!™/SavedGame/`. Every run then shares it, so the first-boot flow (forced instruction pages, `Menu.IgnoreInputReason.FirstBoot`) only shows on the first run of a machine. With `-portSaveDir <dir>` the block goes to `<dir>` instead (made absolute, created if missing; a `dirName` given to `MemorySaveInit` is still a subfolder).

`pipeline/run_player.py --fresh-save` creates the empty folder `work/runs/<name>/save`, passes it as `-portSaveDir` and lists what the run saved there. Without the flag nothing changes. PlayerPrefs (used by `Localization`, `UISavedOption`, `ScoreUI` and the PlayMaker PlayerPrefs actions) still live in the shared `~/Library/Preferences/unity.<company>.<product>.plist`; `--fresh-save` does not reset them.

## How the input gets in

The game reads the legacy input manager in its own code: `MultiControllerInput` (a thin wrapper over `Input.GetKey*`/`GetAxis`) for the front end, `Menu` and the game flow; NGUI's `UICamera` key delegates (bound to `Input.GetKeyDown` and friends); the uGUI `PS4InputModule` on the language select screen (`GetButtonDown("Submit")`, `GetAxisRaw("Horizontal"/"Vertical")`); and PlayMaker actions. Unity cannot inject events into `UnityEngine.Input`, so `InputShim.cs` declares a global-namespace `Input` class. C# resolves an unqualified `Input` in Assembly-CSharp to it before the `UnityEngine.Input` brought in by `using UnityEngine;`, with no game file changed. Every member forwards to `UnityEngine.Input` and ORs in `PortInput`'s injected state.

`GetButton*` and `GetAxis` for injected keys use the InputManager axis table that `PortBuild` writes to `StreamingAssets/PortInputAxes.json`, so `Return` also counts as `Submit`, `UpArrow` as positive `Vertical`, and so on.

Limits: Assembly-CSharp-firstpass (Plugins, Standard Assets) still reads `UnityEngine.Input` directly. Mouse and touch are not injected.
