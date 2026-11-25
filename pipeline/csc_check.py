"""One-pass C# compile of the whole game project outside Unity (about 1 s).

Unity compiles Assembly-CSharp-firstpass before Assembly-CSharp and stops at the first failing
stage, so its log only shows one layer of errors. This compiles every non-Editor .cs file under
Assets/ plus our own code (unity/Port) in a single csc run, using the compiler options,
references and defines Unity's build system wrote to Library/Bee/.../Assembly-CSharp.rsp.

CLI: python3 -m pipeline.csc_check [--json out.json]  -> prints a summary, writes work/reports/csc.json
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import tempfile
from collections import Counter
from pathlib import Path

from pipeline.config import REPO_ROOT

PORT_DIR = REPO_ROOT / "unity" / "Port"
ERR = re.compile(r"^(?P<file>.+?\.cs)\((?P<line>\d+),(?P<col>\d+)\): error (?P<code>CS\d+): (?P<msg>.*)$")
DROP = ("-out:", "-refout:", "-analyzer:", "/additionalfile")


def find_rsp(project: Path) -> Path:
    found = sorted(Path(project).glob("Library/Bee/artifacts/*.dag/Assembly-CSharp.rsp"))
    if not found:
        raise FileNotFoundError("no Library/Bee/.../Assembly-CSharp.rsp: run the Unity import once (build_all)")
    return found[-1]


def compiler_options(rsp_text: str, out_dir: Path) -> list[str]:
    opts = []
    for line in rsp_text.splitlines():
        s = line.strip()
        if not s or s.startswith('"') or not s.startswith(("-", "/")):
            continue  # source files are collected separately
        if s.startswith(DROP) or "Assembly-CSharp-firstpass" in s:
            continue
        opts.append(s)
    opts.append(f'-out:"{Path(out_dir) / "check.dll"}"')
    return opts


def sources(project: Path, extra_dirs: list[Path]) -> list[str]:
    out = []
    for root in [Path(project) / "Assets", *extra_dirs]:
        for dirpath, dirs, files in os.walk(root):
            dirs[:] = [d for d in dirs if d != "Editor"]
            out += [str(Path(dirpath) / f) for f in files if f.endswith(".cs")]
    return sorted(out)


def parse(output: str, project: Path) -> list[dict]:
    seen, errs = set(), []
    proj = Path(project).resolve().as_posix().rstrip("/") + "/"
    repo = REPO_ROOT.as_posix().rstrip("/") + "/"
    for raw in output.splitlines():
        m = ERR.match(raw.strip())
        if not m:
            continue
        f = m["file"].replace("\\", "/")
        for prefix in (proj, repo):
            if f.startswith(prefix):
                f = f[len(prefix):]
        key = (f, m["line"], m["col"], m["code"], m["msg"])
        if key in seen:
            continue
        seen.add(key)
        errs.append({"file": f, "line": int(m["line"]), "col": int(m["col"]), "code": m["code"], "msg": m["msg"]})
    return errs


def toolchain(editor: Path) -> tuple[Path, Path]:
    scripting = Path(editor).resolve().parent.parent / "Resources" / "Scripting" / "DotNetSdk"
    cscs = sorted(scripting.glob("sdk/*/Roslyn/bincore/csc.dll"))
    if not cscs:
        raise FileNotFoundError(f"csc.dll not found under {scripting}")
    return scripting / "dotnet", cscs[-1]


def run(project: Path, editor: Path, extra_dirs: list[Path] | None = None) -> list[dict]:
    extra_dirs = [PORT_DIR] if extra_dirs is None else extra_dirs
    dotnet, csc = toolchain(editor)
    with tempfile.TemporaryDirectory() as tmp:
        rsp = Path(tmp) / "check.rsp"
        lines = compiler_options(find_rsp(project).read_text(), Path(tmp))
        lines += [f'"{s}"' for s in sources(project, [d for d in extra_dirs if Path(d).is_dir()])]
        rsp.write_text("\n".join(lines) + "\n")
        r = subprocess.run([str(dotnet), str(csc), "/noconfig", f"@{rsp}"], cwd=project,
                           capture_output=True, text=True)
    return parse(r.stdout + r.stderr, project)


def main() -> None:
    from pipeline import config

    ap = argparse.ArgumentParser()
    ap.add_argument("--json", type=Path)
    ap.add_argument("--project", type=Path, help="project dir (default work/project)")
    a = ap.parse_args()
    c = config.load()
    errs = run(a.project or c.work_dir / "project", c.unity_editor)
    out = a.json or c.work_dir / "reports" / "csc.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(errs, indent=1))
    print(f"{len(errs)} errors in {len({e['file'] for e in errs})} files -> {out}")
    for code, n in Counter(e["code"] for e in errs).most_common(12):
        print(f"  {n:5d} {code}")


if __name__ == "__main__":
    main()
