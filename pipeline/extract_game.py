"""Export the merged game dump to a Unity project with AssetRipper."""
from __future__ import annotations

import os
from pathlib import Path

from pipeline import assetripper, config
from pipeline.merge import _place_file

GAME_SETTINGS = {
    "IgnoreStreamingAssets": True,  # 4.4 GB; cloned from the merged dump instead
    "ScriptExportMode": "Decompiled",
    "ScriptContentLevel": "Level2",
    "ScriptLanguageVersion": "AutoSafe",
    "ShaderExportMode": "Decompile",
    "BundledAssetsExportMode": "DirectExport",
    "AudioExportFormat": "Default",
    "ImageExportFormat": "Png",
    "LightmapTextureExportFormat": "Yaml",
    "SpriteExportMode": "Yaml",
    "TextExportMode": "Parse",
    "PreferOriginalTextureExtension": True,
    "DefaultVersion": "0.0.0a0",
    "TargetVersion": "0.0.0a0",
}


def clone_streaming_assets(media: Path, project: Path) -> int:
    src_root = Path(media) / "StreamingAssets"
    dst_root = Path(project) / "Assets" / "StreamingAssets"
    n = 0
    for dirpath, _dirs, files in os.walk(src_root):
        for name in files:
            src = Path(dirpath) / name
            dst = dst_root / src.relative_to(src_root)
            if not dst.exists():
                _place_file(src, dst)
            n += 1
    return n


def main() -> None:
    c = config.load()
    media = c.work_dir / "merged" / "Media"
    out = c.work_dir / "export" / "game"
    assetripper.export(media, out, GAME_SETTINGS, exe=assetripper.default_exe(c.work_dir))
    n = clone_streaming_assets(media, out / "ExportedProject")
    print(f"exported to {out}, {n} StreamingAssets files")


if __name__ == "__main__":
    main()
