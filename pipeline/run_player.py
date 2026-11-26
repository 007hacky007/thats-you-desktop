"""Run the macOS player build for a while and summarize what happened.

Starts work/builds/mac/ThatsYou.app windowed at 1280x720 with the PortDebug observer enabled
(-portObserve <run>/shots, optional -portKeys), waits until it exits or the timeout passes,
kills it, then prints errors and exceptions (deduplicated), scenes loaded, how the game's UI
state changed between the observer's "probe" events (menu page, selection, title, visible label
texts) and screenshots.

--fresh-save starts from an empty save (-portSaveDir <run>/save) instead of the shared one in
Application.persistentDataPath. --no-image-effects passes -portNoImageEffects (camera OnRenderImage
effects switched off, except the ones the game needs such as MenuRenderer and ScreenCopier);
--fx-only "<Type,Type>" narrows that to the listed types (-portNoImageEffectsOnly) and --fx-keep
"<Type,Type>" keeps more types on (-portKeepImageEffects). --render-summary prints the cameras, image effects and shaders
the render probe saw in Home (<run>/shots/render.jsonl), flagging the placeholder shaders exported
as DummyShaderTextExporter under work/project/Assets/Resources.

The player runs under `caffeinate -d -i -u` (on macOS, unless --no-caffeinate): with the display
asleep the player blocks in Metal presentation after its first frame. While it runs, a warning is
printed when no new timer screenshot appears for a while after the first one (a stalled player).
--shot-interval SEC sets the timer screenshot interval (-portShotInterval, default 2 s);
--key-shots also saves a screenshot with the probe 0.5 s after each scripted key is released
(-portKeyShots), and a "<t>:shot" entry in --keys saves one at <t> without pressing anything.
--extra "<args>" appends arbitrary player arguments, split like a shell would (shlex), e.g.
--extra '-portNoEnvFix' or --extra '-portNoLightShadows Spot,Point'.

Audio (see unity/Port/Runtime/PortDebug/README.md, section Audio): --audio-trace writes the PortAudio
trace to <run>/audio.jsonl (-portAudioTrace), --audio-record records the final mix to <run>/mix.wav
(-portAudioRecord), --audio-root DIR reads converted audio from DIR instead of StreamingAssets/PortAudio
(-portAudioRoot), --voice-language NAME picks the voice language (-portVoiceLanguage) and --audio-off
switches PortAudio off (-portAudioOff). The summary then gets an "audio:" section: trace event counts,
unknown events, voice starts/stops/failures, and an analysis of the recording (duration, peak, audible
seconds, longest silence) with a WARNING when a requested recording is missing or silent.

CLI: python3 -m pipeline.run_player [--name NAME] [--seconds 60] [--keys "3:cross,6:down"]
     [--fresh-save] [--no-image-effects [--fx-only T,T] [--fx-keep T,T]] [--render-summary]
     [--shot-interval SEC] [--key-shots] [--no-caffeinate] [--extra "-flag value ..."]
     [--audio-trace] [--audio-record] [--audio-root DIR] [--voice-language NAME] [--audio-off]
"""
from __future__ import annotations

import argparse
import json
import math
import os
import plistlib
import re
import shlex
import shutil
import struct
import subprocess
import sys
import time
import wave
from array import array
from collections import Counter
from pathlib import Path

EXCEPTION = re.compile(r"^([A-Za-z_][\w.]*(Exception|Error)): .*$")
NATIVE_ERROR = re.compile(r"^(Shader '.*' .*not (found|supported).*|.*is not supported on this GPU.*"
                          r"|The referenced script .*is missing!|DllNotFoundException.*"
                          r"|Unable to load .*|Failed to .*|Could not .*)$")
CRASH = re.compile(r"Received signal SIG|Crash!!!|Obtained \d+ stack frames")
SCENE = "[PortObserve] scene loaded: "
KEY = re.compile(r"^\d+(\.\d+)?:\S.*$")
DUMMY_MARK = "DummyShaderTextExporter"
SHADER_NAME = re.compile(r'^\s*Shader\s+"([^"]+)"', re.M)
TYPE_NAME = re.compile(r"^[A-Za-z_][\w.+`]*$")
TIMER_SHOT = "_timer.png"
MIN_SHOT_INTERVAL = 0.1
STALL_SECONDS = 8.0
FIRST_SHOT_GRACE = 20.0
STALL_HINT = "player stalled - display asleep? see pmset -g log | grep Display"
AUDIO_TRACE = "audio.jsonl"
AUDIO_RECORD = "mix.wav"
AUDIBLE_DB = -50.0
WAV_WINDOW = 0.1
SILENT_DB = -200.0


def find_executable(app: Path) -> Path:
    info = Path(app) / "Contents" / "Info.plist"
    if not info.is_file():
        raise FileNotFoundError(f"no player build at {app} (run python3 -m pipeline.build_player)")
    with open(info, "rb") as f:
        exe = Path(app) / "Contents" / "MacOS" / plistlib.load(f)["CFBundleExecutable"]
    if not exe.is_file():
        raise FileNotFoundError(f"executable missing: {exe}")
    return exe


