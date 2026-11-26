"""Build the phone page into work/web-dist: our page source plus the app's art and texts.

CLI: python3 -m pipeline.build_web [--out DIR] [--no-art] [--sounds]

The page source comes from web/ (committed). The art, fonts and localisation come from the owner's
own app export (<work>/export/app/ExportedProject/Assets) and are only ever written to the build
output, never to the repository. Without the export (or with --no-art) the output is the neutral
page. Output layout and art/manifest.json follow the networking contract, section 6.1.

Sprite entries in the manifest: x, y (from the top of the atlas), w, h, border [left, right, top,
bottom] and padding [left, right, top, bottom], in atlas pixels.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import struct
import sys
from pathlib import Path

from pipeline import web_common

UI_FILES = [
    ("polaroid", "blankPolaroid"),
    ("polaroidLight", "blankPolaroidLight"),
    ("photoFrame", "photo_frame_polaroid"),
    ("polaroidMini", "PolaroidMini_Albedo"),
    ("textCard", "TextEntryCard"),
    ("joker", "Joker_01_Albedo"),
    ("blankFeed", "BlankCameraFeed"),
    ("flash", "fullScreenFlash"),
    ("tableTop", "TableTop2_Albedo"),
    ("newspaperCanvas", "NewsPaperClipping_CanvasOpinion_MAT_Albedo"),
]
NEWSPAPERS = [f"NewsPaperClipping_{i:02d}_MAT_Albedo" for i in range(1, 7)]
EFFECTS = ["Warp", "Shrink", "Stretch", "Kaleidoscope", "Bulge", "Twist", "Square", "Grid", "Hourglass"]
FONT_ROLES = {
    "futuraltpro-heavy": "heading",
    "futurapro-demi": "body",
    "museo_slab_700": "slab",
    "caeciliaet": "text",
    "achilleiicyrfy-regular_l_cyr": "cyrillic",
    "americantypeitcpro-med": "typewriter",
    "pss65": "symbols",
}
NO_CACHE = ["/", "/index.html", "/sw.js", "/precache.json", "/manifest.webmanifest"]
NOT_PRECACHED = {"precache.json", "sw.js", "_headers"}


class BuildError(Exception):
    pass


# ---------------------------------------------------------------- parsing helpers

def lenient_json(text: str):
    """JSON with trailing commas and // or /* */ comments (the app's data files)."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
        elif c == ",":
            j = i + 1
            while j < n and text[j] in " \t\r\n":
                j += 1
            if j < n and text[j] in "}]":
                i += 1
            else:
                out.append(c)
                i += 1
        else:
            out.append(c)
            i += 1
    return json.loads("".join(out).lstrip("﻿"))


SPRITE_START = re.compile(r"^\s*- name:\s*(.*?)\s*$")
SPRITE_FIELD = re.compile(r"^\s*([A-Za-z]+):\s*(-?\d+)\s*$")


def parse_atlas_prefab(text: str) -> dict:
    """NGUI atlas sprite list (mSprites) -> {name: {x, y, w, h, border, padding}}."""
    sprites: dict = {}
    cur = None
    list_indent = None
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped:
            continue
        indent = len(line) - len(line.lstrip(" "))
        if list_indent is None:
            if stripped == "mSprites:":
                list_indent = indent
            continue
        if indent < list_indent or (indent == list_indent and not stripped.startswith("- ")):
            break
        m = SPRITE_START.match(line)
        if m and indent == list_indent:
            cur = {"name": m.group(1).strip("'\"")}
            sprites[cur["name"]] = cur
            continue
        f = SPRITE_FIELD.match(line)
        if f and cur is not None:
            cur[f.group(1)] = int(f.group(2))
    result = {}
    for name, s in sprites.items():
        if not all(k in s for k in ("x", "y", "width", "height")):
            continue
        result[name] = {
            "x": s["x"], "y": s["y"], "w": s["width"], "h": s["height"],
            "border": [s.get("borderLeft", 0), s.get("borderRight", 0), s.get("borderTop", 0), s.get("borderBottom", 0)],
            "padding": [s.get("paddingLeft", 0), s.get("paddingRight", 0), s.get("paddingTop", 0), s.get("paddingBottom", 0)],
        }
    return result


