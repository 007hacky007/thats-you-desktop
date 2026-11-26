"""Turn the game's Wwise v113 banks into the PortAudio graph JSON read by the runtime.

Inputs: the PS4 GeneratedSoundBanks folder (`LocaleIndependent/*.bnk` and `<Language>/*.bnk`) and,
optionally, the decompiled AK.cs for debug names (only names whose FNV-1 hash matches are kept).

Outputs (audio architecture 3.4, 4.2, 4.3, 3.6):
- <out>/manifest.json                      languages, banks, verified names;
- <out>/banks/<Bank>.json                  LocaleIndependent banks (PaBank);
- <out>/banks/<langKey>/<Bank>.json        localized banks;
- <report_dir>/graph-report.json           object counts, unparsed objects, reachable media per event.

The JSON mirrors the PaModel classes (unity/Port/Audio/Core/Data/PaModel.cs): key = field name with
a lowercase first letter, keys at their C# default are omitted, enums are member-name strings.

CLI: python3 -m pipeline.audio_graph --source <PS4 dir> --out <dir> [--report-dir <dir>] [--names AK.cs]
"""
from __future__ import annotations

import argparse
import json
import math
import re
import sys
from pathlib import Path

from pipeline import wwise_bank as wb

VERSION = "3"
FORMAT = 1
LI_FOLDER = "LocaleIndependent"
DEFAULT_LANGUAGE = "English(UK)"

NODE_TYPES = {
    "Sound": "Sound", "RanSeqCntr": "RanSeq", "SwitchCntr": "Switch", "LayerCntr": "Layer",
    "ActorMixer": "ActorMixer", "MusicSegment": "MusicSegment", "MusicTrack": "MusicTrack",
    "MusicRanSeq": "MusicRanSeq", "MusicSwitch": "MusicSwitch",
}
BUS_TYPES = {"Bus", "AuxBus"}
EFFECT_TYPES = {"FxShareSet", "FxCustom"}

ACTION_TYPES = {
    "play": "Play", "playAndContinue": "Play", "playEvent": "PlayEvent", "stop": "Stop", "pause": "Pause",
    "resume": "Resume", "break": "Break", "seek": "Seek", "setState": "SetState", "setSwitch": "SetSwitch",
    "setGameParameter": "SetGameParameter", "resetGameParameter": "ResetGameParameter", "trigger": "Trigger",
    "mute": "Mute", "unmute": "UnMute", "setVolume": "SetVolume", "resetVolume": "ResetVolume",
    "setPitch": "SetPitch", "resetPitch": "ResetPitch", "setLpf": "SetLPF", "resetLpf": "ResetLPF",
    "setBusVolume": "SetBusVolume", "resetBusVolume": "ResetBusVolume",
}

# Raw prop id -> PaProps field. Everything else goes to Props.Unknown.
PROP_FIELDS = {0: "volume", 1: "lfe", 2: "pitch", 3: "lpf", 5: "busVolume", 6: "priority", 11: "panLR",
               12: "panFR", 13: "centerPct", 16: "probability", 23: "outputBusVolume", 58: "loop",
               59: "initialDelay"}
PROP_DEFAULTS = {"probability": 100.0, "priority": 50.0, "loop": -1}
RAND_FIELDS = {0: "volumeRand", 2: "pitchRand", 3: "lpfRand", 59: "initialDelayRand"}
DUCK_TARGETS = {0: "volume", 5: "busVolume"}

PLUGIN_NAMES = {0x00690003: "parametricEq", 0x006A0003: "delay", 0x006C0003: "compressor",
                0x006D0003: "expander", 0x006E0003: "peakLimiter", 0x00730003: "matrixReverb",
                0x00760003: "roomVerb", 0x00810003: "meter", 0x008B0003: "gain", 0x00C80002: "audioInput"}

LINEAR = 4


# ---------------------------------------------------------------- names and keys

def lang_key(name: str) -> str:
    """Lowercase, every run outside [a-z0-9] becomes '_', strip leading/trailing '_'."""
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def fnv1(name: str) -> int:
    """Wwise ID: 32-bit FNV-1 of the lowercase UTF-8 name; '' -> 0."""
    if not name:
        return 0
    h = 2166136261
    for b in name.lower().encode("utf-8"):
        h = (h * 16777619) & 0xFFFFFFFF
        h ^= b
    return h


_AK_CONST = re.compile(r"\bpublic\s+static\s+(?:readonly\s+)?uint\s+(\w+)\s*=\s*(\d+)u?\s*;")


