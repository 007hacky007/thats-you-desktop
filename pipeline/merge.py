"""Overlay the patch dump onto the base dump (patch files win).

Output is built in <out>.tmp and renamed at the end; a manifest of the input
file list is written last so an interrupted run is redone. Files are placed
with copy-on-write clones where the filesystem supports it (APFS), otherwise
copied. Hard links are never used: a write to the output would change the
read-only inputs.
"""
from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

MANIFEST = ".merge-manifest.json"


@dataclass(frozen=True)
class MergeResult:
    files: int
    from_patch: int
    cloned: bool


def _scan(root: Path) -> dict[str, Path]:
    out = {}
    for dirpath, _dirs, files in os.walk(root):
        for name in files:
            if name == ".DS_Store":
                continue
            p = Path(dirpath) / name
            out[p.relative_to(root).as_posix()] = p
    return out


def _plan(base: Path, patch: Path) -> dict[str, tuple[Path, str]]:
    plan = {rel: (p, "base") for rel, p in _scan(base).items()}
    plan.update({rel: (p, "patch") for rel, p in _scan(patch).items()})
    return plan


def _digest(plan: dict[str, tuple[Path, str]]) -> str:
    h = hashlib.sha256()
    for rel in sorted(plan):
        src, origin = plan[rel]
        st = src.stat()
        h.update(f"{rel}\0{st.st_size}\0{st.st_mtime_ns}\0{origin}\n".encode())
    return h.hexdigest()


def _place_file(src: Path, dst: Path) -> bool:
    """Clone src to dst if possible, else copy. Returns True when cloned."""
    dst.parent.mkdir(parents=True, exist_ok=True)
    if sys.platform == "darwin":
        r = subprocess.run(["/bin/cp", "-c", "-p", str(src), str(dst)], capture_output=True)
        if r.returncode == 0:
            return True
    shutil.copy2(src, dst)
    return False


def merge(base: Path, patch: Path, out: Path) -> MergeResult:
    base, patch, out = Path(base), Path(patch), Path(out)
    plan = _plan(base, patch)
    digest = _digest(plan)
    from_patch = sum(1 for _, o in plan.values() if o == "patch")
    manifest = out / MANIFEST
    if manifest.is_file():
        try:
            if json.loads(manifest.read_text())["digest"] == digest:
                return MergeResult(len(plan), from_patch, False)
        except (ValueError, KeyError):
            pass

    tmp = out.with_name(out.name + ".tmp")
    if tmp.exists():
        shutil.rmtree(tmp)
    cloned = True
    for rel, (src, _origin) in plan.items():
        cloned &= _place_file(src, tmp / rel)
    (tmp / MANIFEST).write_text(json.dumps({"digest": digest, "files": len(plan), "from_patch": from_patch}))
    if out.exists():
        shutil.rmtree(out)
    tmp.rename(out)
    return MergeResult(len(plan), from_patch, cloned and bool(plan))


def main() -> None:
    from pipeline import config

    c = config.load()
    r = merge(c.base_dump, c.patch_dump, c.work_dir / "merged")
    print(f"merged {r.files} files ({r.from_patch} from patch), cloned={r.cloned}")


if __name__ == "__main__":
    main()
