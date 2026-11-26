"""Restore texture import settings that the AssetRipper export loses.

The PS4 build stores every Texture2D untiled with its pixel data decoded correctly by AssetRipper,
but the exported .meta files import every texture as a Sprite with sRGB sampling on. The original
Texture2D objects still say how each texture was used:

- m_LightmapFormat (texture usage) 3/4 = normal map (DXT5nm / plain),
- m_ColorSpace 0 = linear data (metallic/smoothness, masks, ...), 1 = sRGB colour.

In a linear-colour-space project a normal map sampled as an sRGB sprite gives bent normals and
blocky, tinted shading, and metallic maps read too dark. The AssetRipper metas (Unity 5.3 format)
mark normal maps correctly but never bypass sRGB, and Unity 6's meta upgrade turns every texture
(spriteMode 1) into a Sprite. This step reads the original serialized files (Unity 5.x format 15,
little endian, no type trees) and rewrites only textureType, sRGBTexture and alphaIsTransparency
in Assets/Texture2D/*.meta. Textures used by Sprite assets keep their Sprite import. It works on
metas already upgraded by Unity 6 (first import); old-format metas are reported and left alone.

The Unity 6 upgrade also leaves two DefaultTexturePlatform entries in platformSettings: the upgraded
one (compression taken from the legacy textureFormat) and an overridden, compressed copy of the
exported buildTargetSettings entry. Which one wins is undefined, so this step keeps only the first,
clears its override flag and sets textureCompression from the original m_TextureFormat: uncompressed
formats (Amplify Color LUTs are RGB24, UI masks Alpha8, ...) stay uncompressed instead of becoming
DXT1/DXT5. Other platform entries keep overridden 0.

Usage: python3 -m pipeline.fix_textures [--media DIR] [--project DIR] [--dry-run] [--json OUT]
"""
from __future__ import annotations

import argparse
import json
import re
import struct
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path

from pipeline.config import REPO_ROOT

TEXTURE2D = 28
NORMAL_USAGES = (3, 4)  # TextureUsageMode NormalmapDXT5nm, NormalmapPlain
TYPE_DEFAULT, TYPE_NORMAL = 0, 1
COMPRESSION_NONE, COMPRESSION_NORMAL = 0, 1
DEFAULT_PLATFORM = "DefaultTexturePlatform"
# UnityEngine.TextureFormat values that store plain (non block-compressed) texels.
UNCOMPRESSED_FORMATS = frozenset({
    1, 2, 3, 4, 5, 7, 9,          # Alpha8 ARGB4444 RGB24 RGBA32 ARGB32 RGB565 R16
    13, 14,                       # RGBA4444 BGRA32
    15, 16, 17, 18, 19, 20,       # RHalf RGHalf RGBAHalf RFloat RGFloat RGBAFloat
    21, 22,                       # YUY2 RGB9e5Float
    62, 63, 72, 73, 74,           # RG16 R8 RG32 RGB48 RGBA64
    75, 76, 77, 78,               # R8_SIGNED RG16_SIGNED RGB24_SIGNED RGBA32_SIGNED
})


@dataclass(frozen=True)
class TexInfo:
    name: str
    width: int
    height: int
    fmt: int
    mips: int
    filter: int
    aniso: int
    wrap: int
    usage: int
    color_space: int


def _read_string(b: bytes, pos: int) -> tuple[str, int]:
    (n,) = struct.unpack_from("<i", b, pos)
    s = b[pos + 4:pos + 4 + n].decode("utf-8", "replace")
    return s, (pos + 4 + n + 3) & ~3


def _object_table(data: bytes):
    """Yield (class_id, absolute_offset, size) for each object of a format-15 serialized file."""
    if len(data) < 20:
        raise ValueError("not a serialized file")
    _meta_size, _file_size, version, data_off = struct.unpack(">IIII", data[:16])
    if version != 15 or data[16] != 0:
        raise ValueError(f"unsupported serialized file (format {version}, big endian {data[16]})")
    pos = data.index(b"\0", 20) + 1  # Unity version string
    pos += 4  # target platform
    if data[pos]:
        raise ValueError("type trees not supported")
    pos += 1
    (ntypes,) = struct.unpack_from("<i", data, pos)
    pos += 4
    for _ in range(ntypes):
        (cid,) = struct.unpack_from("<i", data, pos)
        pos += 4 + (16 if cid < 0 else 0) + 16
    (nobj,) = struct.unpack_from("<i", data, pos)
    pos += 4
    for _ in range(nobj):
        pos = (pos + 3) & ~3
        _pid, start, size, _tidx, cid, _destroyed = struct.unpack_from("<qIIiHH", data, pos)
        pos += 25
        yield cid, data_off + start, size


