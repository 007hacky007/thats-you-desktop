"""Runs the phone page's own tests (node --test over web/test) as part of the Python suite."""
import shutil
import subprocess
import unittest

from pipeline.web_common import WEB_SRC

TEST_DIR = WEB_SRC / "test"
NODE = shutil.which("node")


@unittest.skipUnless(NODE, "node not installed")
@unittest.skipUnless(TEST_DIR.is_dir(), "web/test does not exist yet")
class NodeWebTests(unittest.TestCase):
    def test_node_test_suite(self):
        files = sorted(str(p) for p in TEST_DIR.rglob("*.test.mjs"))
        if not files:
            self.skipTest("no *.test.mjs files in web/test")
        proc = subprocess.run([NODE, "--test", *files], cwd=str(WEB_SRC.parent), stdout=subprocess.PIPE,
                              stderr=subprocess.STDOUT, text=True, timeout=600)
        if proc.returncode != 0:
            self.fail("node --test web/test failed:\n" + proc.stdout[-8000:])


if __name__ == "__main__":
    unittest.main()
