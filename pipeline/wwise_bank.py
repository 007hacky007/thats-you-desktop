"""Read Wwise 2015.1 (bank version 113) SoundBanks into plain Python dicts.

Written from scratch from the bank bytes themselves (see docs/notes/wwise-banks.md for the layout and
the evidence behind every field name). Only the chunks and HIRC object types that the game's 103
banks use are decoded:

- chunks: BKHD, DIDX, DATA (offsets only), HIRC, STMG, PLAT; ENVS and anything else is skipped;
- HIRC: Sound, Action, Event, RanSeqCntr, SwitchCntr, ActorMixer, LayerCntr, Bus, AuxBus,
  MusicSegment, MusicTrack, MusicRanSeq, MusicSwitch, Attenuation, FxShareSet, FxCustom.

Every object must consume exactly its declared size. An object that does not (or an unknown type)
is still returned with what was parsed and is listed in `unparsed` with a reason.

read_bank(data) -> {"chunks", "header", "didx", "dataOffset", "objects", "unparsed",
                    "stateGroups", "switchGroups", "gameParameters", "settings", "platform"}

Values keep the units stored in the bank (times in ms, angles in degrees) except RTPC and
attenuation curve y values, which are converted to the parameter's natural unit (dB, cents, 0..100).

CLI: python3 -m pipeline.wwise_bank <bank.bnk> [--dump <id>]
"""
from __future__ import annotations

import argparse
import json
import math
import struct
import sys
from pathlib import Path

BANK_VERSION = 113

HIRC_TYPES = {
    1: "State", 2: "Sound", 3: "Action", 4: "Event", 5: "RanSeqCntr", 6: "SwitchCntr",
    7: "ActorMixer", 8: "Bus", 9: "LayerCntr", 10: "MusicSegment", 11: "MusicTrack",
    12: "MusicSwitch", 13: "MusicRanSeq", 14: "Attenuation", 15: "DialogueEvent",
    16: "FeedbackBus", 17: "FeedbackNode", 18: "FxShareSet", 19: "FxCustom", 20: "AuxBus",
    21: "LFO", 22: "Envelope",
}

# Node property IDs (AkPropBundle keys) for bank version 113. Evidence in docs/notes/wwise-banks.md.
PROP_NAMES = {
    0: "volume", 1: "lfe", 2: "pitch", 3: "lpf", 4: "hpf", 5: "busVolume", 6: "priority",
    11: "panLR", 12: "panFR", 13: "centerPct", 14: "delay", 15: "transition", 16: "probability",
    23: "outputBusVolume", 58: "loop", 59: "initialDelay",
}
PROP_IDS = {v: k for k, v in PROP_NAMES.items()}
# Integer-valued props (the rest are float32): delay/transition in ms, loop count (0 = infinite).
INT_PROPS = {14, 15, 58}

# RTPC target parameter IDs (AkRTPC_ParameterID, v113) -> PaRtpc.Param names.
RTPC_PARAMS = {0: "volume", 1: "lfe", 2: "pitch", 3: "lpf", 5: "busVolume", 7: "makeUpGain"}
VOLUME_LIKE_PARAMS = {"volume", "lfe", "busVolume", "outputBusVolume", "makeUpGain"}

RTPC_ACCUMULATION = {0: "exclusive", 1: "additive", 2: "multiply", 3: "boolean"}
CURVE_SCALING_NONE, CURVE_SCALING_DB = 0, 2
CURVE_SCALINGS = ["none", "unsupported", "db", "log", "dbToLin"]

ACTION_NAMES = {
    0x01: "stop", 0x02: "pause", 0x03: "resume", 0x04: "play", 0x05: "playAndContinue",
    0x06: "mute", 0x07: "unmute", 0x08: "setPitch", 0x09: "resetPitch", 0x0A: "setVolume",
    0x0B: "resetVolume", 0x0C: "setBusVolume", 0x0D: "resetBusVolume", 0x0E: "setLpf",
    0x0F: "resetLpf", 0x10: "useState", 0x11: "unuseState", 0x12: "setState",
    0x13: "setGameParameter", 0x14: "resetGameParameter", 0x19: "setSwitch",
    0x1A: "bypassFx", 0x1B: "resetBypassFx", 0x1C: "break", 0x1D: "trigger", 0x1E: "seek",
    0x1F: "release", 0x20: "setHpf", 0x21: "playEvent", 0x22: "resetPlaylist",
    0x30: "resetHpf", 0x31: "setEffect", 0x32: "resetEffect", 0x33: "resetAllEffects",
}
TARGET_MODES = {2: "Element", 3: "Element", 4: "All", 5: "All", 8: "AllExcept", 9: "AllExcept"}
VALUE_MEANING = {0: "default", 1: "absolute", 2: "offset"}

SYNC_TYPES = ["immediate", "nextGrid", "nextBar", "nextBeat", "nextMarker", "nextUserMarker",
              "entryMarker", "exitMarker", "exitNever", "lastExitPosition"]
