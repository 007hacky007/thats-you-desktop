"""Offscreen tests of the builder window (needs PySide6: run with work/venv-builder/bin/python)."""
import os
import tempfile
import time
import unittest
from pathlib import Path

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
try:
    from PySide6.QtWidgets import QApplication
except ImportError:  # the plain test run has no Qt; the builder venv has
    QApplication = None

from pipeline import inputs


def info(supported=True, kind="folder"):
    return inputs.GameInfo("CUSA08389", "EP9000-CUSA08389_00-1705161243TYM1E2", "01.00", kind, supported)


@unittest.skipIf(QApplication is None, "PySide6 not installed")
class WindowTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from builder import gui
        cls.gui = gui
        cls.app = QApplication.instance() or QApplication([])

    def make(self, **kw):
        opened = []
        built = []

        def check_game(path, role):
            if "bad" in str(path):
                raise inputs.InputError("not That's You!")
            return info(supported="old" not in str(path))

        def check_apk(path):
            if "wrong" in str(path):
                raise inputs.InputError("this is not the That's You! app version 1.6")

        def build(choices, progress, log):
            built.append(choices)
            for i in range(5):
                progress(i / 4, f"step {i}")
            log("all done")
            return choices.out

        services = self.gui.Services(check_game=check_game, check_apk=check_apk,
                                     unity_status=kw.get("unity_status", lambda: "ready"),
                                     hub_installed=lambda: True, open_url=opened.append, build=build,
                                     cf_login=kw.get("cf_login", lambda: True))
        w = self.gui.Window(services, lang="en")
        return w, opened, built

    def test_game_page_needs_both_inputs(self):
        w, _, _ = self.make()
        self.assertFalse(w.next_button.isEnabled())
        w.set_game("base", Path("/g/base"))
        self.assertFalse(w.next_button.isEnabled())
        w.set_game("update", Path("/g/update"))
        self.assertTrue(w.next_button.isEnabled())
        w.set_game("update", Path("/g/bad"))
        self.assertFalse(w.next_button.isEnabled())
        self.assertIn("not That's You!", w.game_status["update"].text())

    def test_untested_release_needs_try_anyway(self):
        w, _, _ = self.make()
        w.set_game("base", Path("/g/old-base"))
        w.set_game("update", Path("/g/update"))
        self.assertFalse(w.next_button.isEnabled())
        self.assertTrue(w.try_untested.isVisible() or not w.isVisible())
        w.try_untested.setChecked(True)
        self.assertTrue(w.next_button.isEnabled())

    def test_apk_page(self):
        w, opened, _ = self.make()
        w.go("apk")
        self.assertFalse(w.next_button.isEnabled())
        w.find_apk.click()
        self.assertEqual(opened, [inputs.APK_SEARCH_URL])
        w.set_apk(Path("/a/wrong.apk"))
        self.assertFalse(w.next_button.isEnabled())
        self.assertIn("1.6", w.apk_status.text())
        self.assertFalse(w.disclaimer.isVisible() and not w.no_apk.isChecked())
        w.no_apk.setChecked(True)
        self.assertTrue(w.next_button.isEnabled())
        self.assertIn("not been really tested", w.disclaimer.text())
        w.no_apk.setChecked(False)
        w.set_apk(Path("/a/good.apk"))
        self.assertTrue(w.next_button.isEnabled())

    def test_hosting_choice_and_unity_gate(self):
        w, _, _ = self.make(unity_status=lambda: "unlicensed")
        w.go("hosting")
        self.assertTrue(w.use_tunnel.isChecked())
        w.go("unity")
        w.refresh_unity()
        self.assertFalse(w.next_button.isEnabled())
        self.assertIn("sign in", w.unity_status_label.text().lower())

    def test_build_runs_to_the_end(self):
        w, _, built = self.make()
        with tempfile.TemporaryDirectory() as d:
            w.set_game("base", Path("/g/base"))
            w.set_game("update", Path("/g/update"))
            w.set_apk(Path("/a/good.apk"))
            w.use_account.setChecked(True)
            w.go("build")
            w.out_edit.setText(d)
            w.start_build()
            deadline = time.time() + 10
            while w.progress.value() < 100 and time.time() < deadline:
                self.app.processEvents()
                time.sleep(0.01)
            self.assertEqual(w.progress.value(), 100)
            self.assertEqual(built[0].host, "account")
            self.assertEqual(built[0].apk, Path("/a/good.apk"))
            self.assertTrue(w.open_game.isEnabled())
            self.assertIn("all done", w.details.toPlainText())

    def test_czech_texts(self):
        from builder import texts
        self.assertEqual(set(texts.TEXTS["en"]), set(texts.TEXTS["cs"]))
        for t in list(texts.TEXTS["en"].values()) + list(texts.TEXTS["cs"].values()):
            self.assertNotIn("—", t)
            self.assertNotIn("–", t)


if __name__ == "__main__":
    unittest.main()
