import hashlib
import json
import struct
import tempfile
import unittest
import zlib
from pathlib import Path

from pipeline import build_web


def tiny_png(w=2, h=2, rgb=(200, 10, 10)) -> bytes:
    def chunk(kind, data):
        c = kind + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)
    raw = b"".join(b"\x00" + bytes(rgb) * w for _ in range(h))
    ihdr = struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


PREFAB = """%YAML 1.1
--- !u!114 &1
MonoBehaviour:
  mSprites:
  - name: icon_camera
    x: 338
    y: 922
    width: 348
    height: 282
    borderLeft: 1
    borderRight: 2
    borderTop: 3
    borderBottom: 4
    paddingLeft: 0
    paddingRight: 0
    paddingTop: 0
    paddingBottom: 0
  - name: button_round
    x: 0
    y: 0
    width: 64
    height: 64
    borderLeft: 0
    borderRight: 0
    borderTop: 0
    borderBottom: 0
    paddingLeft: 0
    paddingRight: 0
    paddingTop: 0
    paddingBottom: 0
  mPixelSize: 1
"""

CARDS = """{
  "$type": "CardData, Assembly-CSharp",
  "cards": [
    { "cardId": "CARD_1", "cardTexturePath": "Cards/Textures/card_artist", },
    { "cardId": "CARD_2", "cardTexturePath": "Cards/Textures/card_missing", },
  ],
}
"""

SCENARIOS = """{
  "scenarios": [
    { "scenarioId": -1, "backgroundTexturePath": "Scenarios/Backgrounds/Home_BG_TILED",
      "cardTexturePath": "Scenarios/Cards/Wild", "uvRectWidth": 2, "uvRectHeight": 2, },
    { "scenarioId": 3, "backgroundTexturePath": "Scenarios/Backgrounds/Train_BG",
      "cardTexturePath": "Scenarios/Cards/Travel", "uvRectWidth": 1, "uvRectHeight": 1, },
  ],
  "backgrounds": [],
}
"""

SOUNDS = """{ "sounds": [
  { "soundEvent": "GENERIC_BUTTON_CLICKED", "soundPath": "IWYK_Click", },
  { "soundEvent": "GONE", "soundPath": "IWYK_Gone", },
] }"""


def make_web(root: Path) -> Path:
    web = root / "web"
    (web / "js").mkdir(parents=True)
    (web / "test" / "net").mkdir(parents=True)
    (web / "index.html").write_text("<!doctype html><title>x</title>\n")
    (web / "sw.js").write_text("// sw\n")
    (web / "manifest.webmanifest").write_text("{}\n")
    (web / "js" / "main.js").write_text("export {};\n")
    (web / "README.md").write_text("doc\n")
    (web / ".hidden").write_text("x\n")
    (web / "test" / "net" / "a.test.mjs").write_text("x\n")
    return web


def make_export(root: Path) -> Path:
    a = root / "Assets"
    t = a / "Texture2D"
    t.mkdir(parents=True)
    (t / "GUISharedNewAtlas.png").write_bytes(tiny_png(4, 4))
    (a / "GameObject").mkdir()
    (a / "GameObject" / "GUISharedNewAtlas.prefab").write_text(PREFAB)
    for name in ("blankPolaroid", "TextEntryCard", "NewsPaperClipping_01_MAT_Albedo",
                 "NewsPaperClipping_03_MAT_Albedo", "TableTop2_Albedo", "Warp", "Kaleidoscope"):
        (t / f"{name}.png").write_bytes(tiny_png())
    (t / "Warp.png.meta").write_text("meta\n")
    r = a / "Resources"
    (r / "cards" / "textures").mkdir(parents=True)
    (r / "cards" / "CardData.txt").write_text(CARDS)
    (r / "cards" / "textures" / "card_artist.png").write_bytes(tiny_png())
    (r / "scenarios" / "backgrounds_half").mkdir(parents=True)
    (r / "scenarios" / "cards").mkdir(parents=True)
    (r / "scenarios" / "ScenarioData.json").write_text(SCENARIOS)
    (r / "scenarios" / "backgrounds_half" / "Home_BG_Tiled.png").write_bytes(tiny_png())
    (r / "scenarios" / "backgrounds_half" / "train_bg.png").write_bytes(tiny_png())
    (r / "scenarios" / "cards" / "Wild.png").write_bytes(tiny_png())
    (r / "scenarios" / "cards" / "Travel.png").write_bytes(tiny_png())
    (r / "localisedlogoimages").mkdir()
    (r / "localisedlogoimages" / "mainLogo_EN.png").write_bytes(tiny_png())
    (r / "localisedlogoimages" / "stickerLogo_EN.png").write_bytes(tiny_png())
    (a / "Font").mkdir()
    (a / "Font" / "FuturaLTPro-Heavy.otf").write_bytes(b"OTTO-heading")
    (a / "Font" / "CaeciliaET.ttf").write_bytes(b"\x00\x01\x00\x00text")
    loc = r / "localisation"
    loc.mkdir()
    (loc / "appText_en.loc.loc").write_bytes(
        "String Id\tLocalised Text\r\nHello\tHi there{n}you\r\nTabbed\ta\tb\r\n\r\nNoTab\r\n".encode("utf-8"))
    (loc / "appText_cs.loc.loc").write_bytes(
        "\ufeffString Id\tLocalised Text\r\nHello\tAhoj \u010dlov\u011bk\r\n".encode("utf-8"))
    (loc / "appText_DBG_KEYS.loc.loc").write_bytes(b"String Id\tLocalised Text\r\nHello\tHello\r\n")
    (loc / "appText_en.loc.loc.meta").write_text("meta\n")
    (r / "audio" / "sounds" / "sub").mkdir(parents=True)
    (r / "audio" / "SoundData.json").write_text(SOUNDS)
    (r / "audio" / "sounds" / "sub" / "iwyk_click.ogg").write_bytes(b"OggS")
    return a


class BuildWebTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.web = make_web(self.root)
        self.assets = make_export(self.root)
        self.out = self.root / "work" / "web-dist"
        self.warnings = []

    def tearDown(self):
        self.tmp.cleanup()

    def build(self, **kw):
        kw.setdefault("assets", self.assets)
        return build_web.build(self.web, out=self.out, warn=self.warnings.append, **kw)

    def manifest(self):
        return json.loads((self.out / "art" / "manifest.json").read_text())

    def test_copies_web_without_tests_md_and_dotfiles(self):
        self.build()
        self.assertTrue((self.out / "index.html").is_file())
        self.assertTrue((self.out / "js" / "main.js").is_file())
        self.assertFalse((self.out / "test").exists())
        self.assertFalse((self.out / "README.md").exists())
        self.assertFalse((self.out / ".hidden").exists())
        self.assertFalse(Path(str(self.out) + ".tmp").exists())

    def test_atlas_sprites(self):
        self.build()
        atlas = self.manifest()["atlas"]
        self.assertEqual(atlas["image"], "art/gui/atlas.png")
        self.assertEqual((atlas["width"], atlas["height"]), (4, 4))
        self.assertEqual(atlas["sprites"]["icon_camera"],
                         {"x": 338, "y": 922, "w": 348, "h": 282, "border": [1, 2, 3, 4], "padding": [0, 0, 0, 0]})
        self.assertIn("button_round", atlas["sprites"])
        self.assertTrue((self.out / "art" / "gui" / "atlas.png").is_file())

    def test_ui_effects_and_missing_warnings(self):
        self.build()
        m = self.manifest()
        self.assertEqual(m["ui"]["polaroid"], "art/ui/blankPolaroid.png")
        self.assertEqual(m["ui"]["textCard"], "art/ui/TextEntryCard.png")
        self.assertEqual(m["ui"]["tableTop"], "art/ui/TableTop2_Albedo.png")
        self.assertEqual(m["ui"]["newspaper"], ["art/ui/NewsPaperClipping_01_MAT_Albedo.png",
                                                "art/ui/NewsPaperClipping_03_MAT_Albedo.png"])
        self.assertNotIn("joker", m["ui"])
        self.assertNotIn("newspaperCanvas", m["ui"])
        self.assertEqual(m["effects"], {"Kaleidoscope": "art/fx/Kaleidoscope.png", "Warp": "art/fx/Warp.png"})
        self.assertEqual((self.out / "art" / "fx" / "Warp.png").read_bytes(),
                         (self.assets / "Texture2D" / "Warp.png").read_bytes())
        self.assertTrue(any("Joker_01_Albedo" in w for w in self.warnings))
        self.assertTrue(any("Twist" in w for w in self.warnings))
        self.assertFalse((self.out / "art" / "ui" / "Joker_01_Albedo.png").exists())

    def test_cards_and_scenarios(self):
        self.build()
        m = self.manifest()
        self.assertEqual(m["cards"], {"CARD_1": "art/cards/card_artist.png"})
        self.assertTrue(any("card_missing" in w for w in self.warnings))
        self.assertEqual(m["scenarios"]["-1"], {"background": "art/scenarios/Home_BG_Tiled.png",
                                                "card": "art/scenarios/cards/Wild.png", "tiled": True})
        self.assertEqual(m["scenarios"]["3"], {"background": "art/scenarios/train_bg.png",
                                               "card": "art/scenarios/cards/Travel.png", "tiled": False})

    def test_scenario_background_falls_back_to_full_size(self):
        half = self.assets / "Resources" / "scenarios" / "backgrounds_half" / "Home_BG_Tiled.png"
        full = self.assets / "Resources" / "scenarios" / "backgrounds"
        full.mkdir()
        half.rename(full / "Home_BG_Tiled.png")
        self.build()
        self.assertEqual(self.manifest()["scenarios"]["-1"]["background"], "art/scenarios/Home_BG_Tiled.png")

    def test_logos_and_fonts(self):
        self.build()
        m = self.manifest()
        self.assertEqual(m["logos"], {"EN": "art/logos/mainLogo_EN.png"})
        self.assertEqual(m["fonts"], {"heading": "art/fonts/FuturaLTPro-Heavy.otf",
                                      "text": "art/fonts/CaeciliaET.ttf"})
        self.assertEqual(m["schema"], 1)
        self.assertNotIn("sounds", m)

    def test_sounds_only_with_flag(self):
        self.build(sounds=True)
        m = self.manifest()
        self.assertEqual(m["sounds"], {"GENERIC_BUTTON_CLICKED": "art/sounds/iwyk_click.ogg"})
        self.assertTrue(any("IWYK_Gone" in w for w in self.warnings))

    def test_i18n(self):
        self.build()
        idx = json.loads((self.out / "i18n" / "index.json").read_text())
        self.assertEqual(idx, {"default": "en", "languages": {"cs": "i18n/cs.json", "en": "i18n/en.json"}})
        en = json.loads((self.out / "i18n" / "en.json").read_text(encoding="utf-8"))
        self.assertEqual(en, {"Hello": "Hi there{n}you", "Tabbed": "a\tb"})
        cs = json.loads((self.out / "i18n" / "cs.json").read_text(encoding="utf-8"))
        self.assertEqual(cs["Hello"], "Ahoj \u010dlov\u011bk")
        self.assertFalse((self.out / "i18n" / "DBG_KEYS.json").exists())

    def test_precache_and_headers(self):
        self.build()
        pc = json.loads((self.out / "precache.json").read_text())
        files = pc["files"]
        self.assertEqual(files, sorted(files))
        self.assertIn("index.html", files)
        self.assertIn("art/manifest.json", files)
        self.assertIn("i18n/en.json", files)
        for skip in ("precache.json", "sw.js", "_headers"):
            self.assertNotIn(skip, files)
        h = hashlib.sha256()
        for f in files:
            h.update(f"{f}\t{hashlib.sha256((self.out / f).read_bytes()).hexdigest()}\n".encode())
        self.assertEqual(pc["version"], h.hexdigest()[:16])
        headers = (self.out / "_headers").read_text()
        for p in ("/index.html", "/sw.js", "/precache.json", "/manifest.webmanifest"):
            self.assertIn(f"{p}\n  Cache-Control: no-cache\n", headers)

    def test_deterministic(self):
        self.build(sounds=True)
        first = {p.relative_to(self.out).as_posix(): p.read_bytes() for p in self.out.rglob("*") if p.is_file()}
        self.build(sounds=True)
        second = {p.relative_to(self.out).as_posix(): p.read_bytes() for p in self.out.rglob("*") if p.is_file()}
        self.assertEqual(first, second)

    def test_neutral_with_no_art(self):
        self.build(art=False)
        self.assertFalse((self.out / "art").exists())
        self.assertFalse((self.out / "i18n").exists())
        self.assertTrue((self.out / "precache.json").is_file())
        self.assertTrue(any("neutral" in w for w in self.warnings))

    def test_neutral_without_export(self):
        self.build(assets=self.root / "nope")
        self.assertFalse((self.out / "art").exists())
        self.assertTrue((self.out / "index.html").is_file())

    def test_replaces_previous_output(self):
        self.out.mkdir(parents=True)
        (self.out / "stale.txt").write_text("old")
        Path(str(self.out) + ".tmp").mkdir()
        (Path(str(self.out) + ".tmp") / "junk").write_text("x")
        self.build()
        self.assertFalse((self.out / "stale.txt").exists())
        self.assertFalse(Path(str(self.out) + ".tmp").exists())

    def test_missing_web_dir_is_error(self):
        with self.assertRaises(build_web.BuildError):
            build_web.build(self.root / "noweb", out=self.out, assets=self.assets, warn=self.warnings.append)

    def test_lenient_json(self):
        self.assertEqual(build_web.lenient_json('{"a": [1, 2,], // c\n "b": "x,]",}'), {"a": [1, 2], "b": "x,]"})

    def test_main_uses_config_work_dir(self):
        cfg = self.root / "local.json"
        cfg.write_text(json.dumps({"work_dir": str(self.root / "w2")}))
        rc = build_web.main(["--no-art", "--config", str(cfg), "--web", str(self.web)])
        self.assertEqual(rc, 0)
        self.assertTrue((self.root / "w2" / "web-dist" / "index.html").is_file())


if __name__ == "__main__":
    unittest.main()
