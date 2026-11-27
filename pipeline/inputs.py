"""Check the player's inputs: the PS4 game (folder or fake .pkg), its update, and the app APK.

Tested and supported: the European release EP9000-CUSA08389, base 01.00 with update 01.05.
Other releases of That's You! are accepted as untested; the game edits are hash-guarded and stop
with a clear message when their code differs.
"""
from __future__ import annotations

import hashlib
import shutil
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import Literal

from pipeline import pkg as pkgmod

TITLE_ID = "CUSA08389"
SUPPORTED = {"content_id": "EP9000-CUSA08389_00-1705161243TYM1E2", "base": "01.00", "update": "01.05"}
# Other That's You! releases; listed so a player gets "untested" instead of "not That's You!".
KNOWN_TITLES = {"CUSA08389", "CUSA06348"}
APK_SHA256 = "6bf13282cd189e3a960e56fff0f8f5acd1e4460d831d0c21ee022c55889379d5"
APK_SEARCH_URL = "https://www.google.com/search?q=site%3Awww.apkmirror.com+thats+you+1.6+apk+download"
DONE = ".unpacked"


class InputError(Exception):
    pass


@dataclass(frozen=True)
class GameInfo:
    title: str
    content_id: str
    version: str
    kind: Literal["folder", "pkg"]
    supported: bool


def kind(path: Path) -> Literal["folder", "pkg"]:
    p = Path(path)
    if p.is_dir():
        return "folder"
    if p.is_file():
        with open(p, "rb") as f:
            if f.read(4) == pkgmod.PKG_MAGIC:
                return "pkg"
        raise InputError(f"{p.name} is neither a game folder nor a PS4 package (.pkg)")
    raise InputError(f"not found: {p}")


def read_sfo(data_or_path) -> dict:
    """Entries of a param.sfo (strings and integers)."""
    b = Path(data_or_path).read_bytes() if not isinstance(data_or_path, (bytes, bytearray)) else bytes(data_or_path)
    if len(b) < 20 or b[:4] != b"\0PSF":
        raise InputError("param.sfo is damaged")
    _magic, _ver, kt, dt, n = struct.unpack_from("<4sIIII", b)
    out = {}
    for i in range(n):
        ko, fmt, ln, _mx, do = struct.unpack_from("<HHIII", b, 20 + 16 * i)
        end = b.index(b"\0", kt + ko)
        key = b[kt + ko:end].decode("ascii", "replace")
        raw = b[dt + do:dt + do + ln]
        out[key] = struct.unpack("<I", raw[:4])[0] if fmt == 0x0404 else raw.rstrip(b"\0").decode("utf-8", "replace")
    return out


def _sfo_of(path: Path, k: str) -> dict:
    if k == "folder":
        sfo = Path(path) / "sce_sys" / "param.sfo"
        if not sfo.is_file():
            raise InputError(f"{Path(path).name} has no sce_sys/param.sfo: not a dumped PS4 game folder")
        return read_sfo(sfo)
    try:
        return read_sfo(pkgmod.read_entry(Path(path), pkgmod.PARAM_SFO))
    except pkgmod.PkgError as e:
        raise InputError(str(e)) from None


def check_game(path: Path, role: Literal["base", "update"]) -> GameInfo:
    k = kind(path)
    if k == "pkg":
        cid = pkgmod.read_header(Path(path))["content_id"]
        try:
            sfo = _sfo_of(path, k)
        except InputError:
            sfo = {"CONTENT_ID": cid, "TITLE_ID": cid[7:16]}
    else:
        sfo = _sfo_of(path, k)
    cid = str(sfo.get("CONTENT_ID", ""))
    title = str(sfo.get("TITLE_ID", cid[7:16]))
    if title not in KNOWN_TITLES:
        raise InputError(f"{Path(path).name} is {title or 'an unknown title'}, not That's You!")
    cat = sfo.get("CATEGORY")
    if cat is not None:
        if role == "base" and cat != "gd":
            raise InputError(f"{Path(path).name} is an update, not the base game")
        if role == "update" and cat != "gp":
            raise InputError(f"{Path(path).name} is the base game, not an update")
    if k == "folder" and role == "base" and not (Path(path) / "eboot.bin").is_file():
        raise InputError(f"{Path(path).name} has no eboot.bin: not a dumped base game")
    version = str(sfo.get("APP_VER", ""))
    supported = cid == SUPPORTED["content_id"] and (version == SUPPORTED[role] or not version)
    return GameInfo(title, cid, version, k, supported)


def check_apk(path: Path) -> None:
    p = Path(path)
    if not p.is_file():
        raise InputError(f"not found: {p}")
    h = hashlib.sha256()
    with open(p, "rb") as f:
        while chunk := f.read(1 << 20):
            h.update(chunk)
    if h.hexdigest() != APK_SHA256:
        raise InputError("this is not the That's You! app version 1.6 (1623) that the port needs, "
                         "or the file is damaged")


def _pkg_key(p: Path) -> str:
    st = p.stat()
    return f"{p.name}:{st.st_size}:{st.st_mtime_ns}"


def unpack_pkg(pkg: Path, out: Path, scratch: Path, unpack=pkgmod.unpack, log=print) -> Path:
    """Unpack a package into out once; a later call with the same package returns at once."""
    out = Path(out)
    mark = out / DONE
    if mark.is_file() and mark.read_text() == _pkg_key(Path(pkg)):
        return out
    if out.exists():
        shutil.rmtree(out)
    unpack(Path(pkg), out, Path(scratch), log=log)
    mark.write_text(_pkg_key(Path(pkg)))
    return out
