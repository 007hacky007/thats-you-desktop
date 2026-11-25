import tempfile
import unittest
from pathlib import Path

from pipeline import csc_check

RSP = """-target:library
-out:"Library/Bee/artifacts/x.dag/Assembly-CSharp.dll"
-refout:"Library/Bee/artifacts/x.dag/Assembly-CSharp.ref.dll"
-define:UNITY_6000
-r:"/U/UnityEngine.dll"
-r:"Library/Bee/artifacts/x.dag/Assembly-CSharp-firstpass.ref.dll"
-analyzer:"/U/a.dll"
-langversion:9.0
/additionalfile:"Library/Bee/artifacts/x.dag/Assembly-CSharp.UnityAdditionalFile.txt"
/nowarn:0169
"Assets/Scripts/Assembly-CSharp/A.cs"
"""


class CscCheckTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.p = Path(self.tmp.name) / "proj"
        for f in ("Assets/Scripts/Assembly-CSharp/A.cs", "Assets/Plugins/F/B.cs", "Assets/Editor/E.cs",
                  "Assets/Scripts/X/Editor/E2.cs", "Assets/Scripts/C (1).cs"):
            (self.p / f).parent.mkdir(parents=True, exist_ok=True)
            (self.p / f).write_text("//")
        self.extra = Path(self.tmp.name) / "repo/unity/Port"
        (self.extra / "Ps4").mkdir(parents=True)
        (self.extra / "Ps4/Stub.cs").write_text("//")

    def tearDown(self):
        self.tmp.cleanup()

    def test_options_keep_refs_and_defines_drop_outputs_analyzers_firstpass(self):
        opts = csc_check.compiler_options(RSP, out_dir=Path("/o"))
        self.assertIn("-define:UNITY_6000", opts)
        self.assertIn('-r:"/U/UnityEngine.dll"', opts)
        self.assertIn("-langversion:9.0", opts)
        self.assertNotIn("firstpass", " ".join(opts))
        self.assertFalse(any(o.startswith(("-analyzer", "/additionalfile", "-refout")) for o in opts))
        self.assertIn('-out:"/o/check.dll"', opts)
        self.assertFalse(any(o.startswith('"Assets/') for o in opts))

    def test_sources_skip_editor_folders_and_include_extra(self):
        srcs = csc_check.sources(self.p, [self.extra])
        names = sorted(Path(s).name for s in srcs)
        self.assertEqual(names, ["A.cs", "B.cs", "C (1).cs", "Stub.cs"])

    def test_synced_assets_port_not_compiled_twice(self):
        (self.p / "Assets/Port/Ps4").mkdir(parents=True)
        (self.p / "Assets/Port/Ps4/Stub.cs").write_text("//")
        srcs = csc_check.sources(self.p, [self.extra])
        self.assertEqual(sum(1 for s in srcs if s.endswith("Stub.cs")), 1)
        self.assertTrue(any(s.startswith(str(self.extra)) for s in srcs if s.endswith("Stub.cs")))

    def test_parse_errors_relative_paths(self):
        out = ('/abs/proj/Assets/Scripts/C (1).cs(3,4): error CS0103: The name x does not exist\n'
               'Assets/Plugins/F/B.cs(1,2): warning CS0618: obsolete\n'
               'Assets/Plugins/F/B.cs(5,6): error CS0246: The type Foo could not be found\n'
               'Assets/Plugins/F/B.cs(5,6): error CS0246: The type Foo could not be found\n')
        errs = csc_check.parse(out, Path("/abs/proj"))
        self.assertEqual(len(errs), 2)
        self.assertEqual(errs[0]["file"], "Assets/Scripts/C (1).cs")
        self.assertEqual(errs[0]["code"], "CS0103")
        self.assertEqual(errs[1]["line"], 5)


if __name__ == "__main__":
    unittest.main()