def load_names(path: Path | None) -> dict[int, str]:
    """AK.cs constants whose FNV-1 hash matches their value (events, states, switches, parameters,
    triggers, busses, banks, external sources)."""
    if path is None or not Path(path).is_file():
        return {}
    names: dict[int, str] = {}
    for name, value in _AK_CONST.findall(Path(path).read_text(encoding="utf-8", errors="replace")):
        v = int(value)
        if fnv1(name) == v and v not in names:
            names[v] = name
    return names


# ---------------------------------------------------------------- JSON helpers

_ZERO = object()


def _put(d: dict, key: str, value, default=_ZERO) -> None:
    """Set d[key] unless value is None or equals the C# field default (zero/false/empty by default)."""
    if value is None:
        return
    if default is _ZERO:
        if value is False or value == "" or (isinstance(value, (list, dict)) and not value):
            return
        if isinstance(value, (int, float)) and not isinstance(value, bool) and value == 0:
            return
    elif value == default and isinstance(value, bool) == isinstance(default, bool):
        return
    d[key] = value


def _num(v):
    if isinstance(v, float) and not math.isfinite(v):
        return 0.0
    return v


def _clean(o):
    if isinstance(o, dict):
        return {k: _clean(v) for k, v in o.items()}
    if isinstance(o, list):
        return [_clean(v) for v in o]
    return _num(o)


def dumps(obj) -> bytes:
    return json.dumps(_clean(obj), separators=(",", ":"), ensure_ascii=False, allow_nan=False).encode("utf-8")


# ---------------------------------------------------------------- model conversion

def props_json(raw: dict, ranges: dict | None = None) -> dict:
    out: dict = {}
    unknown: dict = {}
    for pid, v in sorted(raw.items()):
        field = PROP_FIELDS.get(pid)
        if field:
            _put(out, field, v, PROP_DEFAULTS.get(field))
        else:
            unknown[str(pid)] = v
    for pid, (lo, hi) in sorted((ranges or {}).items()):
        field = RAND_FIELDS.get(pid)
        if field and (lo or hi):
            out[field] = {"min": lo, "max": hi}
    if unknown:
        out["unknown"] = unknown
    return out


def rtpc_json(r: dict) -> dict:
    d: dict = {"rtpc": r["rtpc"], "param": r["param"]}
    _put(d, "accumulation", r["accumulation"])
    _put(d, "scaling", wb.enum_name(wb.CURVE_SCALINGS, r["scaling"]))
    _put(d, "paramId", r["paramId"])
    _put(d, "curve", r["points"])
    return d


def source_json(s: dict) -> dict:
    d: dict = {}
    _put(d, "plugin", s["plugin"])
    _put(d, "media", s["id"])
    _put(d, "inMemorySize", s["inMemorySize"])
    _put(d, "kind", s["kind"])
    _put(d, "stream", s["stream"])
    _put(d, "languageSpecific", s["languageSpecific"])
    return d


def positioning_json(o: dict) -> dict | None:
    p = o.get("positioning") or {}
    if not p.get("overrideParent"):
        return None
    d: dict = {}
    three_d = bool(p.get("threeD"))
    _put(d, "threeD", three_d)
    _put(d, "spatialize", three_d and bool(p.get("gameDefined")))
    _put(d, "usePanner", bool(p.get("pannerEnabled")))
    _put(d, "attenuation", p.get("attenuation", 0))
    raw = o.get("rawProps", {})
    _put(d, "panLR", raw.get(11, 0.0))
    _put(d, "panFR", raw.get(12, 0.0))
    _put(d, "centerPct", raw.get(13, 0.0))
    return d


def advanced_json(a: dict | None) -> dict | None:
    if not a or not (a["maxInstances"] or a["killNewest"] or a["useVirtualBehavior"]):
        return None
    d: dict = {}
    _put(d, "maxInstances", a["maxInstances"])
    _put(d, "virtualBehavior", a["virtualQueueBehavior"])
    _put(d, "belowThreshold", a["belowThresholdBehavior"])
    _put(d, "killNewest", a["killNewest"])
    _put(d, "useVirtual", a["useVirtualBehavior"])
    _put(d, "globalLimit", True)
    return d


def fade_fields(d: dict, fade: dict, ms_key: str, offset_key: str | None, curve_key: str, curve_default) -> None:
    _put(d, ms_key, fade["timeMs"])
    if offset_key:
        _put(d, offset_key, fade["offsetMs"])
    _put(d, curve_key, fade["curve"], curve_default)