def png_size(path: Path) -> tuple[int, int] | None:
    with open(path, "rb") as f:
        head = f.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return None
    return struct.unpack(">II", head[16:24])


def parse_loc(data: bytes) -> dict:
    """appText_<code>.loc.loc: header line, then key<TAB>text lines (split at the first tab)."""
    text = data.decode("utf-8-sig", errors="replace")
    result = {}
    for i, line in enumerate(text.splitlines()):
        if i == 0:
            continue
        line = line.rstrip("\r")
        if "\t" not in line:
            continue
        key, value = line.split("\t", 1)
        key = key.strip()
        if key:
            result[key] = value
    return result


def find_ci(directory: Path, name: str) -> Path | None:
    """File `name` in `directory`, matched case-insensitively (Unity resource paths)."""
    if not directory.is_dir():
        return None
    low = name.lower()
    found = None
    for p in sorted(directory.iterdir()):
        if p.name == name and p.is_file():
            return p
        if found is None and p.name.lower() == low and p.is_file():
            found = p
    return found


# ---------------------------------------------------------------- build steps

class _Builder:
    def __init__(self, assets: Path, tmp: Path, warn):
        self.assets = assets
        self.tmp = tmp
        self.warn = warn

    def copy(self, src: Path | None, rel: str, what: str) -> str | None:
        if src is None or not src.is_file():
            self.warn(f"missing art: {what}")
            return None
        dst = self.tmp / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, dst)
        return rel

    def art(self, sounds: bool) -> dict:
        a = self.assets
        tex = a / "Texture2D"
        manifest: dict = {"schema": 1}

        atlas_png = tex / "GUISharedNewAtlas.png"
        prefab = a / "GameObject" / "GUISharedNewAtlas.prefab"
        if atlas_png.is_file() and prefab.is_file():
            rel = self.copy(atlas_png, "art/gui/atlas.png", "Texture2D/GUISharedNewAtlas.png")
            size = png_size(atlas_png) or (0, 0)
            manifest["atlas"] = {"image": rel, "width": size[0], "height": size[1],
                                 "sprites": parse_atlas_prefab(prefab.read_text(encoding="utf-8", errors="replace"))}
        else:
            self.warn("missing art: GUISharedNewAtlas.png or GUISharedNewAtlas.prefab (no atlas)")

        ui: dict = {}
        for key, name in UI_FILES:
            rel = self.copy(find_ci(tex, name + ".png"), f"art/ui/{name}.png", f"Texture2D/{name}.png")
            if rel:
                ui[key] = rel
        papers = []
        for name in NEWSPAPERS:
            rel = self.copy(find_ci(tex, name + ".png"), f"art/ui/{name}.png", f"Texture2D/{name}.png")
            if rel:
                papers.append(rel)
        if papers:
            ui["newspaper"] = papers
        manifest["ui"] = ui

        effects = {}
        for name in EFFECTS:
            rel = self.copy(find_ci(tex, name + ".png"), f"art/fx/{name}.png", f"Texture2D/{name}.png")
            if rel:
                effects[name] = rel
        manifest["effects"] = effects

        manifest["cards"] = self.cards()
        manifest["scenarios"] = self.scenarios()

        logos = {}
        logo_dir = a / "Resources" / "localisedlogoimages"
        if logo_dir.is_dir():
            for p in sorted(logo_dir.glob("mainLogo_*.png")):
                code = p.stem[len("mainLogo_"):]
                logos[code] = self.copy(p, f"art/logos/{p.name}", p.name)
        else:
            self.warn("missing art: Resources/localisedlogoimages")
        manifest["logos"] = logos

        fonts = {}
        font_dir = a / "Font"
        if font_dir.is_dir():
            for p in sorted(font_dir.iterdir()):
                if p.suffix.lower() not in (".otf", ".ttf") or not p.is_file():
                    continue
                rel = self.copy(p, f"art/fonts/{p.name}", p.name)
                role = FONT_ROLES.get(p.stem.lower())
                if role:
                    fonts[role] = rel
        else:
            self.warn("missing art: Font folder")
        manifest["fonts"] = fonts

        if sounds:
            manifest["sounds"] = self.sounds()
        return manifest

    def _json(self, rel: str):
        p = self.assets / rel
        if not p.is_file():
            self.warn(f"missing art: {rel}")
            return None
        try:
            return lenient_json(p.read_text(encoding="utf-8-sig"))
        except ValueError as e:
            self.warn(f"cannot parse {rel}: {e}")
            return None

    def cards(self) -> dict:
        data = self._json("Resources/cards/CardData.txt")
        cards = {}
        tex_dir = self.assets / "Resources" / "cards" / "textures"
        for c in (data or {}).get("cards", []):
            cid, path = c.get("cardId"), c.get("cardTexturePath")
            if not cid or not path:
                continue
            name = path.replace("\\", "/").rsplit("/", 1)[-1] + ".png"
            src = find_ci(tex_dir, name)
            rel = self.copy(src, f"art/cards/{src.name if src else name}", f"Resources/cards/textures/{name}")
            if rel:
                cards[cid] = rel
        return cards

    def scenarios(self) -> dict:
        data = self._json("Resources/scenarios/ScenarioData.json")
        base = self.assets / "Resources" / "scenarios"
        result = {}
        for s in (data or {}).get("scenarios", []):
            if "scenarioId" not in s:
                continue
            sid = str(s["scenarioId"])
            entry: dict = {}
            bg = (s.get("backgroundTexturePath") or "").replace("\\", "/").rsplit("/", 1)[-1]
            if bg:
                # half-size backgrounds as in the app; a few exist only at full size
                src = find_ci(base / "backgrounds_half", bg + ".png") or find_ci(base / "backgrounds", bg + ".png")
                rel = self.copy(src, f"art/scenarios/{src.name if src else bg + '.png'}",
                                f"Resources/scenarios/backgrounds_half/{bg}.png")
                if rel:
                    entry["background"] = rel
            card = (s.get("cardTexturePath") or "").replace("\\", "/").rsplit("/", 1)[-1]
            if card:
                src = find_ci(base / "cards", card + ".png")
                rel = self.copy(src, f"art/scenarios/cards/{src.name if src else card + '.png'}",
                                f"Resources/scenarios/cards/{card}.png")
                if rel:
                    entry["card"] = rel
            if entry:
                tiled = "tiled" in bg.lower() or s.get("uvRectWidth", 1) != 1 or s.get("uvRectHeight", 1) != 1
                entry["tiled"] = bool(tiled)
                result[sid] = entry
        return result

    def sounds(self) -> dict:
        data = self._json("Resources/audio/SoundData.json")
        sound_dir = self.assets / "Resources" / "audio" / "sounds"
        index = {}
        if sound_dir.is_dir():
            for p in sorted(sound_dir.rglob("*.ogg")):
                index.setdefault(p.stem.lower(), p)
        result = {}
        for s in (data or {}).get("sounds", []):
            event, path = s.get("soundEvent"), s.get("soundPath")
            if not event or not path or event in result:
                continue
            stem = path.replace("\\", "/").rsplit("/", 1)[-1]
            src = index.get(stem.lower())
            rel = self.copy(src, f"art/sounds/{src.name if src else stem + '.ogg'}", f"sounds/{stem}.ogg")
            if rel:
                result[event] = rel
        return result

    def i18n(self) -> bool:
        loc_dir = self.assets / "Resources" / "localisation"
        if not loc_dir.is_dir():
            self.warn("missing localisation: Resources/localisation (built-in English only)")
            return False
        languages = {}
        for p in sorted(loc_dir.glob("appText_*.loc.loc")):
            code = p.name[len("appText_"):-len(".loc.loc")]
            if not code or code.upper().startswith("DBG"):
                continue
            table = parse_loc(p.read_bytes())
            _write_json(self.tmp / "i18n" / f"{code}.json", table)
            languages[code] = f"i18n/{code}.json"
        if not languages:
            self.warn("missing localisation: no appText_*.loc.loc files")
            return False
        _write_json(self.tmp / "i18n" / "index.json", {"default": "en", "languages": languages})
        return True


