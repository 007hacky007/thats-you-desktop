"""Run the macOS player build for a while and summarize what happened.

Starts work/builds/mac/ThatsYou.app windowed at 1280x720 with the PortDebug observer enabled
(-portObserve <run>/shots, optional -portKeys), waits until it exits or the timeout passes,
kills it, then prints errors and exceptions (deduplicated), scenes loaded and screenshots.

CLI: python3 -m pipeline.run_player [--name NAME] [--seconds 60] [--keys "3:cross,6:down"]
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


def player_argv(exe: Path, run_dir: Path, keys: str | None = None, width: int = 1280, height: int = 720) -> list[str]:
    argv = [str(exe), "-logFile", str(run_dir / "player.log"), "-portObserve", str(run_dir / "shots")]
    if keys:
        argv += ["-portKeys", keys]
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
    problems, scenes = Counter(), []
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
    return {"problems": problems, "scenes": scenes}


def screenshots(shots: Path) -> list[Path]:
    return sorted(p for p in Path(shots).glob("*.png")) if Path(shots).is_dir() else []


def run(exe: Path, run_dir: Path, seconds: float, keys: str | None) -> int | None:
    (run_dir / "shots").mkdir(parents=True, exist_ok=True)
    argv = player_argv(exe, run_dir, keys)
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


def report(run_dir: Path, rc: int | None, top: int = 25) -> str:
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
    out.append(f"screenshots ({len(shots)}) in {run_dir / 'shots'}:")
    out += [f"  {s.name}" for s in shots]
    if shots:
        out.append(f"last screenshot: {shots[-1]}")
    out.append(f"log: {log}")
    return "\n".join(out)


def main(argv: list[str] | None = None) -> int:
    from pipeline import config

    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--name", default=time.strftime("run-%Y%m%d-%H%M%S"), help="run name (work/runs/<name>)")
    ap.add_argument("--seconds", type=float, default=60.0)
    ap.add_argument("--keys", help='scripted input, "<t>:<key>,<t>:<key>" (see unity/Port/Runtime/PortDebug)')
    ap.add_argument("--app", type=Path, help="player .app (default work/builds/mac/ThatsYou.app)")
    a = ap.parse_args(argv)
    c = config.load()
    try:
        keys = validate_keys(a.keys) if a.keys else None
        exe = find_executable(a.app or c.work_dir / "builds" / "mac" / "ThatsYou.app")
    except (ValueError, FileNotFoundError) as e:
        print(e, file=sys.stderr)
        return 2
    run_dir = (c.work_dir / "runs" / a.name).resolve()
    if run_dir.exists() and any(run_dir.iterdir()):
        print(f"run dir not empty: {run_dir}; pick another --name", file=sys.stderr)
        return 2
    rc = run(exe, run_dir, a.seconds, keys)
    print(report(run_dir, rc))
    return 0 if rc in (None, 0) else 1


if __name__ == "__main__":
    raise SystemExit(main())
