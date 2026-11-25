"""Download pinned third-party tools into <work>/tools with sha256 verification."""
from __future__ import annotations

import hashlib
import json
import platform
import shutil
import sys
import tarfile
import urllib.request
import zipfile
from pathlib import Path

VERSIONS = Path(__file__).with_name("versions.json")


class ChecksumError(Exception):
    pass


def current_platform() -> str:
    arm = platform.machine().lower() in ("arm64", "aarch64")
    if sys.platform == "darwin":
        return "macos-arm64" if arm else "macos-x64"
    if sys.platform == "win32":
        return "win-arm64" if arm else "win-x64"
    return "linux-arm64" if arm else "linux-x64"


def platform_spec(entry: dict, plat: str) -> dict:
    if "url" in entry:  # platform-independent tool
        return {"version": entry["version"], "url": entry["url"], "sha256": entry["sha256"]}
    return {"version": entry["version"], "url": entry[f"url_{plat}"], "sha256": entry[f"sha256_{plat}"]}


def _safe_target(dest: Path, name: str) -> Path:
    target = (dest / name).resolve()
    if not target.is_relative_to(dest.resolve()):
        raise ValueError(f"unsafe path in archive: {name}")
    return target


def _extract(archive: Path, dest: Path) -> None:
    if archive.name.endswith(".zip"):
        with zipfile.ZipFile(archive) as z:
            for info in z.infolist():
                _safe_target(dest, info.filename)
            z.extractall(dest)
            for info in z.infolist():
                mode = info.external_attr >> 16
                if mode & 0o111:
                    (dest / info.filename).chmod(mode & 0o777)
    else:
        with tarfile.open(archive) as t:
            for m in t.getmembers():
                _safe_target(dest, m.name)
                if m.issym() or m.islnk():
                    _safe_target(dest, str(Path(m.name).parent / m.linkname))
            t.extractall(dest, filter="data")


def ensure(name: str, work_dir: Path, spec: dict | None = None) -> Path:
    if spec is None:
        spec = platform_spec(json.loads(VERSIONS.read_text())[name], current_platform())
    root = Path(work_dir) / "tools"
    dest = root / f"{name}-{spec['version']}"
    marker = dest / ".tool-complete"
    if marker.is_file() and marker.read_text() == spec["sha256"]:
        return dest

    root.mkdir(parents=True, exist_ok=True)
    archive = root / Path(spec["url"].split("?")[0]).name
    h = hashlib.sha256()
    with urllib.request.urlopen(spec["url"]) as r, open(archive, "wb") as f:
        while chunk := r.read(1 << 20):
            h.update(chunk)
            f.write(chunk)
    if h.hexdigest() != spec["sha256"].lower():
        archive.unlink()
        raise ChecksumError(f"{name}: sha256 {h.hexdigest()} != pinned {spec['sha256']}")
    if dest.exists():
        shutil.rmtree(dest)
    tmp = dest.with_name(dest.name + ".tmp")
    if tmp.exists():
        shutil.rmtree(tmp)
    try:
        _extract(archive, tmp)
    finally:
        archive.unlink()
    tmp.rename(dest)
    marker.write_text(spec["sha256"])
    return dest
