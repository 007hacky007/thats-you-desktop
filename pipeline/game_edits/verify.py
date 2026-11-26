"""Owner-only check that the recipes reproduce the old private patches exactly.

python3 -m pipeline.game_edits.verify [--only NNN] [--keep]

Clones the pristine export twice (APFS clones, cheap), applies local-patches/*.patch to one and
the recipes to the other, and compares every path the patches touch. Needs the private
local-patches/ folder, so it only runs on the machine that has it.
"""
from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
from pathlib import Path

from pipeline import config, game_edits, patches


def clone(src: Path, dst: Path) -> None:
    if dst.exists():
        shutil.rmtree(dst)
    dst.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["/bin/cp", "-cR", str(src), str(dst)], check=True)


def touched(patch_file: Path) -> list[str]:
    out = []
    for line in patch_file.read_text(errors="replace").splitlines():
        m = re.match(r"^diff --git a/(.+?) b/(.+)$", line)
        if m:
            out.append(m.group(2))
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="python3 -m pipeline.game_edits.verify")
    ap.add_argument("--only", help="limit the comparison to patches whose name starts with this")
    ap.add_argument("--keep", action="store_true")
    a = ap.parse_args(argv)
    c = config.load()
    pristine = c.work_dir / "export" / "game" / "ExportedProject"
    base = c.work_dir / "verify-edits"
    want, got = base / "patched", base / "edited"
    if not (want / ".done").is_file():
        clone(pristine, want)
        patches.apply_all(want, patches.PATCH_DIR)
        (want / ".done").write_text("ok")
    clone(pristine, got)
    game_edits.apply_all(got, log=lambda *_: None)
    bad = 0
    for pf in sorted(patches.PATCH_DIR.glob("*.patch")):
        if a.only and not pf.name.startswith(a.only):
            continue
        diffs = []
        for rel in touched(pf):
            w, g = want / rel, got / rel
            if w.exists() != g.exists():
                diffs.append(f"{rel}: {'missing' if w.exists() else 'should be gone'}")
            elif w.is_file() and w.read_bytes() != g.read_bytes():
                diffs.append(f"{rel}: differs")
        print(f"{'OK  ' if not diffs else 'FAIL'} {pf.stem} ({len(touched(pf))} paths)")
        for d in diffs[:8]:
            print("     " + d)
        bad += bool(diffs)
    if not a.keep:
        shutil.rmtree(got)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