def validate_keys(spec: str) -> str:
    items = [s.strip() for s in spec.split(",") if s.strip()]
    for s in items:
        if not KEY.match(s):
            raise ValueError(f"bad key entry {s!r}; expected <seconds>:<key>")
    if not items:
        raise ValueError("empty key list")
    return ",".join(items)


def validate_types(spec: str) -> str:
    """'UltimateBloom, AmplifyColorEffect' -> 'UltimateBloom,AmplifyColorEffect'."""
    items = [s.strip() for s in re.split(r"[,;]", spec) if s.strip()]
    for s in items:
        if not TYPE_NAME.match(s):
            raise ValueError(f"bad type name {s!r}; expected a C# type name or full name")
    if not items:
        raise ValueError("empty type list")
    return ",".join(dict.fromkeys(items))


def player_argv(exe: Path, run_dir: Path, keys: str | None = None, width: int = 1280, height: int = 720,
                save_dir: Path | None = None, no_image_effects: bool = False, fx_only: str | None = None,
                fx_keep: str | None = None, shot_interval: float | None = None,
                key_shots: bool = False, extra: list[str] | None = None, audio_trace: bool = False,
                audio_record: bool = False, audio_root: Path | None = None, voice_language: str | None = None,
                audio_off: bool = False) -> list[str]:
    argv = [str(exe), "-logFile", str(run_dir / "player.log"), "-portObserve", str(run_dir / "shots")]
    if keys:
        argv += ["-portKeys", keys]
    if shot_interval is not None:
        argv += ["-portShotInterval", f"{shot_interval:g}"]
    if key_shots:
        argv.append("-portKeyShots")
    if save_dir is not None:
        argv += ["-portSaveDir", str(save_dir)]
    if no_image_effects:
        argv.append("-portNoImageEffects")
        if fx_only:
            argv += ["-portNoImageEffectsOnly", fx_only]
        if fx_keep:
            argv += ["-portKeepImageEffects", fx_keep]
    if audio_trace:
        argv += ["-portAudioTrace", str(run_dir / AUDIO_TRACE)]
    if audio_record:
        argv += ["-portAudioRecord", str(run_dir / AUDIO_RECORD)]
    if audio_root is not None:
        argv += ["-portAudioRoot", str(audio_root)]
    if voice_language:
        argv += ["-portVoiceLanguage", voice_language]
    if audio_off:
        argv.append("-portAudioOff")
    argv += ["-screen-width", str(width), "-screen-height", str(height), "-screen-fullscreen", "0"]
    return argv + list(extra or [])


def split_extra(spec: str | None) -> list[str]:
    """Split --extra shell-like; raises ValueError on unbalanced quotes."""
    if not spec:
        return []
    return shlex.split(spec)


def validate_shot_interval(value: float) -> float:
    if not value >= MIN_SHOT_INTERVAL:  # also rejects NaN
        raise ValueError(f"--shot-interval must be at least {MIN_SHOT_INTERVAL} s")
    return value


def caffeinate_argv(pid: int) -> list[str] | None:
    """Keep the display awake (-d), the system out of idle sleep (-i) and wake the display (-u)
    until `pid` exits; None where caffeinate does not exist (not macOS)."""
    exe = shutil.which("caffeinate") if sys.platform == "darwin" else None
    return [exe, "-d", "-i", "-u", "-w", str(pid)] if exe else None


class StallWatch:
    """Warns when no new timer screenshot appeared for `limit` seconds after the first one, or
    when none at all appeared within `first` seconds of the launch."""

    def __init__(self, shots: Path, interval: float = 2.0, limit: float = STALL_SECONDS,
                 first: float = FIRST_SHOT_GRACE):
        self.shots = Path(shots)
        self.limit = max(limit, 3 * interval)
        self.first = max(first, self.limit + interval)
        self.count = 0
        self.last_change: float | None = None
        self.stalled = False
        self.warnings: list[str] = []

    def timer_shots(self) -> int:
        try:
            return sum(1 for n in os.listdir(self.shots) if n.endswith(TIMER_SHOT))
        except OSError:
            return 0

    def check(self, now: float, elapsed: float) -> str | None:
        """Call periodically; returns a warning or recovery line when the state changes."""
        n = self.timer_shots()
        if n != self.count:
            self.count, self.last_change = n, now
            if self.stalled:
                self.stalled = False
                msg = f"t~{elapsed:.0f}s: player resumed ({n} timer shots)"
                self.warnings.append(msg)
                return msg
            return None
        if self.stalled:
            return None
        if self.last_change is None:
            if elapsed < self.first:
                return None
            self.stalled = True
            msg = f"WARNING t~{elapsed:.0f}s: no timer screenshot yet: {STALL_HINT}"
            self.warnings.append(msg)
            return msg
        if now - self.last_change < self.limit:
            return None
        self.stalled = True
        msg = (f"WARNING t~{elapsed:.0f}s: no new timer screenshot for {now - self.last_change:.0f}s "
               f"after {n}: {STALL_HINT}")
        self.warnings.append(msg)
        return msg


