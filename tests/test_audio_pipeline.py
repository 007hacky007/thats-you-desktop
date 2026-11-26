import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

from pipeline import audio

SOURCE = Path("merged/Media/StreamingAssets/Audio/GeneratedSoundBanks/PS4")

REPORT = {
    "banks": [{"name": "Frontend", "id": 7, "language": "", "objects": {"event": 2}, "unparsed": []}],
    "events": [
        {"id": 1, "name": "PLAY_MUSIC_FRONTEND", "bank": "Frontend", "language": "",
         "media": [{"id": 100, "scope": "li"}, {"id": 101, "scope": "li"}], "reachesNothing": False,
         "missingTargets": []},
        {"id": 2, "name": "PLAY_COMMENTARY", "bank": "Commentary_Global_SoundBank", "language": "English(UK)",
         "media": [{"id": 200, "scope": "english_uk"}, {"id": 5, "scope": "external"}],
         "reachesNothing": False, "missingTargets": []},
        {"id": 3, "name": "", "bank": "Frontend", "language": "", "media": [], "reachesNothing": True,
         "missingTargets": [999]},
    ],
    "names": {"events": 3, "resolved": 2},
}


def write_json(p: Path, data) -> None:
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(data))


class Fakes:
    """Injectable steps that record calls and write the outputs the real modules would."""

    def __init__(self, work: Path):
        self.work = work
        self.calls = []
        self.graph_version = "g1"
        self.convert_version = "c1"

    def ns(self):
        return SimpleNamespace(ensure_vgmstream=self.ensure_vgmstream, ensure_codebooks=self.ensure_codebooks,
                               graph_build=self.graph_build, referenced_media=self.referenced_media,
                               convert=self.convert, install=self.install,
                               graph_version=self.graph_version, convert_version=self.convert_version)

    def ensure_vgmstream(self, work_dir):
        self.calls.append("vgmstream")
        return Path(work_dir) / "tools/vgm/vgmstream-cli"

    def ensure_codebooks(self, work_dir):
        self.calls.append("codebooks")
        return Path(work_dir) / "tools/src/vorbis_codebooks_wwise.h"

    def graph_build(self, source_root, out_root, *, report_dir, names=None, log=print):
        self.calls.append(("graph", Path(source_root), Path(out_root), Path(report_dir), names))
        write_json(Path(out_root) / "manifest.json", {"format": 1})
        write_json(Path(report_dir) / "graph-report.json", REPORT)
        return REPORT

    def referenced_media(self, report):
        self.calls.append("referenced")
        return {"li": {100, 101}, "english_uk": {200}}

    def convert(self, source_root, out_root, codebooks_header, *, report_dir, vgmstream=None, wanted=None,
                jobs=None, verify="sample", log=print):
        self.calls.append(("convert", Path(codebooks_header).name, Path(vgmstream).name, wanted, jobs, verify))
        out_root = Path(out_root)
        write_json(out_root / "media.json", {"format": 1, "scope": "li", "language": "", "externalPrefix": "",
                                              "media": [{"id": 100, "file": "media/100.ogg", "format": "ogg"},
                                                        {"id": 101, "file": "media/101.ogg", "format": "ogg"}],
                                              "external": []})
        (out_root / "media").mkdir(parents=True, exist_ok=True)
        (out_root / "media/100.ogg").write_bytes(b"OggS")  # 101 is in the table but its file is missing
        write_json(out_root / "lang/english_uk/media.json",
                   {"format": 1, "scope": "english_uk", "language": "English(UK)", "externalPrefix": "en-GB_",
                    "media": [], "external": [{"name": "en-gb_ty1", "file": "lang/english_uk/ext/en-GB_TY1.ogg"}]})
        rep = {"scopes": {}, "verify": {"mode": verify, "checked": 0, "mismatch": []},
               "totals": {"bytesIn": 0, "bytesOut": 0, "seconds": 0}}
        write_json(Path(report_dir) / "convert-report.json", rep)
        return rep

    def install(self, staged, project):
        self.calls.append(("install", Path(staged), Path(project)))
        return {"copied": 1, "unchanged": 0, "deleted": 0, "pruned": 0}


class AudioBuildTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.work = Path(self.tmp.name) / "work"
        (self.work / SOURCE).mkdir(parents=True)
        write_json(self.work / "merged/.merge-manifest.json", {"digest": "m1"})
        self.project = self.work / "project"
        (self.project / "Assets/Scripts/Assembly-CSharp").mkdir(parents=True)
        self.fakes = Fakes(self.work)

    def tearDown(self):
        self.tmp.cleanup()

    def run_build(self, **kw):
        kw.setdefault("log", lambda *a: None)
        return audio.build(self.work, self.project, steps=self.fakes.ns(), **kw)

    def names(self, calls):
        return [c if isinstance(c, str) else c[0] for c in calls]

    def test_full_run_order_and_arguments(self):
        r = self.run_build(jobs=3, verify="all")
        self.assertEqual(self.names(self.fakes.calls),
                         ["vgmstream", "codebooks", "graph", "referenced", "convert", "install"])
        graph = self.fakes.calls[2]
        self.assertEqual(graph[1], self.work / SOURCE)
        self.assertEqual(graph[2], self.work / "audio/PortAudio")
        self.assertEqual(graph[3], self.work / "audio/reports")
        self.assertIsNone(graph[4])  # no AK.cs in this project
        conv = self.fakes.calls[4]
        self.assertEqual(conv, ("convert", "vorbis_codebooks_wwise.h", "vgmstream-cli",
                                {"li": {100, 101}, "english_uk": {200}}, 3, "all"))
        self.assertEqual(self.fakes.calls[5], ("install", self.work / "audio/PortAudio", self.project))
        self.assertEqual(r["graph"], "built")
        self.assertEqual(r["convert"], "built")
        self.assertEqual(r["install"]["copied"], 1)

    def test_names_file_is_ak_cs_when_present(self):
        ak = self.project / "Assets/Scripts/Assembly-CSharp/AK.cs"
        ak.write_text("public class AK {}\n")
        self.run_build()
        self.assertEqual(self.fakes.calls[2][4], ak)

    def test_unchanged_inputs_skip_graph_and_convert(self):
        self.run_build()
        self.fakes.calls.clear()
        r = self.run_build()
        self.assertEqual(self.names(self.fakes.calls), ["vgmstream", "codebooks", "install"])
        self.assertEqual((r["graph"], r["convert"]), ("skipped", "skipped"))
        self.assertTrue((self.work / "audio/reports/coverage.json").is_file())

    def test_force_redoes_everything(self):
        self.run_build()
        self.fakes.calls.clear()
        self.run_build(force=True)
        self.assertIn("graph", self.names(self.fakes.calls))
        self.assertIn("convert", self.names(self.fakes.calls))

    def test_changed_inputs_rerun_the_affected_steps(self):
        self.run_build()
        self.fakes.calls.clear()
        self.run_build(verify="none")  # verify mode only feeds the converter
        self.assertEqual(self.names(self.fakes.calls), ["vgmstream", "codebooks", "referenced", "convert", "install"])
        self.fakes.calls.clear()
        self.fakes.graph_version = "g2"
        self.run_build(verify="none")
        self.assertIn("graph", self.names(self.fakes.calls))
        self.fakes.calls.clear()
        self.fakes.convert_version = "c2"
        self.run_build(verify="none")
        self.assertEqual(self.names(self.fakes.calls), ["vgmstream", "codebooks", "referenced", "convert", "install"])
        self.fakes.calls.clear()
        write_json(self.work / "merged/.merge-manifest.json", {"digest": "m2"})
        self.run_build(verify="none")
        self.assertIn("graph", self.names(self.fakes.calls))
        self.assertIn("convert", self.names(self.fakes.calls))

    def test_missing_outputs_rerun_the_step(self):
        self.run_build()
        (self.work / "audio/PortAudio/media.json").unlink()
        self.fakes.calls.clear()
        self.run_build()
        self.assertEqual(self.names(self.fakes.calls), ["vgmstream", "codebooks", "referenced", "convert", "install"])

    def test_no_install(self):
        r = self.run_build(install=False)
        self.assertNotIn("install", self.names(self.fakes.calls))
        self.assertIsNone(r["install"])
        self.fakes.calls.clear()
        audio.build(self.work, None, steps=self.fakes.ns(), log=lambda *a: None, force=True)
        self.assertNotIn("install", self.names(self.fakes.calls))

    def test_graph_only(self):
        r = self.run_build(graph_only=True)
        self.assertEqual(self.names(self.fakes.calls), ["graph"])
        self.assertEqual(r["convert"], "not run")
        cov = json.loads((self.work / "audio/reports/coverage.json").read_text())
        self.assertEqual(cov["summary"]["events"], 3)

    def test_missing_source_fails_clearly(self):
        import shutil
        shutil.rmtree(self.work / SOURCE)
        with self.assertRaises(FileNotFoundError) as cm:
            self.run_build()
        self.assertIn("GeneratedSoundBanks", str(cm.exception))

    def test_coverage_report(self):
        self.run_build()
        cov = json.loads((self.work / "audio/reports/coverage.json").read_text())
        ev = {e["id"]: e for e in cov["events"]}
        self.assertEqual(ev[1]["name"], "PLAY_MUSIC_FRONTEND")
        self.assertEqual(ev[1]["bank"], "Frontend")
        self.assertEqual(ev[1]["media"], 2)
        self.assertEqual(ev[1]["missing"], [{"id": 101, "scope": "li", "reason": "file missing"}])
        self.assertFalse(ev[1]["ok"])
        self.assertEqual(ev[2]["media"], 1)
        self.assertEqual(ev[2]["external"], 1)
        self.assertEqual(ev[2]["missing"], [{"id": 200, "scope": "english_uk", "reason": "not converted"}])
        self.assertTrue(ev[3]["reachesNothing"])
        self.assertEqual(ev[3]["missingTargets"], [999])
        self.assertEqual(cov["summary"], {"events": 3, "ok": 0, "withMissingMedia": 2, "reachesNothing": 1,
                                          "missingMedia": 2, "named": 2})
        md = (self.work / "audio/reports/coverage.md").read_text()
        self.assertIn("PLAY_MUSIC_FRONTEND", md)
        self.assertIn("| Frontend |", md)
        self.assertTrue(md.isascii())


