"""Give a fresh AssetRipper export the asset GUIDs of the export the port was verified against.

AssetRipper assigns most asset GUIDs differently on every export, but the port's shaders, the
game edit recipes and the scene clean-ups refer to the GUIDs of the owner's verified export. This
step rewrites a fresh export so every asset has the GUID it had there: in its .meta file and in
every reference to it in the text (YAML) assets.

The table (pipeline/guid_table.json) maps a short hash of each asset's path to its GUID, so no game
file names are published; GUIDs are random numbers made by AssetRipper, not game content.
Owner: python3 -m pipeline.guid_align --make   (from work/export/game/ExportedProject)
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
from pathlib import Path

TABLE = Path(__file__).with_name("guid_table.json")
MARKER = ".guids-aligned"
GUID_LINE = re.compile(rb"^guid: ([0-9a-f]{32})\s*$", re.M)
GUID_REF = re.compile(rb"guid: ([0-9a-f]{32})")
YAML_HEAD = b"%YAML"


def key(rel: str) -> str:
    return hashlib.sha256(rel.encode("utf-8")).hexdigest()[:16]


def _metas(root: Path):
    assets = Path(root) / "Assets"
    for dirpath, _dirs, files in os.walk(assets):
        for f in files:
            if f.endswith(".meta"):
                p = Path(dirpath) / f
                yield p, p.relative_to(root).as_posix()[:-5]


def _guid_of(meta: Path) -> str | None:
    m = GUID_LINE.search(meta.read_bytes())
    return m.group(1).decode() if m else None


def make_table(root: Path) -> dict[str, str]:
    table = {}
    for meta, rel in _metas(root):
        g = _guid_of(meta)
        if g:
            table[key(rel)] = g
    return dict(sorted(table.items()))


def table_digest(table: dict[str, str]) -> str:
    return hashlib.sha256(json.dumps(table, sort_keys=True).encode()).hexdigest()


def align(root: Path, table: dict[str, str] | None = None, log=lambda *_: None) -> dict:
    """Rewrite root (an ExportedProject folder) to the table's GUIDs. Idempotent."""
    root = Path(root)
    if table is None:
        table = json.loads(TABLE.read_text())
    mapping: dict[bytes, bytes] = {}
    for meta, rel in _metas(root):
        want = table.get(key(rel))
        have = _guid_of(meta)
        if want and have and want != have:
            mapping[have.encode()] = want.encode()
    if not mapping:
        (root / MARKER).write_text(table_digest(table))
        return {"remapped": 0, "files": 0}

    def sub(m: re.Match) -> bytes:
        return b"guid: " + mapping.get(m.group(1), m.group(1))

    changed = 0
    for dirpath, _dirs, files in os.walk(root / "Assets"):
        for f in files:
            p = Path(dirpath) / f
            with open(p, "rb") as fh:
                head = fh.read(5)
            if not (f.endswith(".meta") or head == YAML_HEAD):
                continue
            data = p.read_bytes()
            if b"guid: " not in data:
                continue
            new = GUID_REF.sub(sub, data)  # one pass: swapped GUIDs cannot chain
            if new != data:
                p.write_bytes(new)
                changed += 1
    (root / MARKER).write_text(table_digest(table))
    log(f"guid_align: {len(mapping)} GUIDs aligned in {changed} files")
    return {"remapped": len(mapping), "files": changed}


def ensure(root: Path, table: dict[str, str] | None = None, log=lambda *_: None) -> dict:
    """align() unless this export was already aligned to this table."""
    if table is None:
        table = json.loads(TABLE.read_text())
    marker = Path(root) / MARKER
    if marker.is_file() and marker.read_text() == table_digest(table):
        return {"remapped": 0, "files": 0, "skipped": True}
    return align(root, table, log)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--make", action="store_true", help="write the table from the owner's verified export")
    ap.add_argument("--root", type=Path, help="ExportedProject folder (default work/export/game/ExportedProject)")
    a = ap.parse_args(argv)
    from pipeline import config
    root = a.root or config.load().work_dir / "export" / "game" / "ExportedProject"
    if a.make:
        table = make_table(root)
        TABLE.write_text(json.dumps(table, indent=0, sort_keys=True) + "\n")
        print(f"wrote {len(table)} GUIDs to {TABLE}")
        return 0
    print(align(root, log=print))
    return 0


if __name__ == "__main__":
    sys.exit(main())