ENTRY_TYPES = ["entryMarker", "sameTime", "randomMarker", "randomUserMarker", "lastExitTime"]
RANSEQ_TRANSITIONS = ["none", "crossfadeAmp", "crossfadePower", "delay", "sampleAccurate", "triggerRate"]
TRACK_TYPES = ["normal", "random", "sequence", "switch"]
PLAYLIST_TYPES = ["sequenceContinuous", "sequenceStep", "randomContinuous", "randomStep"]
DECISION_MODES = ["bestMatch", "weighted"]
GROUP_TYPES = ["switch", "state"]
RAMP_TYPES = ["none", "slewRate", "filteringOverTime"]
STREAM_TYPES = ["embedded", "prefetch", "streamed"]
ATTENUATION_USES = ["volumeDry", "volumeWetGame", "volumeWetUser", "lpf", "hpf", "spread", "focus"]
CLIP_AUTOMATION = ["volume", "lpf", "hpf", "fadeIn", "fadeOut"]

PLUGIN_VORBIS = 0x00040001
PLUGIN_EXTERNAL = 0x00080001
PLUGIN_AUDIO_INPUT = 0x00C80002
PLUGIN_METER = 0x00810003
PLUGIN_GAIN = 0x008B0003

DB_FLOOR = -200.0


class BankError(Exception):
    pass


def enum_name(table, value, prefix="raw"):
    if 0 <= value < len(table) if isinstance(table, list) else value in table:
        return table[value]
    return f"{prefix}{value}"


class Reader:
    def __init__(self, data: bytes, pos: int = 0, end: int | None = None):
        self.data = data
        self.pos = pos
        self.end = len(data) if end is None else end

    def _take(self, fmt: str, size: int):
        if self.pos + size > self.end:
            raise BankError(f"read past end at {self.pos} (+{size} > {self.end})")
        v = struct.unpack_from(fmt, self.data, self.pos)[0]
        self.pos += size
        return v

    def u8(self): return self._take("<B", 1)
    def s8(self): return self._take("<b", 1)
    def u16(self): return self._take("<H", 2)
    def s16(self): return self._take("<h", 2)
    def u32(self): return self._take("<I", 4)
    def s32(self): return self._take("<i", 4)
    def f32(self): return self._take("<f", 4)
    def f64(self): return self._take("<d", 8)

    def raw(self, n: int) -> bytes:
        if n < 0 or self.pos + n > self.end:
            raise BankError(f"read past end at {self.pos} (+{n} > {self.end})")
        v = self.data[self.pos:self.pos + n]
        self.pos += n
        return bytes(v)

    def var(self) -> int:
        """7-bit big-endian variable-length integer (continuation in the high bit)."""
        v = 0
        for _ in range(5):
            c = self.u8()
            v = (v << 7) | (c & 0x7F)
            if not c & 0x80:
                return v
        raise BankError("variable-length integer too long")

    def count(self, n: int, item_size: int) -> int:
        if n * item_size > self.end - self.pos:
            raise BankError(f"count {n} x {item_size} exceeds remaining {self.end - self.pos}")
        return n

    @property
    def left(self) -> int:
        return self.end - self.pos


def _f(v: float) -> float:
    """Round float32 values to a stable, readable float (7 significant digits)."""
    if v != v or v in (math.inf, -math.inf):
        return v
    return float(f"{v:.7g}")


# ---------------------------------------------------------------- shared structures

def read_props(r: Reader) -> dict:
    n = r.u8()
    ids = [r.u8() for _ in range(r.count(n, 5))]
    out = {}
    for pid in ids:
        if pid in INT_PROPS:
            v = r.s32()
        else:
            v = _f(r.f32())
        out[pid] = v
    return out


def read_ranged_props(r: Reader) -> dict:
    n = r.u8()
    ids = [r.u8() for _ in range(r.count(n, 9))]
    out = {}
    for pid in ids:
        if pid in INT_PROPS:
            lo, hi = r.s32(), r.s32()
        else:
            lo, hi = _f(r.f32()), _f(r.f32())
        out[pid] = (lo, hi)
    return out


def named_props(values: dict, ranges: dict | None = None) -> dict:
    """{propId: value} + {propId: (min, max)} -> {"volume": -3.0, "volumeRand": {"min","max"},
    "unknown": {"26": 0.0}, "unknownRand": {"26": {...}}}."""
    out: dict = {}
    for pid, v in sorted(values.items()):
        name = PROP_NAMES.get(pid)
        if name:
            out[name] = v
        else:
            out.setdefault("unknown", {})[str(pid)] = v
    for pid, (lo, hi) in sorted((ranges or {}).items()):
        rng = {"min": lo, "max": hi}
        name = PROP_NAMES.get(pid)
        if name:
            out[name + "Rand"] = rng
        else:
            out.setdefault("unknownRand", {})[str(pid)] = rng
    return out


def _props_into(d: dict, values: dict, ranges: dict) -> None:
    """Keep the raw bundles (rawProps {id: value}, rawRanges {id: [min, max]}) and a named view."""
    d["rawProps"] = values
    d["rawRanges"] = {k: list(v) for k, v in ranges.items()}
    d["props"] = named_props(values, ranges)


def db_from_stored(y: float) -> float:
    """dB-scaled curve values are stored as (linear gain - 1); -1 is silence."""
    lin = 1.0 + y
    if lin <= 1e-10:
        return DB_FLOOR
    return max(DB_FLOOR, 20.0 * math.log10(lin))