def _write_json(path: Path, data, indent=None):
    path.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(data, ensure_ascii=False, sort_keys=True, indent=indent,
                      separators=(",", ": ") if indent else (",", ":"))
    path.write_text(text + "\n", encoding="utf-8")


def copy_web(web: Path, dst: Path) -> None:
    for p in sorted(web.rglob("*")):
        rel = p.relative_to(web)
        parts = rel.parts
        if parts[0] == "test" or any(part.startswith(".") for part in parts):
            continue
        if p.is_dir():
            continue
        if p.suffix.lower() == ".md":
            continue
        target = dst / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(p, target)


def output_files(root: Path) -> list[str]:
    return sorted(p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file())


def write_precache(root: Path) -> dict:
    files = [f for f in output_files(root) if f not in NOT_PRECACHED]
    h = hashlib.sha256()
    for f in files:
        h.update(f"{f}\t{hashlib.sha256((root / f).read_bytes()).hexdigest()}\n".encode("utf-8"))
    data = {"version": h.hexdigest()[:16], "files": files}
    (root / "precache.json").write_text(json.dumps(data, indent=1) + "\n", encoding="utf-8")
    return data


def write_headers(root: Path) -> None:
    lines = []
    for p in NO_CACHE:
        lines.append(f"{p}\n  Cache-Control: no-cache\n")
    (root / "_headers").write_text("".join(lines), encoding="utf-8")