def transition_json(t: dict) -> dict:
    d: dict = {"src": t["src"], "dst": t["dst"]}
    s = t["srcRule"]
    src: dict = {}
    fade_fields(src, s["fade"], "fadeMs", "fadeOffsetMs", "curve", LINEAR)
    _put(src, "syncRaw", s["syncRaw"])
    _put(src, "sync", s["sync"])
    _put(src, "cueFilter", s["cueFilter"])
    _put(src, "playPostExit", s["playPostExit"])
    d["srcRule"] = src
    t2 = t["dstRule"]
    dst: dict = {}
    fade_fields(dst, t2["fade"], "fadeMs", "fadeOffsetMs", "curve", LINEAR)
    _put(dst, "entryRaw", t2["entryRaw"])
    _put(dst, "cueFilter", t2["cueFilter"])
    _put(dst, "jumpTo", t2["jumpTo"])
    _put(dst, "entry", t2["entry"])
    _put(dst, "playPreEntry", t2["playPreEntry"])
    _put(dst, "matchSourceCueName", t2["matchSourceCue"])
    d["dstRule"] = dst
    tobj = t.get("transitionObject")
    if tobj:
        seg: dict = {"segment": tobj["segment"]}
        _put(seg, "fadeInMs", tobj["fadeIn"]["timeMs"])
        _put(seg, "fadeOutMs", tobj["fadeOut"]["timeMs"])
        _put(seg, "fadeInCurve", tobj["fadeIn"]["curve"])
        _put(seg, "fadeOutCurve", tobj["fadeOut"]["curve"])
        _put(seg, "playPreEntry", tobj["playPreEntry"])
        _put(seg, "playPostExit", tobj["playPostExit"])
        d["segment"] = seg
    return d


def playlist_item_json(item: dict) -> dict:
    d: dict = {}
    _put(d, "id", item["id"])
    _put(d, "segment", item["segment"])
    _put(d, "type", item["type"])
    _put(d, "loop", item["loop"], 1)
    _put(d, "weight", item["weight"])
    _put(d, "avoidRepeat", item["avoidRepeatCount"])
    _put(d, "shuffle", item["shuffle"])
    _put(d, "children", [playlist_item_json(c) for c in item["children"]])
    return d


def music_json(o: dict) -> dict:
    t = o["type"]
    m: dict = {}
    if t != "MusicTrack":
        _put(m, "tempo", o["tempo"])
        _put(m, "timeSigNum", o["timeSigBeats"])
        _put(m, "timeSigDen", o["timeSigBeatValue"])
        _put(m, "gridPeriodMs", o["gridPeriodMs"])
        _put(m, "gridOffsetMs", o["gridOffsetMs"])
        stingers = []
        for s in o["stingers"]:
            sd: dict = {}
            _put(sd, "trigger", s["trigger"])
            _put(sd, "segment", s["segment"])
            _put(sd, "cueFilter", s["cueFilter"])
            _put(sd, "sync", s["sync"])
            _put(sd, "dontRepeatMs", s["dontRepeatMs"])
            _put(sd, "lookAheadSegments", s["lookAhead"])
            stingers.append(sd)
        _put(m, "stingers", stingers)
    if t in ("MusicSwitch", "MusicRanSeq"):
        _put(m, "transitions", [transition_json(x) for x in o["transitions"]])
    if t == "MusicSegment":
        _put(m, "durationMs", o["durationMs"])
        markers = o["markers"]
        if markers:
            _put(m, "entryMs", markers[0]["positionMs"])
            _put(m, "exitMs", markers[-1]["positionMs"] if len(markers) > 1 else o["durationMs"])
        out = []
        for mk in markers:
            md: dict = {}
            _put(md, "id", mk["id"])
            _put(md, "posMs", mk["positionMs"])
            _put(md, "name", mk["name"] or None)
            _put(md, "kind", mk["kind"])
            out.append(md)
        _put(m, "markers", out)
    elif t == "MusicTrack":
        _put(m, "trackType", o["trackType"])
        _put(m, "lookAheadMs", o["lookAheadMs"])
        _put(m, "sources", [source_json(s) for s in o["sources"]])
        clips = []
        for c in o["clips"]:
            cd: dict = {}
            _put(cd, "subTrack", c["subTrack"])
            _put(cd, "media", c["source"])
            _put(cd, "playAtMs", c["playAtMs"])
            _put(cd, "beginTrimMs", c["beginTrimMs"])
            _put(cd, "endTrimMs", c["endTrimMs"])
            _put(cd, "srcDurationMs", c["sourceDurationMs"])
            clips.append(cd)
        _put(m, "clips", clips)
        autos = []
        for a in o["automation"]:
            ad: dict = {}
            _put(ad, "clip", a["clip"])
            _put(ad, "type", a["type"])
            _put(ad, "points", a["points"])
            autos.append(ad)
        _put(m, "automation", autos)
    elif t == "MusicRanSeq":
        roots = o["playlist"]
        if len(roots) == 1:
            m["playlist"] = playlist_item_json(roots[0])
        elif roots:
            m["playlist"] = playlist_item_json({"id": 0, "segment": 0, "type": "sequenceContinuous", "loop": 1,
                                                "weight": 0, "avoidRepeatCount": 0, "shuffle": False,
                                                "children": roots})
    elif t == "MusicSwitch":
        _put(m, "mode", o["decisionMode"])
        _put(m, "continuePlaying", o["continuePlayback"])
        _put(m, "arguments", [{"group": a["group"], **({"isState": True} if a["groupType"] == "state" else {})}
                              for a in o["arguments"]])
        paths = []
        for p in o["paths"]:
            pd: dict = {"keys": p["keys"]}
            _put(pd, "node", p["node"])
            _put(pd, "weight", p["weight"])
            paths.append(pd)
        _put(m, "paths", paths)
    return m