def _shape(interp: int, t: float) -> float:
    t = min(1.0, max(0.0, t))
    if interp == 0: return 1 - (1 - t) ** 3            # Log3
    if interp == 1: return math.sin(t * math.pi / 2)   # Sine
    if interp == 2: return 1 - (1 - t) ** 1.41         # Log1
    if interp == 3: return 0.5 + math.asin(2 * t - 1) / math.pi  # InvSCurve
    if interp == 5: return (1 - math.cos(math.pi * t)) / 2  # SCurve
    if interp == 6: return t ** 1.41                    # Exp1
    if interp == 7: return 1 - math.cos(t * math.pi / 2)  # SineRecip
    if interp == 8: return t ** 3                       # Exp3
    if interp == 9: return 0.0 if t < 1 else 1.0       # Constant
    return t                                            # Linear


DB_SEGMENT_STEPS = 16


def convert_points(points: list, scaling: int, natural_db: bool) -> list:
    """Curve points [(x, stored_y, interp)] -> [[x, y, interp]] with y in the natural unit.

    For dB-scaled curves Wwise interpolates the stored (linear - 1) value, so a segment between two
    different values is resampled into DB_SEGMENT_STEPS linear sub-segments in dB, which keeps the
    loudness contour when the runtime interpolates in dB."""
    if not (natural_db and scaling == CURVE_SCALING_DB):
        return [[_f(x), _f(y), i] for x, y, i in points]
    out = []
    for k, (x, y, interp) in enumerate(points):
        out.append([_f(x), round(db_from_stored(y), 4), interp])
        if k + 1 < len(points):
            x2, y2, _ = points[k + 1]
            if interp != 9 and y2 != y and x2 > x:
                out[-1][2] = 4
                for s in range(1, DB_SEGMENT_STEPS):
                    t = s / DB_SEGMENT_STEPS
                    yy = y + (y2 - y) * _shape(interp, t)
                    out.append([_f(x + (x2 - x) * t), round(db_from_stored(yy), 4), 4])
    return out


def read_points(r: Reader, n: int) -> list:
    return [(r.f32(), r.f32(), r.u32()) for _ in range(r.count(n, 12))]


def read_rtpcs(r: Reader) -> list:
    n = r.u16()
    out = []
    for _ in range(r.count(n, 13)):
        rtpc_id = r.u32()
        rtpc_type = r.u8()
        accum = r.u8()
        param_id = r.var()
        curve_id = r.u32()
        scaling = r.u8()
        npts = r.u16()
        pts = read_points(r, npts)
        param = RTPC_PARAMS.get(param_id, "other")
        out.append({
            "rtpc": rtpc_id, "rtpcType": rtpc_type, "accumulation": RTPC_ACCUMULATION.get(accum, "additive"),
            "param": param, "paramId": param_id, "curveId": curve_id, "scaling": scaling,
            "points": convert_points(pts, scaling, param in VOLUME_LIKE_PARAMS),
        })
    return out


def read_states(r: Reader) -> list:
    n = r.u32()
    out = []
    for _ in range(r.count(n, 7)):
        group = r.u32()
        sync = r.u8()
        ns = r.u16()
        states = [{"state": r.u32(), "instance": r.u32()} for _ in range(r.count(ns, 8))]
        out.append({"group": group, "syncType": sync, "states": states})
    return out


def read_fx_list(r: Reader, with_override: bool) -> dict:
    out = {}
    if with_override:
        out["overrideParentFx"] = bool(r.u8())
    n = r.u8()
    fx = []
    if n:
        out["bypassBits"] = r.u8()
        for _ in range(r.count(n, 7)):
            fx.append({"index": r.u8(), "id": r.u32(), "shareSet": bool(r.u8()), "rendered": bool(r.u8())})
    out["fx"] = fx
    return out


def read_source(r: Reader) -> dict:
    plugin = r.u32()
    stream = r.u8()
    src = {"plugin": plugin, "stream": enum_name(STREAM_TYPES, stream), "id": r.u32(),
           "inMemorySize": r.u32(), "sourceBits": r.u8()}
    src["languageSpecific"] = bool(src["sourceBits"] & 1)
    if plugin == PLUGIN_VORBIS:
        src["kind"] = "vorbis"
    elif plugin == PLUGIN_EXTERNAL:
        src["kind"] = "external"
        src["cookie"] = src["id"]
    elif plugin == PLUGIN_AUDIO_INPUT:
        src["kind"] = "audioInput"
    else:
        src["kind"] = "other"
    if plugin & 0xF == 2:  # source plugin: parameter block follows
        size = r.u32()
        src["params"] = r.raw(size).hex()
    return src


