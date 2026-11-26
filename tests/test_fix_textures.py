import struct
import tempfile
import unittest
from pathlib import Path

from pipeline import fix_textures as ft


def _str(s: str) -> bytes:
    b = s.encode()
    out = struct.pack("<i", len(b)) + b
    return out + b"\0" * (-len(out) % 4)


def texture_blob(name, w, h, fmt=12, mips=11, usage=0, color_space=1, filt=1, aniso=1, wrap=0):
    b = _str(name)
    b += struct.pack("<5i", w, h, w * h, fmt, mips)
    b += b"\0\0\0\0"  # readable, readAllowed, align
    b += struct.pack("<iiiifiii", 1, 2, filt, aniso, 0.0, wrap, usage, color_space)
    b += struct.pack("<i", 0)  # inline image data
    b += struct.pack("<II", 0, w * h) + _str("x.resS")
    return b


def serialized_file(objects):
    """objects: list of (path_id, class_id, payload). Builds a Unity 5.x (format 15) little-endian file."""
    meta = b"5.3.6p6\0" + struct.pack("<i", 31) + b"\0"
    classes = sorted({c for _p, c, _b in objects})
    meta += struct.pack("<i", len(classes))
    for c in classes:
        meta += struct.pack("<i", c) + b"\0" * 16
    meta += struct.pack("<i", len(objects))
    entries, data = [], b""
    for pid, cid, payload in objects:
        entries.append((pid, len(data), len(payload), classes.index(cid), cid))
        data += payload + b"\0" * (-len(payload) % 8)
    for pid, start, size, tidx, cid in entries:
        meta += b"\0" * (-(20 + len(meta)) % 4)
        meta += struct.pack("<qIIiHHB", pid, start, size, tidx, cid, 0, 0)
    data_off = 20 + len(meta)
    data_off += -data_off % 16
    header = struct.pack(">IIII", len(meta), data_off + len(data), 15, data_off) + b"\0\0\0\0"
    body = header + meta
    return body + b"\0" * (data_off - len(body)) + data


def png_header(w, h):
    return b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)