def node_json(o: dict, names: dict) -> dict:
    t = o["type"]
    d: dict = {"id": o["id"]}
    _put(d, "parent", o.get("parent", 0))
    _put(d, "bus", o.get("bus", 0))
    if NODE_TYPES[t] != "Sound":
        d["type"] = NODE_TYPES[t]
    _put(d, "name", names.get(o["id"]))
    _put(d, "children", o.get("children", []))
    _put(d, "props", props_json(o.get("rawProps", {}), o.get("rawRanges", {})))
    _put(d, "positioning", positioning_json(o))
    _put(d, "rtpcs", [rtpc_json(r) for r in o.get("rtpcs", [])])
    _put(d, "advanced", advanced_json(o.get("advanced")))
    if t == "Sound":
        d["source"] = source_json(o["source"])
    elif t == "RanSeqCntr":
        rs: dict = {}
        _put(rs, "sequence", o["mode"] == "sequence")
        _put(rs, "shuffle", o["randomMode"] == "shuffle")
        _put(rs, "continuous", o["continuous"])
        _put(rs, "resetEachPlay", o["resetPlaylistAtEachPlay"])
        _put(rs, "restartBackward", o["restartBackward"])
        _put(rs, "global", o["global"])
        _put(rs, "useWeights", o["usingWeight"])
        _put(rs, "avoidRepeat", o["avoidRepeatCount"])
        _put(rs, "loopCount", o["loopCount"], 1)
        _put(rs, "loopMin", o["loopModMin"])
        _put(rs, "loopMax", o["loopModMax"])
        _put(rs, "transition", o["transitionMode"], "none")
        _put(rs, "transitionSec", o["transitionTime"] / 1000.0)
        _put(rs, "transitionMinSec", o["transitionTimeModMin"] / 1000.0)
        _put(rs, "transitionMaxSec", o["transitionTimeModMax"] / 1000.0)
        _put(rs, "playlist", [[p["id"], p["weight"]] if p["weight"] else [p["id"]] for p in o["playlist"]])
        d["ranSeq"] = rs
    elif t == "SwitchCntr":
        sw: dict = {}
        _put(sw, "isState", o["groupType"] == "state")
        _put(sw, "continuous", o["continuousValidation"])
        _put(sw, "group", o["group"])
        _put(sw, "default", o["defaultSwitch"])
        _put(sw, "assignments", [{"value": a["switch"], **({"nodes": a["nodes"]} if a["nodes"] else {})}
                                 for a in o["assignments"]])
        params = []
        for p in o["childParams"]:
            pd: dict = {"node": p["node"]}
            _put(pd, "firstOnly", p["firstOnly"])
            _put(pd, "continuePlayback", p["continuePlayback"])
            _put(pd, "onSwitchMode", 1 if p["onSwitchMode"] == "stop" else 0)
            _put(pd, "fadeOutMs", p["fadeOutMs"])
            _put(pd, "fadeInMs", p["fadeInMs"])
            params.append(pd)
        _put(sw, "childParams", params)
        d["switch"] = sw
    elif t.startswith("Music"):
        d["music"] = music_json(o)
    return d