def read_positioning(r: Reader) -> dict:
    bits = r.u8()
    pos = {"bits": bits, "overrideParent": bool(bits & 1)}
    has_2d = bool(bits & 2)
    has_3d = bool(bits & 8)
    pos["twoD"] = has_2d
    pos["pannerEnabled"] = bool(bits & 4)
    pos["threeD"] = has_3d
    if pos["overrideParent"] and has_3d:
        bits3d = r.u8()
        pos["bits3d"] = bits3d
        pos["attenuation"] = r.u32()
        pos["gameDefined"] = bool(bits3d & 1)
        if not bits3d & 1:
            pos["pathMode"] = r.u8()
            pos["transitionMs"] = r.s32()
            nv = r.u32()
            pos["vertices"] = [[_f(r.f32()), _f(r.f32()), _f(r.f32()), r.s32()] for _ in range(r.count(nv, 16))]
            npl = r.u32()
            pos["playlist"] = [[r.u32(), r.u32()] for _ in range(r.count(npl, 20))]
            pos["ranges"] = [[_f(r.f32()), _f(r.f32()), _f(r.f32())] for _ in range(npl)]
    return pos


def read_aux(r: Reader) -> dict:
    bits = r.u8()
    out = {"bits": bits}
    if bits & 8:
        out["auxBusses"] = [r.u32() for _ in range(4)]
    return out


def read_advanced(r: Reader) -> dict:
    b1 = r.u8()
    vq = r.u8()
    max_inst = r.u16()
    below = r.u8()
    b2 = r.u8()
    return {"killNewest": bool(b1 & 1), "useVirtualBehavior": bool(b1 & 2), "bits": b1,
            "maxInstancesIgnoreParent": bool(b1 & 4), "virtualVoiceOverrideParent": bool(b1 & 8),
            "virtualQueueBehavior": vq, "maxInstances": max_inst, "belowThresholdBehavior": below,
            "bits2": b2}


def read_node_base(r: Reader) -> dict:
    d = read_fx_list(r, with_override=True)
    d["overrideAttachmentParams"] = r.u8()
    d["bus"] = r.u32()
    d["parent"] = r.u32()
    d["priorityBits"] = r.u8()
    _props_into(d, read_props(r), read_ranged_props(r))
    d["positioning"] = read_positioning(r)
    d["aux"] = read_aux(r)
    d["advanced"] = read_advanced(r)
    d["states"] = read_states(r)
    d["rtpcs"] = read_rtpcs(r)
    return d


def read_children(r: Reader) -> list:
    n = r.u32()
    return [r.u32() for _ in range(r.count(n, 4))]


# ---------------------------------------------------------------- HIRC objects

def parse_sound(r: Reader, d: dict) -> None:
    d["source"] = read_source(r)
    d.update(read_node_base(r))


def parse_event(r: Reader, d: dict) -> None:
    n = r.u32()
    d["actions"] = [r.u32() for _ in range(r.count(n, 4))]


def _exceptions(r: Reader) -> list:
    n = r.u32()
    return [{"id": r.u32(), "isBus": bool(r.u8())} for _ in range(r.count(n, 5))]


def parse_action(r: Reader, d: dict) -> None:
    raw = r.u16()
    kind = raw >> 8
    low = raw & 0xFF
    d["rawType"] = raw
    d["action"] = ACTION_NAMES.get(kind, "other")
    d["targetMode"] = TARGET_MODES.get(low, "Element")
    d["gameObjectScoped"] = bool(low & 1)
    d["target"] = r.u32()
    d["targetIsBus"] = bool(r.u8())
    _props_into(d, read_props(r), read_ranged_props(r))
    if kind in (0x01, 0x02, 0x03):  # stop / pause / resume: fade curve, flags, exception list
        d["fadeCurve"] = r.u8() & 0x1F
        if kind == 0x02:
            bits = r.u8()
            d["includePendingResume"] = bool(bits & 1)
            d["flags"] = bits
        elif kind == 0x03:
            bits = r.u8()
            d["masterResume"] = bool(bits & 1)
            d["flags"] = bits
        d["exceptions"] = _exceptions(r)
    elif kind in (0x04, 0x05):  # play
        d["fadeCurve"] = r.u8() & 0x1F
        d["bank"] = r.u32()
    elif kind in (0x13, 0x14):  # set / reset game parameter
        d["fadeCurve"] = r.u8() & 0x1F
        d["bypassTransition"] = bool(r.u8())
        d["valueMeaning"] = VALUE_MEANING.get(r.u8(), "absolute")
        d["value"] = _f(r.f32())
        d["valueMin"] = _f(r.f32())
        d["valueMax"] = _f(r.f32())
        d["exceptions"] = _exceptions(r)
    elif kind == 0x12:  # set state
        d["stateGroup"] = r.u32()
        d["state"] = r.u32()
    elif kind == 0x19:  # set switch
        d["switchGroup"] = r.u32()
        d["switch"] = r.u32()
    elif kind == 0x21:  # play event: target is the event, no extra block
        pass
    else:
        raise BankError(f"action type 0x{raw:04x} not supported")