META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 1
    linearTexture: 0
  bumpmap:
    convertToNormalMap: 0
  textureSettings:
    serializedVersion: 2
    filterMode: 1
  spriteMode: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  textureType: 8
  textureShape: 1
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
"""


class ParseTests(unittest.TestCase):
    def test_reads_texture_fields_and_skips_other_classes(self):
        data = serialized_file([
            (1, 21, b"\x01\x02\x03\x04"),
            (2, 28, texture_blob("Wall_Normal", 1024, 512, usage=3, color_space=0)),
            (3, 28, texture_blob("Wall_Albedo", 256, 256, fmt=10, usage=0, color_space=1, wrap=1)),
        ])
        infos = ft.parse_textures(data)
        self.assertEqual([i.name for i in infos], ["Wall_Normal", "Wall_Albedo"])
        n, a = infos
        self.assertEqual((n.width, n.height, n.usage, n.color_space), (1024, 512, 3, 0))
        self.assertEqual((a.fmt, a.wrap, a.color_space), (10, 1, 1))

    def test_rejects_unknown_format_version(self):
        data = bytearray(serialized_file([]))
        data[8:12] = struct.pack(">I", 22)
        with self.assertRaises(ValueError):
            ft.parse_textures(bytes(data))


class DecideTests(unittest.TestCase):
    def info(self, usage, cs, name="T", w=4, h=4):
        return ft.TexInfo(name=name, width=w, height=h, fmt=12, mips=3, filter=1, aniso=1, wrap=0,
                          usage=usage, color_space=cs)

    def test_normal_map(self):
        self.assertEqual(ft.desired([self.info(3, 0)], is_sprite=False),
                         {"textureType": 1, "sRGBTexture": 0, "alphaIsTransparency": 0})

    def test_linear_data_texture(self):
        self.assertEqual(ft.desired([self.info(0, 0)], is_sprite=False),
                         {"textureType": 0, "sRGBTexture": 0, "alphaIsTransparency": 0})

    def test_color_texture(self):
        self.assertEqual(ft.desired([self.info(0, 1)], is_sprite=False),
                         {"textureType": 0, "sRGBTexture": 1, "alphaIsTransparency": 0})

    def test_sprite_keeps_sprite_import(self):
        self.assertEqual(ft.desired([self.info(6, 1)], is_sprite=True), {"sRGBTexture": 1})

    def test_disagreeing_candidates_are_ambiguous(self):
        self.assertIsNone(ft.desired([self.info(3, 0), self.info(0, 1)], is_sprite=False))

    def test_agreeing_candidates(self):
        self.assertIsNotNone(ft.desired([self.info(0, 0), self.info(0, 0)], is_sprite=False))


class MatchTests(unittest.TestCase):
    def setUp(self):
        mk = lambda n, w: ft.TexInfo(n, w, w, 10, 1, 1, 1, 0, 0, 1)
        self.by_name = {"Badge_D": [mk("Badge_D", 1024), mk("Badge_D", 256)], "Font_0": [mk("Font_0", 64)]}

    def test_exact_name_and_size(self):
        self.assertEqual([i.width for i in ft.candidates("Badge_D", (256, 256), self.by_name)], [256])

    def test_dedup_suffix(self):
        self.assertEqual([i.width for i in ft.candidates("Badge_D_3", (1024, 1024), self.by_name)], [1024])

    def test_name_ending_in_digits_matches_exactly(self):
        self.assertEqual(len(ft.candidates("Font_0", (64, 64), self.by_name)), 1)

    def test_size_mismatch(self):
        self.assertEqual(ft.candidates("Badge_D", (512, 512), self.by_name), [])


class PatchMetaTests(unittest.TestCase):
    def test_sets_only_named_keys(self):
        text = META.format(guid="ab")
        out = ft.patch_meta(text, {"textureType": 1, "sRGBTexture": 0, "alphaIsTransparency": 0})
        self.assertIn("\n  textureType: 1\n", out)
        self.assertIn("\n    sRGBTexture: 0\n", out)
        self.assertIn("\n  alphaIsTransparency: 0\n", out)
        self.assertIn("\n  spriteMode: 1\n", out)
        self.assertEqual(len(out.splitlines()), len(text.splitlines()))

    def test_idempotent(self):
        s = {"textureType": 0, "sRGBTexture": 0, "alphaIsTransparency": 0}
        once = ft.patch_meta(META.format(guid="ab"), s)
        self.assertEqual(ft.patch_meta(once, s), once)

    def test_missing_key_raises(self):
        with self.assertRaises(KeyError):
            ft.patch_meta("TextureImporter:\n  textureType: 8\n", {"sRGBTexture": 0})


class RunTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = Path(self.tmp.name)
        self.media, self.project = r / "Media", r / "project"
        self.media.mkdir()
        (self.media / "sharedassets0.assets").write_bytes(serialized_file([
            (1, 28, texture_blob("Rock_Normal", 8, 8, usage=3, color_space=0)),
            (2, 28, texture_blob("Rock_Metallic", 8, 8, usage=0, color_space=0)),
            (3, 28, texture_blob("Flag", 8, 4, usage=6, color_space=1)),
            (4, 28, texture_blob("Odd", 4, 4, usage=3, color_space=0)),
            (5, 28, texture_blob("Odd", 4, 4, usage=0, color_space=1)),
        ]))
        (self.media / "sharedassets0.assets.resS").write_bytes(b"\0" * 64)
        (self.media / "Managed").mkdir()
        self.tex = self.project / "Assets/Texture2D"
        self.tex.mkdir(parents=True)
        for stem, size, guid in [("Rock_Normal", (8, 8), "a1"), ("Rock_Metallic_0", (8, 8), "a2"),
                                 ("Flag", (8, 4), "a3"), ("Odd", (4, 4), "a4"), ("Gone", (2, 2), "a5")]:
            (self.tex / f"{stem}.png").write_bytes(png_header(*size))
            (self.tex / f"{stem}.png.meta").write_text(META.format(guid=guid))
        sprites = self.project / "Assets/Sprite"
        sprites.mkdir(parents=True)
        (sprites / "Flag.asset").write_text(
            "Sprite:\n  m_RD:\n    texture: {fileID: 2800000, guid: a3, type: 3}\n")

    def tearDown(self):
        self.tmp.cleanup()

    def meta(self, stem):
        return (self.tex / f"{stem}.png.meta").read_text()

    def test_run_rewrites_metas_and_reports(self):
        report = ft.run(self.media, self.project)
        self.assertIn("\n  textureType: 1\n", self.meta("Rock_Normal"))
        self.assertIn("\n    sRGBTexture: 0\n", self.meta("Rock_Normal"))
        self.assertIn("\n    sRGBTexture: 0\n", self.meta("Rock_Metallic_0"))
        self.assertIn("\n  textureType: 0\n", self.meta("Rock_Metallic_0"))
        self.assertIn("\n  textureType: 8\n", self.meta("Flag"))
        self.assertEqual(self.meta("Odd"), META.format(guid="a4"))
        self.assertEqual(report["changed"], 2)
        self.assertEqual(report["normal_maps"], 1)
        self.assertEqual(report["ambiguous"], ["Odd.png"])
        self.assertEqual(report["unmatched"], ["Gone.png"])

    def test_old_format_meta_is_reported_not_touched(self):
        old = "fileFormatVersion: 2\nguid: a1\nTextureImporter:\n  mipmaps:\n    linearTexture: 0\n  textureType: 1\n"
        (self.tex / "Rock_Normal.png.meta").write_text(old)
        report = ft.run(self.media, self.project)
        self.assertEqual(report["old_format"], ["Rock_Normal.png"])
        self.assertEqual(self.meta("Rock_Normal"), old)

    def test_dry_run_writes_nothing(self):
        report = ft.run(self.media, self.project, dry_run=True)
        self.assertEqual(report["changed"], 2)
        self.assertEqual(self.meta("Rock_Normal"), META.format(guid="a1"))

    def test_second_run_changes_nothing(self):
        ft.run(self.media, self.project)
        self.assertEqual(ft.run(self.media, self.project)["changed"], 0)


if __name__ == "__main__":
    unittest.main()