def action_json(o: dict) -> dict:
    d: dict = {"id": o["id"]}
    raw = o.get("rawProps", {})
    ranges = o.get("rawRanges", {})
    _put(d, "target", o["target"])
    _put(d, "stateGroup", o.get("stateGroup"))
    _put(d, "state", o.get("state"))
    _put(d, "switchGroup", o.get("switchGroup"))
    _put(d, "switchValue", o.get("switch"))
    _put(d, "bankId", o.get("bank"))
    _put(d, "type", ACTION_TYPES.get(o["action"]))
    _put(d, "rawType", o["rawType"])
    _put(d, "targetMode", o["targetMode"], "Element")
    _put(d, "gameObjectScoped", o["gameObjectScoped"])
    _put(d, "targetIsBus", o["targetIsBus"])
    _put(d, "valueRelative", o.get("valueMeaning") == "offset")
    _put(d, "bypassTransition", o.get("bypassTransition", False))
    _put(d, "delayMs", raw.get(14, 0))
    lo, hi = ranges.get(14, (0, 0))
    _put(d, "delayMinMs", lo)
    _put(d, "delayMaxMs", hi)
    _put(d, "transitionMs", raw.get(15, 0))
    lo, hi = ranges.get(15, (0, 0))
    _put(d, "transitionMinMs", lo)
    _put(d, "transitionMaxMs", hi)
    _put(d, "curve", o.get("fadeCurve", LINEAR), LINEAR)
    _put(d, "probability", raw.get(16, 100.0), 100.0)
    _put(d, "value", o.get("value", 0.0))
    _put(d, "valueMin", o.get("valueMin", 0.0))
    _put(d, "valueMax", o.get("valueMax", 0.0))
    _put(d, "exceptions", [{"id": e["id"], **({"isBus": True} if e["isBus"] else {})} for e in o.get("exceptions", [])])
    return d


def bus_json(o: dict, names: dict) -> dict:
    d: dict = {"id": o["id"]}
    _put(d, "parent", o["parent"])
    _put(d, "name", names.get(o["id"]))
    _put(d, "isAux", o["type"] == "AuxBus")
    _put(d, "backgroundMusic", o["backgroundMusic"])
    _put(d, "props", props_json(o["rawProps"]))
    _put(d, "rtpcs", [rtpc_json(r) for r in o["rtpcs"]])
    ducks = []
    for k in o["ducks"]:
        dk: dict = {"bus": k["bus"]}
        _put(dk, "volumeDb", k["volume"])
        _put(dk, "fadeOutMs", k["fadeOutMs"])
        _put(dk, "fadeInMs", k["fadeInMs"])
        _put(dk, "curve", k["curve"])
        _put(dk, "targetProp", DUCK_TARGETS.get(k["targetProp"], str(k["targetProp"])))
        ducks.append(dk)
    _put(d, "ducks", ducks)
    _put(d, "duckRecoveryMs", o["recoveryTimeMs"])
    _put(d, "maxDuckDb", o["maxDuckVolume"])
    bypass = o.get("bypassBits", 0)
    fx = []
    for f in o["fx"]:
        fd: dict = {}
        _put(fd, "index", f["index"])
        _put(fd, "effect", f["id"])
        _put(fd, "bypass", bool(bypass & (1 << f["index"])) or bool(bypass & 0x10))
        fx.append(fd)
    _put(d, "fx", fx)
    return d


def attenuation_json(o: dict) -> dict:
    d: dict = {"id": o["id"]}
    _put(d, "coneUse", o["coneEnabled"])
    cone = o.get("cone")
    if cone:
        _put(d, "coneInnerDeg", cone["insideDegrees"])
        _put(d, "coneOuterDeg", cone["outsideDegrees"])
        _put(d, "coneMaxDb", cone["outsideVolume"])
        _put(d, "coneLpf", cone["lpf"])
    curves = []
    for c in o["curves"]:
        cd: dict = {"use": c["use"]}
        _put(cd, "scaling", wb.enum_name(wb.CURVE_SCALINGS, c["scaling"]))
        _put(cd, "points", c["points"])
        curves.append(cd)
    _put(d, "curves", curves)
    _put(d, "rtpcs", [rtpc_json(r) for r in o["rtpcs"]])
    return d


def effect_json(o: dict) -> dict:
    d: dict = {"id": o["id"]}
    _put(d, "plugin", o["plugin"])
    _put(d, "pluginName", PLUGIN_NAMES.get(o["plugin"], "other"))
    _put(d, "rawParams", o["params"] or None)
    _put(d, "shareSet", o["type"] == "FxShareSet")
    m = o.get("meter")
    if m:
        md: dict = {}
        _put(md, "attackSec", m["attack"])
        _put(md, "releaseSec", m["release"])
        _put(md, "holdSec", m["hold"])
        _put(md, "minDb", m["min"])
        _put(md, "maxDb", m["max"])
        _put(md, "mode", m["mode"])
        _put(md, "gameParameter", m["gameParameter"])
        d["meter"] = md
    g = o.get("gain")
    if g:
        _put(d, "gainDb", g["fullband"])
    _put(d, "rtpcs", [rtpc_json(r) for r in o["rtpcs"]])
    return d


