import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from builder import state, steps


def choices(root: Path, **kw) -> steps.Choices:
    d = dict(base=root / "base", update=root / "upd", apk=root / "a.apk", host="tunnel",
             out=root / "out", work=root / "work")
    d.update(kw)
    return steps.Choices(**d)


class PlanTest(unittest.TestCase):
    def test_order(self):
        names = [s.name for s in steps.plan(choices(Path("/x")))]
        self.assertEqual(names, ["inputs", "unity", "merge", "extract-game", "extract-app", "project",
                                 "phone-page", "hosting", "player", "finish"])
        self.assertAlmostEqual(sum(s.weight for s in steps.plan(choices(Path("/x")))), 1.0, places=6)

    def test_config_written_for_the_pipeline(self):
        import os
        before = os.environ.get("IWYK_CONFIG")
        with tempfile.TemporaryDirectory() as d:
            c = choices(Path(d), apk=None, host="account")
            p = steps.write_config(c, page_url="https://x.example.workers.dev/")
            data = json.loads(p.read_text())
            self.assertEqual(data["work_dir"], str(Path(d) / "work"))
            self.assertNotIn("apk", data)
            self.assertEqual((data["page_host"], data["phone_page_url"]), ("url", "https://x.example.workers.dev/"))
            c = choices(Path(d))
            data = json.loads(steps.write_config(c).read_text())
            self.assertEqual(data["page_host"], "tunnel")
            self.assertEqual(data["apk"], str(Path(d) / "a.apk"))
        self.assertEqual(os.environ.get("IWYK_CONFIG"), before, "writing the config must not change the environment")


class RunTest(unittest.TestCase):
    def fake_steps(self, log, fail_at=None):
        out = []
        for i, name in enumerate(["a", "b", "c"]):
            def run(ctx, progress, name=name):
                log.append(name)
                progress(0.5, f"{name} half")
                if name == fail_at:
                    raise RuntimeError(f"{name} broke")
            out.append(steps.Step(name, 1 / 3, run, digest=lambda ctx, name=name: name + "-v1"))
        return out

    def test_rerun_skips_finished_steps_and_failure_leaves_no_mark(self):
        with tempfile.TemporaryDirectory() as d:
            st = state.State(Path(d))
            log, seen = [], []
            with self.assertRaises(RuntimeError):
                steps.run_all(self.fake_steps(log, fail_at="b"), None, st, lambda f, m: seen.append(f))
            self.assertEqual(log, ["a", "b"])
            self.assertTrue(st.done("a", "a-v1"))
            self.assertFalse(st.done("b", "b-v1"))
            self.assertEqual(seen, sorted(seen), "progress never goes back")
            log.clear()
            seen.clear()
            steps.run_all(self.fake_steps(log), None, state.State(Path(d)), lambda f, m: seen.append(f))
            self.assertEqual(log, ["b", "c"])
            self.assertEqual(seen[-1], 1.0)
            self.assertEqual(seen, sorted(seen), "progress never goes back")

    def test_changed_digest_reruns(self):
        with tempfile.TemporaryDirectory() as d:
            st = state.State(Path(d))
            st.mark("a", "a-v0")
            log = []
            steps.run_all(self.fake_steps(log), None, st, lambda f, m: None)
            self.assertEqual(log, ["a", "b", "c"])


class DiskTest(unittest.TestCase):
    def test_check_disk(self):
        with mock.patch("shutil.disk_usage", return_value=mock.Mock(free=10 * 2**30)):
            with self.assertRaises(steps.BuildError) as cm:
                steps.check_disk(Path("/"), need_gb=60)
            self.assertIn("60 GB", str(cm.exception))
        with mock.patch("shutil.disk_usage", return_value=mock.Mock(free=100 * 2**30)):
            steps.check_disk(Path("/"), need_gb=60)


class FinishTest(unittest.TestCase):
    def test_finish_moves_the_build_into_the_output_folder(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            build = d / "work" / "builds" / "mac"
            (build / "ThatsYou.app" / "Contents").mkdir(parents=True)
            (build / "iwyk-net.json").write_text("{}")
            (build / "PhonePage").mkdir()
            out = d / "out"
            got = steps.finish(build, out)
            self.assertEqual(got, out)
            self.assertTrue((out / "ThatsYou.app" / "Contents").is_dir())
            self.assertTrue((out / "iwyk-net.json").is_file())
            self.assertFalse(build.exists())


if __name__ == "__main__":
    unittest.main()
