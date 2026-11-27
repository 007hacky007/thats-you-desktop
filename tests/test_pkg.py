import struct
import tempfile
import unittest
import zlib
from pathlib import Path

from pipeline import pfs, pkg

BS = 0x1000


def dirent(ino: int, typ: int, name: str) -> bytes:
    n = name.encode()
    size = (16 + len(n) + 1 + 7) // 8 * 8
    return struct.pack("<IiiI", ino, typ, len(n), size) + n + bytes(size - 16 - len(n))


def plain_image(files: dict[str, bytes], scatter: bool = False) -> bytes:
    """Plain PFS image: super root (inode 0) -> uroot (1) -> sub (2) and files from inode 3.
    files maps "name" or "sub/name" to content. scatter stores the first file's blocks in
    reverse order through direct pointers, like a fragmented image."""
    inodes = {}
    blocks = {}
    nxt = [2]  # block 0 header, 1 inode table

    def alloc(data: bytes, reverse=False):
        n = max(1, -(-len(data) // BS))
        nums = list(range(nxt[0], nxt[0] + n))
        nxt[0] += n
        order = list(reversed(nums)) if reverse else nums
        for i, b in enumerate(order):
            blocks[b] = data[i * BS:(i + 1) * BS].ljust(BS, b"\0")
        return order

    root_files = {k: v for k, v in files.items() if "/" not in k}
    sub_files = {k.split("/", 1)[1]: v for k, v in files.items() if k.startswith("sub/")}
    ino = 3
    root_ents, sub_ents = [], []
    first = True
    for group, ents in ((root_files, root_ents), (sub_files, sub_ents)):
        for name, data in group.items():
            order = alloc(data, reverse=scatter and first)
            if scatter and first:
                db = order + [-1] * (12 - len(order))
            else:
                db = [order[0]] + [-1] * 11
            inodes[ino] = (len(data), len(order), db)
            ents.append(dirent(ino, 2, name))
            ino += 1
            first = False
    sub = alloc(b"".join(sub_ents))
    inodes[2] = (BS, 1, [sub[0]] + [-1] * 11)
    ur = alloc(b"".join(root_ents) + dirent(2, 3, "sub"))
    inodes[1] = (BS, 1, [ur[0]] + [-1] * 11)
    sr = alloc(dirent(1, 3, "uroot"))
    inodes[0] = (BS, 1, [sr[0]] + [-1] * 11)
    table = bytearray(BS)
    for i in range(ino):
        size, nb, db = inodes[i]
        o = i * 0xA8
        struct.pack_into("<HHIqq", table, o, 0, 1, 0, size, size)
        struct.pack_into("<I", table, o + 0x60, nb)
        struct.pack_into("<12i", table, o + 0x64, *db)
        struct.pack_into("<5i", table, o + 0x94, *([-1] * 5))
    head = bytearray(BS)
    struct.pack_into("<qqqBBBBHHIIqqqq", head, 0, 1, pfs.MAGIC, 0, 0, 0, 0, 0, 0, 0, BS, 0,
                     nxt[0], ino, 0, 1)
    out = bytearray(head) + table
    for b in range(2, nxt[0]):
        out += blocks[b]
    return bytes(out)


def pfsc(data: bytes, bs: int = BS) -> bytes:
    n = -(-len(data) // bs)
    table_off = 0x30
    start = table_off + 8 * (n + 1)
    offs, body = [], b""
    for i in range(n):
        block = data[i * bs:(i + 1) * bs].ljust(bs, b"\0")
        c = zlib.compress(block)
        stored = c if len(c) < bs else block
        offs.append(start + len(body))
        body += stored
    offs.append(start + len(body))
    head = struct.pack("<4siiiqqQq", b"PFSC", 0, 2, bs, bs, table_off, start, len(data))
    return head + struct.pack(f"<{n + 1}q", *offs) + body


class PfsTest(unittest.TestCase):
    def test_extract_plain_image_with_subdir_and_fragmented_file(self):
        files = {"big.bin": bytes(range(256)) * 40, "a.txt": b"hello", "sub/b.txt": b"world" * 1000}
        with tempfile.TemporaryDirectory() as d:
            img = Path(d) / "x.img"
            img.write_bytes(plain_image(files, scatter=True))
            self.assertEqual(pfs.extract(img, Path(d) / "out"), 3)
            for k, v in files.items():
                self.assertEqual((Path(d) / "out" / k).read_bytes(), v, k)

    def test_unsafe_names_refused(self):
        for bad in ("..", "a/b", "c:d", "x.", ""):
            with self.assertRaises(pfs.PfsError):
                pfs._safe_name(bad)

    def test_pfsc_round_trip_and_damage(self):
        data = bytes(range(256)) * 50 + b"\x01" * 3000
        with tempfile.TemporaryDirectory() as d:
            out = Path(d) / "o"
            blob = pfsc(data)
            self.assertEqual(pfs.decompress_pfsc([blob[:100], blob[100:]], out), len(data))
            self.assertEqual(out.read_bytes(), data)
            bad = bytearray(blob)
            bad[-5] ^= 0xFF  # inside the last compressed block: Adler-32 must catch it
            with self.assertRaises(pfs.PfsError):
                pfs.decompress_pfsc([bytes(bad)], out)


class PkgTest(unittest.TestCase):
    def test_ekpfs_is_deterministic_and_checks_lengths(self):
        cid = "EP9000-CUSA08389_00-1705161243TYM1E2"
        a = pkg.ekpfs_from_passcode(cid)
        self.assertEqual(len(a), 32)
        self.assertEqual(a, pkg.ekpfs_from_passcode(cid, "0" * 32))
        self.assertNotEqual(a, pkg.ekpfs_from_passcode(cid, "1" * 32))
        with self.assertRaises(pkg.PkgError):
            pkg.ekpfs_from_passcode("short")

    def test_keys_split(self):
        t, d, s = pkg.pfs_keys(b"k" * 32, b"s" * 16, False)
        self.assertEqual((len(t), len(d), len(s)), (16, 16, 32))
        self.assertNotEqual(pkg.pfs_keys(b"k" * 32, b"s" * 16, True)[0], t)

    def test_not_a_package(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "x.pkg"
            p.write_bytes(b"nope" * 400)
            with self.assertRaises(pkg.PkgError):
                pkg.read_header(p)


if __name__ == "__main__":
    unittest.main()


class PfscLimitsTest(unittest.TestCase):
    def test_block_that_inflates_past_the_block_size_is_refused(self):
        bs = BS
        bomb = zlib.compress(bytes(bs * 64))  # tiny, but inflates to 64 blocks
        start = 0x30 + 16
        blob = struct.pack("<4siiiqqQq", b"PFSC", 0, 2, bs, bs, 0x30, start, bs) + \
            struct.pack("<2q", start, start + len(bomb)) + bomb
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(pfs.PfsError):
                pfs.decompress_pfsc([blob], Path(d) / "o")

    def test_absurd_table_or_block_sizes_are_refused_before_buffering(self):
        bs = BS
        far = struct.pack("<4siiiqqQq", b"PFSC", 0, 2, bs, bs, 1 << 40, 0, bs)
        huge_block = struct.pack("<4siiiqqQq", b"PFSC", 0, 2, bs, bs, 0x30, 0x40, bs) + struct.pack("<2q", 0x40, 0x40 + bs * 1000)
        with tempfile.TemporaryDirectory() as d:
            for blob in (far, huge_block):
                with self.assertRaises(pfs.PfsError):
                    pfs.decompress_pfsc([blob], Path(d) / "o")
