# Wwise 2015.1 banks (BKHD v113) - layout and field evidence

Parser: `pipeline/wwise_bank.py` (`read_bank(data)`, CLI `python3 -m pipeline.wwise_bank <bnk> [--dump <id>]`).
Graph JSON: `pipeline/audio_graph.py` (`build(...)`, CLI `python3 -m pipeline.audio_graph --source <PS4 dir> --out <dir>`).
Tests: `tests/test_wwise_bank.py`, `tests/test_audio_graph.py`, synthetic banks from `tests/wwise_bank_builder.py`.
`PORT_AUDIO_REAL=1` also parses the 103 real banks.

Everything here was worked out from the game's own banks (no SDK source). "Evidence" says what in the data
supports a name. Field names that are only a guess are marked as such. Values that never vary in this
game's banks are parsed and kept, but their meaning is unconfirmed.

## Real-data result

All 103 banks (13 LocaleIndependent, 10 per language x 9 languages) parse with **0 unparsed objects**.
Every HIRC object consumes exactly its declared size.

| Type | Count | Type | Count |
|---|---|---|---|
| Sound | 16186 | MusicTrack | 228 |
| RanSeqCntr | 7461 | MusicSegment | 189 |
| Action | 3013 | MusicRanSeq | 134 |
| Event | 2679 | MusicSwitch | 22 |
| SwitchCntr | 2036 | Attenuation | 142 |
| Bus | 118 | FxCustom | 21 |
| ActorMixer | 116 | FxShareSet | 15 |
| AuxBus | 1 | LayerCntr | 1 |

There are 639 unique event IDs in LocaleIndependent plus English(UK). 638 of them get an FNV-verified AK.cs name.
The LocaleIndependent banks hold 319 music transition rules. Sync: exitMarker 222, nextBeat 61, immediate 34,
nextBar 2. Entry: entryMarker 234, randomUserMarker 43, sameTime 26, randomMarker 16. None has a transition
segment. All of this matches the research.

The 9 audio-input Sounds (one per language, in Commentary_Global) are fully consumed. Their source has
plugin 0x00C80002, which makes it a source plugin (low nibble 2). For a source plugin, a u32 parameter size
follows the media info, and here it is 0. The research parser left 1 byte because it did not read that field.
The source id (e.g. 210846186) is the id of an FxCustom holding the AudioInput parameters (4 bytes: gain
-12 dB as float32). No tail is left unexplained.

## Chunks

- `BKHD`: version u32 (113), bank id u32 (FNV-1 of the bank name), language id u32, feedback u32, project id u32,
  then zero padding (the chunk is 20..32 bytes so that DATA starts aligned). Language ids: Czech 6, Danish 7,
  English(UK) 11, Finnish 13, Greek 17, Hungarian 19, Norwegian 25, Swedish 35, Turkish 36, LocaleIndependent 0.
- `DIDX`: 12-byte entries (media id, offset in DATA, size). `DATA`: only its file offset is recorded.
- `HIRC`: u32 count, then per object: u8 type, u32 size, body (starts with the u32 object id).
- `STMG` (Init only): volume threshold f32 (-80), max voices u16 (256), then:
  - state groups u32: (id, default transition ms s32, n transitions u32 x (from, to, ms));
  - switch groups u32: (id, rtpc id, n points u32 x point) (0 in this game);
  - game parameters u32: (id, default f32, ramp type u32, ramp up f32, ramp down f32, builtin u8) = 21 bytes each (34).
- `ENVS`: skipped. `PLAT`: u32 length + "PS4".

## Shared structures

- PropBundle: u8 n, n x u8 id, n x 4-byte value. Ranged bundle: u8 n, ids, n x (min, max).
  Props 14, 15 and 58 are int32. All others are float32 (see the table below).
- Point: f32 x, f32 y, u32 interpolation (AkCurveInterpolation 0..9: Log3, Sine, Log1, InvSCurve, Linear, SCurve,
  Exp1, SineRecip, Exp3, Constant).
- RTPC list: u16 n, then per item: rtpc id u32, rtpc type u8, accumulation u8, param id (7-bit big-endian varint),
  curve id u32, scaling u8, u16 n points, points.
