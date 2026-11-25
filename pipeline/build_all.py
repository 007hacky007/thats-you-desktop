"""Run the whole pipeline: merge -> extract game -> extract app -> project -> patches -> Unity import -> report."""
from __future__ import annotations

import hashlib
import json
import os
import shutil
from pathlib import Path

from pipeline import compile_report, config, extract_app, extract_game, merge, patches, unity
from pipeline.merge import _place_file

PROJECT_MARKER = ".project-state.json"


def _patch_digest(patch_dir: Path) -> str:
    h = hashlib.sha256()
    for p in sorted(Path(patch_dir).glob("*.patch")):
        h.update(p.name.encode() + b"\0" + p.read_bytes())
    return h.hexdigest()


def _clone_tree(src: Path, dst: Path) -> None:
    for dirpath, _d, files in os.walk(src):
        for f in files:
            s = Path(dirpath) / f
            _place_file(s, dst / s.relative_to(src))


def prepare_project(pristine: Path, project: Path, patch_dir: Path) -> bool:
    """(Re)build the working project from the pristine export when patches changed.

    Returns True when the project was rebuilt. Unity's Library cache is kept."""
    pristine_marker = pristine / ".export-complete"
    digest = _patch_digest(patch_dir) + ":" + (pristine_marker.read_text() if pristine_marker.is_file() else "")
    state = project / PROJECT_MARKER
    if state.is_file() and json.loads(state.read_text()).get("patches") == digest:
        return False
    tmp = project.with_name(project.name + ".tmp")
    if tmp.exists():
        shutil.rmtree(tmp)
    _clone_tree(pristine, tmp)
    patches.apply_all(tmp, patch_dir)
    if (project / "Library").is_dir():
        (project / "Library").rename(tmp / "Library")
    if project.exists():
        shutil.rmtree(project)
    tmp.rename(project)
    state.write_text(json.dumps({"patches": digest}))
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


def main() -> None:
    c = config.load()
    check_unity_editor(c.unity_editor)
    w = c.work_dir
    print("merge:", merge.merge(c.base_dump, c.patch_dump, w / "merged"))
    extract_game.main()
    extract_app.main()
    pristine = w / "export" / "game" / "ExportedProject"
    project = w / "project"
    print("project rebuilt:", prepare_project(pristine, project, patches.PATCH_DIR))
    log = w / "reports" / "unity-import.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    # -accept-apiupdate: let Unity's script updater rewrite obsolete APIs during the 5.3 -> 6 upgrade
    rc = unity.run_batch(c.unity_editor, project, ["-accept-apiupdate"], log)
    report = compile_report.parse(log, exit_code=rc)
    compile_report.write(report, w / "reports")
    print(f"unity exit {rc}; {report.total} compile errors; report: {w / 'reports' / 'compile.md'}")
    if report.failed:
        raise SystemExit(f"Unity failed (exit {rc}); see {log}")


if __name__ == "__main__":
    main()
