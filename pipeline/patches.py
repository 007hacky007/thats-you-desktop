"""Local patches against the decompiled game export.

WARNING: patches contain decompiled game code. local-patches/ is its own git
repository, ignored by the main repo, and must never be published. Before any
publication they are converted to build-time compiled-assembly edits (Plan 7).
"""
from __future__ import annotations

import filecmp
import os
import subprocess
from pathlib import Path

from pipeline.config import REPO_ROOT

PATCH_DIR = REPO_ROOT / "local-patches"
MD_TEMPLATE = """# {name}

What it changes:

Why:

How to redo it as a compiled-assembly edit (Plan 7):
"""


class PatchError(Exception):
    def __init__(self, name: str, stderr: str):
        super().__init__(f"patch {name}: {stderr.strip()}")
        self.name = name
        self.stderr = stderr


def _files(root: Path) -> set[str]:
    out = set()
    for dirpath, _d, files in os.walk(root):
        for f in files:
            if f == ".export-complete":
                continue
            out.add((Path(dirpath) / f).relative_to(root).as_posix())
    return out


def _diff_one(pristine: Path, project: Path, rel: str) -> str:
    a = pristine / rel
    b = project / rel
    args = ["git", "diff", "--no-index", "--binary", "--no-color",
            str(a) if a.exists() else os.devnull, str(b) if b.exists() else os.devnull]
    r = subprocess.run(args, capture_output=True, text=True)
    if r.returncode not in (0, 1):
        raise PatchError(rel, r.stderr)
    text = r.stdout
    for side, path in (("a", a), ("b", b)):
        text = text.replace(f"{side}/{path.as_posix().lstrip('/')}", f"{side}/{rel}")
        text = text.replace(f"{side}{path.as_posix()}", f"{side}/{rel}")
    return text


def make(project: Path, name: str, pristine: Path | None = None, patch_dir: Path = PATCH_DIR) -> Path:
    project = Path(project)
    pristine = Path(pristine) if pristine else project.with_name(project.name + "-pristine")
    pa, pb = _files(pristine), _files(project)
    changed = sorted((pa ^ pb) | {r for r in pa & pb if not filecmp.cmp(pristine / r, project / r, shallow=False)})
    if not changed:
        raise PatchError(name, "no changes against pristine export")
    out = Path(patch_dir) / f"{name}.patch"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("".join(_diff_one(pristine, project, r) for r in changed))
    md = out.with_suffix(".md")
    if not md.exists():
        md.write_text(MD_TEMPLATE.format(name=name))
    return out


def apply_all(project: Path, patch_dir: Path = PATCH_DIR) -> list[str]:
    applied = []
    for p in sorted(Path(patch_dir).glob("*.patch")):
        name = p.stem
        if not p.with_suffix(".md").is_file():
            raise PatchError(name, f"missing {p.with_suffix('.md').name} describing the change")
        r = subprocess.run(["git", "apply", "--whitespace=nowarn", "--unsafe-paths", str(p.resolve())],
                           cwd=project, capture_output=True, text=True)
        if r.returncode != 0:
            raise PatchError(name, r.stderr)
        applied.append(name)
    return applied
