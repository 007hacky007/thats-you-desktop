import json
import tempfile
import unittest
from pathlib import Path

from pipeline import config
from pipeline.config import ConfigError


def make_inputs(root: Path, apk_name: str = "app.apk") -> dict:
    base = root / "base"
    patch = root / "patch"
    (base / "sce_sys").mkdir(parents=True)
    (base / "eboot.bin").write_bytes(b"\x7fELF")
    (base / "sce_sys" / "param.sfo").write_bytes(b"x")
    (patch / "sce_sys" / "changeinfo").mkdir(parents=True)
    (patch / "sce_sys" / "param.sfo").write_bytes(b"x")
    apk = root / apk_name
    apk.write_bytes(b"PK")
    return {"base_dump": str(base), "patch_dump": str(patch), "apk": str(apk)}


class ConfigTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def write(self, data: dict) -> Path:
        p = self.root / "local.json"
        p.write_text(json.dumps(data))
        return p

    def test_loads_valid_config(self):
        data = make_inputs(self.root)
        data["work_dir"] = str(self.root / "w")
        c = config.load(self.write(data))
        self.assertEqual(c.base_dump, Path(data["base_dump"]))
        self.assertEqual(c.patch_dump, Path(data["patch_dump"]))
        self.assertEqual(c.apk, Path(data["apk"]))
        self.assertEqual(c.work_dir, self.root / "w")
        self.assertIsNone(c.unity_editor)
        self.assertIsNone(c.phone_page_url)

    def test_missing_key_names_key(self):
        data = make_inputs(self.root)
        del data["apk"]
        with self.assertRaises(ConfigError) as cm:
            config.load(self.write(data))
        self.assertEqual(cm.exception.key, "apk")

    def test_patch_equal_to_base_rejected(self):
        data = make_inputs(self.root)
        data["patch_dump"] = data["base_dump"]
        with self.assertRaises(ConfigError) as cm:
            config.load(self.write(data))
        self.assertEqual(cm.exception.key, "patch_dump")

    def test_base_without_eboot_rejected(self):
        data = make_inputs(self.root)
        (Path(data["base_dump"]) / "eboot.bin").unlink()
        with self.assertRaises(ConfigError) as cm:
            config.load(self.write(data))
        self.assertEqual(cm.exception.key, "base_dump")

    def test_work_dir_defaults_to_repo_work(self):
        c = config.load(self.write(make_inputs(self.root)))
        self.assertEqual(c.work_dir, config.REPO_ROOT / "work")

    def test_paths_with_spaces_and_parens(self):
        c = config.load(self.write(make_inputs(self.root, "a (b) c.apk")))
        self.assertEqual(c.apk.name, "a (b) c.apk")

    def test_missing_config_file_names_path(self):
        with self.assertRaises(ConfigError) as cm:
            config.load(self.root / "nope.json")
        self.assertEqual(cm.exception.key, "config")


if __name__ == "__main__":
    unittest.main()
