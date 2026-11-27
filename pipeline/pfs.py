"""Read PS4 PFS file system images and copy their files out.

Used for game packages (see pipeline/pkg.py): the package holds an encrypted, signed "outer"
image whose only file is a compressed (PFSC) "inner" image that holds the game files.

Layout: block 0 holds the header (block size, inode count, inode block count, mode flags, and at
0x370 the 16-byte seed for the keys). The inode table starts at block 1: 0xA8 bytes per inode in
plain images, 0x2C8 bytes in signed ones, where each block pointer carries a 32-byte HMAC-SHA256
of the block. Directories are blocks of entries (inode u32, type u32, name length u32, entry size
u32, name). Plain images: a file whose second direct block is -1 occupies consecutive blocks;
otherwise its blocks are 12 direct pointers followed by single/double/triple indirect blocks.
The game root is the directory "uroot" inside the super root (inode 0).
"""
from __future__ import annotations

import hashlib
import hmac
import struct
import zlib
from pathlib import Path

MAGIC = 20130315
MODE_SIGNED = 0x1
FLAG_COMPRESSED = 0x1
T_FILE, T_DIR = 2, 3
CHUNK = 1 << 22
PFSC_MAGIC = b"PFSC"


class PfsError(Exception):
    pass


class FileSource:
    """Plain image file."""

    def __init__(self, path: Path):
        self.f = open(path, "rb")

    def read(self, offset: int, n: int) -> bytes:
        self.f.seek(offset)
        return self.f.read(n)

    def close(self):
        self.f.close()


class Image:
    """A PFS image on a source with read(offset, n). sign_key enables block signature checks."""

    def __init__(self, source, sign_key: bytes | None = None):
        self.src = source
        head = source.read(0, 0x400)
        if len(head) < 0x380:
            raise PfsError("not a PFS image (too short)")
        (ver, magic, _id, _fm, _cl, _ro, _rsv, self.mode, _u, self.bs, _nb, _nblock,
         self.ninodes, _ndblock, self.inode_blocks) = struct.unpack_from("<qqqBBBBHHIIqqqq", head)
        if magic != MAGIC or ver != 1:
            raise PfsError("not a PFS image (bad magic)")
        self.seed = head[0x370:0x380]
        self.signed = bool(self.mode & MODE_SIGNED)
        self.sign_key = sign_key if self.signed else None
        self.checked = 0
        size = 0x2C8 if self.signed else 0xA8
        per_block = self.bs // size
        self.inodes = []
        for b in range(self.inode_blocks):
            data = source.read(self.bs * (1 + b), self.bs)
            for j in range(per_block):
                if len(self.inodes) >= self.ninodes:
                    break
                self.inodes.append(self._parse_inode(data, j * size))

    def _parse_inode(self, data: bytes, o: int):
        flags, size, csize = struct.unpack_from("<Iqq", data, o + 4)
        blocks = struct.unpack_from("<I", data, o + 0x60)[0]
        if self.signed:
            ptr = [(data[o + 0x64 + 36 * i:o + 0x64 + 36 * i + 32],
                    struct.unpack_from("<i", data, o + 0x64 + 36 * i + 32)[0]) for i in range(17)]
        else:
            ptr = [(None, v) for v in struct.unpack_from("<17i", data, o + 0x64)]
        return flags, size, csize, blocks, ptr[:12], ptr[12:]

    def close(self):
        self.src.close()

    def _inode(self, n: int):
        if not 0 <= n < len(self.inodes):
            raise PfsError(f"inode {n} out of range")
        return self.inodes[n]

    def _block(self, n: int, sig: bytes | None) -> bytes:
        data = self.src.read(self.bs * n, self.bs)
        if len(data) != self.bs:
            raise PfsError(f"block {n}: image ends early")
        if self.sign_key and sig is not None:
            if not hmac.compare_digest(hmac.new(self.sign_key, data, hashlib.sha256).digest(), sig):
                raise PfsError(f"block {n}: signature does not match (wrong key or damaged package)")
            self.checked += 1
        return data

    def _pointers(self, block: int, sig, depth: int, want: int) -> list:
        data = self._block(block, sig)
        if self.signed:
            ptrs = [(data[i:i + 32], struct.unpack_from("<i", data, i + 32)[0])
                    for i in range(0, self.bs - 35, 36)]
        else:
            ptrs = [(None, v) for v in struct.unpack(f"<{self.bs // 4}i", data)]
        if depth == 0:
            return ptrs[:want]
        out: list = []
        for s, p in ptrs:
            if len(out) >= want or p < 1:
                break
            out += self._pointers(p, s, depth - 1, want - len(out))
        return out

    def block_list(self, ino: int) -> list:
        """[(signature or None, block number)] of an inode, in file order."""
        _fl, _sz, _cs, blocks, db, ib = self._inode(ino)
        if blocks == 0:
            return []
        if db[0][1] < 1:
            raise PfsError(f"inode {ino} is corrupt")
        if not self.signed and (blocks == 1 or db[1][1] == -1):
            return [(None, b) for b in range(db[0][1], db[0][1] + blocks)]
        out = list(db[:min(12, blocks)])
        for depth, (sig, top) in enumerate(ib[:3]):
            if len(out) >= blocks:
                break
            if top < 1:
                raise PfsError(f"inode {ino}: missing indirect block")
            out += self._pointers(top, sig, depth, blocks - len(out))
        if len(out) != blocks or min(b for _s, b in out) < 1:
            raise PfsError(f"inode {ino}: block list does not match its size")
        return out

    def entries(self, ino: int):
        for sig, x in self.block_list(ino):
            data = self._block(x, sig)
            pos = 0
            while pos + 16 <= len(data):
                child, typ, nlen, esize = struct.unpack_from("<IiiI", data, pos)
                if esize == 0:
                    break
                name = data[pos + 16:pos + 16 + nlen].decode("utf-8")
                if typ in (T_FILE, T_DIR):
                    yield name, typ, child
                pos += esize

    def file_size(self, ino: int) -> int:
        return self._inode(ino)[1]

    def iter_file(self, ino: int, stored: bool = False):
        """The file's bytes in chunks (signature-checked per block when signed). A compressed
        file is refused unless stored=True, which yields its stored (PFSC) bytes."""
        flags, size, csize, blocks, *_ = self._inode(ino)
        if flags & FLAG_COMPRESSED and size != csize:
            if not stored:
                raise PfsError(f"inode {ino}: compressed files are not supported")
            # which of the two size fields holds the stored length differs between packages:
            # pass on every block and let the container's own offset table decide
            size = blocks * self.bs
        left = size
        if self.signed:
            for sig, b in self.block_list(ino):
                if not left:
                    break
                data = self._block(b, sig)
                yield data[:left]
                left -= min(left, self.bs)
        else:
            runs: list[list[int]] = []  # [first block, count] of consecutive blocks
            for _s, b in self.block_list(ino):
                if runs and runs[-1][0] + runs[-1][1] == b:
                    runs[-1][1] += 1
                else:
                    runs.append([b, 1])
            for first, count in runs:
                pos, part = self.bs * first, min(left, count * self.bs)
                while part:
                    buf = self.src.read(pos, min(CHUNK, part))
                    if not buf:
                        raise PfsError(f"inode {ino}: image ends early")
                    yield buf
                    pos += len(buf)
                    part -= len(buf)
                    left -= len(buf)
                if not left:
                    break
        if left:
            raise PfsError(f"inode {ino}: blocks do not cover its size")

    def find(self, *path: str) -> tuple[int, int]:
        """(inode, type) of a path below the super root, for example find("uroot", "eboot.bin")."""
        ino, typ = 0, T_DIR
        for name in path:
            if typ != T_DIR:
                raise PfsError(f"not a directory on the way to {'/'.join(path)}")
            hit = next(((c, t) for n, t, c in self.entries(ino) if n == name), None)
            if hit is None:
                raise PfsError(f"{'/'.join(path)} not found in the image")
            ino, typ = hit
        return ino, typ


