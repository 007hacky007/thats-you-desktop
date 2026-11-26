import json
import subprocess
import tempfile
import unittest
from pathlib import Path

from pipeline import audio_tools


class FakeTools:
    """Stands in for tools.ensure: creates <work>/tools/<name>-<version>/<top>/ like an extracted tarball."""

    TOPS = {"vgmstream-src": "vgmstream-r2168", "libogg": "libogg-1.3.6", "libvorbis": "libvorbis-1.3.7",
            "vgmstream": None}

    def __init__(self, work: Path):
        self.work = work
        self.calls = []

    def __call__(self, name, work_dir, spec=None):
        self.calls.append(name)
        dest = Path(work_dir) / "tools" / f"{name}-x"
        top = self.TOPS[name]
        root = dest / top if top else dest
        root.mkdir(parents=True, exist_ok=True)
        if name == "vgmstream-src":
            h = root / "src" / "coding" / "libs" / "vorbis_codebooks_wwise.h"
            h.parent.mkdir(parents=True, exist_ok=True)
            h.write_text("/* synthetic */\n")
        if name == "vgmstream":
            (root / "vgmstream-cli.exe").write_bytes(b"MZ")
        (dest / ".tool-complete").write_text("x")
        return dest


class FakeRun:
    def __init__(self, fail_on=None):
        self.calls = []
        self.fail_on = fail_on

    def __call__(self, argv, **kw):
        self.calls.append(list(argv))
        if self.fail_on and self.fail_on in argv:
            return subprocess.CompletedProcess(argv, 1, stdout="", stderr="boom: no compiler\n")
        if "--build" in argv:
            build = Path(argv[argv.index("--build") + 1])
            exe = build / "cli" / "vgmstream-cli"
            exe.parent.mkdir(parents=True, exist_ok=True)
            exe.write_text("#!/bin/sh\n")
        return subprocess.CompletedProcess(argv, 0, stdout="", stderr="")


def which_all(name):
    return f"/usr/bin/{name}"


class EnsureVgmstreamMacTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.work = Path(self.tmp.name) / "work"
        self.ensure = FakeTools(self.work)

    def tearDown(self):
        self.tmp.cleanup()

    def build(self, run=None, which=which_all):
        return audio_tools.ensure_vgmstream(self.work, plat="macos-arm64", ensure=self.ensure,
                                            run=run or FakeRun(), which=which, log=lambda *a: None)

    def test_builds_from_three_pinned_sources_with_exact_flags(self):
        run = FakeRun()
        exe = self.build(run)
        self.assertEqual(exe, self.work / "tools" / "vgmstream-r2168-macos-arm64" / "vgmstream-cli")
        self.assertTrue(exe.is_file())
        self.assertTrue(exe.stat().st_mode & 0o111)
        self.assertEqual(sorted(self.ensure.calls), ["libogg", "libvorbis", "vgmstream-src"])
        configure, build = run.calls
        src = self.work / "tools" / "vgmstream-src-x" / "vgmstream-r2168"
        ogg = self.work / "tools" / "libogg-x" / "libogg-1.3.6"
        vorbis = self.work / "tools" / "libvorbis-x" / "libvorbis-1.3.7"
        self.assertEqual(configure[:5], ["/usr/bin/cmake", "-S", str(src), "-B", configure[4]])
        self.assertEqual(configure[5:], [
            "-DCMAKE_BUILD_TYPE=Release", "-DUSE_FFMPEG=OFF", "-DUSE_MPEG=OFF", "-DUSE_CELT=OFF",
            "-DUSE_SPEEX=OFF", "-DBUILD_V123=OFF", "-DBUILD_AUDACIOUS=OFF", f"-DOGG_PATH={ogg}",
            f"-DVORBIS_PATH={vorbis}", "-DCMAKE_OSX_DEPLOYMENT_TARGET=12.0", "-DCMAKE_POLICY_VERSION_MINIMUM=3.5"])
        self.assertEqual(build, ["/usr/bin/cmake", "--build", configure[4], "--target", "vgmstream_cli"])
        # the build tree is scratch; only the binary and the marker stay
        self.assertFalse(Path(configure[4]).exists())

    def test_marker_holds_digest_of_pins_and_flags(self):
        exe = self.build()
        marker = exe.parent / ".tool-complete"
        self.assertEqual(marker.read_text(), audio_tools.build_digest())
        versions = json.loads(audio_tools.tools.VERSIONS.read_text())
        changed = dict(versions)
        changed["libogg"] = dict(versions["libogg"], sha256="0" * 64)
        self.assertNotEqual(audio_tools.build_digest(versions), audio_tools.build_digest(changed))

    def test_second_call_is_a_noop(self):
        self.build()
        run = FakeRun()
        self.ensure.calls.clear()
        exe = self.build(run)
        self.assertTrue(exe.is_file())
        self.assertEqual(run.calls, [])
        self.assertEqual(self.ensure.calls, [])

    def test_stale_marker_rebuilds(self):
        exe = self.build()
        (exe.parent / ".tool-complete").write_text("old")
        run = FakeRun()
        self.build(run)
        self.assertEqual(len(run.calls), 2)

    def test_missing_cmake_fails_clearly(self):
        with self.assertRaises(audio_tools.ToolBuildError) as cm:
            self.build(which=lambda n: None if n == "cmake" else which_all(n))
        self.assertIn("cmake", str(cm.exception))

    def test_missing_compiler_fails_clearly(self):
        with self.assertRaises(audio_tools.ToolBuildError) as cm:
            self.build(which=lambda n: None if n in ("cc", "clang", "gcc") else which_all(n))
        self.assertIn("C compiler", str(cm.exception))

    def test_failed_cmake_reports_output(self):
        with self.assertRaises(audio_tools.ToolBuildError) as cm:
            self.build(run=FakeRun(fail_on="--build"))
        self.assertIn("boom: no compiler", str(cm.exception))
        self.assertFalse((self.work / "tools" / "vgmstream-r2168-macos-arm64" / ".tool-complete").exists())