- State chunk: u32 n groups x (group u32, sync u8, u16 n x (state u32, instance u32)).
- Node base (Sound, containers, music nodes):
  1. override-fx u8, n fx u8, [bypass bits u8, n x (index u8, fx id u32, is share set u8, rendered u8)];
  2. one extra u8 (always 0 here; named overrideAttachmentParams as a guess);
  3. output bus u32 (non-zero only where the node overrides it), parent u32, priority bits u8;
  4. props, ranged props;
  5. positioning, aux (bits u8, +4 x u32 aux busses when bit 3), advanced (6 bytes);
  6. state chunk, RTPC list.
- Positioning: bits u8. Bit 0 = override parent, bit 1 = 2D, bit 2 = 2D panner enabled, bit 3 = 3D. Values seen:
  0xC0 (inherit, 26 k nodes), 0xC3/0xC7 (2D, without/with panner), 0xD9/0xF9 (3D). The meaning of bits 4..7 is
  unknown. They are kept raw. When override and 3D are set: bits3d u8 (bit 0 = game-defined, 155 nodes;
  0 = user-defined path, 31 nodes), attenuation id u32, and for user-defined: path mode u8, transition s32,
  vertices (u32 n x 16 bytes), playlist (u32 n x (offset, count)), and per playlist item (x, y, z) ranges.
  Positioning is emitted to JSON only for nodes that override it. The runtime walks up the parents.
  `spatialize` = 3D and game-defined. User-defined paths are not reproduced.
- Advanced (6 bytes): bits u8 (bit 0 kill newest, bit 1 use virtual behavior, bit 2 and 3 set together with every
  instance limit: "ignore parent limit" / "virtual voice override parent", guessed), virtual queue behavior u8
  (always 1), max instances u16, below-threshold behavior u8, bits u8 (always 0). There is no global/per-object
  flag. The JSON says `globalLimit: true`.
- Source (AkBankSourceData): plugin u32, stream type u8, source id u32, in-memory size u32, source bits u8.
  For source plugins (plugin & 0xF == 2), a u32 parameter size and the parameters follow.
  - Plugin 0x00040001 = Vorbis, 0x00080001 = external source (cookie = source id),
    0x00C80002 = audio input.
  - Stream 0 = embedded: the id is in a DIDX and size == DIDX size, for all 2605 LI sources. Stream 2 = streamed:
    the loose `<Lang>/<id>.wem` exists for all 1531, and the size (~2 KB) is not the file size, probably a
    header/prefetch size. Banks without DATA use 2, so it cannot be prefetch-from-bank. Stream 1 (prefetch)
    is not used.
  - Source bits bit 0 = language specific: set exactly on the localized sources.

## Property IDs (v113)

| ID | Name | Evidence |
|---|---|---|
| 0 | volume (dB) | 2227 RanSeq, 450 Sound, ...; values -27..+12 |
| 1 | lfe | table position only (not seen) |
| 2 | pitch (cents) | values 260, 280, -430, -1140; ranged -429..0, 0..531 |
| 3 | lpf (0..100) | RanSeq values 27, 15; ranged 0..62 |
| 4 | hpf (0..100) | values 26..42 on RanSeq/Sound/MusicSwitch. A second filter next to LPF; HPF appeared in Wwise 2015.1, and the attenuation curve list has an extra filter slot (below). PaProps has no HPF field, so it goes to `unknown` |
| 5 | busVolume (dB) | on 65 busses, values -18..+17. Bus ducks use target prop 5 (and 0) |
| 6 | priority | 100.0 on 963 Sounds (commentary and voices) |
| 11, 12 | panLR, panFR | -100..100 values on 2D Sounds/MusicSwitch |
| 13 | centerPct | 90/69/50 on 1300+ nodes |
| 14 | delay (ms, int) | Actions only. Play delays 3000..45000, PlayEvent delays 4000..13000 (RECORDPLAYER chain). Ranged on 4 actions |
| 15 | transition (ms, int) | Actions only. Play/Stop fades and SetGameParameter ramps 300..14000 |
| 16 | probability | 50.0 on one action |
| 23 | outputBusVolume (dB) | 5 and 6 on 18 Sounds and 6 segments |
| 26, 27, 28, 31 | unknown (bus only) | 0, 100, 0, 100 on every bus. Kept in `unknown` |
| 58 | loop (int, 0 = infinite) | 64 Sounds: 0 (62), 2 (2). Ranged 0..0 on 2 |
| 59 | initialDelay (s) | 31 RanSeq + 5 Sound, 2..60, often ranged 0..7 / 0..120 (ambient one-shots) |