def parse_player_log(text: str) -> dict:
    problems, scenes, crashed = Counter(), [], False
    for raw in text.splitlines():
        line = raw.strip()
        if line.startswith(SCENE):
            scenes.append(line[len(SCENE):])
        elif EXCEPTION.match(line) or NATIVE_ERROR.match(line):
            problems[line[:300]] += 1
        if CRASH.search(line):
            crashed = True
    return {"problems": problems, "scenes": scenes, "crashed": crashed}


def parse_events(text: str) -> dict:
    """Read the observer's log.jsonl (one JSON object per line)."""
    problems, scenes, probes = Counter(), [], []
    for raw in text.splitlines():
        try:
            e = json.loads(raw)
        except ValueError:
            continue
        if not isinstance(e, dict):
            continue
        if e.get("kind") == "scene":
            scenes.append(f"{e.get('msg')} t={float(e.get('t', 0)):.2f}")
        elif e.get("kind") == "log" and e.get("type") in ("Error", "Exception", "Assert"):
            problems[f"[{e['type']}] {str(e.get('msg', '')).splitlines()[0][:300] if e.get('msg') else ''}"] += 1
        elif e.get("kind") == "probe" and isinstance(e.get("state"), dict):
            try:
                t = float(e.get("t", 0))
            except (TypeError, ValueError):
                t = 0.0
            probes.append({"t": t, "reason": str(e.get("reason", "")), "msg": str(e.get("msg", "")),
                           "state": e["state"]})
    return {"problems": problems, "scenes": scenes, "probes": probes}


def flatten_state(state: dict, prefix: str = "") -> dict:
    """{"menu": {"page": {"index": 1}}} -> {"menu.page.index": 1}; lists stay values."""
    out = {}
    for k, v in state.items():
        key = f"{prefix}{k}"
        if isinstance(v, dict):
            sub = flatten_state(v, key + ".")
            out.update(sub if sub else {key: {}})
        else:
            out[key] = v
    return out


def _fmt(v) -> str:
    if v is None:
        return "null"
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, str):
        return repr(v)
    return json.dumps(v, ensure_ascii=False)


def _fmt_list(items: list, limit: int) -> str:
    shown = ", ".join(repr(str(x)) for x in items[:limit])
    return f"[{shown}{f', +{len(items) - limit} more' if len(items) > limit else ''}]"


def diff_states(old: dict, new: dict, list_limit: int = 8) -> list[str]:
    """Readable differences between two flattened probe states."""
    parts = []
    for key in list(new) + [k for k in old if k not in new]:
        a, b = old.get(key), new.get(key)
        if a == b:
            continue
        if isinstance(a, list) or isinstance(b, list):
            a_c, b_c = Counter(map(str, a or [])), Counter(map(str, b or []))
            added = [x for x in b or [] if (b_c - a_c)[str(x)] > 0]
            removed = [x for x in a or [] if (a_c - b_c)[str(x)] > 0]
            if not added and not removed:
                parts.append(f"{key}: reordered")
                continue
            seg = []
            if added:
                seg.append("+" + _fmt_list(list(dict.fromkeys(map(str, added))), list_limit))
            if removed:
                seg.append("-" + _fmt_list(list(dict.fromkeys(map(str, removed))), list_limit))
            parts.append(f"{key}: {' '.join(seg)}")
        else:
            parts.append(f"{key}: {_fmt(a)} -> {_fmt(b)}")
    return parts


def describe_state(state: dict, list_limit: int = 8) -> list[str]:
    """Non-null fields of one flattened probe state."""
    parts = []
    for key, v in state.items():
        if v is None or v == {} or v == []:
            continue
        parts.append(f"{key}={_fmt_list([str(x) for x in v], list_limit) if isinstance(v, list) else _fmt(v)}")
    return parts


def probe_changes(probes: list[dict], width: int = 600) -> list[str]:
    """One line for the first probe, then one per probe whose state differs from the previous one."""
    out, prev = [], None
    for p in probes:
        cur = flatten_state(p["state"])
        if prev is None:
            parts = describe_state(cur)
        else:
            parts = diff_states(prev, cur)
        prev = cur
        if not parts:
            continue
        line = f"t={p['t']:.2f} [{p['reason'] or '?'}] " + "; ".join(parts)
        out.append(line if len(line) <= width else line[:width - 3] + "...")
    return out


def dummy_shaders(project: Path) -> dict[str, str]:
    """Shader name -> path (relative to Assets/Resources) of every placeholder shader the export wrote."""
    root = Path(project) / "Assets" / "Resources"
    out: dict[str, str] = {}
    if not root.is_dir():
        return out
    for f in sorted(root.rglob("*")):
        if not f.is_file() or f.suffix == ".meta":
            continue
        try:
            text = f.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        if DUMMY_MARK not in text:
            continue
        m = SHADER_NAME.search(text)
        if m:
            out.setdefault(m.group(1), f.relative_to(root).as_posix())
    return out


def parse_render_events(text: str) -> list[dict]:
    """The render probe's render.jsonl: one {"kind":"render",...} object per line."""
    out = []
    for raw in text.splitlines():
        try:
            e = json.loads(raw)
        except ValueError:
            continue
        if isinstance(e, dict) and e.get("kind") == "render":
            out.append(e)
    return out