def parse_ranseq(r: Reader, d: dict) -> None:
    d.update(read_node_base(r))
    d["loopCount"] = r.u16()
    d["loopModMin"] = r.u16()
    d["loopModMax"] = r.u16()
    d["transitionTime"] = _f(r.f32())
    d["transitionTimeModMin"] = _f(r.f32())
    d["transitionTimeModMax"] = _f(r.f32())
    d["avoidRepeatCount"] = r.u16()
    d["transitionMode"] = enum_name(RANSEQ_TRANSITIONS, r.u8())
    d["randomMode"] = "shuffle" if r.u8() == 1 else "normal"
    d["mode"] = "sequence" if r.u8() == 1 else "random"
    bits = r.u8()
    d["bits"] = bits
    d["usingWeight"] = bool(bits & 1)
    d["resetPlaylistAtEachPlay"] = bool(bits & 2)
    d["restartBackward"] = bool(bits & 4)
    d["continuous"] = bool(bits & 8)
    d["global"] = bool(bits & 16)
    d["children"] = read_children(r)
    n = r.u16()
    d["playlist"] = [{"id": r.u32(), "weight": r.s32()} for _ in range(r.count(n, 8))]


def parse_switch_cntr(r: Reader, d: dict) -> None:
    d.update(read_node_base(r))
    d["groupType"] = enum_name(GROUP_TYPES, r.u8())
    d["group"] = r.u32()
    d["defaultSwitch"] = r.u32()
    d["continuousValidation"] = bool(r.u8())
    d["children"] = read_children(r)
    n = r.u32()
    assigns = []
    for _ in range(r.count(n, 8)):
        sw = r.u32()
        k = r.u32()
        assigns.append({"switch": sw, "nodes": [r.u32() for _ in range(r.count(k, 4))]})
    d["assignments"] = assigns
    n = r.u32()
    params = []
    for _ in range(r.count(n, 14)):
        node = r.u32()
        b1 = r.u8()
        b2 = r.u8()
        params.append({"node": node, "firstOnly": bool(b1 & 1), "continuePlayback": bool(b1 & 2),
                       "onSwitchMode": "stop" if b2 & 1 else "playToEnd", "bits": [b1, b2],
                       "fadeOutMs": r.s32(), "fadeInMs": r.s32()})
    d["childParams"] = params


def parse_actor_mixer(r: Reader, d: dict) -> None:
    d.update(read_node_base(r))
    d["children"] = read_children(r)


def parse_layer_cntr(r: Reader, d: dict) -> None:
    d.update(read_node_base(r))
    d["children"] = read_children(r)
    n = r.u32()
    layers = []
    for _ in range(r.count(n, 4)):
        layer = {"id": r.u32(), "rtpcs": read_rtpcs(r), "crossfadeRtpc": r.u32(), "crossfadeRtpcType": r.u8()}
        na = r.u32()
        assoc = []
        for _ in range(r.count(na, 8)):
            child = r.u32()
            npts = r.u32()
            assoc.append({"child": child, "points": convert_points(read_points(r, npts), 0, False)})
        layer["associations"] = assoc
        layers.append(layer)
    d["layers"] = layers


def parse_bus(r: Reader, d: dict) -> None:
    d["parent"] = r.u32()
    _props_into(d, read_props(r), {})
    b1 = r.u8()
    d["bits"] = b1
    d["killNewest"] = bool(b1 & 1)
    d["useVirtualBehavior"] = bool(b1 & 2)
    d["maxInstanceIgnoreParent"] = bool(b1 & 4)
    d["backgroundMusic"] = bool(b1 & 8)
    d["bits2"] = r.u8()
    d["maxInstances"] = r.u16()
    d["channelConfig"] = r.u32()
    hdr = r.u8()
    d["hdrBits"] = hdr
    d["hdrBus"] = bool(hdr & 1)
    d["hdrReleaseExponential"] = bool(hdr & 2)
    d["recoveryTimeMs"] = r.s32()
    d["maxDuckVolume"] = _f(r.f32())
    n = r.u32()
    ducks = []
    for _ in range(r.count(n, 18)):
        ducks.append({"bus": r.u32(), "volume": _f(r.f32()), "fadeOutMs": r.s32(), "fadeInMs": r.s32(),
                      "curve": r.u8(), "targetProp": r.u8()})
    d["ducks"] = ducks
    d.update(read_fx_list(r, with_override=False))
    d["mixer"] = r.u32()
    d["mixerShareSet"] = bool(r.u8())
    d["overrideAttachmentParams"] = r.u8()
    d["rtpcs"] = read_rtpcs(r)
    d["states"] = read_states(r)


def _music_node(r: Reader, d: dict) -> None:
    d["musicFlags"] = r.u8()
    d.update(read_node_base(r))
    d["children"] = read_children(r)
    d["gridPeriodMs"] = r.f64()
    d["gridOffsetMs"] = r.f64()
    d["tempo"] = _f(r.f32())
    d["timeSigBeats"] = r.u8()
    d["timeSigBeatValue"] = r.u8()
    d["meterInfoFlag"] = r.u8()
    n = r.u32()
    stingers = []
    for _ in range(r.count(n, 24)):
        stingers.append({"trigger": r.u32(), "segment": r.u32(), "sync": enum_name(SYNC_TYPES, r.u32()),
                         "cueFilter": r.u32(), "dontRepeatMs": r.s32(), "lookAhead": r.u32()})
    d["stingers"] = stingers


def _fade(r: Reader) -> dict:
    return {"timeMs": r.s32(), "curve": r.u32(), "offsetMs": r.s32()}


