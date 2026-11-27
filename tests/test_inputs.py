import hashlib
import struct
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from pipeline import inputs

CID = "EP9000-CUSA08389_00-1705161243TYM1E2"


def make_sfo(entries: dict) -> bytes:
    """Minimal param.sfo: strings as utf-8 (fmt 0x0204), ints as fmt 0x0404."""
    keys, data, index = b"", b"", b""
    for k, v in entries.items():
        if isinstance(v, int):
            raw, fmt = struct.pack("<I", v), 0x0404
        else:
            raw, fmt = v.encode() + b"\0", 0x0204
        index += struct.pack("<HHIII", len(keys), fmt, len(raw), len(raw), len(data))
        keys += k.encode() + b"\0"
        data += raw
    kt = 20 + len(index)
    while (kt + len(keys)) % 4:
        keys += b"\0"
    dt = kt + len(keys)
    return struct.pack("<4sIIII", b"\0PSF", 0x101, kt, dt, len(entries)) + index + keys + data


def make_dump(root: Path, role: str, content_id: str = CID, version: str = "01.00") -> Path:
    root.mkdir(parents=True)
    (root / "sce_sys").mkdir()
    cat = "gd" if role == "base" else "gp"
    (root / "sce_sys" / "param.sfo").write_bytes(make_sfo({
        "APP_VER": version, "CATEGORY": cat, "CONTENT_ID": content_id,
        "TITLE_ID": content_id[7:16], "VERSION": "01.00"}))
    if role == "base":
        (root / "eboot.bin").write_bytes(b"x")
    else:
        (root / "sce_sys" / "changeinfo").mkdir()
    return root


def make_pkg(path: Path, content_id: str = CID, category: str = "gd", version: str = "01.00") -> Path:
    """Minimal package: header with content ID, one-entry table holding param.sfo."""
    sfo = make_sfo({"APP_VER": version, "CATEGORY": category, "CONTENT_ID": content_id,
                    "TITLE_ID": content_id[7:16], "VERSION": "01.00"})
    head = bytearray(0x3000)
    head[0:4] = b"\x7fCNT"
    struct.pack_into(">I", head, 0x10, 1)
    struct.pack_into(">I", head, 0x18, 0x1000)
    head[0x40:0x40 + len(content_id)] = content_id.encode()
    struct.pack_into(">IIIIIIQ", head, 0x1000, 0x1000, 0, 0, 0, 0x2000, len(sfo), 0)
    head[0x2000:0x2000 + len(sfo)] = sfo
    path.write_bytes(bytes(head))
    return path


class KindTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def test_folder_pkg_and_junk(self):
        self.assertEqual(inputs.kind(make_dump(self.root / "b", "base")), "folder")
        self.assertEqual(inputs.kind(make_pkg(self.root / "g.pkg")), "pkg")
        junk = self.root / "x.bin"
        junk.write_bytes(b"hello")
        with self.assertRaises(inputs.InputError):
            inputs.kind(junk)
        with self.assertRaises(inputs.InputError):
            inputs.kind(self.root / "missing")

    def test_read_sfo(self):
        d = make_dump(self.root / "b", "base")
        sfo = inputs.read_sfo(d / "sce_sys" / "param.sfo")
        self.assertEqual(sfo["TITLE_ID"], "CUSA08389")
        self.assertEqual(sfo["CONTENT_ID"], CID)


class CheckGameTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def test_supported_base_and_update(self):
        b = inputs.check_game(make_dump(self.root / "b", "base"), "base")
        self.assertTrue(b.supported)
        self.assertEqual((b.title, b.version, b.kind), ("CUSA08389", "01.00", "folder"))
        u = inputs.check_game(make_dump(self.root / "u", "update", version="01.05"), "update")
        self.assertTrue(u.supported)

    def test_other_region_or_version_is_unsupported_but_accepted(self):
        us = "UP9000-CUSA06348_00-THATSYOU00000000"
        self.assertFalse(inputs.check_game(make_dump(self.root / "b", "base", us), "base").supported)
        self.assertFalse(inputs.check_game(make_dump(self.root / "u", "update", version="01.03"), "update").supported)

    def test_wrong_title_and_wrong_role_refused(self):
        other = "EP9000-CUSA00001_00-SOMETHINGELSE000"
        with self.assertRaises(inputs.InputError):
            inputs.check_game(make_dump(self.root / "o", "base", other), "base")
        with self.assertRaises(inputs.InputError):
            inputs.check_game(make_dump(self.root / "b", "base"), "update")

    def test_pkg_title_from_header(self):
        g = inputs.check_game(make_pkg(self.root / "g.pkg"), "base")
        self.assertEqual((g.title, g.content_id, g.kind, g.supported), ("CUSA08389", CID, "pkg", True))
        u = inputs.check_game(make_pkg(self.root / "u.pkg", category="gp", version="01.05"), "update")
        self.assertTrue(u.supported)
        with self.assertRaises(inputs.InputError):
            inputs.check_game(make_pkg(self.root / "u2.pkg", category="gp"), "base")
        with self.assertRaises(inputs.InputError):
            inputs.check_game(make_pkg(self.root / "o.pkg", "EP9000-CUSA00001_00-SOMETHINGELSE000"), "base")


class ApkTest(unittest.TestCase):
    def test_hash_check(self):
        with tempfile.TemporaryDirectory() as d:
            apk = Path(d) / "a.apk"
            apk.write_bytes(b"PK fake")
            with mock.patch.object(inputs, "APK_SHA256", hashlib.sha256(b"PK fake").hexdigest()):
                inputs.check_apk(apk)
            with self.assertRaises(inputs.InputError) as cm:
                inputs.check_apk(apk)
            self.assertIn("1.6", str(cm.exception))

    def test_search_url(self):
        self.assertIn("apkmirror.com", inputs.APK_SEARCH_URL)


class UnpackTest(unittest.TestCase):
    def test_unpack_runs_once_and_marks_done(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            pkg = make_pkg(root / "g.pkg")
            calls = []

            def fake_unpack(path, out, scratch, log=print):
                calls.append(path)
                Path(out).mkdir(parents=True)
                (Path(out) / "eboot.bin").write_bytes(b"x")
                return 1

            res = inputs.unpack_pkg(pkg, root / "out", root / "scratch", unpack=fake_unpack)
            self.assertTrue((res / "eboot.bin").is_file())
            inputs.unpack_pkg(pkg, root / "out", root / "scratch", unpack=fake_unpack)
            self.assertEqual(len(calls), 1)

    def test_failed_unpack_leaves_no_done_mark(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            pkg = make_pkg(root / "g.pkg")

            def broken(path, out, scratch, log=print):
                Path(out).mkdir(parents=True)
                raise RuntimeError("damaged")

            with self.assertRaises(RuntimeError):
                inputs.unpack_pkg(pkg, root / "out", root / "scratch", unpack=broken)
            self.assertFalse((root / "out" / inputs.DONE).exists())


if __name__ == "__main__":
    unittest.main()