def _dicts(v) -> list[dict]:
    return [x for x in v if isinstance(x, dict)] if isinstance(v, list) else []


def _strs(v) -> list[str]:
    return [str(x) for x in v] if isinstance(v, list) else []


def render_summary(events: list[dict], dummies: dict[str, str], scene: str = "Home") -> list[str]:
    """Cameras and effects of the last render event in `scene`, then every shader seen there."""
    seen = [e for e in events if e.get("scene") == scene and not e.get("error")]
    if not seen:
        scenes = sorted({str(e.get("scene")) for e in events})
        return [f"render summary: no render event in {scene} ({len(events)} events, scenes {scenes})"]
    last = seen[-1]
    fx = ""
    if last.get("noImageEffects"):
        only = last.get("noImageEffectsOnly")
        fx = ("; image effects disabled (-portNoImageEffects"
              + (f", only {','.join(_strs(only))}" if isinstance(only, list) else "")
              + (f", kept {','.join(_strs(last.get('keepImageEffects')))}" if _strs(last.get("keepImageEffects")) else "")
              + ")")
    out = [f"render summary for {scene}: {len(seen)} events, last t={float(last.get('t', 0)):.2f} "
           f"[{last.get('reason', '?')}] {last.get('msg', '')}" + fx]
    out.append(f"  cameras ({len(_dicts(last.get('cameras')))}):")
    for c in _dicts(last.get("cameras")):
        rt = c.get("targetTexture")
        out.append(f"    {c.get('name')} depth={c.get('depth')} clear={c.get('clearFlags')} "
                   f"mask={c.get('cullingMask')} rt={rt if rt else 'screen'} rect={c.get('rect')} "
                   f"visible={c.get('visibleRenderers')} shaders={len(_strs(c.get('shaders')))}")
        for fx in _dicts(c.get("effects")):
            state = ("probe-disabled" if fx.get("probeDisabled") else "probe-kept" if fx.get("probeKept")
                     else "on" if fx.get("enabled") else "off")
            sh = _strs(fx.get("shaders"))
            flagged = [f"{n} [DUMMY]" if n in dummies else n for n in sh]
            out.append(f"      effect {fx.get('type')} ({state})" + (f": {', '.join(flagged)}" if flagged else ""))
    ngui = last.get("ngui") if isinstance(last.get("ngui"), dict) else {}
    ngui_shaders = {str(s.get("name")) for e in seen for s in _dicts((e.get("ngui") or {}).get("shaders"))}
    info: dict[str, dict] = {}
    where: dict[str, set] = {}
    for e in seen:
        for s in _dicts(e.get("shaders")):
            info[str(s.get("name"))] = s
        for c in _dicts(e.get("cameras")):
            for n in _strs(c.get("shaders")):
                where.setdefault(n, set()).add(f"cam:{c.get('name')}")
            for fx in _dicts(c.get("effects")):
                for n in _strs(fx.get("shaders")):
                    where.setdefault(n, set()).add(f"fx:{str(fx.get('type')).rsplit('.', 1)[-1]}")
    for n in ngui_shaders:
        where.setdefault(n, set()).add("ngui")
    names = sorted(set(info) | set(where))
    flagged = [n for n in names if n in dummies]
    out.append(f"  ngui: {ngui.get('drawCalls', 0)} draw calls, {len(ngui_shaders)} shaders")
    out.append(f"  shaders ({len(names)}, {len(flagged)} dummy):")
    for n in names:
        s = info.get(n, {})
        tags = []
        if n in dummies:
            tags.append(f"DUMMY {dummies[n]}")
        if s.get("supported") is False:
            tags.append("UNSUPPORTED")
        out.append(f"    {n} passes={s.get('passCount', '?')} queue={s.get('renderQueue', '?')} "
                   f"in={','.join(sorted(where.get(n, ()))) or '-'}" + (f" [{'; '.join(tags)}]" if tags else ""))
    return out


def _num(v) -> float | None:
    if isinstance(v, bool) or not isinstance(v, (int, float)):
        return None
    f = float(v)
    return f if math.isfinite(f) else None


def _label(e: dict, *keys: str) -> str:
    """First present of the keys (name before id), as text; '?' when none."""
    for k in keys:
        v = e.get(k)
        if v is not None and v != "":
            return str(v)
    return "?"


VOICE_OPS = ("start", "end", "stop", "fail", "late", "steal", "clamp")
CALLBACK_TYPES = ("endOfEvent", "bank")
MUSIC_OPS = ("play", "segment", "transition", "late")
NOT_READY = "not ready after"


def _fail_target(e: dict) -> str:
    """What a failed voice tried to play: the file, else a non-zero media id, else node/name/id."""
    f = e.get("file")
    if f is not None and f != "":
        return str(f)
    m = e.get("media")
    if m is not None and m != "" and m != 0:
        return f"media {m}"
    return _label(e, "node", "name", "id")