def parse_textures(data: bytes) -> list[TexInfo]:
    out = []
    for cid, start, size in _object_table(data):
        if cid != TEXTURE2D:
            continue
        b = data[start:start + size]
        name, q = _read_string(b, 0)
        w, h, _complete, fmt, mips = struct.unpack_from("<5i", b, q)
        q = (q + 20 + 2 + 3) & ~3  # two bools, aligned
        _count, _dim, filt, aniso, _bias, wrap, usage, cs = struct.unpack_from("<iiiifiii", b, q)
        out.append(TexInfo(name, w, h, fmt, mips, filt, aniso, wrap, usage, cs))
    return out


def collect(media: Path) -> dict[str, list[TexInfo]]:
    by_name: dict[str, list[TexInfo]] = defaultdict(list)
    for p in sorted(Path(media).iterdir()):
        if not p.is_file() or p.suffix in (".resS", ".resource", ".dll"):
            continue
        head = p.read_bytes()[:20]
        if len(head) < 20 or struct.unpack(">I", head[8:12])[0] != 15:
            continue
        try:
            infos = parse_textures(p.read_bytes())
        except (ValueError, struct.error):
            continue
        for i in infos:
            by_name[i.name].append(i)
    return by_name


def png_size(path: Path) -> tuple[int, int]:
    head = Path(path).read_bytes()[:24]
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"not a PNG: {path}")
    return struct.unpack(">II", head[16:24])


def candidates(stem: str, size: tuple[int, int], by_name: dict[str, list[TexInfo]]) -> list[TexInfo]:
    """Original textures an exported file can come from: same name (or the name before AssetRipper's
    _N de-duplication suffix) and same size."""
    def fit(name):
        return [i for i in by_name.get(name, []) if (i.width, i.height) == tuple(size)]
    found = fit(stem)
    if not found:
        base = re.sub(r"_\d+$", "", stem)
        if base != stem:
            found = fit(base)
    return found


def desired(infos: list[TexInfo], is_sprite: bool) -> dict[str, int] | None:
    kinds = {(i.usage in NORMAL_USAGES, i.color_space) for i in infos}
    if len(kinds) != 1:
        return None
    normal, cs = kinds.pop()
    srgb = 0 if normal else (1 if cs == 1 else 0)
    if is_sprite:
        return {"sRGBTexture": srgb}
    return {"textureType": TYPE_NORMAL if normal else TYPE_DEFAULT, "sRGBTexture": srgb, "alphaIsTransparency": 0}


def compression_for(infos: list[TexInfo], legacy_format: int | None) -> int | None:
    """textureCompression the Default platform should use, from the original texture format(s).
    When same-named candidates disagree, the exported meta's legacy textureFormat (written from the
    actual source object) decides, provided it is one of them."""
    fmts = {i.fmt for i in infos}
    if len(fmts) != 1:
        if legacy_format is not None and (legacy_format in fmts or not fmts) and legacy_format >= 0:
            fmts = {legacy_format}
        else:
            return None
    return COMPRESSION_NONE if fmts.pop() in UNCOMPRESSED_FORMATS else COMPRESSION_NORMAL


def legacy_texture_format(text: str) -> int | None:
    m = re.search(r"^  textureFormat: (-?\d+)$", text, re.M)
    return int(m.group(1)) if m else None


_PLATFORM_BLOCK = re.compile(r"^  platformSettings:\n((?:  - .*\n(?:    .*\n)*)*)", re.M)


def _set_entry_key(entry: str, key: str, value: int, insert_after: str | None = None) -> str:
    pat = re.compile(rf"^(    {key}: )-?\d+$", re.M)
    if pat.search(entry):
        return pat.sub(lambda m: f"{m.group(1)}{value}", entry, count=1)
    if insert_after is None:
        return entry
    anchor = re.search(rf"^    {insert_after}: .*\n", entry, re.M)
    line = f"    {key}: {value}\n"
    return entry[:anchor.end()] + line + entry[anchor.end():] if anchor else entry + line


def normalize_platforms(text: str, compression: int) -> str:
    """Keep one DefaultTexturePlatform entry (the first), non-overridden, with the given
    textureCompression; clear the override flag on every other platform entry."""
    m = _PLATFORM_BLOCK.search(text)
    if not m:
        raise KeyError("platformSettings")
    entries = re.findall(r"  - .*\n(?:    .*\n)*", m.group(1))
    out, seen_default = [], False
    for e in entries:
        target = re.search(r"^(?:  -|   ) buildTarget: (\S+)$", e, re.M)
        if target and target.group(1) == DEFAULT_PLATFORM:
            if seen_default:
                continue
            seen_default = True
            # A missing key means compressed to Unity, so only an uncompressed choice is inserted.
            e = _set_entry_key(e, "textureCompression", compression,
                               insert_after="textureFormat" if compression == COMPRESSION_NONE else None)
        e = _set_entry_key(e, "overridden", 0)
        out.append(e)
    if not seen_default:
        raise KeyError(DEFAULT_PLATFORM)
    return text[:m.start(1)] + "".join(out) + text[m.end(1):]