def bank_json(bank: dict, name: str, language: str, names: dict) -> dict:
    out = {"format": FORMAT, "name": name, "id": bank["header"].get("id", 0), "language": language,
           "languageKey": lang_key(language) if language else "", "events": [], "actions": [], "nodes": [],
           "busses": [], "attenuations": [], "gameParameters": [], "stateGroups": [], "effects": []}
    for o in bank["objects"]:
        t = o["type"]
        try:
            if t == "Event":
                ev: dict = {"id": o["id"]}
                _put(ev, "name", names.get(o["id"]))
                _put(ev, "actions", o.get("actions", []))
                out["events"].append(ev)
            elif t == "Action" and "action" in o:
                out["actions"].append(action_json(o))
            elif t in NODE_TYPES:
                out["nodes"].append(node_json(o, names))
            elif t in BUS_TYPES:
                out["busses"].append(bus_json(o, names))
            elif t == "Attenuation":
                out["attenuations"].append(attenuation_json(o))
            elif t in EFFECT_TYPES:
                out["effects"].append(effect_json(o))
        except KeyError:
            # partially parsed object (listed as unparsed): keep what can be emitted
            out.setdefault("_partial", []).append(o["id"])
    out.pop("_partial", None)
    for g in bank.get("gameParameters", []):
        gd: dict = {"id": g["id"]}
        _put(gd, "name", names.get(g["id"]))
        _put(gd, "rampType", g["rampType"])
        _put(gd, "default", g["default"])
        _put(gd, "rampUp", g["rampUp"])
        _put(gd, "rampDown", g["rampDown"])
        out["gameParameters"].append(gd)
    for s in bank.get("stateGroups", []):
        sd: dict = {"id": s["id"]}
        _put(sd, "name", names.get(s["id"]))
        _put(sd, "defaultTransitionMs", s["defaultTransitionMs"])
        out["stateGroups"].append(sd)
    return out


# ---------------------------------------------------------------- reachability

class Scope:
    """Objects visible to events of one bank language: LocaleIndependent + that language."""

    def __init__(self, key: str, objects: dict, embedded: dict):
        self.key = key            # langKey used for language-specific media
        self.objects = objects    # id -> (object, scope of the defining bank)
        self.embedded = embedded  # media id -> scope ("li" or langKey) of a DIDX that holds it


def source_media(src: dict, origin: str, scope: Scope) -> dict | None:
    kind = src.get("kind")
    if kind == "external":
        return {"id": src["id"], "scope": "external"}
    if kind == "audioInput":
        return {"id": src["id"], "scope": "audioInput"}
    if not src.get("id"):
        return None
    if src.get("stream") == "embedded":
        return {"id": src["id"], "scope": scope.embedded.get(src["id"], origin)}
    return {"id": src["id"], "scope": scope.key if src.get("languageSpecific") else "li"}


def node_refs(o: dict) -> list[int]:
    t = o["type"]
    refs = list(o.get("children", []))
    if t == "SwitchCntr":
        for a in o["assignments"]:
            refs += a["nodes"]
    elif t == "MusicSwitch":
        refs += [p["node"] for p in o["paths"]]
    elif t == "MusicRanSeq":
        stack = list(o["playlist"])
        while stack:
            it = stack.pop()
            refs.append(it["segment"])
            stack += it["children"]
    if t in ("MusicSwitch", "MusicRanSeq"):
        refs += [r["transitionObject"]["segment"] for r in o["transitions"] if "transitionObject" in r]
    if t.startswith("Music") and t != "MusicTrack":
        refs += [s["segment"] for s in o.get("stingers", [])]
    out, seen = [], set()
    for r in refs:
        if r and r not in seen:
            seen.add(r)
            out.append(r)
    return out


def reach_event(event_id: int, scope: Scope) -> tuple[list[dict], list[int]]:
    media: dict = {}
    missing: list[int] = []
    seen_nodes: set = set()
    seen_events: set = set()

    def miss(i):
        if i and i not in missing:
            missing.append(i)

    def walk_node(nid: int):
        if nid in seen_nodes:
            return
        seen_nodes.add(nid)
        hit = scope.objects.get(nid)
        if hit is None:
            miss(nid)
            return
        o, origin = hit
        srcs = [o["source"]] if o["type"] == "Sound" and "source" in o else o.get("sources", [])
        for s in srcs:
            m = source_media(s, origin, scope)
            if m:
                media[(m["scope"], m["id"])] = m
        for r in node_refs(o) if o["type"] in NODE_TYPES else []:
            walk_node(r)

    def walk_event(eid: int):
        if eid in seen_events:
            return
        seen_events.add(eid)
        hit = scope.objects.get(eid)
        if hit is None or hit[0]["type"] != "Event":
            miss(eid)
            return
        for aid in hit[0].get("actions", []):
            ah = scope.objects.get(aid)
            if ah is None:
                miss(aid)
                continue
            a = ah[0]
            if a.get("action") in ("play", "playAndContinue"):
                if a["target"]:
                    walk_node(a["target"])
            elif a.get("action") == "playEvent":
                walk_event(a["target"])

    walk_event(event_id)
    return sorted(media.values(), key=lambda m: (m["scope"], m["id"])), missing