class EnsureVgmstreamWindowsTests(unittest.TestCase):
    def test_uses_official_zip(self):
        with tempfile.TemporaryDirectory() as t:
            work = Path(t)
            ensure = FakeTools(work)
            run = FakeRun()
            exe = audio_tools.ensure_vgmstream(work, plat="win-x64", ensure=ensure, run=run, which=which_all,
                                               log=lambda *a: None)
            self.assertEqual(exe.name, "vgmstream-cli.exe")
            self.assertTrue(exe.is_file())
            self.assertEqual(ensure.calls, ["vgmstream"])
            self.assertEqual(run.calls, [])


class EnsureCodebooksTests(unittest.TestCase):
    def test_returns_header_inside_pinned_source(self):
        with tempfile.TemporaryDirectory() as t:
            work = Path(t)
            ensure = FakeTools(work)
            h = audio_tools.ensure_codebooks(work, ensure=ensure)
            self.assertEqual(h, work / "tools" / "vgmstream-src-x" / "vgmstream-r2168" / "src" / "coding"
                             / "libs" / "vorbis_codebooks_wwise.h")
            self.assertTrue(h.is_file())
            self.assertEqual(ensure.calls, ["vgmstream-src"])

    def test_missing_header_fails(self):
        with tempfile.TemporaryDirectory() as t:
            work = Path(t)

            def ensure(name, work_dir, spec=None):
                d = Path(work_dir) / "tools" / "vgmstream-src-x" / "vgmstream-r2168"
                d.mkdir(parents=True, exist_ok=True)
                return d.parent

            with self.assertRaises(audio_tools.ToolBuildError):
                audio_tools.ensure_codebooks(work, ensure=ensure)


class VersionsTests(unittest.TestCase):
    def test_audio_tool_pins(self):
        v = json.loads(audio_tools.tools.VERSIONS.read_text())
        self.assertEqual(v["vgmstream"]["version"], "r2168")
        self.assertEqual(v["vgmstream"]["sha256_win-x64"],
                         "692d45070852a35c1fa42726aa3c9de528e44eec925d37d99b1074256b99a229")
        self.assertEqual(v["vgmstream-src"]["sha256"],
                         "6390ac616c4a248616f1b9bf6352fe949fcf2350a84844ba9416feac94f02225")
        self.assertEqual(v["libogg"]["sha256"], "83e6704730683d004d20e21b8f7f55dcb3383cdf84c0daedf30bde175f774638")
        self.assertEqual(v["libvorbis"]["sha256"],
                         "0e982409a9c3fc82ee06e08205b1355e5c6aa4c36bca58146ef399621b0ce5ab")


if __name__ == "__main__":
    unittest.main()
