import tempfile
import unittest
from pathlib import Path

from pipeline import guid_align

G = {n: (c * 32) for n, c in (("tex", "1"), ("mat", "2"), ("shader", "3"), ("new_tex", "a"), ("new_mat", "b"),
                              ("new_shader", "c"), ("other", "4"))}


def export(root: Path, tex: str, mat: str, shader: str) -> Path:
    a = root / "Assets"
    (a / "Texture2D").mkdir(parents=True)
    (a / "Material").mkdir()
    (a / "Shader").mkdir()
    (a / "Texture2D" / "wood.png").write_bytes(b"\x89PNG binary guid: " + tex.encode())
    (a / "Texture2D" / "wood.png.meta").write_text(f"fileFormatVersion: 2\nguid: {tex}\n")
    (a / "Shader" / "Lit.shader").write_text("Shader \"Lit\" {}\n")
    (a / "Shader" / "Lit.shader.meta").write_text(f"fileFormatVersion: 2\nguid: {shader}\n")
    (a / "Material" / "Wood.mat").write_text(
        "%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n"
        f"  m_Shader: {{fileID: 4800000, guid: {shader}, type: 3}}\n"
        f"  - _MainTex:\n      m_Texture: {{fileID: 2800000, guid: {tex}, type: 3}}\n")
    (a / "Material" / "Wood.mat.meta").write_text(f"fileFormatVersion: 2\nguid: {mat}\n")
    (a / "Scene.unity").write_text(f"%YAML 1.1\n  m_Materials:\n  - {{fileID: 2100000, guid: {mat}, type: 2}}\n"
                                   f"  - {{fileID: 2100000, guid: {G['other']}, type: 2}}\n")
    return root


class AlignTest(unittest.TestCase):
    def test_table_round_trip_and_alignment(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            ref = export(d / "ref", G["tex"], G["mat"], G["shader"])
            table = guid_align.make_table(ref)
            self.assertEqual(len(table), 3)
            self.assertTrue(all(len(k) == 16 for k in table), "paths are stored only as short hashes")
            self.assertNotIn("wood", repr(table).lower())

            fresh = export(d / "fresh", G["new_tex"], G["new_mat"], G["new_shader"])
            stats = guid_align.align(fresh, table)
            self.assertEqual(stats["remapped"], 3)
            a = fresh / "Assets"
            self.assertIn(G["tex"], (a / "Texture2D" / "wood.png.meta").read_text())
            mat = (a / "Material" / "Wood.mat").read_text()
            self.assertIn(G["shader"], mat)
            self.assertIn(G["tex"], mat)
            self.assertNotIn(G["new_shader"], mat)
            scene = (a / "Scene.unity").read_text()
            self.assertIn(G["mat"], scene)
            self.assertIn(G["other"], scene, "unknown references are left alone")
            self.assertIn(G["new_tex"].encode(), (a / "Texture2D" / "wood.png").read_bytes(), "binary files untouched")
            # the result matches the reference export file for file
            for p in ref.rglob("*"):
                if p.is_file():
                    self.assertEqual(p.read_bytes() if p.suffix == ".png" else p.read_text(),
                                     (fresh / p.relative_to(ref)).read_bytes() if p.suffix == ".png"
                                     else (fresh / p.relative_to(ref)).read_text(), p.name) if p.suffix != ".png" else None
            self.assertEqual(guid_align.align(fresh, table)["remapped"], 0, "a second run changes nothing")

    def test_swapped_guids_are_handled(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            table = guid_align.make_table(export(d / "ref", G["tex"], G["mat"], G["shader"]))
            fresh = export(d / "fresh", G["mat"], G["tex"], G["shader"])  # two assets swapped ids
            guid_align.align(fresh, table)
            self.assertIn(G["tex"], (fresh / "Assets/Texture2D/wood.png.meta").read_text())
            self.assertIn(G["mat"], (fresh / "Assets/Material/Wood.mat.meta").read_text())
            self.assertIn(f"guid: {G['tex']}, type: 3", (fresh / "Assets/Material/Wood.mat").read_text())


if __name__ == "__main__":
    unittest.main()


class EnsureTest(unittest.TestCase):
    def test_ensure_aligns_once(self):
        from unittest import mock
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            table = guid_align.make_table(export(d / "ref", G["tex"], G["mat"], G["shader"]))
            fresh = export(d / "fresh", G["new_tex"], G["new_mat"], G["new_shader"])
            self.assertEqual(guid_align.ensure(fresh, table)["remapped"], 3)
            with mock.patch.object(guid_align, "align", side_effect=AssertionError("already aligned")):
                self.assertEqual(guid_align.ensure(fresh, table), {"remapped": 0, "files": 0, "skipped": True})
