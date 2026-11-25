"""Extract the companion APK: AssetRipper project plus Il2CppDumper metadata."""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import zipfile
from pathlib import Path

from pipeline import assetripper, config, tools

APP_SETTINGS = {
    "ScriptExportMode": "Decompiled",
    "ScriptContentLevel": "Level2",
    "ShaderExportMode": "Decompile",
    "ImageExportFormat": "Png",
    "AudioExportFormat": "Default",
    "SpriteExportMode": "Yaml",
    "TextExportMode": "Parse",
    "PreferOriginalTextureExtension": True,
    "BundledAssetsExportMode": "DirectExport",
    "DefaultVersion": "0.0.0a0",
    "TargetVersion": "0.0.0a0",
}
LIB = Path("lib/arm64-v8a/libil2cpp.so")
METADATA = Path("assets/bin/Data/Managed/Metadata/global-metadata.dat")


def unzip_apk(apk: Path, dest: Path) -> None:
    dest = Path(dest)
    marker = dest / ".unzip-complete"
    if marker.is_file():
        return
    with zipfile.ZipFile(apk) as z:
        for info in z.infolist():
            if not (dest / info.filename).resolve().is_relative_to(dest.resolve()):
                raise ValueError(f"unsafe path in APK: {info.filename}")
        if dest.exists():
            shutil.rmtree(dest)
        z.extractall(dest)
    marker.write_text("ok")


def run_il2cppdumper(apk_dir: Path, tool_dir: Path, out: Path) -> None:
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise RuntimeError("Il2CppDumper needs the .NET runtime: install it so that 'dotnet' is on PATH")
    out = Path(out)
    if (out / "dump.cs").is_file():
        return
    out.mkdir(parents=True, exist_ok=True)
    cfg_path = Path(tool_dir) / "config.json"
    if cfg_path.is_file():  # its "press any key" prompt crashes without a console
        cfg = json.loads(cfg_path.read_text(encoding="utf-8-sig"))
        cfg["RequireAnyKey"] = False
        cfg_path.write_text(json.dumps(cfg, indent=2))
    env =dict(os.environ, DOTNET_ROLL_FORWARD="Major")  # tool targets net7, newer runtimes are fine
    r = subprocess.run([dotnet, str(Path(tool_dir) / "Il2CppDumper.dll"), str(Path(apk_dir) / LIB),
                        str(Path(apk_dir) / METADATA), str(out)],
                       input="\n", capture_output=True, text=True, env=env)
    if r.returncode != 0 or not (out / "dump.cs").is_file():
        raise RuntimeError(f"Il2CppDumper failed ({r.returncode}): {r.stdout[-2000:]}{r.stderr[-2000:]}")


def main() -> None:
    c = config.load()
    apk_dir = c.work_dir / "apk"
    unzip_apk(c.apk, apk_dir)
    run_il2cppdumper(apk_dir, tools.ensure("il2cppdumper", c.work_dir), c.work_dir / "export" / "app-il2cpp")
    assetripper.export(apk_dir, c.work_dir / "export" / "app", APP_SETTINGS, exe=assetripper.default_exe(c.work_dir))
    print("phone app extracted")


if __name__ == "__main__":
    main()