PLAY_ACTIONS = ("play", "playAndContinue")
# Every action kind the bank parser names except the ones that play something. "other" (a kind the
# parser does not know) is deliberately absent, so an unknown action is never assumed to be harmless.
CONTROL_ACTIONS = frozenset(wb.ACTION_NAMES.values()) - {"play", "playAndContinue", "playEvent"}


def control_only(event: dict, scope: Scope, _seen: set | None = None) -> bool:
    """True when the event has at least one action, every action is a known non-playing kind (stop, pause,
    resume, set game parameter...) or a PlayEvent chain to such events. Such events reach no media by design.
    An event with no actions, or with an action no bank defines or the parser does not know, is not."""
    seen = _seen if _seen is not None else set()
    seen.add(event["id"])
    actions = event.get("actions", [])
    if not actions:
        return False
    for aid in actions:
        hit = scope.objects.get(aid)
        if hit is None:
            return False
        kind = hit[0].get("action")
        if kind == "playEvent":
            tid = hit[0].get("target")
            if tid in seen:
                continue
            target = scope.objects.get(tid)
            if target is None or target[0]["type"] != "Event" or not control_only(target[0], scope, seen):
                return False
        elif kind not in CONTROL_ACTIONS:
            return False
    return True


def referenced_media(report: dict) -> dict[str, set[int]]:
    """scope ("li" or langKey) -> media ids (embedded + streamed) used by any event or bank node."""
    out: dict[str, set[int]] = {}
    entries = [m for e in report.get("events", []) for m in e.get("media", [])]
    entries += [m for b in report.get("banks", []) for m in b.get("media", [])]
    for m in entries:
        if m["scope"] in ("external", "audioInput"):
            continue
        out.setdefault(m["scope"], set()).add(m["id"])
    return out


# ---------------------------------------------------------------- build

def discover(source_root: Path) -> list[tuple[str, Path]]:
    """[(folder, bank path)] with LocaleIndependent first, then languages alphabetically."""
    out = []
    folders = sorted((p for p in Path(source_root).iterdir() if p.is_dir()),
                     key=lambda p: (p.name != LI_FOLDER, p.name.lower()))
    for f in folders:
        for b in sorted(f.glob("*.bnk"), key=lambda p: p.name.lower()):
            out.append((f.name, b))
    return out


def _write(path: Path, payload: bytes, written: set) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.is_file() or path.read_bytes() != payload:
        path.write_bytes(payload)
    written.add(path.resolve())


def build(source_root: Path, out_root: Path, *, report_dir: Path, names: Path | None = None, log=print) -> dict:
    source_root, out_root, report_dir = Path(source_root), Path(out_root), Path(report_dir)
    name_map = load_names(names)
    banks = []
    for folder, path in discover(source_root):
        parsed = wb.read_bank(path.read_bytes())
        language = "" if folder == LI_FOLDER else folder
        scope = "li" if not language else lang_key(language)
        banks.append({"folder": folder, "name": path.stem, "language": language, "scope": scope, "bank": parsed})
    if not banks:
        raise FileNotFoundError(f"no .bnk files under {source_root}")

    written: set = set()
    languages: dict[str, dict] = {}
    manifest_banks: dict[str, dict] = {}
    for b in banks:
        bank = b["bank"]
        if b["language"]:
            languages.setdefault(b["language"], {"name": b["language"], "key": b["scope"],
                                                 "bankLanguageId": bank["header"].get("language", 0)})
            rel = f"banks/{b['scope']}/{b['name']}.json"
            info = manifest_banks.setdefault(b["name"], {"name": b["name"], "id": bank["header"].get("id", 0),
                                                         "localized": True,
                                                         "file": f"banks/{{lang}}/{b['name']}.json",
                                                         "languages": []})
            info["languages"].append(b["scope"])
        else:
            rel = f"banks/{b['name']}.json"
            manifest_banks[b["name"]] = {"name": b["name"], "id": bank["header"].get("id", 0), "file": rel}
        _write(out_root / rel, dumps(bank_json(bank, b["name"], b["language"], name_map)), written)

    for info in manifest_banks.values():
        if "languages" in info:
            info["languages"] = sorted(set(info["languages"]))
    langs = sorted(languages.values(), key=lambda l: l["name"].lower())
    default_language = DEFAULT_LANGUAGE if DEFAULT_LANGUAGE in languages else (langs[0]["name"] if langs else "")
    manifest = {"format": FORMAT, "wwiseBankVersion": wb.BANK_VERSION, "defaultLanguage": default_language,
                "languages": langs, "banks": list(manifest_banks.values()),
                "names": [[i, n] for i, n in sorted(name_map.items())]}
    _write(out_root / "manifest.json", dumps(manifest), written)

    # stale bank JSON from an earlier run
    banks_dir = out_root / "banks"
    for p in sorted(banks_dir.rglob("*.json"), reverse=True):
        if p.resolve() not in written:
            p.unlink()
    for d in sorted((p for p in banks_dir.rglob("*") if p.is_dir()), reverse=True):
        if not any(d.iterdir()):
            d.rmdir()

    report = build_report(banks, name_map, default_language)
    report_dir.mkdir(parents=True, exist_ok=True)
    (report_dir / "graph-report.json").write_bytes(
        json.dumps(_clean(report), indent=1, allow_nan=False).encode("utf-8"))
    unparsed = sum(len(b["unparsed"]) for b in report["banks"])
    log(f"audio graph: {len(banks)} banks, {len(langs)} languages, {len(report['events'])} event entries, "
        f"{unparsed} unparsed objects, names {report['names']['resolved']}/{report['names']['events']}")
    return report


