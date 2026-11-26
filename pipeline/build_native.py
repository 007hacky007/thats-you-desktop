"""Build the iwyknet native library (native/iwyknet, Go c-shared) and install it into a Unity project.

    python3 -m pipeline.build_native [--platform macos-arm64|win-x64|all] [--out DIR] [--bot] [--install PROJECT]

Outputs (default <work>/native, work_dir from config/local.json, default <repo>/work):
    <out>/macos-arm64/iwyknet.dylib, iwyknet.h, build-info.json
    <out>/win-x64/iwyknet.dll, iwyknet.h, build-info.json
    <out>/<host>/iwykbot            (--bot)

The Windows DLL is cross-compiled with mingw-w64 (env IWYK_WIN_CC, x86_64-w64-mingw32-gcc on PATH, or
`zig cc -target x86_64-windows-gnu`). Without a compiler the Windows build is skipped with the native
Windows command. --install copies the binaries with their PluginImporter .meta templates into
PROJECT/Assets/PortNative/iwyknet and refuses while Unity holds the project.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import hashlib
import json
import os
import platform as _platform
import re
import shlex
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CONFIG_PATH = REPO_ROOT / "config" / "local.json"
NATIVE_DIR = REPO_ROOT / "native" / "iwyknet"
HEADER = NATIVE_DIR / "include" / "iwyknet.h"
META_DIR = NATIVE_DIR / "unity"

PLATFORMS = ("macos-arm64", "win-x64")
LIB_NAME = {"macos-arm64": "iwyknet.dylib", "win-x64": "iwyknet.dll"}
GO_TARGET = {"macos-arm64": ("darwin", "arm64"), "win-x64": ("windows", "amd64")}
INSTALL_REL = {
    "macos-arm64": Path("Assets/PortNative/iwyknet/macOS/iwyknet.dylib"),
    "win-x64": Path("Assets/PortNative/iwyknet/Windows/x86_64/iwyknet.dll"),
}
MINGW_CC = "x86_64-w64-mingw32-gcc"
MINGW_OBJDUMP = "x86_64-w64-mingw32-objdump"
ZIG_CC = "zig cc -target x86_64-windows-gnu"
ALLOWED_DLL_IMPORT = re.compile(r"^(kernel32\.dll|api-ms-win-crt-[a-z0-9-]+\.dll)$", re.I)
WINDOWS_NATIVE_HINT = (
    "on Windows with MSYS2 or winlibs mingw-w64 gcc on PATH, in native\\iwyknet run:\n"
    "  set CGO_ENABLED=1& set GOOS=windows& set GOARCH=amd64& "
    "go build -trimpath -buildmode=c-shared -ldflags \"-s -w\" -o iwyknet.dll ./capi\n"
    "or there: python -m pipeline.build_native --platform win-x64"
)


class BuildError(Exception):
    pass


def work_dir(config_path: Path | None = None) -> Path:
    """work_dir from config/local.json (read directly; the dump paths need not exist)."""
    path = Path(config_path) if config_path else CONFIG_PATH
    data = {}
    if path.is_file():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            data = {}
    w = data.get("work_dir") if isinstance(data, dict) else None
    return Path(w).expanduser() if w else REPO_ROOT / "work"


def host_platform(system: str | None = None, machine: str | None = None) -> str:
    system = (system or _platform.system()).lower()
    machine = (machine or _platform.machine()).lower()
    if system == "darwin" and machine in ("arm64", "aarch64"):
        return "macos-arm64"
    if system == "windows" and machine in ("amd64", "x86_64"):
        return "win-x64"
    return f"{system}-{machine}"


def find_windows_cc(env=None, which=shutil.which) -> str | None:
    """C compiler command for the Windows cross build, or None."""
    env = os.environ if env is None else env
    if env.get("IWYK_WIN_CC"):
        return env["IWYK_WIN_CC"]
    if which(MINGW_CC):
        return MINGW_CC
    if which("zig"):
        return ZIG_CC
    return None


def go_build_cmd(platform: str, out_file: Path) -> list[str]:
    return ["go", "build", "-trimpath", "-buildmode=c-shared", "-ldflags", "-s -w",
            "-o", str(out_file), "./capi"]


def build_env(platform: str, cc: str | None, base: dict | None = None) -> dict:
    env = dict(os.environ if base is None else base)
    goos, goarch = GO_TARGET[platform]
    env.update({"CGO_ENABLED": "1", "GOOS": goos, "GOARCH": goarch})
    if cc:
        env["CC"] = cc
    return env


def _sha256(p: Path) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def _out(run, cmd, cwd=None, env=None) -> str:
    try:
        r = run(cmd, cwd=cwd, env=env, capture_output=True, text=True)
    except OSError:
        return ""
    return (r.stdout or "").strip() if r.returncode == 0 else ""


def dll_imports(dll: Path, run=subprocess.run, which=shutil.which) -> list[str] | None:
    """DLL names imported by a PE file (None when no objdump is available)."""
    objdump = which(MINGW_OBJDUMP) or which("objdump")
    if not objdump:
        return None
    text = _out(run, [objdump, "-p", str(dll)])
    if not text:
        return None
    return re.findall(r"DLL Name:\s*(\S+)", text)


def check_dll_imports(dll: Path, run=subprocess.run, which=shutil.which) -> list[str] | None:
    names = dll_imports(dll, run=run, which=which)
    if names is None:
        return None
    bad = [n for n in names if not ALLOWED_DLL_IMPORT.match(n)]
    if bad:
        raise BuildError(f"{dll.name} imports {', '.join(bad)}; only KERNEL32 and api-ms-win-crt-* are allowed")
    return names


def build(platform: str, out: Path, run=subprocess.run, env: dict | None = None, which=shutil.which,
          now: _dt.datetime | None = None, host: str | None = None) -> dict:
    """Build one platform into <out>/<platform>. Returns {"platform", "status": built|skipped, ...}."""
    if platform not in PLATFORMS:
        raise BuildError(f"unknown platform {platform!r}; use one of {', '.join(PLATFORMS)}")
    base = dict(os.environ if env is None else env)
    host = host or host_platform()
    cc = None
    if platform == "win-x64" and host != "win-x64":
        cc = find_windows_cc(base, which)
        if cc is None:
            return {"platform": platform, "status": "skipped",
                    "message": "no mingw-w64 compiler (set IWYK_WIN_CC, install mingw-w64 or zig); "
                               + WINDOWS_NATIVE_HINT}
    elif platform == "win-x64":
        cc = base.get("IWYK_WIN_CC") or None
    elif platform == "macos-arm64" and host != "macos-arm64":
        return {"platform": platform, "status": "skipped",
                "message": "the macOS arm64 library is built on an Apple Silicon Mac"}

    dest = Path(out) / platform
    dest.mkdir(parents=True, exist_ok=True)
    lib = dest / LIB_NAME[platform]
    benv = build_env(platform, cc, base)
    r = run(go_build_cmd(platform, lib), cwd=str(NATIVE_DIR), env=benv, capture_output=True, text=True)
    if r.returncode != 0:
        raise BuildError(f"go build for {platform} failed:\n{(r.stderr or '') + (r.stdout or '')}".rstrip())
    if not lib.is_file():
        raise BuildError(f"go build for {platform} did not produce {lib}")
    # go build writes a cgo header named after the output; consumers use the committed one.
    shutil.copyfile(HEADER, dest / "iwyknet.h")
    imports = check_dll_imports(lib, run=run, which=which) if platform == "win-x64" else None
    go_version = _out(run, ["go", "env", "GOVERSION"], cwd=str(NATIVE_DIR), env=base) or "unknown"
    commit = _out(run, ["git", "-C", str(REPO_ROOT), "rev-parse", "HEAD"]) or "unknown"
    info = {
        "platform": platform,
        "file": lib.name,
        "sha256": _sha256(lib),
        "size": lib.stat().st_size,
        "go_version": go_version,
        "commit": commit,
        "built_utc": (now or _dt.datetime.now(_dt.timezone.utc)).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "cc": cc or "",
    }
    if imports is not None:
        info["dll_imports"] = imports
    (dest / "build-info.json").write_text(json.dumps(info, indent=2) + "\n", encoding="utf-8")
    return {"platform": platform, "status": "built", "path": str(lib), "info": info}


def build_bot(out: Path, run=subprocess.run, env: dict | None = None) -> Path:
    """Build cmd/iwykbot for the host into <out>/<host>/iwykbot[.exe]."""
    host = host_platform()
    name = "iwykbot.exe" if host.startswith("win") else "iwykbot"
    dest = Path(out) / host
    dest.mkdir(parents=True, exist_ok=True)
    bot = dest / name
    benv = dict(os.environ if env is None else env)
    benv["CGO_ENABLED"] = "0"
    for k in ("GOOS", "GOARCH", "CC"):
        benv.pop(k, None)
    r = run(["go", "build", "-trimpath", "-o", str(bot), "./cmd/iwykbot"], cwd=str(NATIVE_DIR), env=benv,
            capture_output=True, text=True)
    if r.returncode != 0:
        raise BuildError(f"go build for iwykbot failed:\n{(r.stderr or '') + (r.stdout or '')}".rstrip())
    return bot


def _copy_if_changed(src: Path, dst: Path) -> bool:
    if dst.is_file() and dst.stat().st_size == src.stat().st_size and _sha256(dst) == _sha256(src):
        return False
    dst.parent.mkdir(parents=True, exist_ok=True)
    tmp = dst.with_name(dst.name + ".tmp")
    shutil.copyfile(src, tmp)
    os.replace(tmp, dst)
    return True


def _meta_guid(text: str) -> str | None:
    m = re.search(r"^guid:\s*([0-9a-f]{32})\s*$", text, re.M)
    return m.group(1) if m else None


def install(native_dir: Path, project: Path, holders=None) -> dict:
    """Copy the built binaries and their .meta templates into PROJECT/Assets/PortNative/iwyknet.

    Refuses while Unity has the project open. Unchanged binaries are not rewritten; an existing .meta
    with the template's GUID is kept (Unity may have normalised its YAML)."""
    native_dir, project = Path(native_dir), Path(project)
    if not (project / "Assets").is_dir():
        raise BuildError(f"{project} is not a Unity project (no Assets folder)")
    if holders is None:
        from pipeline.build_player import lock_holders as holders
    pids = holders(project)
    if pids:
        raise BuildError(f"Unity holds {project} (pids {', '.join(map(str, pids))}); close it first")
    report = {"copied": [], "unchanged": [], "meta_written": [], "missing": []}
    for plat in PLATFORMS:
        src = native_dir / plat / LIB_NAME[plat]
        if not src.is_file():
            report["missing"].append(plat)
            continue
        dst = project / INSTALL_REL[plat]
        rel = INSTALL_REL[plat].as_posix()
        (report["copied"] if _copy_if_changed(src, dst) else report["unchanged"]).append(rel)
        template = (META_DIR / (LIB_NAME[plat] + ".meta")).read_text(encoding="utf-8")
        meta = dst.with_name(dst.name + ".meta")
        current = meta.read_text(encoding="utf-8") if meta.is_file() else None
        if current is None or _meta_guid(current) != _meta_guid(template):
            meta.write_text(template, encoding="utf-8")
            report["meta_written"].append(rel + ".meta")
    if len(report["missing"]) == len(PLATFORMS):
        raise BuildError(f"no built binaries in {native_dir}; run python3 -m pipeline.build_native first")
    return report


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="python3 -m pipeline.build_native", description=__doc__.split("\n\n")[0])
    ap.add_argument("--platform", default="all", choices=PLATFORMS + ("all",))
    ap.add_argument("--out", type=Path, default=None, help="output dir (default <work>/native)")
    ap.add_argument("--bot", action="store_true", help="also build iwykbot for the host")
    ap.add_argument("--install", type=Path, metavar="PROJECT", help="install the binaries into a Unity project")
    args = ap.parse_args(argv)
    out = args.out or work_dir() / "native"
    plats = PLATFORMS if args.platform == "all" else (args.platform,)
    rc = 0
    for plat in plats:
        try:
            r = build(plat, out)
        except BuildError as e:
            print(f"build_native: {plat}: FAILED: {e}", file=sys.stderr)
            rc = 1
            continue
        if r["status"] == "skipped":
            print(f"build_native: {plat}: skipped: {r['message']}")
        else:
            info = r["info"]
            extra = f" imports {', '.join(info['dll_imports'])}" if "dll_imports" in info else ""
            print(f"build_native: {plat}: {r['path']} ({info['size']} bytes, {info['go_version']}){extra}")
    if args.bot:
        try:
            print(f"build_native: iwykbot: {build_bot(out)}")
        except BuildError as e:
            print(f"build_native: iwykbot: FAILED: {e}", file=sys.stderr)
            rc = 1
    if args.install and rc == 0:
        try:
            rep = install(out, args.install)
        except BuildError as e:
            print(f"build_native: install: {e}", file=sys.stderr)
            return 1
        print("build_native: install: " + json.dumps(rep))
    return rc


if __name__ == "__main__":
    sys.exit(main())
