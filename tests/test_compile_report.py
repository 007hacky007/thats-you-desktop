import tempfile
import unittest
from pathlib import Path
from unittest import mock

from pipeline import compile_report, unity

LOG = """\
Some header line
Assets/Scripts/Assembly-CSharp/PS4Wifi.cs(12,7): error CS0234: The type or namespace name 'PS4' does not exist in the namespace 'UnityEngine' (are you missing an assembly reference?)
Assets/Scripts/Assembly-CSharp/PS4Wifi.cs(40,3): error CS0234: The type or namespace name 'PS4' does not exist in the namespace 'UnityEngine' (are you missing an assembly reference?)
Assets/Scripts/Assembly-CSharp/Foo.cs(5,9): error CS0619: 'GUIText' is obsolete: 'GUIText has been removed. Use UI.Text instead.'
Assets/Scripts/PlayMaker/HutongGames/Fsm.cs(1,1): error CS1061: 'Thing' does not contain a definition for 'x'
Assets/Scripts/Assembly-UnityScript/Blink.cs(3,3): error CS0103: The name 'y' does not exist in the current context
Assets/Scripts/Assembly-CSharp/Bar.cs(7,7): error CS0246: The type or namespace name 'Sony' could not be found
Assets/Scripts/Assembly-CSharp/Baz.cs(8,8): error CS0029: Cannot implicitly convert type 'int' to 'string'
Assets/Scripts/Assembly-CSharp/Foo.cs(9,9): warning CS0618: something is obsolete
"""


class CompileReportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.log = Path(self.tmp.name) / "unity.log"

    def tearDown(self):
        self.tmp.cleanup()

    def test_parses_cs_errors(self):
        self.log.write_text(LOG)
        r = compile_report.parse(self.log)
        self.assertEqual(r.total, 7)
        cats = r.by_category()
        self.assertEqual(cats["ps4_api"], 3)
        self.assertEqual(cats["removed_unity_api"], 1)
        self.assertEqual(cats["third_party"], 1)
        self.assertEqual(cats["unityscript"], 1)
        self.assertEqual(cats["other"], 1)
        first = r.groups[("CS0234", "Assembly-CSharp", "Assets/Scripts/Assembly-CSharp/PS4Wifi.cs")]
        self.assertEqual(first.category, "ps4_api")

    def test_groups_duplicates(self):
        self.log.write_text(LOG)
        r = compile_report.parse(self.log)
        self.assertEqual(r.groups[("CS0234", "Assembly-CSharp", "Assets/Scripts/Assembly-CSharp/PS4Wifi.cs")].count, 2)
        self.assertEqual(len(r.groups), 6)

    def test_duplicate_lines_counted_once(self):
        # Unity often prints the same error twice (console + summary)
        line = LOG.splitlines()[1]
        self.log.write_text(line + "\n" + line + "\n")
        self.assertEqual(compile_report.parse(self.log).total, 1)

    def test_no_errors_report_says_clean(self):
        self.log.write_text("Compilation succeeded\n")
        r = compile_report.parse(self.log)
        self.assertEqual(r.total, 0)
        self.assertIn("No compile errors", r.markdown())

    def test_partial_compile_flagged(self):
        # firstpass failed: Unity never compiled the later assemblies, so counts are a lower bound
        self.log.write_text(
            "Assets/Plugins/Assembly-CSharp-firstpass/ForcedReset.cs(3,3): error CS0619: 'GUITexture' is obsolete\n"
            "Scripts have compiler errors.\n")
        r = compile_report.parse(self.log)
        self.assertTrue(r.partial)
        self.assertIn("lower bound", r.markdown())

    def test_full_compile_not_partial(self):
        self.log.write_text(LOG + "Scripts have compiler errors.\n")
        self.assertFalse(compile_report.parse(self.log).partial)


class UnityRunTests(unittest.TestCase):
    def test_run_batch_argv(self):
        with mock.patch("subprocess.run") as run:
            run.return_value.returncode = 0
            rc = unity.run_batch(Path("/U/Unity"), Path("/p roj"), ["-buildTarget", "OSXUniversal"], Path("/l.log"))
        self.assertEqual(rc, 0)
        argv = run.call_args.args[0]
        self.assertEqual(argv[:6], ["/U/Unity", "-batchmode", "-nographics", "-quit", "-projectPath", "/p roj"])
        self.assertIn("-logFile", argv)
        self.assertEqual(argv[argv.index("-logFile") + 1], "/l.log")
        self.assertFalse(run.call_args.kwargs.get("shell", False))


if __name__ == "__main__":
    unittest.main()
