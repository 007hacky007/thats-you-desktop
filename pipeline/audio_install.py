"""Install the converted audio (work/audio/PortAudio) into the Unity project and drop the PS4 Wwise audio.

install(staged, project) mirrors the staged tree into <project>/Assets/StreamingAssets/PortAudio:
files whose size or mtime differ are cloned (APFS) or copied, installed files that are no longer
staged are deleted together with their Unity .meta, then prune_wwise_audio runs.

prune_wwise_audio(project) deletes <project>/Assets/StreamingAssets/Audio/GeneratedSoundBanks/PS4/**
except LocaleIndependent/Init.bnk (AkBasePathGetter only enables audio init when it exists) and its
.meta. A project rebuild clones the PS4 audio again; the next install prunes it again.
"""
from __future__ import annotations

import os
from pathlib import Path

from pipeline.merge import _place_file

INSTALL_DIR = Path("Assets") / "StreamingAssets" / "PortAudio"
WWISE_DIR = Path("Assets") / "StreamingAssets" / "Audio" / "GeneratedSoundBanks" / "PS4"
KEEP = {"LocaleIndependent/Init.bnk", "LocaleIndependent/Init.bnk.meta"}
IGNORED = {".DS_Store", ".gitkeep"}


def _staged_files(staged: Path) -> dict[str, Path]:
    out = {}
    for dirpath, _d, files in os.walk(staged):
        for f in files:
            if f in IGNORED:
                continue
            p = Path(dirpath) / f
            out[p.relative_to(staged).as_posix()] = p
    return out


def _parents(rels) -> set[str]:
    dirs = set()
    for rel in rels:
        parts = rel.split("/")[:-1]
        for i in range(1, len(parts) + 1):
            dirs.add("/".join(parts[:i]))
    return dirs


def _remove_empty_dirs(root: Path) -> None:
    for dirpath, _d, _f in os.walk(root, topdown=False):
        p = Path(dirpath)
        if p != root and not any(p.iterdir()):
            p.rmdir()


def install(staged: Path, project: Path) -> dict:
    staged, project = Path(staged), Path(project)
    if not staged.is_dir():
        raise FileNotFoundError(f"no staged audio at {staged} (run python3 -m pipeline.audio)")
    dst_root = project / INSTALL_DIR
    want = _staged_files(staged)
    want_dirs = _parents(want)
    copied = unchanged = deleted = 0
    for rel, src in sorted(want.items()):
        dst = dst_root / rel
        s = src.stat()
        if dst.is_file() and not dst.is_symlink():
            d = dst.stat()
            if d.st_size == s.st_size and d.st_mtime_ns == s.st_mtime_ns:
                unchanged += 1
                continue
        if dst.is_symlink() or dst.exists():
            dst.unlink()
        _place_file(src, dst)
        copied += 1
    if dst_root.is_dir():
        for dirpath, dirnames, files in os.walk(dst_root):
            for f in files:
                p = Path(dirpath) / f
                rel = p.relative_to(dst_root).as_posix()
                if rel in want:
                    continue
                base = rel[:-5] if rel.endswith(".meta") else None
                if base is not None and (base in want or base in want_dirs):
                    continue
                p.unlink()
                deleted += 1
        _remove_empty_dirs(dst_root)
    pruned = prune_wwise_audio(project)
    return {"copied": copied, "unchanged": unchanged, "deleted": deleted, "pruned": pruned}


def prune_wwise_audio(project: Path) -> int:
    """Delete the PS4 Wwise audio in the project except Init.bnk. Returns the number of entries removed."""
    project = Path(project)
    root = project / WWISE_DIR
    if not root.exists() and not root.is_symlink():
        return 0
    if not root.resolve().is_relative_to(project.resolve()) or root.is_symlink():
        raise ValueError(f"refusing to prune {root}: it resolves outside the project {project}")
    keep_dirs = _parents(KEEP)
    removed = 0
    for dirpath, dirnames, files in os.walk(root):
        here = Path(dirpath)
        for d in list(dirnames):
            p = here / d
            if p.is_symlink():  # never follow a link out of the project; drop the link itself
                p.unlink()
                dirnames.remove(d)
                removed += 1
        for f in files:
            p = here / f
            rel = p.relative_to(root).as_posix()
            if rel in KEEP:
                continue
            if rel.endswith(".meta") and rel[:-5] in keep_dirs:
                continue
            p.unlink()
            removed += 1
    _remove_empty_dirs(root)
    return removed