def _transitions(r: Reader, d: dict) -> None:
    n = r.u32()
    rules = []
    for _ in range(r.count(n, 8)):
        ns = r.u32()
        src = [r.s32() for _ in range(r.count(ns, 4))]
        nd = r.u32()
        dst = [r.s32() for _ in range(r.count(nd, 4))]
        sf = _fade(r)
        sync = r.u32()
        srule = {"fade": sf, "sync": enum_name(SYNC_TYPES, sync), "syncRaw": sync, "cueFilter": r.u32(),
                 "playPostExit": bool(r.u8())}
        df = _fade(r)
        drule = {"fade": df, "cueFilter": r.u32(), "jumpTo": r.u32()}
        entry = r.u16()
        drule.update({"entry": enum_name(ENTRY_TYPES, entry), "entryRaw": entry, "playPreEntry": bool(r.u8()),
                      "matchSourceCue": bool(r.u8())})
        rule = {"src": src, "dst": dst, "srcRule": srule, "dstRule": drule}
        if r.u8():
            rule["transitionObject"] = {"segment": r.u32(), "fadeIn": _fade(r), "fadeOut": _fade(r),
                                        "playPreEntry": bool(r.u8()), "playPostExit": bool(r.u8())}
        rules.append(rule)
    d["transitions"] = rules


def parse_music_segment(r: Reader, d: dict) -> None:
    _music_node(r, d)
    d["durationMs"] = r.f64()
    n = r.u32()
    markers = []
    for _ in range(r.count(n, 16)):
        mid = r.u32()
        pos = r.f64()
        sl = r.u32()
        name = r.raw(sl).split(b"\0", 1)[0].decode("utf-8", "replace")
        markers.append({"id": mid, "positionMs": pos, "name": name})
    for k, m in enumerate(markers):
        if k == 0:
            m["kind"] = "entry"
        elif k == len(markers) - 1:
            m["kind"] = "exit"
        else:
            m["kind"] = "custom"
    d["markers"] = markers


def clip_length_ms(clip: dict) -> float:
    return clip["sourceDurationMs"] - clip["beginTrimMs"] + clip["endTrimMs"]


def clip_automation(clip_index: int, typ: int, pts: list, clips: list) -> dict:
    """Clip automation x values are stored in seconds from the clip start. fadeIn x becomes ms from
    the clip start, fadeOut x ms relative to the clip end (<= 0); y stays a 0..1 gain."""
    name = enum_name(CLIP_AUTOMATION, typ, "other")
    if name not in ("volume", "lpf", "fadeIn", "fadeOut"):
        name = "other"
    end = clip_length_ms(clips[clip_index]) if 0 <= clip_index < len(clips) else 0.0
    points = []
    for x, y, interp in pts:
        ms = x * 1000.0
        if name == "fadeOut":
            ms -= end
        if name == "volume":
            y = db_from_stored(y)
        points.append([round(ms, 3), _f(y), interp])
    return {"clip": clip_index, "type": name, "rawType": typ, "points": points}


def parse_music_track(r: Reader, d: dict) -> None:
    d["musicFlags"] = r.u8()
    n = r.u32()
    d["sources"] = [read_source(r) for _ in range(r.count(n, 14))]
    n = r.u32()
    clips = []
    for _ in range(r.count(n, 40)):
        clips.append({"subTrack": r.u32(), "source": r.u32(), "playAtMs": r.f64(), "beginTrimMs": r.f64(),
                      "endTrimMs": r.f64(), "sourceDurationMs": r.f64()})
    d["clips"] = clips
    d["subTrackCount"] = r.u32() if n else 0
    n = r.u32()
    autos = []
    for _ in range(r.count(n, 12)):
        clip = r.u32()
        typ = r.u32()
        npts = r.u32()
        autos.append(clip_automation(clip, typ, read_points(r, npts), clips))
    d["automation"] = autos
    d.update(read_node_base(r))
    tt = r.u8()
    d["trackType"] = enum_name(TRACK_TYPES, tt)
    if tt == 3:
        d["switchGroupType"] = enum_name(GROUP_TYPES, r.u8())
        d["switchGroup"] = r.u32()
        d["defaultSwitch"] = r.u32()
        na = r.u32()
        d["switchAssociations"] = [r.u32() for _ in range(r.count(na, 4))]
        d["switchTransition"] = {"srcFade": _fade(r), "sync": enum_name(SYNC_TYPES, r.u32()),
                                 "cueFilter": r.u32(), "dstFade": _fade(r)}
    d["lookAheadMs"] = r.s32()


def parse_music_switch(r: Reader, d: dict) -> None:
    _music_node(r, d)
    _transitions(r, d)
    d["continuePlayback"] = bool(r.u8())
    depth = r.u32()
    groups = [r.u32() for _ in range(r.count(depth, 5))]
    gtypes = [r.u8() for _ in range(depth)]
    d["arguments"] = [{"group": g, "groupType": enum_name(GROUP_TYPES, t)} for g, t in zip(groups, gtypes)]
    size = r.u32()
    d["decisionMode"] = enum_name(DECISION_MODES, r.u8())
    tree = Reader(r.raw(size))
    nodes = []
    while tree.left >= 12:
        nodes.append((tree.u32(), tree.u32(), tree.u16(), tree.u16()))
    if tree.left:
        raise BankError(f"decision tree size {size} is not a multiple of 12")
    d["paths"] = flatten_tree(nodes, depth)


