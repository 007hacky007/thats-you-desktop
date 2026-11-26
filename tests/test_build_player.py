import subprocess
import tempfile
import unittest
from pathlib import Path

from pipeline import build_player

LOG = """\
Refreshing native plugins compatible for Editor in 12.34 ms
Assets/Scripts/Assembly-CSharp/Foo.cs(10,5): error CS0103: The name 'x' does not exist in the current context
Assets/Scripts/Assembly-CSharp/Foo.cs(10,5): error CS0103: The name 'x' does not exist in the current context
Assets/Scripts/Assembly-CSharp/Bar.cs(3,1): warning CS0618: obsolete
Shader error in 'Wish/Water': undeclared identifier 'foo' at line 12 (on metal)
Shader error in 'Wish/Water': undeclared identifier 'foo' at line 12 (on metal)
The referenced script (Unknown) on this Behaviour is missing!
The referenced script on this Behaviour (Game Object 'Menu') is missing!
The referenced script on this Behaviour (Game Object 'Menu') is missing!
[PortBuild] scenes: 15
DllNotFoundException: dlopen(/w/project/AkSoundEngine, 1): no such file
DllNotFoundException: dlopen(/w/project/AkSoundEngine, 1): no such file
Error building Player: 2 errors
Build Finished, Result: Failure.
"""


class SummarizeLogTests(unittest.TestCase):
    def test_categories_are_deduplicated_with_counts(self):
        s = build_player.summarize_log(LOG)
        self.assertEqual(s["compiler"], [("Assets/Scripts/Assembly-CSharp/Foo.cs(10,5): error CS0103: "
                                          "The name 'x' does not exist in the current context", 2)])
        self.assertEqual(s["shader"], [("Shader error in 'Wish/Water': undeclared identifier 'foo' "
                                        "at line 12 (on metal)", 2)])
        self.assertEqual(dict(s["missing_script"]), {
            "The referenced script on this Behaviour (Game Object 'Menu') is missing!": 2,
            "The referenced script (Unknown) on this Behaviour is missing!": 1})
        self.assertIn(("Error building Player: 2 errors", 1), s["build"])
        self.assertEqual(s["result"], "Failure")
        self.assertEqual(s["exception"], [("DllNotFoundException: dlopen(/w/project/AkSoundEngine, 1): no such file", 2)])
        self.assertEqual(s["port"], ["[PortBuild] scenes: 15"])

    def test_success_result(self):
        s = build_player.summarize_log("Build Finished, Result: Success.\n")
        self.assertEqual(s["result"], "Success")
        self.assertEqual(s["compiler"], [])

    def test_format_summary_mentions_counts(self):
        text = build_player.format_summary(build_player.summarize_log(LOG), rc=1)
        self.assertIn("exit code 1", text)
        self.assertIn("compiler errors: 1 distinct", text)
        self.assertIn("(x2)", text)


class LockTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.project = Path(self.tmp.name) / "project"
        (self.project / "Temp").mkdir(parents=True)

    def tearDown(self):
        self.tmp.cleanup()

    def fake_run(self, lsof_out="", ps_out=""):
        calls = []

        def run(argv, **kw):
            calls.append(argv)
            out = lsof_out if argv[0] == "lsof" else ps_out
            return subprocess.CompletedProcess(argv, 0 if out else 1, stdout=out, stderr="")
        return run, calls

    def test_no_lockfile_no_process_means_free(self):
        run, calls = self.fake_run(ps_out="  12 /usr/bin/other -projectPath /elsewhere\n")
        self.assertEqual(build_player.lock_holders(self.project, run=run), [])
        self.assertFalse(any(c[0] == "lsof" for c in calls))

    def test_lockfile_held_reports_pid(self):
        (self.project / "Temp/UnityLockfile").write_text("")
        run, _ = self.fake_run(lsof_out="4242\n")
        self.assertEqual(build_player.lock_holders(self.project, run=run), [4242])

    def test_stale_lockfile_is_free(self):
        (self.project / "Temp/UnityLockfile").write_text("")
        run, _ = self.fake_run(lsof_out="")
        self.assertEqual(build_player.lock_holders(self.project, run=run), [])

    def test_unity_process_with_project_path_counts(self):
        ps = (f"  77 /Applications/Unity/Hub/Editor/x/Unity.app/Contents/MacOS/Unity -projectpath {self.project} -x\n"
              f"  78 /bin/zsh -c echo {self.project}\n")
        run, _ = self.fake_run(ps_out=ps)
        self.assertEqual(build_player.lock_holders(self.project, run=run), [77])

    def test_build_argv(self):
        argv = build_player.build_argv(Path("/U/Unity"), Path("/w/project"), Path("/w/reports/build-mac.log"))
        self.assertEqual(argv, ["/U/Unity", "-batchmode", "-quit", "-projectPath", "/w/project",
                                "-executeMethod", "PortBuild.BuildMac", "-buildTarget", "OSXUniversal",
                                "-logFile", "/w/reports/build-mac.log"])


class InstallAudioTests(unittest.TestCase):
    def test_installs_when_staged_audio_exists(self):
        with tempfile.TemporaryDirectory() as t:
            work = Path(t)
            (work / "audio/PortAudio").mkdir(parents=True)
            (work / "audio/PortAudio/manifest.json").write_text("{}")
            calls, logs = [], []

            def install(staged, project):
                calls.append((staged, project))
                return {"copied": 2, "unchanged": 5, "deleted": 0, "pruned": 0}

            r = build_player.install_audio(work, work / "project", install=install, log=logs.append)
            self.assertEqual(calls, [(work / "audio/PortAudio", work / "project")])
            self.assertEqual(r["copied"], 2)
            self.assertIn("2 copied", logs[0])

    def test_warns_and_continues_without_staged_audio(self):
        with tempfile.TemporaryDirectory() as t:
            logs = []
            r = build_player.install_audio(Path(t), Path(t) / "project",
                                           install=lambda *a: self.fail("must not install"), log=logs.append)
            self.assertIsNone(r)
            self.assertEqual(logs, ["audio: no converted audio (run python3 -m pipeline.audio); "
                                    "the player will be silent"])


if __name__ == "__main__":
    unittest.main()