def build_report(banks: list[dict], names: dict, default_language: str) -> dict:
    li_objects: dict = {}
    all_ids = {o["id"] for b in banks for o in b["bank"]["objects"]}
    li_embedded: dict = {}
    lang_objects: dict[str, dict] = {}
    lang_embedded: dict[str, dict] = {}
    for b in banks:
        objs = li_objects if not b["language"] else lang_objects.setdefault(b["scope"], {})
        emb = li_embedded if not b["language"] else lang_embedded.setdefault(b["scope"], {})
        for o in b["bank"]["objects"]:
            objs.setdefault(o["id"], (o, b["scope"]))
        for m in b["bank"]["didx"]:
            emb.setdefault(m["id"], b["scope"])

    def scope_for(key: str) -> Scope:
        objects = dict(lang_objects.get(key, {}))
        objects.update(li_objects)
        embedded = dict(lang_embedded.get(key, {}))
        embedded.update(li_embedded)
        return Scope(key, objects, embedded)

    default_key = lang_key(default_language) if default_language else "li"
    scopes: dict[str, Scope] = {}
    report_banks, events = [], []
    event_ids: set = set()
    for b in banks:
        key = b["scope"] if b["language"] else default_key
        scope = scopes.get(key) or scopes.setdefault(key, scope_for(key))
        bank = b["bank"]
        counts: dict = {}
        bank_media: dict = {}
        for o in bank["objects"]:
            counts[o["type"]] = counts.get(o["type"], 0) + 1
            srcs = [o["source"]] if o["type"] == "Sound" and "source" in o else o.get("sources", [])
            for s in srcs:
                m = source_media(s, b["scope"], scope)
                if m and m["scope"] not in ("external", "audioInput"):
                    bank_media[(m["scope"], m["id"])] = m
        for m in bank["didx"]:
            bank_media[(b["scope"], m["id"])] = {"id": m["id"], "scope": b["scope"]}
        report_banks.append({"name": b["name"], "id": bank["header"].get("id", 0), "language": b["language"],
                             "objects": counts, "unparsed": bank["unparsed"],
                             "media": sorted(bank_media.values(), key=lambda m: (m["scope"], m["id"]))})
        for o in bank["objects"]:
            if o["type"] != "Event":
                continue
            event_ids.add(o["id"])
            media, missing = reach_event(o["id"], scope)
            ev = {"id": o["id"], "name": names.get(o["id"]), "bank": b["name"], "language": b["language"],
                  "media": media, "reachesNothing": not media, "missingTargets": missing,
                  # missing targets that no bank of any language defines: dead references in the original data
                  "deadTargets": [t for t in missing if t not in all_ids],
                  "controlOnly": control_only(o, scope)}
            events.append(ev)
    return {"version": VERSION, "banks": report_banks, "events": events,
            "names": {"events": len(event_ids), "resolved": sum(1 for i in event_ids if i in names)}}


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Convert Wwise v113 banks to PortAudio graph JSON")
    ap.add_argument("--source", type=Path, required=True, help="GeneratedSoundBanks/PS4 directory")
    ap.add_argument("--out", type=Path, required=True, help="PortAudio output root")
    ap.add_argument("--report-dir", type=Path, help="where graph-report.json goes (default: <out>/../reports)")
    ap.add_argument("--names", type=Path, help="AK.cs for FNV-verified debug names")
    a = ap.parse_args(argv)
    report_dir = a.report_dir or a.out.parent / "reports"
    report = build(a.source, a.out, report_dir=report_dir, names=a.names)
    unparsed = sum(len(b["unparsed"]) for b in report["banks"])
    return 1 if unparsed else 0


if __name__ == "__main__":
    sys.exit(main())
