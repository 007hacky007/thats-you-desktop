import tempfile
import unittest
from pathlib import Path
from unittest import mock

from pipeline import unity_install

NO_LICENSE_LOG = """[Licensing::Module] Error: Access token is unavailable; failed to update
No valid Unity Editor license found. Please activate your license.
"""
OK_LOG = """[Licensing::Module] Successfully resolved entitlement details
Exiting batchmode successfully now!
"""


class StatusTest(unittest.TestCase):
    def test_missing_unlicensed_ready(self):
        self.assertEqual(unity_install.status("v", find=lambda v: None, licensed=lambda e: True), "missing")
        self.assertEqual(unity_install.status("v", find=lambda v: Path("/u"), licensed=lambda e: False), "unlicensed")
        self.assertEqual(unity_install.status("v", find=lambda v: Path("/u"), licensed=lambda e: True), "ready")

    def test_licence_log_parsing(self):
        self.assertFalse(unity_install.log_says_licensed(NO_LICENSE_LOG, 1))
        self.assertTrue(unity_install.log_says_licensed(OK_LOG, 0))
        self.assertFalse(unity_install.log_says_licensed(OK_LOG, 1))

    def test_licensed_runs_a_throwaway_project(self):
        calls = []

        def run(argv, **kw):
            calls.append(argv)
            Path(argv[argv.index("-logFile") + 1]).write_text(OK_LOG)
            return mock.Mock(returncode=0)

        self.assertTrue(unity_install.licensed(Path("/u/Unity"), run=run))
        argv = calls[0]
        self.assertEqual(argv[0], "/u/Unity")
        for flag in ("-batchmode", "-quit", "-nographics", "-createProject"):
            self.assertIn(flag, argv)


class HubTest(unittest.TestCase):
    def test_hub_paths(self):
        self.assertEqual(unity_install.hub_exe("darwin"), Path("/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"))
        self.assertTrue(str(unity_install.hub_exe("win32")).endswith("Unity Hub.exe"))

    def test_headless_install_argv(self):
        argv = unity_install.install_argv(Path("/h/Unity Hub"), "6000.6.10f1", "40e81493c67d")
        self.assertEqual(argv, ["/h/Unity Hub", "--", "--headless", "install", "--version", "6000.6.10f1",
                                "--changeset", "40e81493c67d"])

    def test_install_needs_hub_and_reports_failure(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(unity_install.UnityInstallError):
                unity_install.install("v", "c", hub=Path(d) / "missing", run=lambda *a, **k: None)
            hub = Path(d) / "hub"
            hub.write_text("")
            found = iter([None, Path("/u/Unity")])
            got = unity_install.install("v", "c", hub=hub, run=lambda a, **k: mock.Mock(returncode=0, stdout="", stderr=""),
                                        find=lambda v: next(found))
            self.assertEqual(got, Path("/u/Unity"))
            with self.assertRaises(unity_install.UnityInstallError):
                unity_install.install("v", "c", hub=hub, run=lambda a, **k: mock.Mock(returncode=1, stdout="no", stderr=""),
                                      find=lambda v: None)

    def test_signature_check_on_macos(self):
        good = mock.Mock(returncode=0, stdout="", stderr="Authority=Developer ID Application: Unity Technologies SF (9QW8UQUTAA)\nTeamIdentifier=9QW8UQUTAA\n")
        bad = mock.Mock(returncode=0, stdout="", stderr="Authority=Developer ID Application: Unity Technologies SF (XXXX)\nTeamIdentifier=XXXX\n")
        self.assertTrue(unity_install.signed_by_unity(Path("/x.app"), "darwin", run=lambda a, **k: good))
        self.assertFalse(unity_install.signed_by_unity(Path("/x.app"), "darwin", run=lambda a, **k: bad))

    def test_signature_check_on_windows(self):
        good = mock.Mock(returncode=0, stdout="Valid|CN=Unity Technologies SF, O=Unity Technologies SF\n", stderr="")
        bad = mock.Mock(returncode=0, stdout="NotSigned|\n", stderr="")
        self.assertTrue(unity_install.signed_by_unity(Path("C:/x.exe"), "win32", run=lambda a, **k: good))
        self.assertFalse(unity_install.signed_by_unity(Path("C:/x.exe"), "win32", run=lambda a, **k: bad))


if __name__ == "__main__":
    unittest.main()
