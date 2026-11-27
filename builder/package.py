"""Package the builder app: a DMG for macOS (arm64) or a single EXE for Windows (x64).

python3 -m builder.package --prebuilt DIR [--out DIR]   (run with the builder venv's Python)

DIR holds what the build would otherwise need a compiler for: native/<platform>/iwyknet.* and,
for macOS, vgmstream-cli. The Python sources are bundled as source files at their repository
paths, so the pipeline finds unity/Port, web/ and its pins exactly as in a checkout. Nothing from
work/, local-patches/ or config/ is ever bundled, and no bundled file may contain the owner's
private page address.
"""
from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SOURCE_DIRS = ("pipeline", "builder", "web", "unity/Port", "native/iwyknet/unity", "protocol")
SKIP_PARTS = {"__pycache__", "test", "tests", ".DS_Store", "node_modules"}
APP_NAME = "That's You! Builder"


def artifact_name(plat: str) -> str:
    return "ThatsYou-Builder-macOS.dmg" if plat.startswith("macos") else "ThatsYou-Builder-Windows.exe"


def data_files(repo: Path = REPO) -> list[tuple[str, str]]:
    """(source file, destination path inside the bundle) for every bundled repository file."""
    out = []
    for top in SOURCE_DIRS:
        base = repo / top
        for p in sorted(base.rglob("*")):
            rel = p.relative_to(repo)
            if not p.is_file() or SKIP_PARTS & set(rel.parts) or p.suffix == ".pyc":
                continue
            out.append((str(p), rel.as_posix()))
    out.append((str(repo / "LICENSE"), "LICENSE"))
    return out


def files_containing(files: list[tuple[str, str]], needle: str) -> list[str]:
    hits = []
    low = needle.lower().encode()
    for src, dest in files:
        if low and low in Path(src).read_bytes().lower():
            hits.append(dest)
    return hits


def private_host() -> str:
    try:
        v = subprocess.run(["git", "config", "--get", "iwyk.privateUrl"], capture_output=True, text=True,
                           cwd=REPO).stdout.strip()
    except OSError:
        v = ""
    from urllib.parse import urlparse
    return (urlparse(v if "://" in v else "//" + v).hostname or "") if v else ""


def stage(prebuilt: Path, stage_dir: Path) -> Path:
    """Copy everything to bundle into one folder (the PyInstaller data root)."""
    if stage_dir.exists():
        shutil.rmtree(stage_dir)
    files = data_files()
    host = private_host()
    if host and files_containing(files, host):
        raise SystemExit(f"refusing to package: the private page address is in {files_containing(files, host)}")
    for src, dest in files:
        d = stage_dir / dest
        d.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, d)
    shutil.copytree(prebuilt, stage_dir / "prebuilt")
    return stage_dir


def pyinstaller_argv(stage_dir: Path, dist: Path, work: Path, plat: str) -> list[str]:
    sep = ";" if plat.startswith("win") else ":"
    icon = REPO / "builder" / "assets" / ("icon.ico" if plat.startswith("win") else "icon.icns")
    argv = [sys.executable, "-m", "PyInstaller", "--noconfirm", "--clean", "--windowed",
            "--name", APP_NAME if not plat.startswith("win") else "ThatsYou-Builder",
            "--distpath", str(dist), "--workpath", str(work), "--specpath", str(work),
            "--icon", str(icon),
            "--exclude-module", "tkinter", "--exclude-module", "PySide6.QtWebEngineCore",
            "--exclude-module", "PySide6.QtQml", "--exclude-module", "PySide6.QtQuick",
            "--exclude-module", "PySide6.QtMultimedia", "--exclude-module", "PySide6.Qt3DCore",
            "--hidden-import", "cryptography.hazmat.primitives.ciphers",
            "--collect-submodules", "pipeline", "--collect-submodules", "builder"]
    for item in sorted(stage_dir.iterdir()):
        argv += ["--add-data", f"{item}{sep}{item.name}"]
    if plat.startswith("win"):
        argv.append("--onefile")
    argv.append(str(REPO / "builder" / "launch.py"))
    return argv


def make_icons() -> None:
    """icon.icns / icon.ico from icon.png (Pillow writes both)."""
    from PIL import Image
    png = REPO / "builder" / "assets" / "icon.png"
    img = Image.open(png)
    img.save(REPO / "builder" / "assets" / "icon.ico", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
    if sys.platform == "darwin":
        img.save(REPO / "builder" / "assets" / "icon.icns")


def make_dmg(app: Path, out: Path) -> Path:
    src = out.parent / "dmg-src"
    if src.exists():
        shutil.rmtree(src)
    src.mkdir(parents=True)
    shutil.copytree(app, src / app.name, symlinks=True)
    os.symlink("/Applications", src / "Applications")
    if out.exists():
        out.unlink()
    subprocess.run(["hdiutil", "create", "-volname", "That's You Builder", "-srcfolder", str(src),
                    "-ov", "-format", "UDZO", str(out)], check=True)
    shutil.rmtree(src)
    return out


def sha256_file(path: Path) -> Path:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(1 << 20):
            h.update(chunk)
    side = path.with_name(path.name + ".sha256")
    side.write_text(f"{h.hexdigest()}  {path.name}\n")
    return side


def main(argv=None) -> int:
    from pipeline import tools
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--prebuilt", type=Path, required=True)
    ap.add_argument("--out", type=Path, default=REPO / "work" / "package")
    a = ap.parse_args(argv)
    plat = tools.current_platform()
    out = a.out.resolve()
    make_icons()
    stage_dir = stage(a.prebuilt.resolve(), out / "stage")
    subprocess.run(pyinstaller_argv(stage_dir, out / "dist", out / "build", plat), check=True)
    if plat.startswith("macos"):
        app = out / "dist" / f"{APP_NAME}.app"
        subprocess.run(["codesign", "--force", "--deep", "-s", "-", str(app)], check=True)
        artifact = make_dmg(app, out / artifact_name(plat))
    else:
        artifact = out / artifact_name(plat)
        shutil.copy2(out / "dist" / "ThatsYou-Builder.exe", artifact)
    print(f"built {artifact}")
    print(f"sha256 in {sha256_file(artifact)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
