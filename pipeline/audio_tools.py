"""Pinned audio decoder toolchain: vgmstream-cli and the Wwise Vorbis codebook header.

- win-x64: the official vgmstream r2168 zip (self-contained), via tools.ensure("vgmstream").
- macOS (and other non-Windows hosts): the official Mac zip links absolute Homebrew dylibs, so
  vgmstream-cli is built from three pinned source tarballs (vgmstream-src, libogg, libvorbis) with
  cmake. OGG_PATH / VORBIS_PATH point at the extracted sources, so the build needs no network.
  Output: <work>/tools/vgmstream-r2168-<platform>/vgmstream-cli plus a .tool-complete marker that
  holds a digest of the three sha256 pins and the cmake flags.
- The codebook header (src/coding/libs/vorbis_codebooks_wwise.h) is read from the pinned vgmstream
  source in work/tools at conversion time. It is never copied into the repo.
"""
from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path

from pipeline import tools

SOURCE_TOOLS = ("vgmstream-src", "libogg", "libvorbis")
CMAKE_FLAGS = ("-DCMAKE_BUILD_TYPE=Release", "-DUSE_FFMPEG=OFF", "-DUSE_MPEG=OFF", "-DUSE_CELT=OFF",
               "-DUSE_SPEEX=OFF", "-DBUILD_V123=OFF", "-DBUILD_AUDACIOUS=OFF")
CMAKE_TAIL_FLAGS = ("-DCMAKE_OSX_DEPLOYMENT_TARGET=12.0", "-DCMAKE_POLICY_VERSION_MINIMUM=3.5")
CODEBOOKS = Path("src") / "coding" / "libs" / "vorbis_codebooks_wwise.h"
COMPILERS = ("cc", "clang", "gcc")


class ToolBuildError(Exception):
    pass


def _versions() -> dict:
    return json.loads(tools.VERSIONS.read_text())


def build_digest(versions: dict | None = None) -> str:
    """Digest of the three source pins and the cmake flags; changes force a rebuild."""
    versions = versions or _versions()
    data = {"sha256": [versions[n]["sha256"] for n in SOURCE_TOOLS],
            "flags": list(CMAKE_FLAGS + CMAKE_TAIL_FLAGS), "target": "vgmstream_cli"}
    return hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest()


def source_root(dest: Path) -> Path:
    """The single top-level directory of an extracted source tarball (or dest itself)."""
    dirs = [p for p in Path(dest).iterdir() if p.is_dir()]
    return dirs[0] if len(dirs) == 1 else Path(dest)


def cmake_configure_argv(cmake: str, src: Path, build: Path, ogg: Path, vorbis: Path) -> list[str]:
    return [cmake, "-S", str(src), "-B", str(build), *CMAKE_FLAGS, f"-DOGG_PATH={ogg}",
            f"-DVORBIS_PATH={vorbis}", *CMAKE_TAIL_FLAGS]


def _run(run, argv: list[str], what: str) -> None:
    r = run(argv, capture_output=True, text=True)
    if r.returncode != 0:
        out = ((r.stdout or "") + (r.stderr or "")).strip().splitlines()
        tail = "\n".join(out[-25:])
        raise ToolBuildError(f"vgmstream {what} failed (exit {r.returncode}): {' '.join(argv)}\n{tail}")


def ensure_vgmstream(work_dir: Path, *, plat: str | None = None, ensure=tools.ensure, run=subprocess.run,
                     which=shutil.which, log=print) -> Path:
    """Path to a vgmstream-cli executable for this host, downloading/building it when needed."""
    work_dir = Path(work_dir)
    plat = plat or tools.current_platform()
    pre = os.environ.get("IWYK_PREBUILT_DIR")
    if pre:  # the builder app ships a prebuilt vgmstream-cli
        exe = Path(pre) / ("vgmstream-cli.exe" if plat.startswith("win") else "vgmstream-cli")
        if exe.is_file():
            return exe
    versions = _versions()
    if plat.startswith("win"):
        dest = ensure("vgmstream", work_dir)
        exe = dest / "vgmstream-cli.exe"
        if not exe.is_file():
            found = sorted(dest.rglob("vgmstream-cli.exe"))
            if not found:
                raise ToolBuildError(f"vgmstream-cli.exe not found in {dest}")
            exe = found[0]
        return exe

    out = work_dir / "tools" / f"vgmstream-{versions['vgmstream']['version']}-{plat}"
    exe = out / "vgmstream-cli"
    marker = out / ".tool-complete"
    digest = build_digest(versions)
    if exe.is_file() and marker.is_file() and marker.read_text() == digest:
        return exe

    cmake = which("cmake")
    if not cmake:
        raise ToolBuildError("cmake not found on PATH; it is needed to build vgmstream-cli "
                             "(macOS: brew install cmake)")
    if not any(which(c) for c in COMPILERS):
        raise ToolBuildError("no C compiler (cc/clang/gcc) found on PATH; it is needed to build vgmstream-cli "
                             "(macOS: xcode-select --install)")
    src, ogg, vorbis = (source_root(ensure(n, work_dir)) for n in SOURCE_TOOLS)
    build = out.with_name(out.name + ".build")
    if build.exists():
        shutil.rmtree(build)
    if marker.exists():
        marker.unlink()
    log(f"vgmstream: building vgmstream-cli {versions['vgmstream']['version']} from source into {out}")
    _run(run, cmake_configure_argv(cmake, src, build, ogg, vorbis), "configure")
    _run(run, [cmake, "--build", str(build), "--target", "vgmstream_cli"], "build")
    built = build / "cli" / "vgmstream-cli"
    if not built.is_file():
        found = sorted(p for p in build.rglob("vgmstream-cli") if p.is_file())
        if not found:
            raise ToolBuildError(f"vgmstream-cli not produced under {build}")
        built = found[0]
    out.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(built, exe)
    exe.chmod(0o755)
    shutil.rmtree(build)
    marker.write_text(digest)
    return exe


def ensure_codebooks(work_dir: Path, *, ensure=tools.ensure) -> Path:
    """vorbis_codebooks_wwise.h inside the pinned vgmstream source (read at conversion time)."""
    header = source_root(ensure("vgmstream-src", Path(work_dir))) / CODEBOOKS
    if not header.is_file():
        raise ToolBuildError(f"codebook header not found: {header}")
    return header


def main() -> None:
    from pipeline import config

    w = config.load().work_dir
    print("vgmstream:", ensure_vgmstream(w))
    print("codebooks:", ensure_codebooks(w))


if __name__ == "__main__":
    main()
