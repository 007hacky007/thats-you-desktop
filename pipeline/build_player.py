"""Build the macOS player (work/builds/mac/ThatsYou.app) with Unity in batch mode.

Syncs unity/Port and the converted audio (work/audio/PortAudio) into the project first, refuses to start while another Unity has the project
open, runs PortBuild.BuildMac and prints a short summary of the build log.

CLI: python3 -m pipeline.build_player [--project DIR] [--log FILE]
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from collections import Counter
from pathlib import Path

COMPILER = re.compile(r"^.+?\.cs\(\d+,\d+\): error CS\d+: .*$")
SHADER = re.compile(r"^Shader error in '.*$|^Shader compiler: .*error.*$", re.I)
MISSING_SCRIPT = re.compile(r"^The referenced script .*is missing!$")
BUILD = re.compile(r"^(Error building Player.*|BuildFailedException.*|BuildPlayerWindow\+BuildMethodException.*"
                   r"|Build failed.*|\[PortBuild\] (ERROR|FAILED).*|Scripts have compiler errors\..*"
                   r"|Aborting batchmode due to failure.*|executeMethod class '.*' could not be found.*"
                   r"|executeMethod method .* could not be found.*)$")
EXCEPTION = re.compile(r"^[A-Za-z_][\w.]*Exception: .*$")
RESULT = re.compile(r"Build Finished, Result: (\w+)")


def build_argv(editor: Path, project: Path, log: Path) -> list[str]:
    return [str(editor), "-batchmode", "-quit", "-projectPath", str(project),
            "-executeMethod", "PortBuild.BuildMac", "-buildTarget", "OSXUniversal", "-logFile", str(log)]


def lock_holders(project: Path, run=subprocess.run) -> list[int]:
    """PIDs of processes that have the Unity project open: whoever holds Temp/UnityLockfile, plus any
    Unity editor process started with -projectPath <project>. Empty when the project is free."""
    project = Path(project)
    pids: set[int] = set()
    lockfile = project / "Temp" / "UnityLockfile"
    if lockfile.exists():
        r = run(["lsof", "-t", str(lockfile)], capture_output=True, text=True)
        pids |= {int(x) for x in r.stdout.split() if x.isdigit()}
    r = run(["ps", "-axww", "-o", "pid=,command="], capture_output=True, text=True)
    want = {str(project), str(project.resolve())}
    for line in r.stdout.splitlines():
        parts = line.strip().split(None, 1)
        if len(parts) != 2 or not parts[0].isdigit():
            continue
        cmd = parts[1]
        exe = cmd.split(" -", 1)[0]
        if not exe.endswith("/Unity"):
            continue
        m = re.search(r"-projectpath\s+(.+?)(?:\s+-\w|$)", cmd, re.I)
        if m and m.group(1).rstrip("/") in want:
            pids.add(int(parts[0]))
    return sorted(pids)


def summarize_log(text: str) -> dict:
    buckets = {k: Counter() for k in ("compiler", "shader", "missing_script", "build", "exception")}
    port, result = [], None
    for raw in text.splitlines():
        line = raw.strip()
        if not line:
            continue
        if COMPILER.match(line):
            buckets["compiler"][line] += 1
        elif SHADER.match(line):
            buckets["shader"][line] += 1
        elif MISSING_SCRIPT.match(line):
            buckets["missing_script"][line] += 1
        elif BUILD.match(line):
            buckets["build"][line] += 1
        elif EXCEPTION.match(line):
            buckets["exception"][line] += 1
        if line.startswith("[PortBuild]"):
            port.append(line)
        m = RESULT.search(line)
        if m:
            result = m.group(1)
    out = {k: c.most_common() for k, c in buckets.items()}
    out["port"] = port
    out["result"] = result
    return out


def format_summary(s: dict, rc: int, top: int = 15) -> str:
    lines = [f"Unity exit code {rc}; build result: {s['result'] or 'unknown'}"]
    for key, title in (("compiler", "compiler errors"), ("shader", "shader errors"),
                       ("missing_script", "missing script references"), ("build", "build errors"),
                       ("exception", "editor exceptions during the build")):
        items = s[key]
        lines.append(f"{title}: {len(items)} distinct, {sum(n for _, n in items)} total")
        for msg, n in items[:top]:
            lines.append(f"  (x{n}) {msg[:300]}")
    for p in s["port"][-20:]:
        lines.append(p[:300])
    return "\n".join(lines)


NO_AUDIO = "audio: no converted audio (run python3 -m pipeline.audio); the player will be silent"


def install_audio(work_dir: Path, project: Path, install=None, log=print) -> dict | None:
    """Install the staged PortAudio data into the project (cheap and idempotent), or warn when
    there is none. Returns the install counts, or None when nothing was staged."""
    staged = Path(work_dir) / "audio" / "PortAudio"
    if not (staged / "manifest.json").is_file():
        log(NO_AUDIO)
        return None
    if install is None:
        from pipeline import audio_install
        install = audio_install.install
    r = install(staged, project)
    log(f"audio: {r.get('copied', 0)} copied, {r.get('unchanged', 0)} unchanged, {r.get('deleted', 0)} stale "
        f"deleted, {r.get('pruned', 0)} PS4 audio files pruned")
    return r


def install_native(work_dir: Path, project: Path, log=print) -> None:
    """Build iwyknet (the phones' WebRTC endpoint) and install it into the project."""
    from pipeline import build_native

    out = work_dir / "native"
    for plat in build_native.PLATFORMS:
        r = build_native.build(plat, out)
        log(f"native {plat}: {r['status']}")
    build_native.install(out, project)


def write_net_config(c, out_dir: Path, log=print) -> None:
    """iwyk-net.json next to the app, with the page address from config/local.json."""
    from pipeline import net_config

    if not c.phone_page_url:
        log("no phone_page_url in config/local.json: the game will show how to set it")
        return
    net_config.write(out_dir, c.phone_page_url)
    log(f"wrote {out_dir / net_config.FILE_NAME}")


def main(argv: list[str] | None = None) -> int:
    from pipeline import build_all, config

    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--project", type=Path, help="Unity project (default work/project)")
    ap.add_argument("--log", type=Path, help="log file (default work/reports/build-mac.log)")
    a = ap.parse_args(argv)
    c = config.load()
    build_all.check_unity_editor(c.unity_editor)
    project = (a.project or c.work_dir / "project").resolve()
    log = (a.log or c.work_dir / "reports" / "build-mac.log").resolve()
    holders = lock_holders(project)
    if holders:
        print(f"refusing to build: {project} is open in Unity (pid {', '.join(map(str, holders))})", file=sys.stderr)
        return 2
    build_all.sync_port(build_all.PORT_DIR, project)
    install_audio(c.work_dir, project)
    install_native(c.work_dir, project)
    log.parent.mkdir(parents=True, exist_ok=True)
    if log.exists():
        log.unlink()
    argv_ = build_argv(c.unity_editor, project, log)
    print("running:", " ".join(argv_), flush=True)
    rc = subprocess.run(argv_).returncode
    s = summarize_log(log.read_text(errors="replace") if log.is_file() else "")
    print(format_summary(s, rc))
    print(f"log: {log}")
    if rc == 0 and s["result"] not in (None, "Success", "Succeeded"):
        rc = 1
    if rc == 0:
        write_net_config(c, c.work_dir / "builds" / "mac")
    return rc


if __name__ == "__main__":
    raise SystemExit(main())