def _safe_name(name: str) -> str:
    if (not name or name in (".", "..") or any(c in name for c in '/\\\0:<>"|?*')
            or name != name.rstrip(" .")):
        raise PfsError(f"unsafe file name in image: {name!r}")
    return name


def extract_image(img: Image, out: Path, progress=None) -> int:
    """Copy every file under uroot into out. Returns the number of files."""
    uroot, typ = img.find("uroot")
    if typ != T_DIR:
        raise PfsError("invalid PFS image (uroot is not a directory)")
    root = Path(out).resolve()
    count = 0
    stack = [(uroot, Path(out))]
    seen = set()
    while stack:
        ino, dst = stack.pop()
        if ino in seen:
            raise PfsError("directory loop in image")
        seen.add(ino)
        dst.mkdir(parents=True, exist_ok=True)
        for name, typ, child in img.entries(ino):
            target = dst / _safe_name(name)
            if not target.resolve().is_relative_to(root):
                raise PfsError(f"file outside the output folder: {name!r}")
            if typ == T_DIR:
                stack.append((child, target))
            else:
                with open(target, "wb") as f:
                    for chunk in img.iter_file(child):
                        f.write(chunk)
                count += 1
                if progress:
                    progress(count, target)
    return count


def extract(image: Path, out: Path, progress=None) -> int:
    img = Image(FileSource(image))
    try:
        return extract_image(img, out, progress)
    finally:
        img.close()


def decompress_pfsc(chunks, dst: Path, progress=None) -> int:
    """Expand a PFSC container, given as an iterable of byte chunks, into a plain image file.

    Header: magic, two unknown int32, block size (int32 and int64), offset of the block offset
    table, data start, uncompressed length. The table has one int64 offset per block plus an end
    offset. A block stored at full size is raw; a shorter one is a zlib stream (its Adler-32
    checksum is verified); a longer one stands for a block of zeros. Returns the image size.
    """
    buf = bytearray()
    pos = 0  # file offset of buf[0]
    it = iter(chunks)

    def need(end: int) -> None:
        while pos + len(buf) < end:
            c = next(it, None)
            if c is None:
                raise PfsError("PFSC container ends early")
            buf.extend(c)

    need(0x30)
    magic, _u4, _u8, bs, bs2, table, _start, length = struct.unpack_from("<4siiiqqQq", buf, 0)
    if magic != PFSC_MAGIC or bs != bs2 or bs <= 0:
        raise PfsError("not a PFSC container")
    n = -(-length // bs)
    need(table + 8 * (n + 1))
    offs = struct.unpack_from(f"<{n + 1}q", buf, table)
    left = length
    with open(dst, "wb") as out:
        for i in range(n):
            start, end = offs[i], offs[i + 1]
            if start < pos:
                raise PfsError("PFSC blocks out of order")
            need(end)
            raw = bytes(buf[start - pos:end - pos])
            size = end - start
            if size == bs:
                block = raw
            elif size > bs:
                block = bytes(bs)
            else:
                try:
                    block = zlib.decompress(raw, 15, bs)
                except zlib.error as e:
                    raise PfsError(f"PFSC block {i} is damaged ({e})") from None
            want = min(bs, left)
            if len(block) < want:
                raise PfsError(f"PFSC block {i} decompressed to {len(block)} bytes")
            out.write(block[:want])
            left -= want
            del buf[:end - pos]
            pos = end
            if progress and i % 1024 == 0:
                progress(i, n)
    return length
