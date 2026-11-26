"""Tests for pipeline.audio_convert on a synthetic GeneratedSoundBanks tree."""
import json
import os
import struct
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

from pipeline import audio_convert as ac
from tests.test_wwise_vorbis import build_wem, read_packets, read_pages, synthetic_header

FAKE_VG = r'''#!{python}
import os, struct, sys
args = sys.argv[1:]
out, src = args[args.index("-o") + 1], args[-1]
log = os.environ.get("FAKE_VG_LOG")
if log:
    with open(log, "a") as f:
        f.write(os.path.basename(src) + "\n")
data = open(src, "rb").read()
if b"FAILVG" in data:
    sys.exit(3)
value = 0
if os.environ.get("FAKE_VG_MODE") == "mismatch" and src.endswith(".ogg"):
    value = 5
pcm = struct.pack("<h", value) * 100
fmt = struct.pack("<HHIIHH", 1, 1, 48000, 96000, 2, 16)
body = b"WAVE" + b"fmt " + struct.pack("<I", 16) + fmt + b"data" + struct.pack("<I", len(pcm)) + pcm
open(out, "wb").write(b"RIFF" + struct.pack("<I", len(body)) + body)
'''


def wav(frames, channels=1, rate=48000):
    pcm = struct.pack(f"<{len(frames)}h", *frames)
    fmt = struct.pack("<HHIIHH", 1, channels, rate, rate * 2 * channels, 2 * channels, 16)
    body = b"WAVE" + b"fmt " + struct.pack("<I", 16) + fmt + b"data" + struct.pack("<I", len(pcm)) + pcm
    return b"RIFF" + struct.pack("<I", len(body)) + body


def bank(bank_id, lang_id, media):
    def chunk(cid, body):
        return cid + struct.pack("<I", len(body)) + body
    out = chunk(b"BKHD", struct.pack("<IIIII", 113, bank_id, lang_id, 0, 0))
    if media:
        didx, data = b"", b""
        for mid, blob in media:
            data += b"\0" * (-len(data) % 16)
            didx += struct.pack("<III", mid, len(data), len(blob))
            data += blob
        out += chunk(b"DIDX", didx) + chunk(b"DATA", data)
    return out + chunk(b"HIRC", struct.pack("<I", 0))


class Tree:
    def __init__(self, root: Path):
        self.root = root
        self.src = root / "PS4"
        self.out = root / "audio" / "PortAudio"
        self.reports = root / "audio" / "reports"
        self.header = root / "vorbis_codebooks_wwise.h"
        self.header.write_text(synthetic_header())
        li, en = self.src / "LocaleIndependent", self.src / "English(UK)"
        li.mkdir(parents=True)
        en.mkdir()
        self.stereo, _ = build_wem(channels=2, rate=44100, cues=((0, "Stereo_One"),))
        self.mono, _ = build_wem(samples=3500, cues=((0, "Mono_Two"), (100, "1j")))
        (li / "Init.bnk").write_bytes(bank(1355168291, 0, []))
        (li / "A_SoundBank.bnk").write_bytes(bank(1, 0, [(10, self.mono), (11, self.stereo)]))
        (li / "B_SoundBank.bnk").write_bytes(bank(2, 0, [(10, self.mono), (12, self.mono)]))
        (en / "Commentary_X_SoundBank.bnk").write_bytes(bank(3, 11, [(20, self.mono), (21, self.mono)]))
        voice, _ = build_wem(cues=((0, "en-GB_TY1"),))
        for name in ("20.wem", "30.wem", "31.wem"):
            (en / name).write_bytes(self.mono)
        (en / "en-GB_TY1.wem").write_bytes(voice)
        (en / "en-GB_Hello World.wem").write_bytes(voice)
        (en / "Wwise.dat").write_bytes(b"junk")
        self.vg = root / "fake-vgmstream"
        self.vg.write_text(FAKE_VG.replace("{python}", sys.executable))
        self.vg.chmod(0o755)

    def run(self, **kw):
        kw.setdefault("wanted", {"english_uk": {20, 30}})
        kw.setdefault("jobs", 1)
        kw.setdefault("verify", "none")
        logs = []
        r = ac.convert(self.src, self.out, self.header, report_dir=self.reports, log=logs.append, **kw)
        self.logs = logs
        return r

    def table(self, rel):
        return json.loads((self.out / rel).read_text())


