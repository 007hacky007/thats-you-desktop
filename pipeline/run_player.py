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

CLI: python3 -m pipeline.run_player [--name NAME] [--seconds 60] [--keys "3:cross,6:down"]
     [--fresh-save] [--no-image-effects [--fx-only T,T] [--fx-keep T,T]] [--render-summary]
"""
from __future__ import annotations

import argparse
import json
import plistlib
import re
import subprocess
import sys
import time
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
                fx_keep: str | None = None) -> list[str]:
    argv = [str(exe), "-logFile", str(run_dir / "player.log"), "-portObserve", str(run_dir / "shots")]
    if keys:
        argv += ["-portKeys", keys]
    if save_dir is not None:
        argv += ["-portSaveDir", str(save_dir)]
    if no_image_effects:
        argv.append("-portNoImageEffects")
        if fx_only:
            argv += ["-portNoImageEffectsOnly", fx_only]
        if fx_keep:
            argv += ["-portKeepImageEffects", fx_keep]
    return argv + ["-screen-width", str(width), "-screen-height", str(height), "-screen-fullscreen", "0"]


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


def screenshots(shots: Path) -> list[Path]:
    return sorted(p for p in Path(shots).glob("*.png")) if Path(shots).is_dir() else []


def run(exe: Path, run_dir: Path, seconds: float, keys: str | None, fresh_save: bool = False,
        no_image_effects: bool = False, fx_only: str | None = None, fx_keep: str | None = None) -> int | None:
    (run_dir / "shots").mkdir(parents=True, exist_ok=True)
    save_dir = None
    if fresh_save:
        save_dir = run_dir / "save"
        save_dir.mkdir(parents=True, exist_ok=True)
    argv = player_argv(exe, run_dir, keys, save_dir=save_dir, no_image_effects=no_image_effects,
                       fx_only=fx_only, fx_keep=fx_keep)
    print("running:", " ".join(f"'{a}'" if " " in a or "'" in a else a for a in argv), flush=True)
    proc = subprocess.Popen(argv, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline and proc.poll() is None:
        time.sleep(0.5)
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


def report(run_dir: Path, rc: int | None, top: int = 25, dummies: dict[str, str] | None = None) -> str:
    log = run_dir / "player.log"
    p = parse_player_log(log.read_text(errors="replace") if log.is_file() else "")
    events = run_dir / "shots" / "log.jsonl"
    e = parse_events(events.read_text(errors="replace")) if events.is_file() else None
    problems = e["problems"] if e and e["problems"] else p["problems"]
    scenes = e["scenes"] if e and e["scenes"] else p["scenes"]
    shots = screenshots(run_dir / "shots")
    out = ["player " + ("still running at timeout, killed" if rc is None else f"exited with code {rc}")
           + ("; CRASH markers in log" if p["crashed"] else "")]
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
    out.append(f"log: {log}")
    return "\n".join(out)


def main(argv: list[str] | None = None) -> int:
    from pipeline import config

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
    a = ap.parse_args(argv)
    c = config.load()
    try:
        keys = validate_keys(a.keys) if a.keys else None
        if (a.fx_only or a.fx_keep) and not a.no_image_effects:
            raise ValueError("--fx-only / --fx-keep need --no-image-effects")
        fx_only = validate_types(a.fx_only) if a.fx_only else None
        fx_keep = validate_types(a.fx_keep) if a.fx_keep else None
        exe = find_executable(a.app or c.work_dir / "builds" / "mac" / "ThatsYou.app")
    except (ValueError, FileNotFoundError) as e:
        print(e, file=sys.stderr)
        return 2
    run_dir = (c.work_dir / "runs" / a.name).resolve()
    if run_dir.exists() and any(run_dir.iterdir()):
        print(f"run dir not empty: {run_dir}; pick another --name", file=sys.stderr)
        return 2
    dummies = dummy_shaders(c.work_dir / "project") if a.render_summary else None
    rc = run(exe, run_dir, a.seconds, keys, fresh_save=a.fresh_save, no_image_effects=a.no_image_effects,
             fx_only=fx_only, fx_keep=fx_keep)
    print(report(run_dir, rc, dummies=dummies))
    return 0 if rc in (None, 0) else 1


if __name__ == "__main__":
    raise SystemExit(main())
