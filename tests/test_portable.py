"""Task 2 of the builder plan: the pipeline finds its tools on players' Macs and Windows PCs."""
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from pipeline import audio_tools, build_player, config, extract_app, unity


class FindEditorTest(unittest.TestCase):
    def test_hub_default_locations_per_os(self):
        self.assertEqual(unity.hub_editor_path(Path("/Applications/Unity/Hub/Editor"), "6000.6.10f1", "darwin"),
                         Path("/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity"))
        self.assertEqual(unity.hub_editor_path(Path("C:/Program Files/Unity/Hub/Editor"), "6000.6.10f1", "win32"),
                         Path("C:/Program Files/Unity/Hub/Editor/6000.6.10f1/Editor/Unity.exe"))

    def test_find_editor_checks_roots_and_hub_secondary_path(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d) / "custom"
            exe = unity.hub_editor_path(root, "6000.6.10f1", "darwin")
            exe.parent.mkdir(parents=True)
            exe.write_text("")
            self.assertIsNone(unity.find_editor("6000.6.10f1", roots=[Path(d) / "none"], plat="darwin"))
            self.assertEqual(unity.find_editor("6000.6.10f1", roots=[Path(d) / "none", root], plat="darwin"), exe)
            hub_cfg = Path(d) / "secondaryInstallPath.json"
            hub_cfg.write_text(f'"{root.as_posix()}"')
            self.assertEqual(unity.find_editor("6000.6.10f1", roots=[], plat="darwin", hub_config=hub_cfg), exe)

    def test_editor_from_config_or_discovery(self):
        c = mock.Mock(unity_editor=Path("/x/Unity"))
        self.assertEqual(config.editor(c), Path("/x/Unity"))
        c = mock.Mock(unity_editor=None)
        with mock.patch.object(unity, "find_editor", return_value=Path("/found/Unity")):
            self.assertEqual(config.editor(c), Path("/found/Unity"))


class HostTargetTest(unittest.TestCase):
    def test_host_target(self):
        with mock.patch("sys.platform", "win32"):
            self.assertEqual(build_player.host_target(), "win")
        with mock.patch("sys.platform", "darwin"):
            self.assertEqual(build_player.host_target(), "mac")


class DotnetTest(unittest.TestCase):
    def test_dotnet_on_path_is_used(self):
        self.assertEqual(extract_app.dotnet_exe(Path("/w"), which=lambda n: "/usr/bin/dotnet",
                                                ensure=lambda *a: self.fail("no download")), "/usr/bin/dotnet")

    def test_portable_runtime_when_missing(self):
        with tempfile.TemporaryDirectory() as d:
            dest = Path(d) / "dotnet-8"
            dest.mkdir()
            (dest / "dotnet").write_text("")
            self.assertEqual(extract_app.dotnet_exe(Path(d), which=lambda n: None, ensure=lambda name, w: dest,
                                                    plat="macos-arm64"), str(dest / "dotnet"))
            (dest / "dotnet.exe").write_text("")
            self.assertEqual(extract_app.dotnet_exe(Path(d), which=lambda n: None, ensure=lambda name, w: dest,
                                                    plat="win-x64"), str(dest / "dotnet.exe"))


class PrebuiltVgmstreamTest(unittest.TestCase):
    def test_prebuilt_dir_wins(self):
        with tempfile.TemporaryDirectory() as d:
            exe = Path(d) / "vgmstream-cli"
            exe.write_text("")
            with mock.patch.dict(os.environ, {"IWYK_PREBUILT_DIR": d}):
                got = audio_tools.ensure_vgmstream(Path(d), plat="macos-arm64",
                                                   ensure=lambda *a, **k: self.fail("no download"),
                                                   which=lambda n: None)
            self.assertEqual(got, Path(d) / "vgmstream-cli")


if __name__ == "__main__":
    unittest.main()
