import unittest
from pathlib import Path

try:
    import yaml
except ImportError:
    yaml = None

WF = Path(__file__).resolve().parent.parent / ".github" / "workflows" / "release.yml"


@unittest.skipIf(yaml is None, "PyYAML not installed")
class WorkflowTest(unittest.TestCase):
    def test_matrix_builds_both_systems_as_drafts(self):
        wf = yaml.safe_load(WF.read_text())
        job = wf["jobs"]["build"]
        plats = {m["platform"]: m["artifact"] for m in job["strategy"]["matrix"]["include"]}
        self.assertEqual(plats, {"macos-arm64": "ThatsYou-Builder-macOS.dmg", "win-x64": "ThatsYou-Builder-Windows.exe"})
        self.assertEqual(wf["permissions"], {"contents": "read"})
        rel = wf["jobs"]["release"]
        self.assertEqual(rel["permissions"], {"contents": "write"})
        self.assertEqual(rel["needs"], "build")
        release = [s for s in rel["steps"] if str(s.get("uses", "")).startswith("softprops/action-gh-release")][0]
        self.assertTrue(release["with"]["draft"])
        import re
        for j in wf["jobs"].values():
            for st in j["steps"]:
                if "uses" in st:
                    self.assertRegex(st["uses"], r"@[0-9a-f]{40}$", "actions pinned to a commit")
        names = [s.get("name") for s in job["steps"]]
        self.assertIn("Tests", names)
        self.assertLess(names.index("Tests"), names.index("Package"))


if __name__ == "__main__":
    unittest.main()