def parse_audio_events(text: str) -> dict:
    """Read PortAudio's audio.jsonl. Bad lines (not a JSON object, e.g. the truncated last line of a
    killed player) are counted, never fatal; missing fields fall back to '?'.

    Current shapes: post (result ok|unknown|silent), voice (op start|end|stop|fail|late|clamp|steal),
    bank (op load|loadAsync|ready|unload, result), callback (type endOfEvent|bank), music (op).
    The older kinds resolve/unknown/start/end/stop/fail and bank `status` are still accepted."""
    kinds, posted, resolved, unknown, silent = Counter(), Counter(), Counter(), Counter(), Counter()
    failed, banks, problems = Counter(), Counter(), Counter()
    voices, callbacks, music = Counter(), Counter(), Counter()
    not_ready: list[str] = []
    levels: list[dict] = []
    lines = bad = 0
    last_t = None
    for raw in text.splitlines():
        if not raw.strip():
            continue
        try:
            e = json.loads(raw)
        except ValueError:
            bad += 1
            continue
        if not isinstance(e, dict):
            bad += 1
            continue
        lines += 1
        kind = e.get("kind") if isinstance(e.get("kind"), str) and e.get("kind") else "?"
        kinds[kind] += 1
        t = _num(e.get("t"))
        if t is not None:
            last_t = t
        if kind == "post":
            name = _label(e, "name", "id")
            posted[name] += 1
            result = e.get("result")
            if result == "ok":
                resolved[name] += 1
            elif result == "unknown":
                unknown[name] += 1
            elif result == "silent":
                silent[name] += 1
        elif kind == "resolve":
            resolved[_label(e, "name", "id")] += 1
        elif kind == "unknown":
            unknown[_label(e, "name", "id")] += 1
        elif kind in ("voice", "start", "end", "stop", "fail"):
            op = _label(e, "op") if kind == "voice" else kind
            voices[op] += 1
            if op == "fail":
                failed[f"{_label(e, 'reason', 'error')} {_fail_target(e)}"] += 1
        elif kind == "bank":
            key = [_label(e, "name", "id")]
            if e.get("op") is not None and e.get("op") != "":
                key.append(str(e["op"]))
            key.append(_label(e, "result", "status"))
            banks[" ".join(key)] += 1
        elif kind == "callback":
            callbacks[_label(e, "type")] += 1
        elif kind == "music":
            music[_label(e, "op")] += 1
        elif kind in ("warn", "error"):
            msg = str(e.get("msg", "?")).splitlines()[0][:300] if e.get("msg") else "?"
            problems[f"[{kind}] {msg}"] += 1
            if NOT_READY in msg:
                not_ready.append(msg)
        elif kind == "level":
            rms, peak = _num(e.get("rmsDb")), _num(e.get("peakDb"))
            if rms is not None:
                levels.append({"sec": _num(e.get("sec")), "rmsDb": rms,
                               "peakDb": peak if peak is not None else rms})
    return {"lines": lines, "bad": bad, "kinds": kinds, "posted": posted, "resolved": resolved,
            "unknown": unknown, "silent": silent, "voices": voices, "failed": failed, "banks": banks,
            "callbacks": callbacks, "music": music, "problems": problems, "not_ready": not_ready,
            "levels": levels, "last_t": last_t}


def _ops(counter: Counter, fixed: tuple[str, ...], optional: tuple[str, ...] = ()) -> str:
    """'a 1, b 0, ...': the fixed names always, the optional ones and any others only when seen."""
    names = list(fixed) + [n for n in optional if counter.get(n)]
    names += sorted(n for n in counter if n not in names)
    return ", ".join(f"{n} {counter.get(n, 0)}" for n in names)


def _db(x: float) -> float:
    return 20.0 * math.log10(x) if x > 0 else SILENT_DB


def _sum_squares(a: array) -> float:
    sp = getattr(math, "sumprod", None)
    if sp is not None:
        return float(sp(a, a))
    return float(sum(v * v for v in a))


def analyze_wav(path: Path, window: float = WAV_WINDOW, audible_db: float = AUDIBLE_DB) -> dict | None:
    """Duration, peak dBFS, audible seconds (windows with RMS above `audible_db`) and the longest
    silence of a PCM WAV; None when the file does not exist, {"error": ...} when it cannot be read."""
    path = Path(path)
    if not path.is_file():
        return None
    try:
        with wave.open(str(path), "rb") as w:
            channels, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
            if width not in (1, 2, 4) or rate <= 0 or channels <= 0:
                return {"error": f"unsupported WAV: {width * 8}-bit, {rate} Hz, {channels} ch"}
            full = float(1 << (8 * width - 1))
            per = max(1, int(round(rate * window)))
            peak = 0.0
            audible = longest = run = 0.0
            run_start = silence_at = 0.0
            total = 0
            while True:
                data = w.readframes(per)
                if not data:
                    break
                a = array({1: "B", 2: "h", 4: "i"}[width])
                usable = len(data) - len(data) % a.itemsize
                a.frombytes(data[:usable])
                if width == 1:
                    a = array("i", (v - 128 for v in a))
                elif sys.byteorder == "big":
                    a.byteswap()
                if not a:
                    break
                n_frames = len(a) // channels
                dur = n_frames / rate
                start = total / rate
                total += n_frames
                hi = max(abs(max(a)), abs(min(a))) / full
                peak = max(peak, hi)
                rms = math.sqrt(_sum_squares(a) / len(a)) / full
                if _db(rms) > audible_db:
                    audible += dur
                    run = 0.0
                else:
                    if run == 0.0:
                        run_start = start
                    run += dur
                    if run > longest + 1e-9:
                        longest, silence_at = run, run_start
    except (wave.Error, EOFError, OSError, ValueError, struct.error) as ex:
        return {"error": f"{type(ex).__name__}: {ex}"}
    return {"path": str(path), "rate": rate, "channels": channels, "bits": width * 8,
            "duration": total / rate, "peak_db": _db(peak), "audible": round(audible, 6),
            "longest_silence": round(longest, 6), "silence_at": round(silence_at, 6),
            "frames_in_header": frames}