_KEY_INDENT = {"textureType": "  ", "alphaIsTransparency": "  ", "sRGBTexture": "    "}


def patch_meta(text: str, settings: dict[str, int]) -> str:
    for key, value in settings.items():
        pat = re.compile(rf"^({_KEY_INDENT[key]}{key}: )-?\d+$", re.M)
        if not pat.search(text):
            raise KeyError(key)
        text = pat.sub(lambda m: f"{m.group(1)}{value}", text, count=1)
    return text


def sprite_texture_guids(project: Path) -> set[str]:
    guids = set()
    sprite_dir = Path(project) / "Assets" / "Sprite"
    for p in sprite_dir.glob("*.asset") if sprite_dir.is_dir() else []:
        guids.update(re.findall(r"texture: \{fileID: \d+, guid: ([0-9a-f]+)", p.read_text(errors="replace")))
    return guids


def run(media: Path, project: Path, dry_run: bool = False) -> dict:
    by_name = collect(media)
    sprites = sprite_texture_guids(project)
    report = {"textures": 0, "changed": 0, "normal_maps": 0, "linear": 0, "uncompressed": 0,
              "duplicate_defaults_removed": 0, "ambiguous": [], "unmatched": [], "old_format": [],
              "platform_unresolved": [], "changed_files": []}
    tex_dir = Path(project) / "Assets" / "Texture2D"
    for png in sorted(tex_dir.glob("*.png")):
        meta_path = png.with_name(png.name + ".meta")
        if not meta_path.is_file():
            continue
        report["textures"] += 1
        meta = meta_path.read_text()
        if not re.search(r"^    sRGBTexture: ", meta, re.M):
            report["old_format"].append(png.name)
            continue
        found = candidates(png.stem, png_size(png), by_name)
        new = meta
        if not found:
            report["unmatched"].append(png.name)
        else:
            guid = re.search(r"^guid: ([0-9a-f]+)", meta, re.M)
            want = desired(found, is_sprite=bool(guid and guid.group(1) in sprites))
            if want is None:
                report["ambiguous"].append(png.name)
            else:
                if want.get("textureType") == TYPE_NORMAL:
                    report["normal_maps"] += 1
                elif want["sRGBTexture"] == 0:
                    report["linear"] += 1
                new = patch_meta(new, want)
        compression = compression_for(found, legacy_texture_format(meta))
        if compression is None:
            report["platform_unresolved"].append(png.name)
        else:
            try:
                before = new.count(f"buildTarget: {DEFAULT_PLATFORM}")
                new = normalize_platforms(new, compression)
                report["duplicate_defaults_removed"] += before - new.count(f"buildTarget: {DEFAULT_PLATFORM}")
                if compression == COMPRESSION_NONE:
                    report["uncompressed"] += 1
            except KeyError:
                report["platform_unresolved"].append(png.name)
        if new != meta:
            report["changed"] += 1
            report["changed_files"].append(meta_path.relative_to(project).as_posix())
            if not dry_run:
                meta_path.write_text(new)
    return report


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--media", type=Path, default=REPO_ROOT / "work" / "merged" / "Media")
    ap.add_argument("--project", type=Path, default=REPO_ROOT / "work" / "project")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--json", type=Path)
    a = ap.parse_args(argv)
    report = run(a.media, a.project, dry_run=a.dry_run)
    if a.json:
        a.json.parent.mkdir(parents=True, exist_ok=True)
        a.json.write_text(json.dumps(report, indent=1))
    print(f"textures={report['textures']} changed={report['changed']} normal_maps={report['normal_maps']} "
          f"linear={report['linear']} ambiguous={len(report['ambiguous'])} unmatched={len(report['unmatched'])} "
          f"old_format={len(report['old_format'])} uncompressed={report['uncompressed']} "
          f"duplicate_defaults_removed={report['duplicate_defaults_removed']} "
          f"platform_unresolved={len(report['platform_unresolved'])}"
          + (" (dry run)" if a.dry_run else ""))
    for k in ("ambiguous", "unmatched", "old_format", "platform_unresolved"):
        if report[k]:
            print(f"{k}: {', '.join(report[k])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