The "lead" names 3/4 = filters and 5 = bus volume replace the older table, where 4 was bus volume. The busses
settle this: 5 carries the bus fader values, and 4 never appears on a bus.

## RTPC parameter IDs and curve units

| Param id | PaRtpc.Param | Evidence |
|---|---|---|
| 0 | volume | bus Music_Vol/SoundEffects_Vol/Speech_Vol/UserMessaging_Vol, *FADEVOLUME, SIDECHAIN, RECORDPLAYER |
| 3 | lpf | LOWPASSFILTER on MusicSwitch, y 0..62 with scaling 0 |
| 1, 2, 5, 7 | lfe, pitch, busVolume, makeUpGain | table position, not seen |
| other | other | FxShareSet RTPCs use the plugin's own parameter ids (3, 4 for TRANSITION_TIME on MatrixReverb) |

- Accumulation byte: 0 exclusive, 1 additive (every item here), 2 multiply, 3 boolean.
- Scaling: 0 none, 1 unsupported, 2 dB, 3 log, 4 dB-to-linear.
- **dB scaling (2):** y is stored as linear gain minus 1. -1 is silence, 0 is 0 dB, 0.4713 is +3.35 dB on
  LOWPASSFILTER. So dB = 20 log10(1 + y), with -1 mapped to -200 dB. Wwise interpolates the stored value. Because
  the runtime interpolates y in dB, each dB segment with two different values is resampled into 16 linear
  sub-segments (`DB_SEGMENT_STEPS`). This keeps the loudness contour, e.g. the midpoint of a -1..0 linear fade is
  -6.02 dB, not -100 dB. Constant segments stay as they are.
- Attenuation volume curves use the same encoding. The LPF/spread curves are 0..100 with scaling 0.

## HIRC objects

- **Event:** u32 n, action ids.
- **Action:** type u16, target u32, target-is-bus u8, props, ranged props, then a block per type:
  - Play 0x04: fade curve u8 (low 5 bits), bank id u32.
  - Stop 0x01: fade curve u8, exceptions. Pause 0x02 and Resume 0x03: fade curve u8, flags u8 (Pause bit 0 =
    include pending resume, Resume bit 0 = master resume), exceptions.
  - SetGameParameter 0x13 / Reset 0x14: fade curve u8, bypass transition u8, value meaning u8 (0 default,
    1 absolute, 2 offset), base f32, min f32, max f32, exceptions.
  - SetState 0x12: group u32, state u32. SetSwitch 0x19: group u32, switch u32 (not present in this game).
  - PlayEvent 0x21: nothing more (the target is the event). Other types go to `unparsed`.
  - Exceptions: u32 n x (id u32, is bus u8).
  - Seen in the banks: Play 0x0403 (2734), Stop 0x0103 (253), SetGameParameter 0x1303 (14), PlayEvent 0x2103 (10),
    Pause 0x0208 (1), Resume 0x0304 (1).
- **Scope (low byte of the type):** 2/3 = Element, 4/5 = All, 8/9 = AllExcept. An odd value means game-object
  scoped.
- **RanSeqCntr:** node base, then:
  - loop u16 (0 = infinite), loop mod min/max u16, transition time f32 (ms), transition mod min/max f32 (ms, offsets);
  - avoid repeat u16, transition mode u8 (none, crossfadeAmp, crossfadePower, delay, sampleAccurate, triggerRate),
    random mode u8 (0 normal, 1 shuffle), mode u8 (0 random, 1 sequence);
  - bits u8 (0 use weights, 1 reset playlist each play, 2 restart backward, 3 continuous, 4 global);
  - children, playlist u16 n x (id u32, weight s32; 50000 = 50%).
  - In the JSON: transitionSec = ms / 1000, and transitionMinSec/MaxSec are the offsets added to it (as Wwise
    does). The ambience generators are 76 "delay" containers with loop 0 and e.g. 1000 ms + 0..8000 ms.