def _top(counter: Counter, limit: int, indent: str = "    ") -> list[str]:
    return [f"{indent}(x{n}) {m}" for m, n in counter.most_common(limit)]


def audio_summary(events: dict | None, wav: dict | None, *, trace_path: Path | None = None,
                  trace_requested: bool = False, record_path: Path | None = None,
                  record_requested: bool = False, top: int = 10) -> list[str]:
    """The "audio:" section of the run summary (lines without a trailing newline)."""
    out = ["audio:"]
    if events is None:
        if trace_requested:
            out.append(f"  WARNING: audio trace requested but missing: {trace_path} "
                       "(PortAudio off or not initialized?)")
    else:
        k = events["kinds"]
        out.append(f"  trace: {trace_path or AUDIO_TRACE}: {events['lines']} events"
                   + (f", {events['bad']} bad lines" if events["bad"] else "")
                   + (f", last t={events['last_t']:.2f}" if events.get("last_t") is not None else ""))
        out.append("  kinds: " + (", ".join(f"{n}={c}" for n, c in sorted(k.items())) or "none"))
        p, r, u = events["posted"], events["resolved"], events["unknown"]
        sil = events.get("silent", Counter())
        out.append(f"  events: posted {sum(p.values())} ({len(p)} distinct), resolved {sum(r.values())}, "
                   f"unknown {sum(u.values())} ({len(u)} distinct), silent {sum(sil.values())}")
        if u:
            out.append(f"  WARNING: {sum(u.values())} posted events unknown (in no loaded bank)")
            out.append("  top unknown events:")
            out += _top(u, top)
        out.append("  voices: " + _ops(events.get("voices", Counter()), VOICE_OPS[:5], VOICE_OPS[5:]))
        if events["failed"]:
            out.append("  top voice failures:")
            out += _top(events["failed"], top)
        cb, mu = events.get("callbacks", Counter()), events.get("music", Counter())
        if cb:
            out.append("  callbacks: " + _ops(cb, CALLBACK_TYPES))
        if mu:
            out.append("  music: " + _ops(mu, MUSIC_OPS))
        if events["banks"]:
            out.append(f"  banks ({sum(events['banks'].values())}):")
            out += _top(events["banks"], top)
        nr = events.get("not_ready", [])
        if nr:
            out.append(f"  WARNING: {len(nr)} banks reported loaded with media not ready:")
            out += _top(Counter(nr), top)
        if events["problems"]:
            out.append(f"  warnings/errors: {len(events['problems'])} distinct, "
                       f"{sum(events['problems'].values())} total")
            out += _top(events["problems"], top)
        lv = events["levels"]
        if lv:
            loud = sum(1 for x in lv if x["rmsDb"] > AUDIBLE_DB)
            out.append(f"  levels: audible {loud} of {len(lv)} s, peak {max(x['peakDb'] for x in lv):.1f} dBFS "
                       f"(recorder level events, RMS above {AUDIBLE_DB:g} dBFS)")
    if wav is None:
        if record_requested:
            out.append(f"  WARNING: recording requested but missing: {record_path} (no audio callback: "
                       "Unity audio disabled in the build, no enabled AudioListener, or PortAudio off?)")
    elif "error" in wav:
        out.append(f"  WARNING: recording unreadable: {record_path}: {wav['error']}")
    else:
        out.append(f"  mix: {record_path or wav.get('path')}: {wav['duration']:.1f} s, {wav['rate']} Hz, "
                   f"{wav['channels']} ch, peak {wav['peak_db']:.1f} dBFS, audible {wav['audible']:.1f} s, "
                   f"longest silence {wav['longest_silence']:.1f} s at {wav['silence_at']:.1f} s")
        if wav["duration"] <= 0:
            out.append("  WARNING: recording is empty")
        elif wav["audible"] <= 0:
            out.append(f"  WARNING: recording is silent (no {WAV_WINDOW:g} s window above {AUDIBLE_DB:g} dBFS RMS)")
    return out


def screenshots(shots: Path) -> list[Path]:
    return sorted(p for p in Path(shots).glob("*.png")) if Path(shots).is_dir() else []


