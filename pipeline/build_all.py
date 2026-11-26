"""Run the whole pipeline: merge -> extract game -> extract app -> project -> game edits -> audio
-> Unity import -> texture import fix -> Unity reimport -> report."""
from __future__ import annotations

import hashlib
import json
import os
import shutil
from pathlib import Path

from pipeline import audio, compile_report, config, extract_app, extract_game, fix_textures, game_edits, merge, unity
from pipeline.csc_check import PORT_DIR
from pipeline.merge import _place_file

PROJECT_MARKER = ".project-state.json"


class GameEdits:
    """The port's edits of the exported game code (pipeline/game_edits)."""

    @staticmethod
    def digest() -> str:
        h = hashlib.sha256()
        base = Path(game_edits.__file__).parent
        for p in sorted(base.rglob("*.py")):
            h.update(p.relative_to(base).as_posix().encode() + b"\0" + p.read_bytes())
        return h.hexdigest()

    @staticmethod
    def apply(root: Path) -> None:
        game_edits.apply_all(root, log=lambda *_: None)


def _clone_tree(src: Path, dst: Path) -> None:
    for dirpath, _d, files in os.walk(src):
        for f in files:
            s = Path(dirpath) / f
            _place_file(s, dst / s.relative_to(src))


def sync_port(port_dir: Path, project: Path) -> None:
    """Mirror our own code (unity/Port) into Assets/Port. Unity-generated .meta files are kept for
    files that still exist so script GUIDs stay stable; stale files are removed."""
    dst_root = project / "Assets" / "Port"
    want = set()
    if Path(port_dir).is_dir():
        for dirpath, _d, files in os.walk(port_dir):
            for f in files:
                if f in (".gitkeep", ".DS_Store"):
                    continue
                src = Path(dirpath) / f
                rel = src.relative_to(port_dir)
                want.add(rel.as_posix())
                dst = dst_root / rel
                if not dst.is_file() or dst.read_bytes() != src.read_bytes():
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(src, dst)
    if dst_root.is_dir():
        for dirpath, _d, files in os.walk(dst_root, topdown=False):
            for f in files:
                rel = (Path(dirpath) / f).relative_to(dst_root).as_posix()
                base = rel[:-5] if rel.endswith(".meta") else rel
                if rel not in want and base not in want and not (dst_root / base).is_dir():
                    (Path(dirpath) / f).unlink()
            if Path(dirpath) != dst_root and not any(Path(dirpath).iterdir()):
                Path(dirpath).rmdir()


def prepare_project(pristine: Path, project: Path, edits=None, port_dir: Path | None = None) -> bool:
    """(Re)build the working project from the pristine export when the edits changed, then sync
    unity/Port into Assets/Port. Returns True when the project was rebuilt. Unity's Library cache is kept."""
    port_dir = PORT_DIR if port_dir is None else port_dir
    rebuilt = _rebuild_if_needed(pristine, project, GameEdits if edits is None else edits)
    sync_port(port_dir, project)
    return rebuilt


def _rebuild_if_needed(pristine: Path, project: Path, edits) -> bool:
    pristine_marker = pristine / ".export-complete"
    digest = edits.digest() + ":" + (pristine_marker.read_text() if pristine_marker.is_file() else "")
    state = project / PROJECT_MARKER
    if state.is_file() and json.loads(state.read_text()).get("edits") == digest:
        return False
    tmp = project.with_name(project.name + ".tmp")
    if tmp.exists():
        shutil.rmtree(tmp)
    _clone_tree(pristine, tmp)
    edits.apply(tmp)
    if (project / "Library").is_dir():
        (project / "Library").rename(tmp / "Library")
    if project.exists():
        shutil.rmtree(project)
    tmp.rename(project)
    state.write_text(json.dumps({"edits": digest}))
    return True


def check_unity_editor(editor: Path | None) -> None:
    """Fail before hours of extraction if the editor path is wrong."""
    if not editor:
        raise config.ConfigError("unity_editor", "missing; set it to the Unity editor binary")
    editor = Path(editor)
    if editor.suffix == ".app" or (editor / "Contents").is_dir():
        raise config.ConfigError("unity_editor", f"{editor} is the app bundle; use {editor}/Contents/MacOS/Unity")
    if not editor.is_file() or not os.access(editor, os.X_OK):
        raise config.ConfigError("unity_editor", f"not an executable file: {editor}")


def import_project(editor: Path, project: Path, media: Path, reports: Path,
                   run_batch=unity.run_batch, fix=fix_textures.run) -> int:
    """Import the project in Unity, then restore the texture import settings the export loses.

    fix_textures only works on metas Unity 6 has already upgraded, so it runs after the first
    import; when it rewrites any meta, Unity imports again so the fixed settings take effect.
    Returns the exit code of the last Unity run. Logs: unity-import.log, unity-reimport.log."""
    reports.mkdir(parents=True, exist_ok=True)
    # -accept-apiupdate: let Unity's script updater rewrite obsolete APIs during the 5.3 -> 6 upgrade
    rc = run_batch(editor, project, ["-accept-apiupdate"], reports / "unity-import.log")
    if rc != 0:
        return rc
    report = fix(media, project)
    (reports / "fix-textures.json").write_text(json.dumps(report, indent=1))
    print(f"fix_textures: {report['changed']} texture metas changed")
    if report["changed"]:
        rc = run_batch(editor, project, [], reports / "unity-reimport.log")
    return rc


def build_project(c: config.Config, pristine: Path, project: Path, *, prepare=prepare_project,
                  audio_build=None, import_=import_project, log=print) -> int:
    """prepare the project -> convert and install the audio -> Unity import. The audio goes in
    before the import so Unity imports the PortAudio files once. Returns the Unity exit code."""
    w = c.work_dir
    log("project rebuilt:", prepare(pristine, project, GameEdits))
    (audio_build or audio.build)(w, project)
    return import_(c.unity_editor, project, w / "merged" / "Media", w / "reports")


def main() -> None:
    c = config.load()
    check_unity_editor(c.unity_editor)
    w = c.work_dir
    print("merge:", merge.merge(c.base_dump, c.patch_dump, w / "merged"))
    extract_game.main()
    extract_app.main()
    pristine = w / "export" / "game" / "ExportedProject"
    project = w / "project"
    log = w / "reports" / "unity-import.log"
    rc = build_project(c, pristine, project)
    report = compile_report.parse(log, exit_code=rc)
    compile_report.write(report, w / "reports")
    print(f"unity exit {rc}; {report.total} compile errors; report: {w / 'reports' / 'compile.md'}")
    if report.failed:
        raise SystemExit(f"Unity failed (exit {rc}); see {log} and {w / 'reports' / 'unity-reimport.log'}")


if __name__ == "__main__":
    main()
