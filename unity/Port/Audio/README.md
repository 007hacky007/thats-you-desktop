# PortAudio runtime

PortAudio replaces the Wwise 2015.1 sound engine that the PS4 build of the game linked natively.
The game's decompiled Wwise integration (AkSoundEngine, AkBank, AkEvent, AkAmbient,
AkCallbackManager and so on) is unchanged. Only the native entry points behind it are new:

```
game C# -> AkSoundEngine (SWIG wrappers) -> AkSoundEnginePINVOKE (seam) -> PaEngine
PaEngine -> PaMusicPlayer (interactive music) and PaUnityVoices (AudioSource voices) -> Unity mixer
```

The audio data is not in this repository. `python3 -m pipeline.audio` converts the game's banks
and media into `StreamingAssets/PortAudio`:

- `manifest.json` is the bank and language index.
- `banks/**.json` holds one JSON file per bank.
- `media.json` and `lang/<key>/media.json` are the media tables.
- The media itself is Ogg Vorbis.

The runtime reads only those files.

## Layout

| Folder | Contents | Unity? |
|---|---|---|
| `Core/Contracts` | Shared types and interfaces: `IPaPlatform`, `IPaWorld`, `IPaVoices`, `IPaMusic`, `PaLog`, `PaRuntime` | no |
| `Core/Data` | JSON reader, model, `PaBankStore` (loaded banks, ref counts, lookups), `PaMediaIndex` | no |
| `Core/Seam` | `PaHandles` (fake native objects) and `PaCallbacks` (the callback serializer the game reads) | no |
| `Core/Engine` | `PaEngine` and the engine core described below | no |
| `Core/Mixer` | Gain, bus, ducking and spatial model | no |
| `Core/Music` | Interactive music player | no |
| `Core/Observe` | Player flags, trace file, WAV writer, level meter | no |
| `Unity/` | `PaUnityPlatform` and `PaDriver`, voices, clip cache, recorder | yes |

`Core` uses only the BCL. It compiles outside Unity, and `tests/cs` runs it with
`python3 -m unittest tests.test_port_audio_cs` (`PA_CS_AREAS=Engine` selects one area).

## Start-up

1. `PaUnityPlatform.Bootstrap` runs (`RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`):
   - It installs `PaRuntime.Platform`.
   - It parses the `-portAudio...` flags.
   - It creates the hidden `PortAudioDriver` object.
2. AkInitializer calls `AkSoundEngine.Init`, which reaches `PaEngine.Init`:
   - **Real mode:** `<StreamingAssets>/PortAudio/manifest.json` (or `-portAudioRoot <dir>`) loads. The engine then creates the world, the bank store, the media index, the voices and the music player, and starts the driver tick and, if asked, the recorder.
   - **Silent mode:** there is no platform, no manifest, or `-portAudioOff` is set. The engine behaves exactly like the old `PortAudioNull` backend: every call succeeds, PostEvent returns a fresh id, and EndOfEvent and AK_Bank arrive with the next callback flush.

   Either way, a log line and the trace `init` record say which mode is active.
3. `AkTerminator` calls `Term`. It stops everything, writes the trace `summary` and closes the trace.

## Threading and the tick

- Every `PaEngine` entry point takes one lock, records the call and returns. A recorded call is a state change, a command in an ordered queue, or both.
- Answers the game needs at once come from data held under that lock: PostEvent's playing id (or 0 for an unknown event), SeekOnEvent (Success or IDNotFound), GetState, GetRTPCValue.
- Finalizers call UnregisterGameObject (from `~AkAutoObject`) and `delete_*` from other threads. Those calls only queue work.

Voices, music and Unity objects are touched only in `PaEngine.Tick`. It runs on the main thread, at most once per frame, from `PaDriver.LateUpdate` or from `RenderAudio`, whichever comes first. Each tick runs these steps in order:

1. Run the queued commands in call order, then notify music and switch containers of state and switch changes.
2. Advance RTPC ramps and slews.
3. Advance async bank requests and start bank preloads.
4. Run delayed actions that are due, and let continuous containers schedule their next item.
5. `Music.Tick()`.
6. `Voices.Tick()`. Voice end callbacks fire here.
7. Complete finished instances and queue `AK_EndOfEvent`.
8. Every 10 s, write the `stats` trace record.

