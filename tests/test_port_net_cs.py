"""C# harness for the PortNet networking core (contract section 4.9).

Compiles unity/Port/Net/Core/**, unity/Port/Net/Native/**, tests/cs/PaTestKit.cs and
tests/cs_net/** into one exe (toolchain helpers shared with the audio harness) and runs every
[PaTest]. Game/ is not compiled here: it needs the game assemblies (pipeline.csc_check covers it).

A pure-Python check keeps unity/Port/Net/Native/IwykNative.cs in sync with every
`#define IWYK_*` of native/iwyknet/include/iwyknet.h (skipped until the header exists).

Optional native loop: when work/native/macos-arm64/iwyknet.dylib and an iwykbot binary exist,
the dylib is copied next to the test exe and IWYK_NATIVE_LOOP_BOT names the bot, so the
NativeLoop test drives the real library over 127.0.0.1. Otherwise that test prints SKIP.
"""
from __future__ import annotations

import os
import re
import shutil
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from tests.test_port_audio_cs import build_and_run, find_toolchain, framework

REPO = Path(__file__).resolve().parent.parent
NET = REPO / "unity" / "Port" / "Net"
CS_NET = REPO / "tests" / "cs_net"
KIT = REPO / "tests" / "cs" / "PaTestKit.cs"
HEADER = REPO / "native" / "iwyknet" / "include" / "iwyknet.h"
IWYK_CS = NET / "Native" / "IwykNative.cs"
NATIVE_OUT = REPO / "work" / "native"

DEFINE = re.compile(r"^\s*#define\s+(IWYK_[A-Z0-9_]+)\s+\(?\s*(-?\d+)\s*\)?", re.M)
CS_CONST = re.compile(r"\bconst\s+int\s+(IWYK_[A-Z0-9_]+)\s*=\s*(-?\d+)\s*;")


def header_defines(text: str) -> dict[str, int]:
    return {m.group(1): int(m.group(2)) for m in DEFINE.finditer(text)}


def cs_constants(text: str) -> dict[str, int]:
    return {m.group(1): int(m.group(2)) for m in CS_CONST.finditer(text)}


def net_sources() -> list[Path]:
    files: list[Path] = []
    for d in (NET / "Core", NET / "Native"):
        files += sorted(d.rglob("*.cs"))
    files.append(KIT)
    files += sorted(CS_NET.rglob("*.cs"))
    return files


def native_loop_inputs(native_out: Path = NATIVE_OUT) -> tuple[Path | None, Path | None, str]:
    """(dylib, iwykbot, "") or (None, None, skip reason)."""
    dylib = native_out / "macos-arm64" / "iwyknet.dylib"
    if not dylib.is_file():
        return None, None, f"no {dylib}"
    for bot in (native_out / "macos-arm64" / "iwykbot", native_out / "iwykbot"):
        if bot.is_file() and os.access(bot, os.X_OK):
            return dylib, bot, ""
    return None, None, "no iwykbot next to the dylib (build_native --bot)"


class HeaderSyncTests(unittest.TestCase):
    def test_parsers(self):
        h = "#define IWYK_OK 0\n#define IWYK_E_ARG            -3   /* bad */\n#define IWYK_X (-5)\n#define OTHER 1\n"
        self.assertEqual(header_defines(h), {"IWYK_OK": 0, "IWYK_E_ARG": -3, "IWYK_X": -5})
        cs = "public const int IWYK_OK = 0;\n  internal const int IWYK_E_ARG = -3; // x\nconst long IWYK_Y = 1;"
        self.assertEqual(cs_constants(cs), {"IWYK_OK": 0, "IWYK_E_ARG": -3})

    def test_iwyk_native_mirrors_header(self):
        if not HEADER.is_file():
            self.skipTest(f"{HEADER.relative_to(REPO)} does not exist yet")
        want = header_defines(HEADER.read_text(encoding="utf-8"))
        self.assertTrue(want, "no #define IWYK_* found in the header")
        have = cs_constants(IWYK_CS.read_text(encoding="utf-8"))
        missing = sorted(k for k in want if k not in have)
        wrong = sorted(f"{k}: header {want[k]}, C# {have[k]}" for k in want if k in have and have[k] != want[k])
        self.assertEqual(missing, [], "IwykNative.cs lacks constants of iwyknet.h")
        self.assertEqual(wrong, [], "IwykNative.cs differs from iwyknet.h")


class NativeLoopInputTests(unittest.TestCase):
    def test_inputs_need_dylib_and_bot(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.assertIsNone(native_loop_inputs(root)[0])
            (root / "macos-arm64").mkdir()
            (root / "macos-arm64" / "iwyknet.dylib").write_bytes(b"")
            dylib, bot, why = native_loop_inputs(root)
            self.assertIsNone(bot)
            self.assertIn("iwykbot", why)
            b = root / "macos-arm64" / "iwykbot"
            b.write_bytes(b"")
            b.chmod(0o755)
            dylib, bot, why = native_loop_inputs(root)
            self.assertEqual((dylib.name, bot, why), ("iwyknet.dylib", b, ""))


class PortNetCsTests(unittest.TestCase):
    def test_net(self):
        if not (NET / "Core").is_dir() or not list(CS_NET.rglob("*.cs")):
            self.skipTest("unity/Port/Net/Core or tests/cs_net missing")
        tc = find_toolchain()
        if tc is None:
            self.skipTest("no C# compiler: neither a configured Unity editor nor dotnet on PATH")
        try:
            framework(tc.root)
        except FileNotFoundError as e:
            self.skipTest(str(e))
        dylib, bot, why = native_loop_inputs()
        env = {"IWYK_NATIVE_LOOP_BOT": ""}
        if dylib is None:
            print(f"SKIP native loop: {why}")
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            if dylib is not None:
                shutil.copy2(dylib, out / "iwyknet.dylib")
                env = {"IWYK_NATIVE_LOOP_BOT": str(bot)}
            with mock.patch.dict(os.environ, env):
                ok, output = build_and_run(tc, "Net", net_sources(), out)
        self.assertTrue(ok, f"C# PortNet tests:\n{output}")


if __name__ == "__main__":
    unittest.main()