class LangKeyTests(unittest.TestCase):
    def test_rule(self):
        self.assertEqual(ac.lang_key("English(UK)"), "english_uk")
        self.assertEqual(ac.lang_key("Czech"), "czech")
        self.assertEqual(ac.lang_key("  Foo--Bar! (x) "), "foo_bar_x")
        self.assertEqual(ac.lang_key("Portugu\u00eas (BR)"), "portugu_s_br")

    def test_sanitize(self):
        self.assertEqual(ac.sanitize_name("en-GB_Hello World(2)"), "en-GB_Hello_World_2_")


class BankReaderTests(unittest.TestCase):
    def test_reads_header_and_media(self):
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "x.bnk"
            raw = bank(77, 11, [(5, b"abc"), (6, b"defgh")])
            p.write_bytes(raw)
            b = ac.read_bank_media(p)
        self.assertEqual((b["version"], b["id"], b["language_id"]), (113, 77, 11))
        self.assertEqual([(m, raw[o:o + n]) for m, o, n in b["media"]], [(5, b"abc"), (6, b"defgh")])


class CompareWavTests(unittest.TestCase):
    def test_tolerance_and_shape(self):
        a = wav([0, 10, -10, 300])
        self.assertEqual(ac.compare_wavs(a, a), (True, "identical"))
        self.assertTrue(ac.compare_wavs(a, wav([1, 9, -10, 300]))[0])
        self.assertFalse(ac.compare_wavs(a, wav([2, 10, -10, 300]))[0])
        self.assertFalse(ac.compare_wavs(a, wav([0, 10, -10]))[0])
        self.assertFalse(ac.compare_wavs(a, wav([0, 10, -10, 300], rate=44100))[0])
        self.assertFalse(ac.compare_wavs(a, b"junk")[0])


class ConvertTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.t = Tree(Path(self.tmp.name))

    def tearDown(self):
        self.tmp.cleanup()

    def test_outputs_and_tables(self):
        r = self.t.run()
        li = self.t.table("media.json")
        self.assertEqual({k: li[k] for k in ("format", "scope", "language", "externalPrefix", "external")},
                         {"format": 1, "scope": "li", "language": "", "externalPrefix": "", "external": []})
        self.assertEqual([m["id"] for m in li["media"]], [10, 11, 12])
        m11 = li["media"][1]
        self.assertEqual((m11["file"], m11["format"], m11["rate"], m11["channels"], m11["samples"], m11["label"]),
                         ("media/11.ogg", "ogg", 44100, 2, 4000, "Stereo_One"))
        self.assertEqual(li["media"][0]["cues"], [[0, "Mono_Two"], [100, "1j"]])
        for m in li["media"]:
            f = self.t.out / m["file"]
            self.assertEqual(f.stat().st_size, m["bytes"])
            pages = read_pages(f.read_bytes())
            self.assertTrue(all(p["crc_ok"] for p in pages))
            self.assertIn(b"WWISE_ID=%d" % m["id"], read_packets(pages)[1][0])

        en = self.t.table("lang/english_uk/media.json")
        self.assertEqual((en["scope"], en["language"], en["externalPrefix"]), ("english_uk", "English(UK)", "en-GB_"))
        self.assertEqual([(m["id"], m["file"]) for m in en["media"]],
                         [(20, "lang/english_uk/20.ogg"), (21, "lang/english_uk/21.ogg"),
                          (30, "lang/english_uk/30.ogg")])
        self.assertEqual([(e["name"], e["file"], e["label"]) for e in en["external"]],
                         [("en-gb_hello world", "lang/english_uk/ext/en-GB_Hello_World.ogg", "en-GB_TY1"),
                          ("en-gb_ty1", "lang/english_uk/ext/en-GB_TY1.ogg", "en-GB_TY1")])
        self.assertTrue(all("id" not in e for e in en["external"]))
        self.assertFalse((self.t.out / "lang/english_uk/31.ogg").exists())

        sc = r["scopes"]
        self.assertEqual({k: sc["li"][k] for k in ("files", "ok", "wav", "skipped", "failed", "duplicates")},
                         {"files": 3, "ok": 3, "wav": 0, "skipped": 0, "failed": [], "duplicates": 1})
        self.assertEqual((sc["english_uk"]["files"], sc["english_uk"]["filtered"], sc["english_uk"]["duplicates"]),
                         (5, 1, 1))
        self.assertEqual(r["verify"]["mode"], "none")
        self.assertGreater(r["totals"]["bytesOut"], 0)
        saved = json.loads((self.t.reports / "convert-report.json").read_text())
        self.assertEqual(saved["scopes"], r["scopes"])
        raw = (self.t.out / "media.json").read_text()
        self.assertNotIn(": ", raw)

    def test_loose_file_wins_over_embedded(self):
        en = self.t.src / "English(UK)"
        other, _ = build_wem(samples=1234)
        (en / "20.wem").write_bytes(other)
        self.t.run()
        m = {e["id"]: e for e in self.t.table("lang/english_uk/media.json")["media"]}
        self.assertEqual(m[20]["samples"], 1234)

    def test_without_wanted_converts_every_loose_wem(self):
        r = self.t.run(wanted=None)
        self.assertEqual(r["scopes"]["english_uk"]["filtered"], 0)
        self.assertTrue((self.t.out / "lang/english_uk/31.ogg").is_file())

    def test_incremental(self):
        self.t.run()
        r = self.t.run()
        self.assertEqual(r["scopes"]["li"]["skipped"], 3)
        self.assertEqual(r["scopes"]["english_uk"]["skipped"], 5)
        self.assertEqual(r["scopes"]["english_uk"]["ok"], 5)
        self.assertEqual(len(self.t.table("lang/english_uk/media.json")["media"]), 3)
        # a changed source, a deleted output and a new VERSION are redone
        src = self.t.src / "English(UK)" / "30.wem"
        st = src.stat()
        os.utime(src, ns=(st.st_atime_ns, st.st_mtime_ns + 5_000_000_000))
        (self.t.out / "media/12.ogg").unlink()
        r = self.t.run()
        self.assertEqual(r["scopes"]["english_uk"]["skipped"], 4)
        self.assertEqual(r["scopes"]["li"]["skipped"], 2)
        self.assertTrue((self.t.out / "media/12.ogg").is_file())
        with mock.patch.object(ac, "VERSION", "test-next"):
            r = self.t.run()
        self.assertEqual(r["scopes"]["li"]["skipped"], 0)

    def test_stale_outputs_pruned(self):
        self.t.run()
        (self.t.src / "English(UK)" / "en-GB_TY1.wem").unlink()
        stray = self.t.out / "media" / ".x.ogg.123.tmp"
        stray.write_bytes(b"x")
        r = self.t.run()
        self.assertFalse((self.t.out / "lang/english_uk/ext/en-GB_TY1.ogg").exists())
        self.assertFalse(stray.exists())
        self.assertEqual(r["totals"]["pruned"], 2)
        names = [e["name"] for e in self.t.table("lang/english_uk/media.json")["external"]]
        self.assertEqual(names, ["en-gb_hello world"])

    def test_verify_all_and_sample(self):
        log = Path(self.tmp.name) / "vg.log"
        with mock.patch.dict(os.environ, {"FAKE_VG_LOG": str(log)}):
            r = self.t.run(vgmstream=self.t.vg, verify="all")
        self.assertEqual(r["verify"]["checked"], 8)
        self.assertEqual(r["verify"]["identical"], 8)
        self.assertEqual(len(log.read_text().splitlines()), 16)
        # sample: at least SAMPLE_MIN per scope (all of them here) plus LI stereo/44.1k
        for f in self.t.out.rglob("*.ogg"):
            f.unlink()
        with mock.patch.object(ac, "SAMPLE_MIN", 0), mock.patch.object(ac, "SAMPLE_FRACTION", 0.0):
            r = self.t.run(vgmstream=self.t.vg, verify="sample")
        self.assertEqual(r["verify"]["checked"], 1)   # only the stereo 44.1k LI item

    def test_mismatch_falls_back_to_wav(self):
        with mock.patch.dict(os.environ, {"FAKE_VG_MODE": "mismatch"}):
            r = self.t.run(vgmstream=self.t.vg, verify="all")
        self.assertEqual(len(r["verify"]["mismatch"]), 8)
        self.assertEqual(r["scopes"]["li"]["wav"], 3)
        li = self.t.table("media.json")["media"]
        self.assertEqual({(m["format"], m["file"]) for m in li},
                         {("wav", "media/10.wav"), ("wav", "media/11.wav"), ("wav", "media/12.wav")})
        self.assertEqual(li[0]["samples"], 100)
        self.assertFalse((self.t.out / "media/10.ogg").exists())
        # fallbacks are kept until the source or VERSION changes; then the wav goes
        r = self.t.run(vgmstream=self.t.vg, verify="all")
        self.assertEqual(r["scopes"]["li"]["skipped"], 3)
        with mock.patch.object(ac, "VERSION", "test-next"):
            r = self.t.run(vgmstream=self.t.vg, verify="all")
        self.assertEqual(r["scopes"]["li"]["ok"], 3)
        self.assertFalse((self.t.out / "media/10.wav").exists())

    def test_remux_failure_fallback_and_failure(self):
        broken, _ = build_wem(book_ids=(0, 1, 2, 9))
        (self.t.src / "English(UK)" / "30.wem").write_bytes(broken)
        r = self.t.run(vgmstream=self.t.vg, verify="none")
        m = {e["id"]: e for e in self.t.table("lang/english_uk/media.json")["media"]}
        self.assertEqual((m[30]["format"], m[30]["file"]), ("wav", "lang/english_uk/30.wav"))
        self.assertEqual(r["scopes"]["english_uk"]["wav"], 1)
        (self.t.src / "English(UK)" / "30.wem").write_bytes(broken + b"FAILVG")
        r = self.t.run(vgmstream=self.t.vg, verify="none")
        failed = r["scopes"]["english_uk"]["failed"]
        self.assertEqual([f["source"] for f in failed], ["English(UK)/30.wem"])
        self.assertIn("remux", failed[0]["error"])
        r = self.t.run(verify="sample")
        self.assertEqual(len(r["scopes"]["english_uk"]["failed"]), 1)
        self.assertNotIn(30, [e["id"] for e in self.t.table("lang/english_uk/media.json")["media"]])

    def test_pool(self):
        r = self.t.run(jobs=2)
        self.assertEqual(r["scopes"]["li"]["ok"] + r["scopes"]["english_uk"]["ok"], 8)

    def test_bad_verify_mode(self):
        with self.assertRaises(ValueError):
            self.t.run(verify="most")


class CliTests(unittest.TestCase):
    def test_one(self):
        with tempfile.TemporaryDirectory() as td:
            td = Path(td)
            (td / "h.h").write_text(synthetic_header())
            wem, _ = build_wem()
            (td / "123.wem").write_bytes(wem)
            with mock.patch("builtins.print"):
                rc = ac.main(["--one", str(td / "123.wem"), "--codebooks", str(td / "h.h"), "-o", str(td / "x.ogg")])
            self.assertEqual(rc, 0)
            pkts = read_packets(read_pages((td / "x.ogg").read_bytes()))
            self.assertIn(b"WWISE_ID=123", pkts[1][0])


if __name__ == "__main__":
    unittest.main()