class CoverageTests(unittest.TestCase):
    def test_all_media_present_is_ok(self):
        with tempfile.TemporaryDirectory() as t:
            out = Path(t)
            write_json(out / "media.json", {"scope": "li", "media": [{"id": 100, "file": "media/100.ogg"}]})
            (out / "media").mkdir()
            (out / "media/100.ogg").write_bytes(b"OggS")
            rep = {"events": [{"id": 1, "name": "A", "bank": "B", "language": "", "media": [{"id": 100, "scope": "li"}],
                               "reachesNothing": False, "missingTargets": []}]}
            cov = audio.coverage(rep, out)
            self.assertTrue(cov["events"][0]["ok"])
            self.assertEqual(cov["summary"]["ok"], 1)


class CliTests(unittest.TestCase):
    def test_parse_args(self):
        a = audio.parse_args(["--verify", "none", "--jobs", "4", "--no-install", "--force", "--graph-only"])
        self.assertEqual((a.verify, a.jobs, a.no_install, a.force, a.graph_only), ("none", 4, True, True, True))
        a = audio.parse_args([])
        self.assertEqual((a.verify, a.jobs, a.no_install, a.force, a.graph_only), ("sample", None, False, False, False))
        with self.assertRaises(SystemExit), contextlib.redirect_stderr(io.StringIO()):
            audio.parse_args(["--verify", "some"])


if __name__ == "__main__":
    unittest.main()
