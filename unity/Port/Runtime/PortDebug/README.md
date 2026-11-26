# PortDebug: observing the player

Inert unless the player is started with `-portObserve <dir>`. `pipeline/run_player.py` passes it.

| Argument | Effect |
| --- | --- |
| `-portObserve <dir>` | Logs `[PortObserve] scene loaded: ...` for every scene load, saves `<dir>/<seq>_<scene>_<reason>.png` every 2 s (`timer`) and on every scene load (`scene`), writes `<dir>/log.jsonl` (scene loads, screenshots, key presses, game-state probes, every Error/Assert/Exception). |
| `-portKeys "<t>:<key>[@<hold>],..."` | Scripted input. `t` is seconds since player start, `hold` defaults to 0.1 s. |
| `-portNoImageEffects` | Diagnostic only, default off: disables the camera MonoBehaviours that have `OnRenderImage`, except the ones the game needs to work (see [Render probe](#render-probe)). Works with or without `-portObserve`. |
| `-portNoImageEffectsOnly <Type,Type>` | With `-portNoImageEffects`: disable only these types. A default-kept type named here is disabled too. |
| `-portKeepImageEffects <Type,Type>` | With `-portNoImageEffects`: never disable these types, in addition to the default-kept ones. |
| `-portCameraHdr` | Lighting experiment, default off: `Camera.allowHDR = true` on every camera (see [Badge lighting](#badge-lighting)). |
| `-portForceForward` | Lighting experiment: `Camera.renderingPath = Forward` on every camera. |
| `-portNoShadows` | Lighting experiment: `QualitySettings.shadows = ShadowQuality.Disable`. |
| `-portNoLightShadows <Spot,Point>` | Lighting experiment: `Light.shadows = None` for lights of the listed `LightType`s (`Spot`, `Point`, `Directional`, case-insensitive). |
| `-portNoEnvFix` | A/B switch: turns off `PortEnvironmentLighting` (skybox reflection, ambient probe, light and reflection probe fixes; see [Environment and lights](#environment-and-lights)). |
| `-portNoProbeFill` | A/B switch: keeps the reflection and ambient fix but skips the light probe fill and the reflection probe disabling. |
| `-portNoLightFix` | A/B switch: turns off `PortRealtimeLights`, so lights keep the baked baking output Unity 6 gives them. |
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

## Environment and lights

Every scene ships a Unity 5.3 LightingDataAsset at version 3, which Unity 6 rejects as below the supported minimum. Two components in `Runtime/Rendering` make up for what that asset held. Both start before the first scene load, work without `-portObserve` and are on by default; their flags exist for A/B runs.

`PortEnvironmentLighting.cs`, one frame after each load of the active scene (after a `DynamicGI.UpdateEnvironment()` and one more frame):

1. Reflection: when the default reflection is Skybox, a skybox is set and `ReflectionProbe.defaultTexture` is missing or under 16 px, it renders the skybox into an HDR cube with mips and installs it as the custom reflection.
2. Ambient: rebuilds `RenderSettings.ambientProbe` from the Flat colour, the Trilight gradient or the skybox face averages, in linear space and scaled by `ambientIntensity`. A Skybox probe that `DynamicGI.UpdateEnvironment()` already made valid is kept. A zero or NaN probe is always replaced.
3. Light probes: when the loaded probe set is all zero (every LightProbes asset of the game is), every probe is set to the ambient probe. Sets with real data are left alone. This is checked again after every load and when Unity reports the probes changed.
4. Reflection probes: Baked or Custom probes without a texture are disabled, so their renderers fall back to the default reflection instead of black.

About 1 s later it checks that the probe and reflection it set are still in place. `-portNoEnvFix` skips all four steps. `-portNoProbeFill` skips only steps 3 and 4.

Log lines in player.log, all prefixed `[PortEnv]`:

| Line | Meaning |
| --- | --- |
| `environment lighting fix active (...)` / `disabled by -portNoEnvFix` | start-up state, including whether the probe fill is off |
| `<scene>: ambientMode=.. reflectionMode=.. skybox=.. defaultTexture=.. probeBefore=ok\|invalid capture=new\|cached reflection=custom <tex>\|kept\|failed\|none ambient=<source> before=dc(..) after=dc(..)` | one per processed scene. `<source>` is `flat`, `trilight`, `skybox`, `fallback flat` or `kept ...`, and `dc(r,g,b)` is the DC colour of the probe |
| `<scene>: lightProbes filled <n> with ambient dc(..)` / `kept (nonzero) count=<n>` / `left zero (ambient probe invalid ..)` / `none` | the light probe decision (logged when it changes) |
| `<scene>: disabled <n> reflection probe(s) without a texture ...` | step 4, with the probe names |
| `<scene>: ambient probe was replaced after it was set` / `custom reflection was replaced after it was set` | warnings from the 1 s check |

`PortRealtimeLights.cs` handles direct light. With the LightingDataAsset gone, `Light.bakingOutput` reports `{Baked, isBaked=true}` for the lights, so Unity skips their direct light and nothing replaces it. While no lightmaps are loaded, it rewrites each such light to a realtime baking output (not baked, `Realtime`, no occlusion mask channel). It runs after every scene load and rescans every 1 s for lights that are added or enabled later. It applies the fix again if something sets a fixed light back. `-portNoLightFix` turns it off. Its player.log lines are prefixed `[PortLights]`:

| Line | Meaning |
| --- | --- |
| `realtime light fix active (...)` / `disabled by -portNoLightFix` | start-up state |
| `<scene>: fixed <n> of <m> lights (was Baked/isBaked)[, <k> fixed earlier][ [rescan]]` | one per loaded scene (also when `n` is 0), and one per rescan that fixed something |
| `...; lightmaps present (<n>), left as is` | lightmaps were loaded, so nothing was changed |

With `-portObserve`, `PortRenderProbe.cs` also writes an `env` event to `render.jsonl` when a scene loads (`reason` `scene load <name>`) and again 1 s later (`scene <name> +1s`), so the second one shows what the fix changed. Each one is also logged as `[PortObserve] env (<reason>): <json>`.

| Field | Content |
| --- | --- |
| `envFix` | `PortEnvironmentLighting.LastStatus`: `<scene>: reflection=<custom tex\|default\|none> ambient=<source\|kept> lightProbesFilled=<n\|off>[ reflectionProbesDisabled=<n>]`, or `off (-portNoEnvFix)` |
| `ambient` | `mode`, `intensity`, the `light`, `sky`, `equator` and `ground` colours, `probeInvalid` (zero or NaN) and `probe` (27 SH floats, r0..r8, g0..g8, b0..b8) |
| `reflection` | default reflection `mode`, `resolution`, `intensity`, `bounces`, and the `custom` and `default` textures (`{name,width,height,dimension}` or null) |
| `lightmaps` | `LightmapSettings.lightmaps` count (0 while the LightingDataAsset is rejected) |
| `lightProbes` | light probe count, or null without a probe set |
| `reflectionProbes[]` | `name` (path), `enabled`, `mode`, `importance`, bounds `center` and `size`, `texture` |

The event also has `colorSpace`, `lightsUseLinearIntensity`, `realtimeReflectionProbes`, `skybox`, `sun` and a `badges[]` dump (see below).

## Badge lighting

In the Wish intro (scene `Home`, camera `ButtonCamera`, deferred with HDR off) the badge `WishBadge_NEW_STATIC` sits inside the cone of the two `Home_Root/WishLogo` spot lights but its white face shows no direct light. The `env` badge dump only runs at scene load, while that badge is still inactive, so `PortRenderProbe.cs` also writes a `badge` event to `render.jsonl` every 2 s while a scene named `Home` is loaded (only with `-portObserve`). It covers the active renderers whose name starts with `WishBadge` and the `GroundPlane` renderers under an object named `WishLogo`. A digest `[PortObserve] badge: renderers=<names> shadows=<quality> experiments=<flags>` goes to player.log when it changes (`(hidden)` marks a renderer with `isVisible` false).

```
{"t":30.1,"kind":"badge","msg":"renderers=WishBadge_NEW_STATIC ...","scene":"Home","experiments":{...},
 "quality":{...},"lightProbes":0,"ambientDc":[r,g,b],"cameras":[...],"renderers":[...]}
```

| Field | Content |
| --- | --- |
| `experiments` | which lighting experiments are on (`cameraHdr`, `forceForward`, `noShadows`, `noLightShadows`) and `changes`, how many objects or settings each one has changed so far |
| `quality` | `QualitySettings.shadows`, `shadowDistance`, `shadowProjection`, `pixelLightCount` |
| `lightProbes`, `ambientDc` | light probe count (null without a probe set) and the DC (L0) term per channel of `RenderSettings.ambientProbe` |
| `cameras[]` | every enabled camera by depth: `name`, `path`, `enabled`, `depth`, `cullingMask`, `renderingPath` (asked for), `actualRenderingPath`, `allowHDR`, `allowMSAA`, `targetTexture` |
| `renderers[]` | `path`, `active`, `enabled`, `visible`, `layer`, world bounds `center` and `size`, `shadowCasting`, `receiveShadows`, `lightProbeUsage`, `probeAnchor` (path or null), `probePos` (anchor position, else the bounds centre), `shDc` (DC term of `LightProbes.GetInterpolatedProbe` at `probePos`), `cameras` (enabled cameras whose mask has the layer) |
| `renderers[].lights[]` | every enabled light in an active object: `name` (path), `type`, `inMask` (its culling mask has the renderer layer), `intensity`, `color`, `range`, `spotAngle`, `distance` to the bounds centre, `inRange`, `inCone` (half the spot angle), `facing` (local +Z face normal dotted with the direction to the light), `renderMode`, `shadows`, `shadowStrength`, `shadowBias`, `shadowNormalBias`, `shadowNearPlane`, `shadowResolution`, `bakeType` and `isBaked` (from `Light.bakingOutput`; a light marked baked adds no realtime light) |

The four lighting experiments are diagnostic switches for finding out what removes the direct light. All are off by default; any one of them starts the probe even without `-portObserve`. Each is applied on every scene load and logged once to player.log as `[PortObserve] <flag>: <what> (<count>, first applied at <reason>)`. Because the game can change these values after the scene loads, the camera ones are applied again in every `LateUpdate` and the quality and light ones every 2 s. They change what the game draws, so never leave them on for behaviour checks.

| Flag | Tests |
| --- | --- |
| `-portCameraHdr` | LDR deferred lighting: with HDR on, the light buffer is a float target instead of the encoded LDR one |
| `-portForceForward` | the deferred path as a whole: forward lighting with the same lights |
| `-portNoShadows` | shadows of every light (shadow maps fully occluding a small object through bias or near plane) |
| `-portNoLightShadows Spot` | shadows of only the listed light types |

Finding: the cause is the baking output. The scenes serialise these lights with `m_Lightmapping: 4`. Once Unity 6 rejects the v3 LightingDataAsset, `Light.bakingOutput` reports `bakeType` `Baked` and `isBaked` true for them (every light entry in the `badge` events of the r3c runs). A light marked baked adds no realtime light, and no lightmap supplies it either. The evidence: `r3c-fwd` (`-portForceForward -portNoShadows`) and `r3c-nofx` (`-portNoImageEffects`) look the same, with no direct light on the badge, so neither the deferred path, the shadows nor the effect chain removes the light. `r3c-noenv` (`-portNoEnvFix`) goes black, so the badge only gets the ambient and probe light that `PortEnvironmentLighting` restores. `PortRealtimeLights` (see [Environment and lights](#environment-and-lights)) is the fix, and `-portNoLightFix` turns it off for comparison.

`pipeline/run_player.py --extra "<args>"` appends any player arguments (split like a shell, with `shlex`), for example `--extra '-portCameraHdr'`, `--extra '-portNoLightShadows Spot,Point'` or `--extra '-portNoEnvFix'`. Compare the Wish intro screenshots (about t=28-33 s with `--keys '5:Return'`) and the `badge` events between runs.

## Fresh save

`PortSaveStore` (`Platform/SavedGame`) normally keeps the save block in `<Application.persistentDataPath>/SavedGame/unitySaveData`, on macOS `~/Library/Application Support/unity.Wish Studios Ltd.That's You!™/SavedGame/`. Every run then shares it, so the first-boot flow (forced instruction pages, `Menu.IgnoreInputReason.FirstBoot`) only shows on the first run of a machine. With `-portSaveDir <dir>` the block goes to `<dir>` instead (made absolute, created if missing; a `dirName` given to `MemorySaveInit` is still a subfolder).

`pipeline/run_player.py --fresh-save` creates the empty folder `work/runs/<name>/save`, passes it as `-portSaveDir` and lists what the run saved there. Without the flag nothing changes. PlayerPrefs (used by `Localization`, `UISavedOption`, `ScoreUI` and the PlayMaker PlayerPrefs actions) still live in the shared `~/Library/Preferences/unity.<company>.<product>.plist`; `--fresh-save` does not reset them.

## How the input gets in

The game reads the legacy input manager in its own code: `MultiControllerInput` (a thin wrapper over `Input.GetKey*`/`GetAxis`) for the front end, `Menu` and the game flow; NGUI's `UICamera` key delegates (bound to `Input.GetKeyDown` and friends); the uGUI `PS4InputModule` on the language select screen (`GetButtonDown("Submit")`, `GetAxisRaw("Horizontal"/"Vertical")`); and PlayMaker actions. Unity cannot inject events into `UnityEngine.Input`, so `InputShim.cs` declares a global-namespace `Input` class. C# resolves an unqualified `Input` in Assembly-CSharp to it before the `UnityEngine.Input` brought in by `using UnityEngine;`, with no game file changed. Every member forwards to `UnityEngine.Input` and ORs in `PortInput`'s injected state.

`GetButton*` and `GetAxis` for injected keys use the InputManager axis table that `PortBuild` writes to `StreamingAssets/PortInputAxes.json`, so `Return` also counts as `Submit`, `UpArrow` as positive `Vertical`, and so on.

Limits: Assembly-CSharp-firstpass (Plugins, Standard Assets) still reads `UnityEngine.Input` directly. Mouse and touch are not injected.

## Audio

PortAudio (`unity/Port/Audio`, the managed replacement for Wwise) reads its options from the player command line through `PaOptions.Parse` (`Audio/Core/Observe/PaOptions.cs`). Flag names match case-insensitively and the last occurrence wins. Paths are made absolute. A bad or missing value logs a `[PortAudio] options: ...` warning and keeps the default; a value that looks like the next flag (`-port...`) is not swallowed.

| Argument | `PaSettings` field | Effect |
| --- | --- | --- |
| `-portAudioTrace <file>` | `TracePath` | JSON-lines trace of what the audio engine does (see the kinds below). |
| `-portAudioRecord <file.wav>` | `RecordPath` | Records the final mix as PCM16 WAV (see [Recorder](#recorder)). |
| `-portAudioRoot <dir>` | `Root` | Reads converted audio from `<dir>` instead of `StreamingAssets/PortAudio`. |
| `-portVoiceLanguage <name>` | `VoiceLanguage` | Voice language, a Wwise name (`English(US)`) or its key (`english_us`); default from the game. |
| `-portAudioOff` | `Off` | PortAudio does nothing (the old silent backend behaviour). |
| `-portAudioSuspendOnFocusLoss` | `SuspendOnFocusLoss` | Pauses all audio while the window has no focus (default: keep playing). |
| `-portAudioVoices <n>` | `RealVoices` | Real (audible) voices, 1..4095, default 128. |
| `-portAudioTrimDb <dB>` | `TrimDb` | Master trim in dB, -96..24, default 0. |
| `-portAudioSeed <n>` | `Seed` | Seed for the random containers, 0..4294967295; default -1 = time based. |

`pipeline/run_player.py` maps its flags onto these:

| run_player | Player argument |
| --- | --- |
| `--audio-trace` | `-portAudioTrace work/runs/<name>/audio.jsonl` |
| `--audio-record` | `-portAudioRecord work/runs/<name>/mix.wav` |
| `--audio-root DIR` | `-portAudioRoot <DIR made absolute>` |
| `--voice-language NAME` | `-portVoiceLanguage NAME` |
| `--audio-off` | `-portAudioOff` |

The rest go through `--extra`, for example `--extra '-portAudioSeed 7 -portAudioTrimDb -6'`. Without audio flags the player argv is unchanged.

### Trace

`PaTraceFile` (`Audio/Core/Observe/PaTraceFile.cs`) is the `IPaTraceSink` behind `PaLog.Trace`. Any thread may write (finalizers included); the line is formatted on the calling thread and a background thread appends the queue to the file every 0.5 s, so a killed player loses at most the last half second (and the last line may be cut, which the summary counts as a bad line). Each line:

```
{"t":12.345,"f":678,"kind":"post","id":3991942870,"name":"Play_Music_Frontend","go":18446744073709551615,"playingId":12}
```

`t` is realtime seconds since startup and `f` the frame count (`PaRuntime.Platform`); off the main thread `t` is extrapolated from the last main-thread reading and `f` is that reading's frame. NaN and infinities are written as `null`. More than 200000 pending lines are dropped and reported as one `traceDropped` line with a `count`.

Kinds the run summary reads (other kinds, such as `action`, `play`, `seek`, `sync`, `stats`, `init`, are counted by name only):

| Kind | Emitted when | Fields read by the summary |
| --- | --- | --- |
| `post` | the game posts an event | `name` (else `id`), `result`: `ok` (found in a loaded bank, its actions ran; counted as resolved), `unknown` (in no loaded bank, or the id is unknown), `silent` (PortAudio runs in silent mode) |
| `voice` | a voice (clip) changes state | `op`: `start`, `end`, `stop`, `fail`, `late` (started 50 ms or more after it was due), `steal` (stopped to free a real voice), `clamp` (gain clamped); for `fail` also `reason`, `file` (else a non-zero `media`, `node`, `name`, `id`) |
| `bank` | a bank load or unload | `name` (else `id`), `op` (`load`, `loadAsync`, `ready`, `unload`), `result` |
| `callback` | a callback was sent to the game | `type`: `endOfEvent` or `bank` |
| `music` | the interactive music player acts | `op`: `play`, `segment`, `transition`, `late`, `stinger`, `end`, `stop` |
| `warn`, `error` | every `PaLog.Warn` / `PaLog.Error` | `msg` (a message containing `not ready after` gets a WARNING line) |
| `level` | the recorder finished one second of the mix | `sec`, `rmsDb`, `peakDb`, `frames` |
| `record` | recorder status: `start`, `listener` (tap moved, `name`, `scene`), `data` (first audio, `channels`, `rate`), `stop` / `noData` (`seconds`, `callbacks`, `overruns`, `mismatched`, `clipped`, `error`) | - |
| `traceDropped` | the queue overflowed | `count` |

For older traces the summary still accepts the kinds `resolve`, `unknown`, `start`, `end`, `stop` and `fail` and a bank `status` field.

### Recorder

`PaRecorder` (`Audio/Unity/PaRecorder.cs`) starts from `IPaPlatform.StartRecorder` when `-portAudioRecord` is given. It creates a hidden DontDestroyOnLoad host that, once a second, finds the enabled `AudioListener` and attaches (or moves) a hidden `PaRecorderTap` component on that GameObject. Its `OnAudioFilterRead` sees the final mix and passes it through unchanged. The audio thread copies the samples into a preallocated single-producer ring (4 s at 8 channels) without allocating or locking; a writer thread feeds `PaWavWriter` (PCM16 at `AudioSettings.outputSampleRate`, channel count of the first callback) and `PaLevelMeter` (one `level` event per second: RMS and peak in dBFS, silence is -200).

- The WAV header is patched and flushed after every second of audio, so the file is valid even when the player is killed; it then holds everything up to the last whole second.
- When the listener is destroyed (scene change), nothing is recorded until the next check finds the new one, so the file can be a little shorter than the run. Ring overruns (writer too slow) and callbacks with a different channel count are dropped and counted in the `record` `stop` event.
- No file is written when no audio ever reaches a listener: Unity audio disabled in the build (`m_DisableAudio`), no enabled `AudioListener`, or PortAudio off. The run summary prints a WARNING then.
- The recording is taken before the OS mixer, so muting the Mac does not affect it. For unattended runs mute the output at OS level (`osascript -e 'set volume output muted true'`, and `false` afterwards) instead of using `AudioListener.volume` or `-portAudioTrimDb`, which change the recorded mix too.

### Run summary

With `--audio-trace` or `--audio-record` (or when `audio.jsonl` / `mix.wav` exist in the run folder), the `run_player` summary ends with an `audio:` section: trace event counts per kind, events posted / resolved / unknown / silent with the most frequent unknown names, voice start / end / stop / fail / late counts (steal and clamp when seen) with the top failures by file or media, callback counts (`endOfEvent`, `bank`), music op counts (`play`, `segment`, `transition`, `late`), bank results by name, op and result, PortAudio warnings and errors, audible seconds from the `level` events, and an analysis of `mix.wav`: duration, rate, channels, peak dBFS, audible seconds (0.1 s windows with RMS above -50 dBFS) and the longest silence with its start. A requested trace or recording that is missing, unreadable, empty or silent gets a WARNING line, and so do unknown posted events and banks whose media was `not ready after` the preload wait.