- **SwitchCntr:** node base, group type u8 (0 switch, 1 state), group u32, default u32, continuous validation u8,
  children, assignments u32 n x (switch u32, u32 n node ids), child params u32 n x 14 bytes:
  - node u32;
  - bits u8 (bit 0 first only, bit 1 continue playback);
  - bits u8 (bit 0 on-switch mode: 1 = stop, 0 = play to end);
  - fade out s32, fade in s32.
  All 5749 child params are (0, 1, 0, 0): "play each, stop on switch", the Wwise defaults.
  1919 containers are on switch groups and 13 on state groups (unique objects).
- **ActorMixer:** node base, children.
- **LayerCntr:** node base, children, u32 n layers x (layer id, RTPC list, crossfade rtpc u32, rtpc type u8,
  u32 n associations x (child u32, u32 n points, points)). There is one, and PaNode does not model layers
  (children only).
- **Bus / AuxBus:**
  - parent u32, props (no ranged bundle);
  - bits u8 (0 kill newest, 1 use virtual, 2 ignore parent instance limit, 3 background music: guessed), one more u8,
    max instances u16, channel config u32, HDR bits u8 (bit 1 = release mode exponential: 2 on every Bus, 0 on the
    AuxBus);
  - recovery time s32 (ms), max duck volume f32 (dB);
  - ducks u32 n x 18 bytes: (bus u32, volume f32 dB, fade out s32, fade in s32, curve u8, target prop u8 = 5 or 0);
  - fx (n u8, [bypass u8, n x 7]), mixer plugin id u32, mixer is share set u8, one u8, RTPC list, state chunk.
  - The first 8 bytes after the props are 0 on every bus, so **no bus sets the background-music flag** (or the
    flag lives elsewhere). The VO bus carries 17 ducks (music busses, SFX, AMBIENCE). Most of them have volume 0
    dB, which leaves the ducking to the SIDECHAIN meter RTPCs.
  - The bypass bits are read as one bit per fx index, and bit 4 means all. The master bus Gain (-4 dB) has
    bypass bit 0 set.
