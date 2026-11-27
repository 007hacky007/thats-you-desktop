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


def apk_key(apk: Path) -> str:
    st = Path(apk).stat()
    return f"{Path(apk).name}:{st.st_size}:{st.st_mtime_ns}"


def unzip_apk(apk: Path, dest: Path) -> None:
    dest = Path(dest)
    marker = dest / ".unzip-complete"
    if marker.is_file() and marker.read_text() == apk_key(apk):
        return
    with zipfile.ZipFile(apk) as z:
        for info in z.infolist():
            if not (dest / info.filename).resolve().is_relative_to(dest.resolve()):
                raise ValueError(f"unsafe path in APK: {info.filename}")
        if dest.exists():
            shutil.rmtree(dest)
        z.extractall(dest)
    marker.write_text(apk_key(apk))


DUMP_OUTPUTS = ("dump.cs", "script.json", "il2cpp.h", "DummyDll")
DUMP_MARKER = ".dump-complete"


def dotnet_exe(work_dir: Path, which=shutil.which, ensure=tools.ensure, plat: str | None = None) -> str:
    """'dotnet' from PATH, else the pinned portable .NET runtime downloaded into work/tools."""
    found = which("dotnet")
    if found:
        return found
    plat = plat or tools.current_platform()
    dest = Path(ensure("dotnet", work_dir))
    return str(dest / ("dotnet.exe" if plat.startswith("win") else "dotnet"))


def run_il2cppdumper(apk_dir: Path, tool_dir: Path, out: Path, dotnet: str | None = None) -> None:
    dotnet = dotnet or shutil.which("dotnet")
    if not dotnet:
        raise RuntimeError("Il2CppDumper needs the .NET runtime: install it so that 'dotnet' is on PATH")
    out = Path(out)
    if (out / DUMP_MARKER).is_file():
        return
    tmp = out.with_name(out.name + ".tmp")
    for d in (tmp, out):
        if d.exists():
            shutil.rmtree(d)
    tmp.mkdir(parents=True)
    cfg_path = Path(tool_dir) / "config.json"
    if cfg_path.is_file():  # its "press any key" prompt crashes without a console
        cfg = json.loads(cfg_path.read_text(encoding="utf-8-sig"))
        cfg["RequireAnyKey"] = False
        cfg_path.write_text(json.dumps(cfg, indent=2))
    env = dict(os.environ, DOTNET_ROLL_FORWARD="Major")  # tool targets net7, newer runtimes are fine
    r = subprocess.run([dotnet, str(Path(tool_dir) / "Il2CppDumper.dll"), str(Path(apk_dir) / LIB),
                        str(Path(apk_dir) / METADATA), str(tmp)],
                       input="\n", capture_output=True, text=True, env=env)
    missing = [f for f in DUMP_OUTPUTS if not (tmp / f).exists()]
    if r.returncode != 0 or missing:
        shutil.rmtree(tmp, ignore_errors=True)
        raise RuntimeError(f"Il2CppDumper failed ({r.returncode}, missing {missing}): "
                           f"{r.stdout[-2000:]}{r.stderr[-2000:]}")
    tmp.rename(out)
    (out / DUMP_MARKER).write_text("ok")


def main() -> None:
    c = config.load()
    if c.apk is None:
        print("no APK configured: skipping the phone app (the phone page will be the neutral one)")
        return
    apk_dir = c.work_dir / "apk"
    unzip_apk(c.apk, apk_dir)
    run_il2cppdumper(apk_dir, tools.ensure("il2cppdumper", c.work_dir), c.work_dir / "export" / "app-il2cpp",
                     dotnet=dotnet_exe(c.work_dir))
    key = apk_key(c.apk) + json.dumps(APP_SETTINGS, sort_keys=True)
    assetripper.export(apk_dir, c.work_dir / "export" / "app", APP_SETTINGS, exe=assetripper.default_exe(c.work_dir),
                       key=key)
    print("phone app extracted")


if __name__ == "__main__":
    main()
