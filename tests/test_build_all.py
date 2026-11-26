import tempfile
import unittest
from unittest import mock
from pathlib import Path
from types import SimpleNamespace

from pipeline import build_all


class FakeEdits:
    def __init__(self):
        self.version, self.fn = "v1", lambda root: None

    def digest(self):
        return self.version

    def apply(self, root):
        self.fn(root)


class PrepareProjectTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = Path(self.tmp.name)
        self.pristine, self.project = r / "pristine", r / "project"
        (self.pristine / "Assets").mkdir(parents=True)
        (self.pristine / "Assets/A.cs").write_text("class A {}\n")
        self.pdir = FakeEdits()

    def tearDown(self):
        self.tmp.cleanup()

    def test_first_prepare_clones_pristine(self):
        self.assertTrue(build_all.prepare_project(self.pristine, self.project, self.pdir))
        self.assertEqual((self.project / "Assets/A.cs").read_text(), "class A {}\n")

    def test_unchanged_edits_skip(self):
        build_all.prepare_project(self.pristine, self.project, self.pdir)
        (self.project / "Assets/A.cs").write_text("edited in editor\n")
        self.assertFalse(build_all.prepare_project(self.pristine, self.project, self.pdir))
        self.assertEqual((self.project / "Assets/A.cs").read_text(), "edited in editor\n")

    def test_changed_edits_rebuild_and_keep_library(self):
        build_all.prepare_project(self.pristine, self.project, self.pdir)
        (self.project / "Library").mkdir()
        (self.project / "Library/cache").write_text("big")
        self.pdir.version = "v2"
        self.pdir.fn = lambda root: (root / "Assets/A.cs").write_text("class A { int x; }\n")
        self.assertTrue(build_all.prepare_project(self.pristine, self.project, self.pdir))
        self.assertEqual((self.project / "Assets/A.cs").read_text(), "class A { int x; }\n")
        self.assertEqual((self.project / "Library/cache").read_text(), "big")

    def test_changed_pristine_rebuilds(self):
        (self.pristine / ".export-complete").write_text("export-v1")
        build_all.prepare_project(self.pristine, self.project, self.pdir)
        (self.pristine / ".export-complete").write_text("export-v2")
        (self.pristine / "Assets/A.cs").write_text("class A2 {}\n")
        self.assertTrue(build_all.prepare_project(self.pristine, self.project, self.pdir))
        self.assertEqual((self.project / "Assets/A.cs").read_text(), "class A2 {}\n")

    def test_port_code_synced_into_assets_port(self):
        port = Path(self.tmp.name) / "port"
        (port / "Stubs").mkdir(parents=True)
        (port / "Stubs/S.cs").write_text("class S {}\n")
        build_all.prepare_project(self.pristine, self.project, self.pdir, port_dir=port)
        self.assertEqual((self.project / "Assets/Port/Stubs/S.cs").read_text(), "class S {}\n")
        # edits and deletions in the repo show up on the next run, even without recipe changes
        (port / "Stubs/S.cs").write_text("class S2 {}\n")
        (port / "New.cs").write_text("class N {}\n")
        build_all.prepare_project(self.pristine, self.project, self.pdir, port_dir=port)
        self.assertEqual((self.project / "Assets/Port/Stubs/S.cs").read_text(), "class S2 {}\n")
        self.assertTrue((self.project / "Assets/Port/New.cs").is_file())
        (port / "New.cs").unlink()
        build_all.prepare_project(self.pristine, self.project, self.pdir, port_dir=port)
        self.assertFalse((self.project / "Assets/Port/New.cs").exists())


