import tempfile
import unittest
from pathlib import Path

from pipeline import game_edits as ge

SRC = """using System;

public class Foo : MonoBehaviour
{
\tprivate int m_a;

\tpublic void Bar(int x)
\t{
\t\tif (x > 0)
\t\t{
\t\t\tm_a = x;
\t\t}
\t\tDebug.Log("{ not a brace }");
\t}

\tpublic void Bar(string s)
\t{
\t\tm_a = s.Length;
\t}

\tpublic class Inner
\t{
\t\tpublic void Baz() { var t = transform.FindChild("x"); }
\t}
}
"""


class LocateTest(unittest.TestCase):
    def test_finds_method_span_with_braces_in_strings(self):
        s, e = ge.member_span(SRC, "Foo.Bar")
        body = SRC[s:e]
        self.assertTrue(body.lstrip().startswith("public void Bar(int x)"))
        self.assertTrue(body.rstrip().endswith("}"))
        self.assertNotIn("string s", body)

    def test_overload_by_index_and_nested_class(self):
        s, e = ge.member_span(SRC, "Foo.Bar#2")
        self.assertIn("string s", SRC[s:e])
        s, e = ge.member_span(SRC, "Foo.Inner.Baz")
        self.assertIn("FindChild", SRC[s:e])

    def test_missing_member_raises(self):
        with self.assertRaises(ge.EditError):
            ge.member_span(SRC, "Foo.Nope")


class OpsTest(unittest.TestCase):
    def test_in_member_edit_counts_matches(self):
        out = ge.InMember("Foo.Bar", [(r"m_a = x;", "m_a = x * 2;")]).apply_text(SRC)
        self.assertIn("m_a = x * 2;", out)
        self.assertIn("m_a = s.Length;", out)
        with self.assertRaises(ge.EditError):
            ge.InMember("Foo.Bar", [(r"nothing", "x")]).apply_text(SRC)

    def test_rename(self):
        out = ge.Rename(r"\.FindChild\(", ".Find(").apply_text(SRC)
        self.assertIn('transform.Find("x")', out)

    def test_replace_add_remove_member(self):
        out = ge.ReplaceMember("Foo.Bar#2", "\tpublic void Bar(string s) { }\n").apply_text(SRC)
        self.assertIn("public void Bar(string s) { }", out)
        out = ge.AddMembers("Foo", "\tpublic int Added;\n").apply_text(SRC)
        self.assertIn("public int Added;", out)
        self.assertLess(out.index("Added"), out.rindex("}"))
        out = ge.InsertAfter("Foo.Bar", "\tpublic int After;\n").apply_text(SRC)
        self.assertIn("\t}\n\tpublic int After;\n\n\tpublic void Bar(string s)", out)
        out = ge.RemoveMember("Foo.Bar").apply_text(SRC)
        self.assertNotIn("int x", out)
        self.assertIn("string s", out)

    def test_lines_checks_hash(self):
        import hashlib
        text = "a\nb\nc\n"
        sha = hashlib.sha256(b"b\n").hexdigest()[:16]
        self.assertEqual(ge.Lines(2, 1, sha, "B1\nB2\n").apply_text(text), "a\nB1\nB2\nc\n")
        self.assertEqual(ge.Lines(2, 0, hashlib.sha256(b"").hexdigest()[:16], "x\n").apply_text(text), "a\nx\nb\nc\n")
        with self.assertRaises(ge.EditError):
            ge.Lines(1, 1, sha, "").apply_text(text)

    def test_material_shader_map(self):
        mat = "Material:\n  m_Shader: {fileID: 4800000, guid: aaa, type: 3}\n"
        out = ge.MaterialShaderMap({(4800000, "aaa"): ("bbb", 4800000, 3)}).apply_text(mat)
        self.assertEqual(out, "Material:\n  m_Shader: {fileID: 4800000, guid: bbb, type: 3}\n")


class ApplyTest(unittest.TestCase):
    def test_apply_recipe_to_tree(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / "A").mkdir()
            (root / "A/Foo.cs").write_text(SRC)
            (root / "A/Gone.cs").write_text("x")
            recipe = ge.Recipe("t", [ge.Delete(["A/Gone.cs"]),
                                     ge.File("A/Foo.cs", [ge.Rename(r"FindChild", "Find")])])
            changed = ge.apply_recipe(root, recipe)
            self.assertFalse((root / "A/Gone.cs").exists())
            self.assertIn("Find(", (root / "A/Foo.cs").read_text())
            self.assertEqual(sorted(changed), ["A/Foo.cs", "A/Gone.cs"])

    def test_delete_of_missing_file_fails(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(ge.EditError):
                ge.apply_recipe(Path(d), ge.Recipe("t", [ge.Delete(["nope.cs"])]))


SCENE = """%YAML 1.1
--- !u!1 &10
GameObject:
  m_Component:
  - 4: {fileID: 11}
  - 114: {fileID: 12}
  - 114: {fileID: 13}
  m_Name: A
--- !u!114 &12
MonoBehaviour:
  m_GameObject: {fileID: 10}
  m_Script: {fileID: 11500000, guid: dead, type: 3}
--- !u!114 &13
MonoBehaviour:
  m_GameObject: {fileID: 10}
  m_Script: {fileID: 1301386320, guid: olddll, type: 3}
"""


class SceneTest(unittest.TestCase):
    def test_remove_missing_scripts_and_their_component_entries(self):
        out = ge.RemoveScripts({"dead"}).apply_text(SCENE)
        self.assertNotIn("&12", out)
        self.assertNotIn("fileID: 12}", out)
        self.assertIn("&13", out)

    def test_remap_script_reference(self):
        out = ge.ScriptRemap({(1301386320, "olddll"): (11500000, "pkg")}).apply_text(SCENE)
        self.assertIn("m_Script: {fileID: 11500000, guid: pkg, type: 3}", out)


if __name__ == "__main__":
    unittest.main()