def run(exe: Path, run_dir: Path, seconds: float, keys: str | None, fresh_save: bool = False,
        no_image_effects: bool = False, fx_only: str | None = None, fx_keep: str | None = None,
        shot_interval: float | None = None, key_shots: bool = False, caffeinate: bool = True,
        warnings: list[str] | None = None, extra: list[str] | None = None,
        audio: dict | None = None) -> int | None:
    (run_dir / "shots").mkdir(parents=True, exist_ok=True)
    save_dir = None
    if fresh_save:
        save_dir = run_dir / "save"
        save_dir.mkdir(parents=True, exist_ok=True)
    argv = player_argv(exe, run_dir, keys, save_dir=save_dir, no_image_effects=no_image_effects,
                       fx_only=fx_only, fx_keep=fx_keep, shot_interval=shot_interval, key_shots=key_shots,
                       extra=extra, **(audio or {}))
    awake = None
    caff = caffeinate_argv(os.getpid()) if caffeinate else None
    if caff:
        # started before the player so the display is on when it creates its Metal layer;
        # -w ties it to this script in case the finally below never runs
        awake = subprocess.Popen(caff, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        print("keeping the display awake:", " ".join(caff), flush=True)
        time.sleep(1.0)
    elif caffeinate:
        print("caffeinate not available; a sleeping display can stall the player", flush=True)
    try:
        print("running:", " ".join(f"'{a}'" if " " in a or "'" in a else a for a in argv), flush=True)
        proc = subprocess.Popen(argv, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        watch = StallWatch(run_dir / "shots", interval=shot_interval or 2.0)
        start = time.monotonic()
        deadline = start + seconds
        while time.monotonic() < deadline and proc.poll() is None:
            time.sleep(0.5)
            now = time.monotonic()
            msg = watch.check(now, now - start)
            if msg:
                print(msg, flush=True)
        if warnings is not None:
            warnings += watch.warnings
            if watch.stalled:
                warnings.append(f"still stalled at the end: {STALL_HINT}")
        rc = proc.poll()
        if rc is None:
            proc.terminate()
            try:
                proc.wait(5)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
            return None
        return rc
    finally:
        if awake is not None and awake.poll() is None:
            awake.terminate()
            try:
                awake.wait(5)
            except subprocess.TimeoutExpired:
                awake.kill()


def report(run_dir: Path, rc: int | None, top: int = 25, dummies: dict[str, str] | None = None,
           warnings: list[str] | None = None, audio_trace: bool = False, audio_record: bool = False) -> str:
    log = run_dir / "player.log"
    p = parse_player_log(log.read_text(errors="replace") if log.is_file() else "")
    events = run_dir / "shots" / "log.jsonl"
    e = parse_events(events.read_text(errors="replace")) if events.is_file() else None
    problems = e["problems"] if e and e["problems"] else p["problems"]
    scenes = e["scenes"] if e and e["scenes"] else p["scenes"]
    shots = screenshots(run_dir / "shots")
    out = ["player " + ("still running at timeout, killed" if rc is None else f"exited with code {rc}")
           + ("; CRASH markers in log" if p["crashed"] else "")]
    out += [f"  {w}" for w in warnings or []]
    out.append(f"errors/exceptions: {len(problems)} distinct, {sum(problems.values())} total"
               + (" (from observer log.jsonl)" if problems is not p["problems"] else " (from player.log)"))
    out += [f"  (x{n}) {m}" for m, n in problems.most_common(top)]
    out.append(f"scenes loaded ({len(scenes)}):")
    out += [f"  {s}" for s in scenes]
    probes = e["probes"] if e else []
    changes = probe_changes(probes)
    out.append(f"probe changes ({len(changes)} of {len(probes)} probes):")
    out += [f"  {c}" for c in changes]
    if probes:
        out.append(f"last probe: t={probes[-1]['t']:.2f} {probes[-1]['msg']}")
    out.append(f"screenshots ({len(shots)}) in {run_dir / 'shots'}:")
    out += [f"  {s.name}" for s in shots]
    if shots:
        out.append(f"last screenshot: {shots[-1]}")
    save = run_dir / "save"
    if save.is_dir():
        files = sorted(p.relative_to(save).as_posix() for p in save.rglob("*") if p.is_file())
        out.append(f"fresh save dir {save}: {', '.join(files) if files else 'empty'}")
    if dummies is not None:
        render = run_dir / "shots" / "render.jsonl"
        events = parse_render_events(render.read_text(errors="replace")) if render.is_file() else []
        out += render_summary(events, dummies)
    trace, mix = run_dir / AUDIO_TRACE, run_dir / AUDIO_RECORD
    if audio_trace or audio_record or trace.is_file() or mix.is_file():
        a = parse_audio_events(trace.read_text(errors="replace")) if trace.is_file() else None
        out += audio_summary(a, analyze_wav(mix), trace_path=trace, trace_requested=audio_trace,
                             record_path=mix, record_requested=audio_record)
    out.append(f"log: {log}")
    return "\n".join(out)


def join_extra_values(argv: list[str]) -> list[str]:
    """Rewrite "--extra X" as "--extra=X": argparse would take a lone "-portFoo" value for an option."""
    out: list[str] = []
    i = 0
    while i < len(argv):
        if argv[i] == "--extra" and i + 1 < len(argv):
            out.append("--extra=" + argv[i + 1])
            i += 2
        else:
            out.append(argv[i])
            i += 1
    return out


def build_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--name", default=time.strftime("run-%Y%m%d-%H%M%S"), help="run name (work/runs/<name>)")
    ap.add_argument("--seconds", type=float, default=60.0)
    ap.add_argument("--keys", help='scripted input, "<t>:<key>,<t>:<key>" (see unity/Port/Runtime/PortDebug)')
    ap.add_argument("--app", type=Path, help="player .app (default work/builds/mac/ThatsYou.app)")
    ap.add_argument("--fresh-save", action="store_true",
                    help="start from an empty save in work/runs/<name>/save (-portSaveDir)")
    ap.add_argument("--no-image-effects", action="store_true",
                    help="diagnostic: disable camera OnRenderImage effects except the game-logic ones "
                         "(MenuRenderer, ScreenCopier, ...) (-portNoImageEffects)")
    ap.add_argument("--fx-only", metavar="TYPE,TYPE",
                    help="with --no-image-effects: disable only these types (-portNoImageEffectsOnly); "
                         "naming a default-kept type disables it too")
    ap.add_argument("--fx-keep", metavar="TYPE,TYPE",
                    help="with --no-image-effects: also keep these types on (-portKeepImageEffects)")
    ap.add_argument("--render-summary", action="store_true",
                    help="print cameras, effects and shaders seen in Home, flagging dummy shaders")
    ap.add_argument("--shot-interval", type=float, metavar="SEC",
                    help="timer screenshot interval in seconds (-portShotInterval; default 2)")
    ap.add_argument("--key-shots", action="store_true",
                    help="also save a screenshot 0.5 s after each scripted key is released (-portKeyShots)")
    ap.add_argument("--no-caffeinate", action="store_true",
                    help="do not run the player under caffeinate -d -i -u")
    ap.add_argument("--extra", metavar="ARGS",
                    help="extra player arguments, split shell-like and appended to the player argv "
                         "(e.g. --extra '-portCameraHdr -portNoLightShadows Spot')")
    ap.add_argument("--audio-trace", action="store_true",
                    help="write the PortAudio trace to work/runs/<name>/audio.jsonl (-portAudioTrace)")
    ap.add_argument("--audio-record", action="store_true",
                    help="record the final mix to work/runs/<name>/mix.wav (-portAudioRecord)")
    ap.add_argument("--audio-root", type=Path, metavar="DIR",
                    help="read converted audio from DIR instead of StreamingAssets/PortAudio (-portAudioRoot)")
    ap.add_argument("--voice-language", metavar="NAME",
                    help="voice language, e.g. 'English(US)' or english_us (-portVoiceLanguage)")
    ap.add_argument("--audio-off", action="store_true", help="switch PortAudio off (-portAudioOff)")
    return ap


def main(argv: list[str] | None = None) -> int:
    from pipeline import config

    a = build_parser().parse_args(join_extra_values(sys.argv[1:] if argv is None else list(argv)))
    c = config.load()
    try:
        keys = validate_keys(a.keys) if a.keys else None
        if (a.fx_only or a.fx_keep) and not a.no_image_effects:
            raise ValueError("--fx-only / --fx-keep need --no-image-effects")
        fx_only = validate_types(a.fx_only) if a.fx_only else None
        fx_keep = validate_types(a.fx_keep) if a.fx_keep else None
        shot_interval = validate_shot_interval(a.shot_interval) if a.shot_interval is not None else None
        extra = split_extra(a.extra)
        exe = find_executable(a.app or c.work_dir / "builds" / "mac" / "ThatsYou.app")
    except (ValueError, FileNotFoundError) as e:
        print(e, file=sys.stderr)
        return 2
    run_dir = (c.work_dir / "runs" / a.name).resolve()
    if run_dir.exists() and any(run_dir.iterdir()):
        print(f"run dir not empty: {run_dir}; pick another --name", file=sys.stderr)
        return 2
    dummies = dummy_shaders(c.work_dir / "project") if a.render_summary else None
    warnings: list[str] = []
    audio = {"audio_trace": a.audio_trace, "audio_record": a.audio_record,
             "audio_root": a.audio_root.resolve() if a.audio_root else None,
             "voice_language": a.voice_language, "audio_off": a.audio_off}
    rc = run(exe, run_dir, a.seconds, keys, fresh_save=a.fresh_save, no_image_effects=a.no_image_effects,
             fx_only=fx_only, fx_keep=fx_keep, shot_interval=shot_interval, key_shots=a.key_shots,
             caffeinate=not a.no_caffeinate, warnings=warnings, extra=extra, audio=audio)
    print(report(run_dir, rc, dummies=dummies, warnings=warnings, audio_trace=a.audio_trace,
                 audio_record=a.audio_record))
    return 0 if rc in (None, 0) else 1


if __name__ == "__main__":
    raise SystemExit(main())