class ImportProjectTests(unittest.TestCase):
    """The first Unity import upgrades the exported 5.3 texture metas; fix_textures then rewrites
    them and Unity has to import again so the fixed settings take effect."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = Path(self.tmp.name)
        self.project, self.media, self.reports = r / "project", r / "media", r / "reports"
        self.calls = []

    def tearDown(self):
        self.tmp.cleanup()

    def batch(self, rcs):
        rcs = list(rcs)

        def run_batch(editor, project, args, log):
            self.calls.append(("unity", Path(log).name, list(args)))
            return rcs.pop(0)
        return run_batch

    def fix(self, changed):
        def run(media, project, dry_run=False):
            self.calls.append(("fix", Path(media), Path(project)))
            return {"changed": changed, "changed_files": ["Assets/Texture2D/a.png.meta"][:changed]}
        return run

    def test_fix_textures_runs_after_import_and_reimports(self):
        rc = build_all.import_project(Path("/u"), self.project, self.media, self.reports,
                                      run_batch=self.batch([0, 0]), fix=self.fix(1))
        self.assertEqual(rc, 0)
        self.assertEqual(self.calls[0], ("unity", "unity-import.log", ["-accept-apiupdate"]))
        self.assertEqual(self.calls[1], ("fix", self.media, self.project))
        self.assertEqual(self.calls[2][:2], ("unity", "unity-reimport.log"))
        self.assertTrue((self.reports / "fix-textures.json").is_file())

    def test_no_reimport_when_no_meta_changed(self):
        rc = build_all.import_project(Path("/u"), self.project, self.media, self.reports,
                                      run_batch=self.batch([0]), fix=self.fix(0))
        self.assertEqual(rc, 0)
        self.assertEqual([c[0] for c in self.calls], ["unity", "fix"])

    def test_failed_import_skips_fix(self):
        rc = build_all.import_project(Path("/u"), self.project, self.media, self.reports,
                                      run_batch=self.batch([3]), fix=self.fix(1))
        self.assertEqual(rc, 3)
        self.assertEqual([c[0] for c in self.calls], ["unity"])

    def test_failed_reimport_returns_its_exit_code(self):
        rc = build_all.import_project(Path("/u"), self.project, self.media, self.reports,
                                      run_batch=self.batch([0, 5]), fix=self.fix(2))
        self.assertEqual(rc, 5)


class UnityEditorCheckTests(unittest.TestCase):
    def test_missing_editor_names_key(self):
        from pipeline.config import ConfigError
        with self.assertRaises(ConfigError) as cm:
            build_all.check_unity_editor(None)
        self.assertEqual(cm.exception.key, "unity_editor")

    def test_app_bundle_instead_of_binary_rejected(self):
        from pipeline.config import ConfigError
        with tempfile.TemporaryDirectory() as t:
            app = Path(t) / "Unity.app"
            app.mkdir()
            with self.assertRaises(ConfigError) as cm:
                build_all.check_unity_editor(app)
            self.assertIn("Contents/MacOS/Unity", cm.exception.message)

    def test_executable_accepted(self):
        with tempfile.TemporaryDirectory() as t:
            exe = Path(t) / "Unity"
            exe.write_text("#!/bin/sh\n")
            exe.chmod(0o755)
            build_all.check_unity_editor(exe)


class BuildProjectTests(unittest.TestCase):
    """Audio is converted and installed after the project is prepared and before Unity imports it."""

    def test_audio_runs_between_prepare_and_import(self):
        calls = []
        c = SimpleNamespace(work_dir=Path("/w"), unity_editor=Path("/u/Unity"))

        def prepare(pristine, project, edits):
            calls.append(("prepare", pristine, project))
            return False

        def audio_build(work_dir, project):
            calls.append(("audio", work_dir, project))

        def import_(editor, project, media, reports):
            calls.append(("import", editor, project, media, reports))
            return 7

        rc = build_all.build_project(c, Path("/w/export"), Path("/w/project"), prepare=prepare,
                                     audio_build=audio_build, import_=import_, log=lambda *a: None)
        self.assertEqual(rc, 7)
        self.assertEqual([x[0] for x in calls], ["prepare", "audio", "import"])
        self.assertEqual(calls[1], ("audio", Path("/w"), Path("/w/project")))
        self.assertEqual(calls[2], ("import", Path("/u/Unity"), Path("/w/project"), Path("/w/merged/Media"),
                                    Path("/w/reports")))

    def test_audio_failure_stops_before_import(self):
        calls = []
        c = SimpleNamespace(work_dir=Path("/w"), unity_editor=Path("/u/Unity"))

        def audio_build(work_dir, project):
            raise RuntimeError("converter broke")

        with self.assertRaises(RuntimeError):
            build_all.build_project(c, Path("/p"), Path("/w/project"), prepare=lambda *a: True,
                                    audio_build=audio_build, import_=lambda *a: calls.append("import") or 0,
                                    log=lambda *a: None)
        self.assertEqual(calls, [])


    def test_default_audio_step_is_the_audio_pipeline(self):
        """A fresh build_all run converts the audio and builds the graph through pipeline.audio.build."""
        c = SimpleNamespace(work_dir=Path("/w"), unity_editor=Path("/u/Unity"))
        with mock.patch.object(build_all.audio, "build") as build:
            build_all.build_project(c, Path("/p"), Path("/w/project"), prepare=lambda *a: False,
                                    import_=lambda *a: 0, log=lambda *a: None)
        build.assert_called_once_with(Path("/w"), Path("/w/project"))


if __name__ == "__main__":
    unittest.main()
