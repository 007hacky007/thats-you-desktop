"""C# harness for the UnityEngine-free PortAudio core (audio architecture 9.3).

Each area under tests/cs/<Area>/ is compiled into its own exe and run outside Unity:
  sources = unity/Port/Audio/Core/{Contracts,Data,Seam} + Core/<Area> (+ Core folders listed in
            tests/cs/<Area>/DEPS, one per line) + tests/cs/PaTestKit.cs + tests/cs/<Area>/**.cs
The exe's Main (PaTestKit) runs every [PaTest] method and returns the failure count.

Compiler: the configured Unity editor's DotNetSdk (csc_check.toolchain), else `dotnet` on PATH.
References come from the runtime's ref pack (packs/Microsoft.NETCore.App.Ref) or, without one,
from the shared framework folder; a generated runtimeconfig.json runs the exe on that framework.
No network, no NuGet. An area is skipped when its Core folder or a dependency folder is missing;
everything is skipped when no compiler is found. PA_CS_AREAS=Data,Seam selects areas.
"""
from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CORE = REPO / "unity" / "Port" / "Audio" / "Core"
CS_TESTS = REPO / "tests" / "cs"
KIT = CS_TESTS / "PaTestKit.cs"
BASE = ("Contracts", "Data", "Seam")
FRAMEWORK = "Microsoft.NETCore.App"


@dataclass(frozen=True)
class Toolchain:
    dotnet: Path        # muxer used to run csc.dll and the test exes
    csc: Path           # Roslyn csc.dll
    root: Path          # dotnet root holding shared/ and packs/


def _version_key(name: str) -> tuple:
    return tuple(int(p) if p.isdigit() else -1 for p in re.split(r"[.-]", name))


def _unity_toolchain() -> Toolchain | None:
    try:
        from pipeline import config, csc_check
        c = config.load()
        if not c.unity_editor:
            return None
        dotnet, csc = csc_check.toolchain(c.unity_editor)
    except Exception:  # no config, no editor, no csc: try the next source
        return None
    if not Path(dotnet).is_file():
        return None
    return Toolchain(Path(dotnet), Path(csc), Path(dotnet).parent)


def _path_toolchain() -> Toolchain | None:
    exe = shutil.which("dotnet")
    if not exe:
        return None
    try:
        out = subprocess.run([exe, "--list-runtimes"], capture_output=True, text=True, timeout=60).stdout
    except (OSError, subprocess.SubprocessError):
        return None
    root = None
    for line in out.splitlines():
        m = re.match(r"^" + re.escape(FRAMEWORK) + r" \S+ \[(.+)\]$", line.strip())
        if m:
            root = Path(m.group(1)).parent.parent
            break
    if root is None:
        return None
    cscs = sorted(root.glob("sdk/*/Roslyn/bincore/csc.dll"), key=lambda p: _version_key(p.parts[-4]))
    if not cscs:
        return None
    return Toolchain(Path(exe), cscs[-1], root)


def find_toolchain() -> Toolchain | None:
    return _unity_toolchain() or _path_toolchain()


def framework(root: Path) -> tuple[str, list[Path]]:
    """(runtime version, reference assemblies) for the newest shared Microsoft.NETCore.App."""
    shared = sorted((p for p in (root / "shared" / FRAMEWORK).glob("*") if p.is_dir()),
                    key=lambda p: _version_key(p.name))
    if not shared:
        raise FileNotFoundError(f"no {FRAMEWORK} runtime under {root / 'shared'}")
    runtime = shared[-1]
    major = runtime.name.split(".")[0]
    packs = sorted((p for p in (root / "packs" / f"{FRAMEWORK}.Ref").glob(f"{major}.*") if p.is_dir()),
                   key=lambda p: _version_key(p.name))
    for pack in reversed(packs):
        tfms = sorted((pack / "ref").glob(f"net{major}.*"))
        if tfms:
            refs = sorted(tfms[-1].glob("*.dll"))
            if refs:
                return runtime.name, refs
    refs = [p for p in sorted(runtime.glob("*.dll")) if _managed(p.name)]
    return runtime.name, refs