def flatten_tree(nodes: list, depth: int) -> list:
    """Decision tree nodes (key, child-or-node, weight, probability), breadth-first. A node at
    depth `depth` is a leaf whose second field is the audio node id; others hold
    (first child index u16, child count u16). Returns [{"keys": [...], "node", "weight", "probability"}]
    in tree order, keys without the root key (0 = any)."""
    out = []
    if not nodes:
        return out

    def walk(index: int, level: int, keys: list, guard: int):
        if guard > 64 or index >= len(nodes):
            raise BankError("decision tree index out of range")
        key, val, weight, prob = nodes[index]
        here = keys + [key] if level > 0 else keys
        if level == depth:
            out.append({"keys": here, "node": val, "weight": weight, "probability": prob})
            return
        first, cnt = val & 0xFFFF, val >> 16
        for c in range(first, first + cnt):
            walk(c, level + 1, here, guard + 1)

    walk(0, 0, [], 0)
    return out


def parse_music_ranseq(r: Reader, d: dict) -> None:
    _music_node(r, d)
    _transitions(r, d)
    n = r.u32()
    flat = []
    for _ in range(r.count(n, 30)):
        flat.append({"segment": r.u32(), "id": r.u32(), "childCount": r.u32(),
                     "type": playlist_type(r.u32()), "loop": r.s16(), "loopMin": r.s16(),
                     "loopMax": r.s16(), "weight": r.u32(), "avoidRepeatCount": r.u16(),
                     "usingWeight": bool(r.u8()), "shuffle": bool(r.u8())})
    d["playlist"] = build_playlist_tree(flat)


def playlist_type(raw: int) -> str | None:
    """Group items carry an AkMusicPlaylistType; segment leaves store 0xFFFFFFFF (None)."""
    return None if raw == 0xFFFFFFFF else enum_name(PLAYLIST_TYPES, raw)


def build_playlist_tree(flat: list) -> list:
    """Playlist items are stored pre-order with a child count; rebuild the tree."""
    pos = 0

    def take():
        nonlocal pos
        if pos >= len(flat):
            raise BankError("playlist child count exceeds item count")
        item = dict(flat[pos])
        pos += 1
        cnt = item.pop("childCount")
        item["children"] = [take() for _ in range(cnt)]
        return item

    roots = []
    while pos < len(flat):
        roots.append(take())
    return roots


def parse_attenuation(r: Reader, d: dict) -> None:
    cone = r.u8()
    d["coneEnabled"] = bool(cone & 1)
    if cone & 1:
        d["cone"] = {"insideDegrees": _f(r.f32()), "outsideDegrees": _f(r.f32()),
                     "outsideVolume": _f(r.f32()), "lpf": _f(r.f32())}
    uses = [r.s8() for _ in range(len(ATTENUATION_USES))]
    n = r.u8()
    raw_curves = []
    for _ in range(r.count(n, 3)):
        scaling = r.u8()
        npts = r.u16()
        raw_curves.append((scaling, read_points(r, npts)))
    curves = []
    for use_index, ci in enumerate(uses):
        if ci < 0 or ci >= len(raw_curves):
            continue
        scaling, pts = raw_curves[ci]
        use = ATTENUATION_USES[use_index]
        curves.append({"use": use if use != "hpf" else "other", "useIndex": use_index, "curve": ci,
                       "scaling": scaling, "points": convert_points(pts, scaling, use.startswith("volume"))})
    d["curveToUse"] = uses
    d["curves"] = curves
    d["rtpcs"] = read_rtpcs(r)


def decode_fx_params(plugin: int, blob: bytes) -> dict | None:
    if plugin == PLUGIN_METER and len(blob) == 25:
        a, rel, mn, mx, hold, mode, gp = struct.unpack("<fffffBI", blob)
        return {"meter": {"attack": _f(a), "release": _f(rel), "min": _f(mn), "max": _f(mx),
                          "hold": _f(hold), "mode": "rms" if mode == 1 else "peak", "gameParameter": gp}}
    if plugin == PLUGIN_GAIN and len(blob) == 8:
        g, lfe = struct.unpack("<ff", blob)
        return {"gain": {"fullband": _f(g), "lfe": _f(lfe)}}
    return None


def parse_fx(r: Reader, d: dict) -> None:
    d["plugin"] = r.u32()
    size = r.u32()
    blob = r.raw(size)
    d["params"] = blob.hex()
    extra = decode_fx_params(d["plugin"], blob)
    if extra:
        d.update(extra)
    n = r.u8()
    d["media"] = [{"index": r.u8(), "source": r.u32()} for _ in range(r.count(n, 5))]
    d["rtpcs"] = read_rtpcs(r)
    d["fxTail"] = r.u16()


PARSERS = {
    2: parse_sound, 3: parse_action, 4: parse_event, 5: parse_ranseq, 6: parse_switch_cntr,
    7: parse_actor_mixer, 8: parse_bus, 9: parse_layer_cntr, 10: parse_music_segment,
    11: parse_music_track, 12: parse_music_switch, 13: parse_music_ranseq, 14: parse_attenuation,
    18: parse_fx, 19: parse_fx, 20: parse_bus,
}


