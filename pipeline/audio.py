"""Convert the PS4 Wwise audio into the PortAudio data the managed player reads.

Steps (each injectable, see build(steps=...)):
  1. tools     audio_tools.ensure_vgmstream / ensure_codebooks (pinned, built or downloaded once)
  2. graph     audio_graph.build: manifest.json + banks/** and reports/graph-report.json
  3. referenced audio_graph.referenced_media: media ids per scope, so stale streamed wems are skipped
  4. convert   audio_convert.convert: media/*.ogg, lang/<key>/**, media tables, reports/convert-report.json
  5. coverage  reports/coverage.json + coverage.md: every event joined with the media tables
  6. install   audio_install.install(work/audio/PortAudio, project)

Graph and convert are digest gated in work/audio/.audio-state.json (merged dump digest, step
VERSION, pinned tool entries, verify mode, AK.cs). --force redoes everything.

CLI: python3 -m pipeline.audio [--verify none|sample|all] [--jobs N] [--no-install] [--force] [--graph-only]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import time
from pathlib import Path
from types import SimpleNamespace

from pipeline import tools
from pipeline.merge import MANIFEST

SOURCE_REL = Path("merged") / "Media" / "StreamingAssets" / "Audio" / "GeneratedSoundBanks" / "PS4"
NAMES_REL = Path("Assets") / "Scripts" / "Assembly-CSharp" / "AK.cs"
STATE = ".audio-state.json"
TOOL_PINS = ("vgmstream", "vgmstream-src", "libogg", "libvorbis")
VERIFY_MODES = ("none", "sample", "all")


def default_steps() -> SimpleNamespace:
    from pipeline import audio_convert, audio_graph, audio_install, audio_tools

    return SimpleNamespace(ensure_vgmstream=audio_tools.ensure_vgmstream,
                           ensure_codebooks=audio_tools.ensure_codebooks,
                           graph_build=audio_graph.build, referenced_media=audio_graph.referenced_media,
                           convert=audio_convert.convert, install=audio_install.install,
                           graph_version=audio_graph.VERSION, convert_version=audio_convert.VERSION)


def _digest(data) -> str:
    return hashlib.sha256(json.dumps(data, sort_keys=True, default=str).encode()).hexdigest()


def _file_digest(p: Path | None) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest() if p and p.is_file() else ""


def merged_digest(work_dir: Path, source_root: Path) -> str:
    """The merge manifest digest; falls back to a listing (name, size, mtime) of the audio source."""
    m = Path(work_dir) / "merged" / MANIFEST
    try:
        return "merge:" + json.loads(m.read_text())["digest"]
    except (OSError, ValueError, KeyError):
        h = hashlib.sha256()
        for p in sorted(source_root.rglob("*")):
            if p.is_file():
                st = p.stat()
                h.update(f"{p.relative_to(source_root).as_posix()}\0{st.st_size}\0{st.st_mtime_ns}\n".encode())
        return "listing:" + h.hexdigest()


def _load_state(path: Path) -> dict:
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return {}


def _save_state(path: Path, state: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(json.dumps(state, indent=1, sort_keys=True))
    tmp.replace(path)


def _load_media_tables(out_root: Path) -> dict[str, dict[int, dict]]:
    tables = {}
    paths = [out_root / "media.json"] + sorted((out_root / "lang").glob("*/media.json"))
    for p in paths:
        if not p.is_file():
            continue
        t = json.loads(p.read_text(encoding="utf-8"))
        scope = t.get("scope") or ("li" if p.parent == out_root else p.parent.name)
        tables[scope] = {int(m["id"]): m for m in t.get("media", [])}
    return tables


STATUSES = ("ok", "control", "deadInOriginal", "problem")


def _status(missing: list, reaches_nothing: bool, control: bool, targets: list, dead: list) -> str:
    """ok: resolves to converted audio; control: stops/pauses/resumes/sets parameters only;
    deadInOriginal: plays only targets that no bank of the original data defines; problem: the rest."""
    if missing:
        return "problem"
    if control:
        return "control"
    if not reaches_nothing:
        return "ok"
    if targets and set(targets) <= set(dead):
        return "deadInOriginal"
    return "problem"


def coverage(report: dict, out_root: Path) -> dict:
    """Join graph-report events with the media tables: does every event resolve to converted audio?"""
    out_root = Path(out_root)
    tables = _load_media_tables(out_root)
    exists: dict[str, bool] = {}
    events, missing_all = [], set()
    for ev in report.get("events", []):
        media = ext = audio_input = 0
        missing = []
        for m in ev.get("media", []):
            scope, mid = m.get("scope"), m.get("id")
            if scope == "external":  # resolved at runtime by name from the language's external table
                ext += 1
                continue
            if scope == "audioInput":  # plugin source fed by the game at runtime, no media file
                audio_input += 1
                continue
            media += 1
            entry = tables.get(scope, {}).get(int(mid))
            if entry is None:
                reason = "not converted"
            else:
                f = entry.get("file", "")
                if f not in exists:
                    exists[f] = bool(f) and (out_root / f).is_file()
                reason = None if exists[f] else "file missing"
            if reason:
                missing.append({"id": mid, "scope": scope, "reason": reason})
                missing_all.add((scope, mid))
        reaches_nothing = bool(ev.get("reachesNothing"))
        targets = list(ev.get("missingTargets") or [])
        dead = list(ev.get("deadTargets") or [])
        status = _status(missing, reaches_nothing, bool(ev.get("controlOnly")), targets, dead)
        events.append({"id": ev.get("id"), "name": ev.get("name") or "", "bank": ev.get("bank") or "",
                       "language": ev.get("language") or "", "media": media, "external": ext,
                       "audioInput": audio_input, "missing": missing, "reachesNothing": reaches_nothing,
                       "missingTargets": targets, "deadTargets": dead, "status": status,
                       "ok": status in ("ok", "control")})
    count = lambda st: sum(e["status"] == st for e in events)
    summary = {"events": len(events), "ok": count("ok"), "control": count("control"),
               "deadInOriginal": count("deadInOriginal"), "problems": count("problem"),
               "withMissingMedia": sum(bool(e["missing"]) for e in events),
               "reachesNothing": sum(e["reachesNothing"] for e in events),
               "missingMedia": len(missing_all), "audioInput": sum(e["audioInput"] for e in events),
               "named": sum(bool(e["name"]) for e in events)}
    return {"summary": summary, "events": events}


def _md_cell(v) -> str:
    s = str(v).replace("|", "/").replace("\n", " ")
    return s.encode("ascii", "replace").decode("ascii")


def _md_list(items: list, limit: int = 8) -> str:
    out = ", ".join(items[:limit])
    if len(items) > limit:
        out += f", +{len(items) - limit} more"
    return out or "-"


def coverage_markdown(cov: dict) -> str:
    s = cov["summary"]
    lines = ["# Audio coverage", "",
             "Every Wwise event from the banks, joined with the converted media tables.", "",
             f"- events: {s['events']} ({s['named']} with an AK.cs name)",
             f"- resolve to converted audio: {s['ok']}",
             f"- control only (stop, pause, resume, parameters): {s['control']}",
             f"- original data: dead target: {s['deadInOriginal']}",
             f"- real problems: {s['problems']}",
             f"- with missing media: {s['withMissingMedia']} ({s['missingMedia']} distinct media ids)",
             f"- reach no media at all: {s['reachesNothing']}",
             f"- audio input sources (fed at runtime): {s['audioInput']}", ""]
    header = ["| event | id | bank | language | media | external | audio input | missing | missing targets "
              "| reaches nothing |",
              "|---|---|---|---|---|---|---|---|---|---|"]

    def row(e):
        miss = _md_list([f"{m['scope']}/{m['id']} ({m['reason']})" for m in e["missing"]])
        dead = set(e.get("deadTargets") or [])
        targets = _md_list([f"{t} (dead)" if t in dead else str(t) for t in e["missingTargets"]])
        return "| " + " | ".join(_md_cell(x) for x in (e["name"] or "-", e["id"], e["bank"], e["language"] or "-",
                                                         e["media"], e["external"], e.get("audioInput", 0), miss,
                                                         targets, "yes" if e["reachesNothing"] else "no")) + " |"

    key = lambda e: (e["bank"], e["language"], e["name"] or "~", e["id"] or 0)
    titles = {"problem": "Real problems", "deadInOriginal": "Original data: dead target",
              "control": "Control-only events", "ok": "Events that resolve"}
    for st in ("problem", "deadInOriginal", "control", "ok"):
        rows = sorted((e for e in cov["events"] if e.get("status") == st), key=key)
        lines += [f"## {titles[st]} ({len(rows)})", ""] + header + [row(e) for e in rows] + [""]
    return "\n".join(lines)


def write_coverage(report: dict, out_root: Path, report_dir: Path) -> dict:
    cov = coverage(report, out_root)
    report_dir.mkdir(parents=True, exist_ok=True)
    (report_dir / "coverage.json").write_text(json.dumps(cov, separators=(",", ":")))
    (report_dir / "coverage.md").write_text(coverage_markdown(cov), encoding="ascii")
    return cov["summary"]


def build(work_dir: Path, project: Path | None, *, verify: str = "sample", jobs: int | None = None,
          force: bool = False, log=print, install: bool = True, graph_only: bool = False,
          steps: SimpleNamespace | None = None) -> dict:
    if verify not in VERIFY_MODES:
        raise ValueError(f"verify must be one of {VERIFY_MODES}, got {verify!r}")
    work_dir = Path(work_dir)
    project = Path(project) if project else None
    steps = steps or default_steps()
    source_root = work_dir / SOURCE_REL
    if not source_root.is_dir():
        raise FileNotFoundError(f"no Wwise audio at {source_root} (run the merge step first)")
    audio_dir = work_dir / "audio"
    out_root = audio_dir / "PortAudio"
    report_dir = audio_dir / "reports"
    state_path = audio_dir / STATE
    state = {} if force else _load_state(state_path)
    names = project / NAMES_REL if project and (project / NAMES_REL).is_file() else None
    merged = merged_digest(work_dir, source_root)
    result = {"graph": "skipped", "convert": "not run", "coverage": None, "install": None}

    vgmstream = codebooks = None
    if not graph_only:
        t = time.monotonic()
        vgmstream = steps.ensure_vgmstream(work_dir)
        codebooks = steps.ensure_codebooks(work_dir)
        log(f"audio: tools ready ({time.monotonic() - t:.1f} s): {vgmstream}, {codebooks}")

    graph_digest = _digest({"merged": merged, "version": steps.graph_version, "names": _file_digest(names)})
    graph_report_path = report_dir / "graph-report.json"
    graph_done = (out_root / "manifest.json").is_file() and graph_report_path.is_file()
    if state.get("graph") == graph_digest and graph_done:
        report = json.loads(graph_report_path.read_text(encoding="utf-8"))
        log("audio: graph unchanged, skipped")
    else:
        t = time.monotonic()
        state.pop("graph", None)
        report = steps.graph_build(source_root, out_root, report_dir=report_dir, names=names, log=log)
        state["graph"] = graph_digest
        _save_state(state_path, state)
        result["graph"] = "built"
        log(f"audio: graph built in {time.monotonic() - t:.1f} s ({len(report.get('events', []))} events)")

    if not graph_only:
        versions = json.loads(tools.VERSIONS.read_text())
        convert_digest = _digest({"merged": merged, "version": steps.convert_version, "verify": verify,
                                  "tools": {k: versions.get(k) for k in TOOL_PINS}, "graph": graph_digest})
        convert_done = (out_root / "media.json").is_file() and (report_dir / "convert-report.json").is_file()
        if state.get("convert") == convert_digest and convert_done:
            result["convert"] = "skipped"
            log("audio: converted media unchanged, skipped")
        else:
            t = time.monotonic()
            state.pop("convert", None)
            _save_state(state_path, state)
            wanted = steps.referenced_media(report)
            steps.convert(source_root, out_root, codebooks, report_dir=report_dir, vgmstream=vgmstream,
                          wanted=wanted, jobs=jobs, verify=verify, log=log)
            state["convert"] = convert_digest
            _save_state(state_path, state)
            result["convert"] = "built"
            log(f"audio: media converted in {time.monotonic() - t:.1f} s")

    result["coverage"] = write_coverage(report, out_root, report_dir)
    c = result["coverage"]
    log(f"audio: coverage {c['events']} events: {c['ok']} ok, {c['control']} control, "
        f"{c['deadInOriginal']} dead in original, {c['problems']} real problems "
        f"({c['withMissingMedia']} with missing media); {report_dir / 'coverage.md'}")

    if install and project is not None and not graph_only:
        result["install"] = steps.install(out_root, project)
        i = result["install"]
        log(f"audio: installed into {project}: {i.get('copied', 0)} copied, {i.get('unchanged', 0)} unchanged, "
            f"{i.get('deleted', 0)} stale deleted, {i.get('pruned', 0)} PS4 audio files pruned")
    return result


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    ap = argparse.ArgumentParser(prog="python3 -m pipeline.audio", description=__doc__.splitlines()[0])
    ap.add_argument("--verify", choices=VERIFY_MODES, default="sample",
                    help="check remuxed media against vgmstream (default sample)")
    ap.add_argument("--jobs", type=int, default=None, help="converter worker processes (default: CPU count)")
    ap.add_argument("--no-install", action="store_true", help="do not install into the Unity project")
    ap.add_argument("--force", action="store_true", help="ignore .audio-state.json and redo every step")
    ap.add_argument("--graph-only", action="store_true", help="only build the graph and the coverage report")
    return ap.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    from pipeline import config

    a = parse_args(argv)
    c = config.load()
    project = c.work_dir / "project"
    if not project.is_dir():
        print(f"audio: no Unity project at {project}; nothing will be installed")
        project = None
    build(c.work_dir, project, verify=a.verify, jobs=a.jobs, force=a.force, install=not a.no_install,
          graph_only=a.graph_only)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
