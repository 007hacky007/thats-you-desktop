"""Runs go vet and go test for the mock console (tools/mockconsole)."""

import json
import shutil
import subprocess
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
MODULE = REPO / "tools" / "mockconsole"
IWYKNET = REPO / "native" / "iwyknet"


def _skip_reason():
    if shutil.which("go") is None:
        return "go is not installed"
    if not (IWYKNET / "server").is_dir() or not (IWYKNET / "client").is_dir():
        return "native/iwyknet (server and client packages) is missing"
    return None


@unittest.skipIf(_skip_reason() is not None, _skip_reason() or "")
class GoMockConsoleTests(unittest.TestCase):
    def go(self, *args, timeout=900):
        proc = subprocess.run(["go", *args], cwd=MODULE, capture_output=True, text=True, timeout=timeout)
        if proc.returncode != 0:
            self.fail("go %s failed:\n%s\n%s" % (" ".join(args), proc.stdout[-4000:], proc.stderr[-4000:]))
        return proc.stdout

    def test_vet(self):
        self.go("vet", "./...")

    def test_go_test(self):
        self.go("test", "./...")


class CatalogueFileTests(unittest.TestCase):
    """Cheap structural checks of protocol/messages.json that need no Go."""

    def setUp(self):
        self.cat = json.loads((REPO / "protocol" / "messages.json").read_text(encoding="utf-8"))

    def test_constants(self):
        self.assertEqual(self.cat["schema"], 1)
        self.assertEqual(self.cat["interfaceVersion"], "2016-07-12-1645")
        self.assertEqual(self.cat["jsonTransferId"], -7503)

    def test_messages_well_formed(self):
        types = {"int", "long", "double", "bool", "string", "string[]"}
        for name, m in self.cat["messages"].items():
            self.assertIn(m["dir"], ("p2g", "g2p", "both"), name)
            if "base" in m:
                self.assertIn(m["base"], self.cat["messages"], name)
            for f in m["fields"]:
                t = f["type"]
                ok = t in types or (t.split(":", 1)[0] in ("object", "list") and t.split(":", 1)[1] in self.cat["types"])
                self.assertTrue(ok, "%s.%s: %s" % (name, f["name"], t))

    def test_ascii_only(self):
        (REPO / "protocol" / "messages.json").read_bytes().decode("ascii")


if __name__ == "__main__":
    unittest.main()