- **MusicSegment / MusicSwitch / MusicRanSeq** share:
  - music flags u8, node base, children;
  - grid period f64 (ms), grid offset f64, tempo f32, time signature beats u8 / beat value u8, meter flag u8;
  - stingers u32 n x 24 (trigger, segment, sync u32, cue filter, don't repeat s32 ms, look-ahead segments u32).
  - All 189 segments are 120 BPM 4/4.
- **MusicSegment:** + duration f64 ms, markers u32 n x (id u32, position f64 ms, u32 length + name bytes).
  The first marker is the entry cue, and the last is the exit cue. The ones in between are custom cues ("1j".., "c3", "C21"..).
  `entryMs`/`exitMs` are the first and last marker positions.
- **MusicTrack:**
  - flags u8, u32 n sources (same source struct);
  - clips u32 n x 40: (sub-track u32, source id u32, playAt f64, beginTrim f64, endTrim f64, source duration f64).
    All are in ms. 32 of 240 clips have a negative playAt;
  - if there are clips: sub-track count u32;
  - clip automation u32 n x (clip index u32, type u32, u32 n points, points);
  - node base, track type u8 (normal, random, sequence, switch: all 228 normal);
  - for switch tracks: group type u8, group u32, default u32, u32 n associations, then source fade (12),
    sync u32, cue filter u32, destination fade (12);
  - look-ahead s32 ms.
  - Automation types: 0 volume, 1 lpf, 2 hpf (-> other), 3 fadeIn (points go 0 -> 1, 17 seen),
    4 fadeOut (1 -> 0, 7 seen). x is stored in seconds from the clip start. The JSON uses ms: fadeIn is from the
    clip start, fadeOut is relative to the clip end (<= 0, where 0 is the end). Clip length = source duration -
    begin trim + end trim, and the last fade-out point lands exactly on it. y stays a 0..1 gain.
- **Transition rule (MusicSwitch / MusicRanSeq):**
  - u32 n src ids (s32, -1 = any), u32 n dst ids;
  - source: fade (time s32, curve u32, offset s32), sync u32 (AkSyncType 0..9), cue filter u32, play post-exit u8;
  - destination: fade (12), cue filter u32, jump-to u32, entry u16 (AkEntryType 0..4), play pre-entry u8, match
    source cue u8;
  - has transition object u8, + (segment u32, fade in 12, fade out 12, play pre-entry u8, play post-exit u8).
- **MusicSwitch:**
  - transition rules, continue playback u8;
  - tree depth u32, then depth x group u32, depth x group type u8;
  - tree size u32, mode u8 (0 best match, 1 weighted), and the tree: 12-byte nodes (key u32, child-or-node u32,
    weight u16, probability u16), stored breadth-first.
  - Nodes above the last level hold (first child index u16, child count u16) in the second field. Leaves hold the
    audio node id (0 = nothing).
  - It is flattened into `paths` (keys without the root key; key 0 = any) in tree order. All 22 use depth 1:
    GROUP state/switch -> scenario or frontend nodes.
- **MusicRanSeq:**
  - transition rules, u32 n playlist items x 30 bytes in pre-order: (segment u32, item id u32, child count u32,
    type u32, loop s16, loop min s16, loop max s16, weight u32, avoid repeat u16, use weight u8, shuffle u8);
  - type is AkMusicPlaylistType (0 sequenceContinuous, 1 sequenceStep, 2 randomContinuous, 3 randomStep), and
    0xFFFFFFFF on segment leaves (no type in the JSON). Loop 0 = infinite.
  - Rebuilt into a tree from the child counts.
- **Attenuation:**
  - cone enabled u8 [+ inside f32, outside f32 (degrees), outside volume f32, LPF f32];
  - **7** curve-to-use s8 (-1 none): volumeDry, volumeWetGame, volumeWetUser, lpf, hpf, spread, focus;
  - u8 n curves x (scaling u8, u16 n points, points), RTPC list.
  - Evidence for 7 slots: the common attenuation (129 bytes) is [0, 0, 0, -1, 1, -1, -1] and only consumes with 7.
    Slot 4 is a 0..100 curve rising with distance (filter-like). The HPF slot maps to `other`.
- **FxShareSet / FxCustom:**
  - plugin id u32, param size u32, params, u8 n media x (index u8, source u32), RTPC list, u16 (always 0);
  - plugins: 0x690003 parametricEq, 0x6C0003 compressor, 0x6E0003 peakLimiter, 0x730003 matrixReverb,
    0x810003 meter, 0x8B0003 gain, 0xC80002 audioInput;
  - Meter params (25 bytes): attack f32 s, release f32 s, min f32 dB, max f32 dB, hold f32 s, mode u8 (1 = RMS),
    game parameter u32. The two meters write PLAYERMESSAGESVOLUME (attack 0.1, release 1, min -48) and SIDECHAIN
    (0.05 / 0.5 / -44.6);
  - Gain params: fullband f32 dB, LFE f32 dB (-4 / -4 on the master bus).

## Graph JSON notes (audio_graph)

- The keys follow `PaModel.cs` exactly. All emitted keys were checked against `PaModelReader.cs`, and all 103 JSON
  files were read back with the C# reader under dotnet.
- Defaults are omitted. `Sound` (PaNodeType), `Unknown` (action type) and `Element` (target mode) therefore never
  appear.
- PaProps fields: 0, 1, 2, 3, 5, 6, 11, 12, 13, 16, 23, 58, 59. Every other prop id goes to `unknown`.
  Rand fields: 0, 2, 3, 59; other ranges are dropped.
- Action props: 14 -> delayMs (+delayMinMs/MaxMs offsets), 15 -> transitionMs, 16 -> probability, and the fade
  curve goes to `curve`.
- Reachability (graph-report): Play -> node subtree. The subtree covers children, every switch assignment, every
  music-switch path, music playlist segments, stinger and transition segments, and sources and clips. PlayEvent
  -> the target event.
- Media scope: embedded = the scope of the DIDX that holds it, streamed = the language key when language-specific,
  external = `{"scope":"external","id":cookie}`, and audio input = `{"scope":"audioInput","id":fx id}`.
  LocaleIndependent events resolve localized nodes against the default language.
- Known data gaps: 31 Commentary_Global events per language play node 632956749, which no bank contains
  (`missingTargets`). One SoundEffect event (STOP_MUSIC_ALL) targets 23957573, which is also absent.
