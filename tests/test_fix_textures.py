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


def _platform(target, compression, overridden, size=2048):
    return (f"  - serializedVersion: 4\n    buildTarget: {target}\n    maxTextureSize: {size}\n"
            f"    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: {compression}\n"
            f"    compressionQuality: 50\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n"
            f"    overridden: {overridden}\n    ignorePlatformSupport: 0\n")


def upgraded_meta(guid="ab", legacy_format=3, first_compression=0):
    """Unity 6 upgraded meta with the duplicate overridden DefaultTexturePlatform entry."""
    head = META.split("  platformSettings:\n")[0].replace("  textureType: 8\n", f"  textureType: 8\n  textureFormat: {legacy_format}\n")
    return (head.format(guid=guid) + "  platformSettings:\n"
            + _platform("DefaultTexturePlatform", first_compression, 0)
            + _platform("DefaultTexturePlatform", 1, 1)
            + _platform("Standalone", first_compression, 0)
            + _platform("WebGL", first_compression, 0)
            + "  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n")


def platform_entries(text):
    import re
    block = text.split("  platformSettings:\n")[1].split("  spriteSheet:")[0]
    return [(re.search(r"buildTarget: (\S+)", e).group(1), int(re.search(r"textureCompression: (\d+)", e).group(1)),
             int(re.search(r"overridden: (\d+)", e).group(1)))
            for e in block.split("  - ")[1:]]


class CompressionTests(unittest.TestCase):
    def info(self, fmt):
        return ft.TexInfo("T", 4, 4, fmt, 1, 1, 1, 0, 0, 1)

    def test_uncompressed_originals_stay_uncompressed(self):
        for fmt in (1, 2, 3, 4, 5, 7, 13, 14, 17, 18, 20):
            self.assertEqual(ft.compression_for([self.info(fmt)], None), 0, fmt)

    def test_block_compressed_originals_stay_compressed(self):
        for fmt in (10, 12, 24, 25, 26, 27, 28, 29):
            self.assertEqual(ft.compression_for([self.info(fmt)], None), 1, fmt)

    def test_mixed_candidates_use_legacy_format(self):
        both = [self.info(1), self.info(12)]
        self.assertEqual(ft.compression_for(both, 1), 0)
        self.assertEqual(ft.compression_for(both, 12), 1)
        self.assertIsNone(ft.compression_for(both, 3))
        self.assertIsNone(ft.compression_for(both, None))

    def test_unmatched_falls_back_to_legacy_format(self):
        self.assertEqual(ft.compression_for([], 3), 0)
        self.assertEqual(ft.compression_for([], 10), 1)
        self.assertIsNone(ft.compression_for([], -1))
        self.assertIsNone(ft.compression_for([], None))


class NormalizePlatformTests(unittest.TestCase):
    def test_drops_duplicate_default_and_sets_uncompressed(self):
        out = ft.normalize_platforms(upgraded_meta(), 0)
        self.assertEqual(platform_entries(out),
                         [("DefaultTexturePlatform", 0, 0), ("Standalone", 0, 0), ("WebGL", 0, 0)])
        self.assertEqual(out.count("buildTarget: DefaultTexturePlatform"), 1)
        self.assertTrue(out.endswith("  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n"))

    def test_compressed_choice(self):
        out = ft.normalize_platforms(upgraded_meta(legacy_format=10, first_compression=1), 1)
        self.assertEqual(platform_entries(out)[0], ("DefaultTexturePlatform", 1, 0))
        self.assertEqual(len(platform_entries(out)), 3)

    def test_first_entry_corrected_when_upgrade_chose_wrong(self):
        out = ft.normalize_platforms(upgraded_meta(first_compression=1), 0)
        self.assertEqual(platform_entries(out)[0], ("DefaultTexturePlatform", 0, 0))

    def test_overridden_platforms_are_cleared(self):
        text = upgraded_meta().replace(_platform("WebGL", 0, 0), _platform("WebGL", 1, 1))
        self.assertEqual(platform_entries(ft.normalize_platforms(text, 0))[-1], ("WebGL", 1, 0))

    def test_inserts_missing_compression_only_when_uncompressed(self):
        text = META.format(guid="ab")
        self.assertEqual(ft.normalize_platforms(text, 1), text)
        self.assertIn("    textureCompression: 0\n", ft.normalize_platforms(text, 0))

    def test_idempotent(self):
        once = ft.normalize_platforms(upgraded_meta(), 0)
        self.assertEqual(ft.normalize_platforms(once, 0), once)

    def test_missing_block_raises(self):
        with self.assertRaises(KeyError):
            ft.normalize_platforms("TextureImporter:\n  textureType: 0\n", 0)


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

    def test_run_normalises_platforms(self):
        (self.media / "lut.assets").write_bytes(serialized_file([
            (1, 28, texture_blob("Home_LUT", 16, 4, fmt=3, usage=0, color_space=0)),
            (2, 28, texture_blob("Noise", 8, 8, fmt=12, usage=0, color_space=0)),
        ]))
        for stem, size, guid, legacy, first in [("Home_LUT", (16, 4), "b1", 3, 0), ("Noise", (8, 8), "b2", 12, 1)]:
            (self.tex / f"{stem}.png").write_bytes(png_header(*size))
            (self.tex / f"{stem}.png.meta").write_text(upgraded_meta(guid, legacy, first))
        report = ft.run(self.media, self.project)
        self.assertEqual(platform_entries(self.meta("Home_LUT"))[0], ("DefaultTexturePlatform", 0, 0))
        self.assertEqual(platform_entries(self.meta("Noise"))[0], ("DefaultTexturePlatform", 1, 0))
        self.assertEqual(len(platform_entries(self.meta("Noise"))), 3)
        self.assertEqual(report["uncompressed"], 1)
        self.assertEqual(report["duplicate_defaults_removed"], 2)
        self.assertIn("Assets/Texture2D/Home_LUT.png.meta", report["changed_files"])
        self.assertEqual(ft.run(self.media, self.project)["changed"], 0)

    def test_second_run_changes_nothing(self):
        ft.run(self.media, self.project)
        self.assertEqual(ft.run(self.media, self.project)["changed"], 0)


if __name__ == "__main__":
    unittest.main()