## Engine core (Core/Engine)

| File | Role |
|---|---|
| `PaEngine.cs` | Public static API (architecture 6.5), silent mode, Init/Term, the tick, game sync entry points |
| `PaCommands.cs` | Ordered command queue |
| `PaWorld.cs` | `IPaWorld`: states, switches, RTPCs (per object and global, ramps with `PaCurves`, Init defaults, slews), game objects, the listener, the seeded `PaRandom` |
| `PaInstances.cs` | Playing instances, voice and music references, completion and EndOfEvent |
| `PaActions.cs` | Event actions: Play, PlayEvent, Stop, Pause, Resume, SetGameParameter, ResetGameParameter, SetState, SetSwitch. Delays, probability, scope and target modes |
| `PaContainers.cs` | Sound voice specs, random and sequence selection, continuous containers, switch containers with continuous validation, layers |
| `PaBankLoader.cs` | Sync and async bank loads, unloads, prepare requests, ref counts, preload waits |
| `PaAudioInput.cs` | Phone clips (PCM16 mono 48 kHz) played as a voice |

Rules worth knowing:

- **Unknown or unloaded events:** PostEvent returns 0, SeekOnEvent returns IDNotFound, and no callback is sent. This matches Wwise. The game's `'Play_FEAR_tvstatic_on '`, with its trailing space, stays silent as before.
- **Instance lifetime:** an instance ends when it has no pending delayed action, live voice, live music context or running continuous container left.
  - An instance that played nothing ends on the next tick.
  - A voice with missing media, including a missing external voice file, ends on the next tick. Commentary pacing therefore never waits for the game's 30 s timeout.
  - A stopped voice ends after its fade, so EndOfEvent follows the fade.
  - A paused instance stays alive.
- **External voice:** the `AkExternalSourceInfo` fields are copied inside PostEvent, because the proxy can be finalized right after it returns.
  - The folder of `szFile` resolves like SetCurrentLanguage.
  - A missing language falls back to the manifest default, English(UK), with a single warning, and the file's locale prefix is swapped to match.
- **Async banks:** the file is read on a pool thread and registered on a tick.
  - AK_Bank waits until the bank's embedded media is ready, or 5 s, whichever comes first.
  - Requests complete in the order they were made.
  - A bank without a JSON file loads as an empty bank and succeeds, because the game retries a failed load forever.
- **UnregisterGameObject:** stops the object's voices and music at once and drops the object's own switch and RTPC values.
- **Suspend and WakeupFromSuspend:** these do nothing unless `-portAudioSuspendOnFocusLoss` is set. On PS4 they meant system sleep. On desktop they would mute the game on every focus loss.
- **Not reproduced:**
  - SeekOnEvent on music (non-music voices restart at the new position);
  - SetMultiplePositions (multi-position emitters keep their single position);
  - marker, duration and music sync callbacks;
  - action types the banks do not contain;
  - Wwise's exact random number generator.

  Each of these is traced once.

## Player flags

| Flag | Effect |
|---|---|
| `-portAudioTrace <file>` | Write the trace, one JSON object per line (kinds: init, bank, post, seek, action, play, voice, music, sync, callback, warn, error, level, stats, summary) |
| `-portAudioRecord <file.wav>` | Record the final mix |
| `-portAudioRoot <dir>` | Read the data from `<dir>`, for example `work/audio/PortAudio`, without rebuilding |
| `-portVoiceLanguage <WwiseLanguage>` | Force the voice language |
| `-portAudioOff` | Silent mode |
| `-portAudioSuspendOnFocusLoss` | Restore the original focus-loss behaviour |
| `-portAudioRealVoices <n>` | Number of real voices |
| `-portAudioTrimDb <dB>` | Output trim |
| `-portAudioSeed <n>` | Start seed for the random generator |

`pipeline.run_player` adds these flags itself through `--audio-trace`, `--audio-record`,
`--audio-root`, `--voice-language` and `--audio-off`.