def _managed(name: str) -> bool:
    if ".Native" in name:
        return False
    return name.startswith(("System.", "Microsoft.", "mscorlib", "netstandard", "WindowsBase"))


def discover_areas(cs_tests: Path = CS_TESTS) -> list[str]:
    if not cs_tests.is_dir():
        return []
    return sorted(p.name for p in cs_tests.iterdir() if p.is_dir() and not p.name.startswith((".", "_")))


def selected_areas(available: list[str], env: str | None) -> list[str]:
    """PA_CS_AREAS (comma separated, case-insensitive) or every area. Unknown names raise."""
    if not env or not env.strip():
        return list(available)
    by_lower = {a.lower(): a for a in available}
    out = []
    for raw in env.split(","):
        name = raw.strip()
        if not name:
            continue
        if name.lower() not in by_lower:
            raise ValueError(f"PA_CS_AREAS names unknown area {name!r} (have: {', '.join(available)})")
        if by_lower[name.lower()] not in out:
            out.append(by_lower[name.lower()])
    return out


def area_dependencies(area: str, cs_tests: Path = CS_TESTS) -> list[str]:
    deps = list(BASE)
    if area not in deps:
        deps.append(area)
    extra = cs_tests / area / "DEPS"
    if extra.is_file():
        for line in extra.read_text(encoding="utf-8").splitlines():
            name = line.split("#", 1)[0].strip()
            if name and name not in deps:
                deps.append(name)
    return deps


def area_sources(area: str, core: Path = CORE, cs_tests: Path = CS_TESTS) -> tuple[list[Path] | None, str]:
    """(sources, "") or (None, skip reason)."""
    files: list[Path] = []
    for dep in area_dependencies(area, cs_tests):
        d = core / dep
        if not d.is_dir():
            return None, f"Core/{dep} is missing"
        files += sorted(d.rglob("*.cs"))
    kit = cs_tests / "PaTestKit.cs"
    if not kit.is_file():
        return None, "tests/cs/PaTestKit.cs is missing"
    tests = sorted((cs_tests / area).rglob("*.cs"))
    if not tests:
        return None, f"tests/cs/{area} has no tests yet"
    files.append(kit)
    files += tests
    seen, out = set(), []
    for f in files:
        r = f.resolve()
        if r not in seen:
            seen.add(r)
            out.append(f)
    return out, ""


def build_and_run(tc: Toolchain, area: str, sources: list[Path], out_dir: Path, timeout: int = 600) -> tuple[bool, str]:
    version, refs = framework(tc.root)
    dll = out_dir / f"PaTests.{area}.dll"
    lines = ["-nologo", "-nostdlib+", "-langversion:9", "-target:exe", "-nullable:disable",
             "-debug:portable", "-main:PortAudio.Tests.PaTestMain", f'-out:"{dll}"']
    lines += [f'-r:"{r}"' for r in refs]
    lines += [f'"{s}"' for s in sources]
    rsp = out_dir / f"{area}.rsp"
    rsp.write_text("\n".join(lines) + "\n", encoding="utf-8")
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
               DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_ROOT=str(tc.root))
    c = subprocess.run([str(tc.dotnet), str(tc.csc), "-noconfig", f"@{rsp}"], capture_output=True, text=True,
                       env=env, timeout=timeout)
    if c.returncode != 0 or not dll.is_file():
        return False, f"compile failed ({area}):\n{c.stdout}{c.stderr}"
    rc = {"runtimeOptions": {"tfm": f"net{version.split('.')[0]}.0",
                             "framework": {"name": FRAMEWORK, "version": version}}}
    (out_dir / f"PaTests.{area}.runtimeconfig.json").write_text(json.dumps(rc), encoding="utf-8")
    r = subprocess.run([str(tc.dotnet), str(dll)], capture_output=True, text=True, env=env,
                       timeout=timeout, cwd=out_dir)
    output = r.stdout + r.stderr
    return r.returncode == 0, output


class HarnessHelperTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.core = self.root / "Core"
        self.cs = self.root / "cs"
        for d in ("Contracts", "Data", "Seam", "Music"):
            (self.core / d).mkdir(parents=True)
            (self.core / d / f"{d}.cs").write_text("//")
        for d in ("Data", "Music", "Engine", ".hidden"):
            (self.cs / d).mkdir(parents=True)
        (self.cs / "PaTestKit.cs").write_text("//")
        (self.cs / "Music" / "MusicTests.cs").write_text("//")

    def tearDown(self):
        self.tmp.cleanup()

    def test_discovers_area_folders_only(self):
        self.assertEqual(discover_areas(self.cs), ["Data", "Engine", "Music"])

    def test_selection_is_case_insensitive_and_rejects_unknown(self):
        areas = ["Data", "Music", "Seam"]
        self.assertEqual(selected_areas(areas, None), areas)
        self.assertEqual(selected_areas(areas, " music, data ,Music"), ["Music", "Data"])
        with self.assertRaises(ValueError):
            selected_areas(areas, "Data,Mixer")

    def test_sources_are_base_plus_area_plus_kit_and_tests(self):
        srcs, why = area_sources("Music", self.core, self.cs)
        self.assertEqual(why, "")
        names = [p.name for p in srcs]
        self.assertEqual(names, ["Contracts.cs", "Data.cs", "Seam.cs", "Music.cs", "PaTestKit.cs", "MusicTests.cs"])
        (self.cs / "Data" / "Sub").mkdir()
        (self.cs / "Data" / "Sub" / "DataTests.cs").write_text("//")
        srcs, _ = area_sources("Data", self.core, self.cs)
        self.assertEqual([p.name for p in srcs], ["Contracts.cs", "Data.cs", "Seam.cs", "PaTestKit.cs", "DataTests.cs"])

    def test_area_without_tests_skips(self):
        srcs, why = area_sources("Data", self.core, self.cs)
        self.assertIsNone(srcs)
        self.assertIn("no tests", why)

    def test_missing_core_or_dependency_folder_skips(self):
        srcs, why = area_sources("Engine", self.core, self.cs)
        self.assertIsNone(srcs)
        self.assertIn("Core/Engine", why)
        (self.cs / "Music" / "DEPS").write_text("# needs the mixer\nMixer\n")
        self.assertEqual(area_dependencies("Music", self.cs), ["Contracts", "Data", "Seam", "Music", "Mixer"])
        srcs, why = area_sources("Music", self.core, self.cs)
        self.assertIsNone(srcs)
        self.assertIn("Core/Mixer", why)

    def test_reference_selection_prefers_matching_ref_pack(self):
        root = self.root / "dotnet"
        rt = root / "shared" / FRAMEWORK / "8.0.21"
        rt.mkdir(parents=True)
        for n in ("System.Runtime.dll", "System.IO.Compression.Native.dll", "coreclr.dll", "mscorlib.dll"):
            (rt / n).write_text("")
        version, refs = framework(root)
        self.assertEqual(version, "8.0.21")
        self.assertEqual([p.name for p in refs], ["System.Runtime.dll", "mscorlib.dll"])
        ref = root / "packs" / f"{FRAMEWORK}.Ref" / "8.0.20" / "ref" / "net8.0"
        ref.mkdir(parents=True)
        (ref / "System.Runtime.dll").write_text("")
        (root / "packs" / f"{FRAMEWORK}.Ref" / "9.0.1" / "ref" / "net9.0").mkdir(parents=True)
        version, refs = framework(root)
        self.assertEqual(refs, [ref / "System.Runtime.dll"])


class PortAudioCsTests(unittest.TestCase):
    def test_areas(self):
        areas = selected_areas(discover_areas(), os.environ.get("PA_CS_AREAS"))
        if not areas:
            self.skipTest("no C# test areas under tests/cs")
        tc = find_toolchain()
        if tc is None:
            self.skipTest("no C# compiler: neither a configured Unity editor nor dotnet on PATH")
        try:
            framework(tc.root)
        except FileNotFoundError as e:
            self.skipTest(str(e))
        for area in areas:
            with self.subTest(area=area):
                sources, why = area_sources(area)
                if sources is None:
                    self.skipTest(f"{area}: {why}")
                with tempfile.TemporaryDirectory() as tmp:
                    ok, output = build_and_run(tc, area, sources, Path(tmp))
                self.assertTrue(ok, f"C# area {area}:\n{output}")


if __name__ == "__main__":
    unittest.main()
