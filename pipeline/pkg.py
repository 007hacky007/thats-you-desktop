"""Unpack a PS4 fake package (a decrypted backup .pkg) into a folder, checking every block.

A fake package keeps the game in an encrypted outer PFS image. Its key (EKPFS) is derived from
the content ID and the package passcode (32 zeros for nearly all backup packages):
SHA-256(SHA-256(index as 4 big-endian bytes) + SHA-256(content ID padded to 48) + passcode).
From EKPFS and the image's seed come the XTS keys (HMAC-SHA256 with index 1: tweak key, data key)
and the block signing key (index 2). The outer image is AES-128-XTS encrypted in 0x1000-byte
sectors from its second block on. Its "pfs_image.dat" is the compressed inner image.

Every outer block is checked against its signature and every compressed inner block against its
Adler-32 checksum, so a wrong passcode or a damaged package stops with an error instead of
producing broken files.
"""
from __future__ import annotations

import hashlib
import hmac
import struct
from pathlib import Path

from pipeline import pfs

PKG_MAGIC = b"\x7fCNT"
ZERO_PASSCODE = "0" * 32
SECTOR = 0x1000
NEW_CRYPT = 0x2000000000000000


class PkgError(Exception):
    pass


def read_header(path: Path) -> dict:
    with open(path, "rb") as f:
        head = f.read(0x460)
    if len(head) < 0x460 or head[:4] != PKG_MAGIC:
        raise PkgError(f"{Path(path).name} is not a PS4 package")
    cid = head[0x40:0x64].split(b"\0", 1)[0].decode("ascii", "replace")
    flags, offset, size = struct.unpack_from(">QQQ", head, 0x408)
    return {"content_id": cid, "pfs_flags": flags, "pfs_offset": offset, "pfs_size": size}


PARAM_SFO = 0x1000


def read_entry(path: Path, entry_id: int) -> bytes:
    """Bytes of a plain (unencrypted) entry of the package's entry table, such as param.sfo."""
    with open(path, "rb") as f:
        head = f.read(0x20)
        count, table = struct.unpack_from(">I", head, 0x10)[0], struct.unpack_from(">I", head, 0x18)[0]
        if count > 10000:
            raise PkgError("package entry table is damaged")
        f.seek(table)
        rows = f.read(32 * count)
        for i in range(count):
            eid, _name, flags, _f2, off, size, _pad = struct.unpack_from(">IIIIIIQ", rows, 32 * i)
            if eid == entry_id:
                if flags & 0x80000000:
                    raise PkgError(f"package entry {entry_id:#x} is encrypted")
                f.seek(off)
                data = f.read(size)
                if len(data) != size:
                    raise PkgError("package ends early")
                return data
    raise PkgError(f"package has no entry {entry_id:#x}")


def ekpfs_from_passcode(content_id: str, passcode: str = ZERO_PASSCODE) -> bytes:
    if len(content_id) != 36 or len(passcode) != 32:
        raise PkgError("content ID must have 36 characters and the passcode 32")
    sha = lambda b: hashlib.sha256(b).digest()  # noqa: E731
    return sha(sha(struct.pack(">I", 1)) + sha(content_id.encode("ascii").ljust(48, b"\0"))
               + passcode.encode("ascii"))


def pfs_keys(ekpfs: bytes, seed: bytes, new_crypt: bool) -> tuple[bytes, bytes, bytes]:
    """(tweak key, data key, sign key)."""
    def gen(key: bytes, index: int) -> bytes:
        return hmac.new(key, struct.pack("<I", index) + seed, hashlib.sha256).digest()
    enc = gen(hmac.new(ekpfs, seed, hashlib.sha256).digest() if new_crypt else ekpfs, 1)
    return enc[:16], enc[16:], gen(ekpfs, 2)


class XtsSource:
    """The outer image inside the package, decrypted on the fly."""

    def __init__(self, path: Path, offset: int, size: int, tweak_key: bytes, data_key: bytes, start_sector: int):
        from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
        self._Cipher, self._algo, self._modes = Cipher, algorithms.AES(data_key + tweak_key), modes
        self.f = open(path, "rb")
        self.offset, self.size, self.start = offset, size, start_sector

    def read(self, offset: int, n: int) -> bytes:
        n = max(0, min(n, self.size - offset))
        first = offset // SECTOR
        last = -(-(offset + n) // SECTOR)
        self.f.seek(self.offset + first * SECTOR)
        raw = self.f.read((last - first) * SECTOR)
        out = bytearray()
        for i in range(len(raw) // SECTOR):
            sec = first + i
            chunk = raw[i * SECTOR:(i + 1) * SECTOR]
            if sec >= self.start:
                d = self._Cipher(self._algo, self._modes.XTS(sec.to_bytes(16, "little"))).decryptor()
                chunk = d.update(chunk) + d.finalize()
            out += chunk
        skip = offset - first * SECTOR
        return bytes(out[skip:skip + n])

    def close(self):
        self.f.close()


def open_outer(path: Path, passcode: str = ZERO_PASSCODE) -> pfs.Image:
    h = read_header(path)
    with open(path, "rb") as f:
        f.seek(h["pfs_offset"])
        head = f.read(0x400)
    seed = head[0x370:0x380]
    bs = struct.unpack_from("<I", head, 0x20)[0]
    ekpfs = ekpfs_from_passcode(h["content_id"], passcode)
    tweak, data, sign = pfs_keys(ekpfs, seed, bool(h["pfs_flags"] & NEW_CRYPT))
    src = XtsSource(path, h["pfs_offset"], h["pfs_size"], tweak, data, bs // SECTOR)
    return pfs.Image(src, sign)


def unpack(path: Path, out: Path, scratch: Path, passcode: str = ZERO_PASSCODE, log=print) -> int:
    """Unpack the package's game files into out (via scratch/inner.img). Returns the file count."""
    outer = open_outer(path, passcode)
    try:
        ino, typ = outer.find("uroot", "pfs_image.dat")
        if typ != pfs.T_FILE:
            raise PkgError("package has no inner image")
        scratch.mkdir(parents=True, exist_ok=True)
        inner = scratch / "inner.img"
        log(f"decompressing the game image ({outer.file_size(ino) / 1e9:.1f} GB)")
        pfs.decompress_pfsc(outer.iter_file(ino, stored=True), inner)
        if outer.signed and not outer.checked:
            raise PkgError("no block signatures were checked")
    finally:
        outer.close()
    log("copying the game files")
    try:
        n = pfs.extract(inner, out)
    finally:
        inner.unlink(missing_ok=True)
    sce = Path(out) / "sce_sys"
    sce.mkdir(parents=True, exist_ok=True)
    (sce / "param.sfo").write_bytes(read_entry(path, PARAM_SFO))
    return n + 1