def parse_object(type_id: int, body: bytes) -> tuple[dict, str | None]:
    """Returns (object, reason) where reason is None when the body was consumed exactly."""
    name = HIRC_TYPES.get(type_id, f"Type{type_id}")
    d: dict = {"type": name, "id": struct.unpack_from("<I", body, 0)[0] if len(body) >= 4 else 0}
    parser = PARSERS.get(type_id)
    if parser is None:
        return d, f"type {name} not supported"
    r = Reader(body, 4)
    try:
        parser(r, d)
    except (BankError, struct.error) as e:
        return d, f"{e}"
    if r.left:
        return d, f"{r.left} bytes left"
    return d, None


# ---------------------------------------------------------------- chunks

def iter_chunks(data: bytes):
    pos = 0
    while pos + 8 <= len(data):
        cid = data[pos:pos + 4].decode("latin1")
        size = struct.unpack_from("<I", data, pos + 4)[0]
        if pos + 8 + size > len(data):
            raise BankError(f"chunk {cid} at {pos} overruns the file")
        yield cid, pos + 8, size
        pos += 8 + size


def read_stmg(r: Reader) -> dict:
    out = {"settings": {"volumeThreshold": _f(r.f32()), "maxVoices": r.u16()}}
    n = r.u32()
    groups = []
    for _ in range(r.count(n, 12)):
        gid = r.u32()
        default_ms = r.s32()
        nt = r.u32()
        trans = [{"from": r.u32(), "to": r.u32(), "timeMs": r.s32()} for _ in range(r.count(nt, 12))]
        groups.append({"id": gid, "defaultTransitionMs": default_ms, "transitions": trans})
    out["stateGroups"] = groups
    n = r.u32()
    sgroups = []
    for _ in range(r.count(n, 12)):
        gid = r.u32()
        rtpc = r.u32()
        npts = r.u32()
        sgroups.append({"id": gid, "rtpc": rtpc, "points": [[_f(x), _f(y), i] for x, y, i in read_points(r, npts)]})
    out["switchGroups"] = sgroups
    n = r.u32()
    params = []
    for _ in range(r.count(n, 21)):
        params.append({"id": r.u32(), "default": _f(r.f32()), "rampType": enum_name(RAMP_TYPES, r.u32()),
                       "rampUp": _f(r.f32()), "rampDown": _f(r.f32()), "builtIn": r.u8()})
    out["gameParameters"] = params
    return out


def read_bank(data: bytes) -> dict:
    bank = {"chunks": [], "header": {}, "didx": [], "dataOffset": None, "objects": [], "unparsed": [],
            "stateGroups": [], "switchGroups": [], "gameParameters": [], "settings": {}, "platform": ""}
    for cid, off, size in iter_chunks(data):
        bank["chunks"].append([cid, size])
        r = Reader(data, off, off + size)
        if cid == "BKHD":
            bank["header"] = {"version": r.u32(), "id": r.u32(), "language": r.u32(),
                              "feedbackInBank": r.u32(), "project": r.u32()}
            if bank["header"]["version"] != BANK_VERSION:
                raise BankError(f"bank version {bank['header']['version']} is not {BANK_VERSION}")
        elif cid == "DIDX":
            for _ in range(size // 12):
                bank["didx"].append({"id": r.u32(), "offset": r.u32(), "size": r.u32()})
        elif cid == "DATA":
            bank["dataOffset"] = off
        elif cid == "STMG":
            st = read_stmg(r)
            if r.left:
                raise BankError(f"STMG: {r.left} bytes left")
            bank.update(st)
        elif cid == "PLAT":
            n = r.u32()
            bank["platform"] = r.raw(n).split(b"\0", 1)[0].decode("latin1")
        elif cid == "HIRC":
            n = r.u32()
            for _ in range(n):
                t = r.u8()
                sz = r.u32()
                body = r.raw(sz)
                obj, reason = parse_object(t, body)
                bank["objects"].append(obj)
                if reason:
                    bank["unparsed"].append({"id": obj["id"], "type": obj["type"], "reason": reason})
    return bank


# ---------------------------------------------------------------- CLI

def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Dump a Wwise v113 SoundBank")
    ap.add_argument("bank", type=Path)
    ap.add_argument("--dump", type=int, help="print one HIRC object as JSON")
    a = ap.parse_args(argv)
    bank = read_bank(a.bank.read_bytes())
    if a.dump is not None:
        found = [o for o in bank["objects"] if o["id"] == a.dump]
        if not found:
            print(f"object {a.dump} not found", file=sys.stderr)
            return 1
        for o in found:
            print(json.dumps(o, indent=1))
        return 0
    counts: dict = {}
    for o in bank["objects"]:
        counts[o["type"]] = counts.get(o["type"], 0) + 1
    print(json.dumps({"header": bank["header"], "chunks": bank["chunks"], "media": len(bank["didx"]),
                      "objects": counts, "unparsed": bank["unparsed"], "platform": bank["platform"],
                      "gameParameters": len(bank["gameParameters"]), "stateGroups": len(bank["stateGroups"])},
                     indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