def build(web: Path, out: Path, assets: Path | None = None, *, art: bool = True, sounds: bool = False,
          warn=None) -> dict:
    """Build into <out>.tmp and replace <out>. Returns {"mode", "files", "version", "warnings"}."""
    warnings: list[str] = []

    def _warn(msg):
        warnings.append(msg)
        if warn:
            warn(msg)

    web = Path(web)
    out = Path(out)
    if not (web / "index.html").is_file():
        raise BuildError(f"page source not found: {web / 'index.html'}")
    tmp = Path(str(out) + ".tmp")
    if tmp.exists():
        shutil.rmtree(tmp)
    tmp.mkdir(parents=True)
    try:
        copy_web(web, tmp)
        mode = "neutral"
        if not art:
            _warn("--no-art: building the neutral page")
        elif assets is None or not Path(assets).is_dir():
            _warn(f"no app export at {assets}: building the neutral page")
        else:
            b = _Builder(Path(assets), tmp, _warn)
            manifest = b.art(sounds)
            _write_json(tmp / "art" / "manifest.json", manifest, indent=1)
            b.i18n()
            mode = "art"
        pre = write_precache(tmp)
        write_headers(tmp)
    except BaseException:
        shutil.rmtree(tmp, ignore_errors=True)
        raise
    old = Path(str(out) + ".old")
    if old.exists():
        shutil.rmtree(old)
    if out.exists():
        out.rename(old)
    tmp.rename(out)
    if old.exists():
        shutil.rmtree(old, ignore_errors=True)
    return {"mode": mode, "files": len(output_files(out)), "version": pre["version"], "warnings": warnings}


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="python3 -m pipeline.build_web", description=__doc__.split("\n")[0])
    ap.add_argument("--out", type=Path, help="output folder (default <work>/web-dist)")
    ap.add_argument("--no-art", action="store_true", help="neutral page without the app's art and texts")
    ap.add_argument("--sounds", action="store_true", help="also copy the app's UI sounds")
    ap.add_argument("--config", type=Path, help=argparse.SUPPRESS)
    ap.add_argument("--web", type=Path, default=web_common.WEB_SRC, help=argparse.SUPPRESS)
    args = ap.parse_args(argv)
    try:
        cfg = web_common.load_local(args.config)
        out = args.out or web_common.web_dist(cfg)
        res = build(args.web, out, web_common.app_assets(cfg), art=not args.no_art, sounds=args.sounds,
                    warn=lambda m: print(f"warning: {m}", file=sys.stderr))
    except (BuildError, web_common.WebConfigError, OSError) as e:
        print(f"build_web: {e}", file=sys.stderr)
        return 1
    print(f"built {res['mode']} page: {res['files']} files, version {res['version']}, "
          f"{len(res['warnings'])} warnings -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
