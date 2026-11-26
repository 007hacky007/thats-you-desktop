import json
import math
import struct
import tempfile
import unittest
import wave
from pathlib import Path

from pipeline import run_player


def write_wav(path: Path, samples: list[int], rate: int = 8000, channels: int = 1) -> None:
    with wave.open(str(path), "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(struct.pack("<%dh" % len(samples), *samples))


def tone(seconds: float, rate: int = 8000, amp: float = 0.5, freq: float = 440.0) -> list[int]:
    n = int(seconds * rate)
    return [int(amp * 32767 * math.sin(2 * math.pi * freq * i / rate)) for i in range(n)]


class AudioArgvTest(unittest.TestCase):
    def test_default_argv_unchanged(self):
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"))
        self.assertEqual(argv, ["/a/exe", "-logFile", "/r/x/player.log", "-portObserve", "/r/x/shots",
                                "-portMute", "-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0"])
        self.assertFalse(any("Audio" in a or "Voice" in a for a in argv))

    def test_audio_flags_mapped(self):
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"), audio_trace=True, audio_record=True,
                                      audio_root=Path("/data/PortAudio"), voice_language="English(US)",
                                      audio_off=True)
        def value(flag):
            return argv[argv.index(flag) + 1]
        self.assertEqual(value("-portAudioTrace"), "/r/x/audio.jsonl")
        self.assertEqual(value("-portAudioRecord"), "/r/x/mix.wav")
        self.assertEqual(value("-portAudioRoot"), "/data/PortAudio")
        self.assertEqual(value("-portVoiceLanguage"), "English(US)")
        self.assertIn("-portAudioOff", argv)
        # audio flags come before the screen flags and before --extra
        self.assertLess(argv.index("-portAudioTrace"), argv.index("-screen-width"))

    def test_audio_flags_before_extra(self):
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"), audio_trace=True, extra=["-x"])
        self.assertEqual(argv[-1], "-x")
        self.assertIn("-portAudioTrace", argv)

    def test_parser_flags(self):
        a = run_player.build_parser().parse_args(["--audio-trace", "--audio-record", "--audio-root", "d",
                                                  "--voice-language", "German", "--audio-off"])
        self.assertTrue(a.audio_trace and a.audio_record and a.audio_off)
        self.assertEqual(a.audio_root, Path("d"))
        self.assertEqual(a.voice_language, "German")
        d = run_player.build_parser().parse_args([])
        self.assertFalse(d.audio_trace or d.audio_record or d.audio_off)
        self.assertIsNone(d.audio_root)
        self.assertIsNone(d.voice_language)


class ParseAudioEventsTest(unittest.TestCase):
    def test_robust_to_bad_lines(self):
        text = "\n".join([
            '{"t":0.1,"f":1,"kind":"post","id":1,"name":"Play_A","result":"ok"}',
            "not json",
            "[1,2]",
            '{"t":0.2,"f":2}',
            '{"t":"x","kind":"post"}',
            '{"t":0.3,"f":3,"kind":"unknown","id":77}',
            '{"t":0.4,"f":3,"kind":"post","id":1,"name":"Play_A"',  # truncated last line (killed)
            "",
        ])
        a = run_player.parse_audio_events(text)
        self.assertEqual(a["lines"], 4)
        self.assertEqual(a["bad"], 3)
        self.assertEqual(a["kinds"]["post"], 2)
        self.assertEqual(a["kinds"]["unknown"], 1)
        self.assertEqual(a["kinds"]["?"], 1)
        self.assertEqual(a["unknown"]["77"], 1)
        self.assertEqual(a["posted"]["Play_A"], 1)
        self.assertEqual(a["posted"]["?"], 1)

    def test_real_shapes(self):
        lines = [{"kind": "post", "id": 5, "name": "Play_X", "result": "ok", "playingId": 3}] * 3
        lines += [{"kind": "post", "id": 9, "name": "Play_Missing", "result": "unknown", "playingId": 0}] * 4
        lines += [{"kind": "post", "id": 10, "result": "unknown"}]
        lines += [{"kind": "post", "id": 11, "name": "Play_Quiet", "result": "silent"}] * 2
        lines += [{"kind": "voice", "op": "start", "media": 1, "file": "media/1.ogg"}] * 2
        lines += [{"kind": "voice", "op": "late", "media": 1, "lateMs": 300}]
        lines += [{"kind": "voice", "op": "clamp", "gainDb": 30}, {"kind": "voice", "op": "steal"}]
        lines += [{"kind": "voice", "op": "end", "reason": "Finished"}] * 3
        lines += [{"kind": "voice", "op": "stop", "reason": "Stopped"}]
        lines += [{"kind": "voice", "op": "fail", "reason": "NoMedia", "media": 0, "file": None}] * 2
        lines += [{"kind": "voice", "op": "fail", "reason": "LoadFailed", "media": 77, "file": None}]
        lines += [{"kind": "voice", "op": "fail", "reason": "LoadFailed", "media": 1, "file": "media/1.ogg"}]
        lines += [{"kind": "bank", "op": "load", "name": "Init", "id": 1, "result": 1}] * 2
        lines += [{"kind": "bank", "op": "loadAsync", "name": "B", "result": 1},
                  {"kind": "bank", "op": "ready", "name": "B", "result": 1},
                  {"kind": "bank", "op": "unload", "name": "B", "result": 1}]
        lines += [{"kind": "callback", "type": "endOfEvent", "playingId": 3}] * 4
        lines += [{"kind": "callback", "type": "bank", "bankId": 2, "result": 1}]
        lines += [{"kind": "music", "op": "play"}, {"kind": "music", "op": "segment"},
                  {"kind": "music", "op": "segment"}, {"kind": "music", "op": "transition"},
                  {"kind": "music", "op": "stinger"}]
        lines += [{"kind": "warn", "msg": "w1"}]
        a = run_player.parse_audio_events("\n".join(json.dumps(x) for x in lines))
        self.assertEqual(sum(a["posted"].values()), 10)
        self.assertEqual(a["resolved"]["Play_X"], 3)
        self.assertEqual(a["unknown"].most_common(1)[0], ("Play_Missing", 4))
        self.assertEqual(a["unknown"]["10"], 1)
        self.assertEqual(a["silent"]["Play_Quiet"], 2)
        self.assertEqual(a["voices"]["start"], 2)
        self.assertEqual(a["voices"]["late"], 1)
        self.assertEqual(a["failed"]["NoMedia ?"], 2)
        self.assertEqual(a["failed"]["LoadFailed media 77"], 1)
        self.assertEqual(a["failed"]["LoadFailed media/1.ogg"], 1)
        self.assertEqual(a["banks"]["Init load 1"], 2)
        self.assertEqual(a["banks"]["B ready 1"], 1)
        self.assertEqual(a["callbacks"]["endOfEvent"], 4)
        self.assertEqual(a["music"]["segment"], 2)
        out = "\n".join(run_player.audio_summary(a, None))
        self.assertIn("posted 10 (4 distinct), resolved 3, unknown 5 (2 distinct), silent 2", out)
        self.assertIn("(x4) Play_Missing", out)
        self.assertIn("voices: start 2, end 3, stop 1, fail 4, late 1, steal 1, clamp 1", out)
        self.assertIn("(x2) NoMedia ?", out)
        self.assertIn("(x2) Init load 1", out)
        self.assertIn("callbacks: endOfEvent 4, bank 1", out)
        self.assertIn("music: play 1, segment 2, transition 1, late 0, stinger 1", out)
        self.assertIn("WARNING: 5 posted events unknown", out)
        self.assertNotIn("not ready", out)

    def test_real_run_line_counts(self):
        # the shape of a real title screen run: 31 posts all ok and 34 voice starts
        lines = [{"kind": "post", "name": "Play_%d" % (i % 7), "result": "ok"} for i in range(31)]
        lines += [{"kind": "voice", "op": "start", "file": "media/%d.ogg" % i} for i in range(34)]
        out = "\n".join(run_player.audio_summary(
            run_player.parse_audio_events("\n".join(json.dumps(x) for x in lines)), None))
        self.assertIn("posted 31 (7 distinct), resolved 31, unknown 0 (0 distinct), silent 0", out)
        self.assertIn("voices: start 34, end 0, stop 0, fail 0, late 0", out)
        self.assertNotIn("WARNING", out)

    def test_warns_on_media_not_ready(self):
        lines = [{"kind": "post", "name": "Play_X", "result": "ok"},
                 {"kind": "warn", "msg": "bank X: media not ready after 5 s; reporting it loaded"},
                 {"kind": "warn", "msg": "bank Y: media not ready after 5 s; reporting it loaded"}]
        a = run_player.parse_audio_events("\n".join(json.dumps(x) for x in lines))
        self.assertEqual(len(a["not_ready"]), 2)
        out = "\n".join(run_player.audio_summary(a, None))
        self.assertIn("WARNING: 2 banks reported loaded with media not ready", out)
        self.assertIn("bank X: media not ready after 5 s", out)
        self.assertNotIn("posted events unknown", out)

    def test_old_kinds_still_counted(self):
        lines = [{"kind": "post", "id": 5, "name": "Play_X"}] * 3
        lines += [{"kind": "resolve", "name": "Play_X"}] * 2
        lines += [{"kind": "unknown", "name": "Play_Missing", "id": 9}] * 4
        lines += [{"kind": "unknown", "id": 10}]
        lines += [{"kind": "start", "playingId": 1}, {"kind": "start", "playingId": 2}]
        lines += [{"kind": "end", "reason": "Finished"}, {"kind": "stop", "reason": "Stopped"}]
        lines += [{"kind": "fail", "reason": "LoadFailed", "file": "media/1.ogg"}]
        lines += [{"kind": "bank", "name": "Frontend", "status": "ok"}, {"kind": "warn", "msg": "w1"}]
        lines += [{"kind": "level", "sec": 0, "rmsDb": -20.0, "peakDb": -3.0},
                  {"kind": "level", "sec": 1, "rmsDb": -90.0, "peakDb": -80.0},
                  {"kind": "level", "sec": 2, "rmsDb": "bad"}]
        a = run_player.parse_audio_events("\n".join(json.dumps(x) for x in lines))
        self.assertEqual(a["posted"]["Play_X"], 3)
        self.assertEqual(a["resolved"]["Play_X"], 2)
        self.assertEqual(a["unknown"].most_common(1)[0], ("Play_Missing", 4))
        self.assertEqual(a["unknown"]["10"], 1)
        self.assertEqual(a["failed"]["LoadFailed media/1.ogg"], 1)
        self.assertEqual(a["banks"]["Frontend ok"], 1)
        self.assertEqual(len(a["levels"]), 2)
        out = "\n".join(run_player.audio_summary(a, None))
        self.assertIn("posted 3 (1 distinct)", out)
        self.assertIn("unknown 5 (2 distinct)", out)
        self.assertIn("(x4) Play_Missing", out)
        self.assertIn("voices: start 2, end 1, stop 1, fail 1", out)
        self.assertIn("audible 1 of 2 s", out)
        self.assertIn("peak -3.0 dBFS", out)
        self.assertIn("WARNING: 5 posted events unknown", out)


class AnalyzeWavTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def test_silent(self):
        p = self.dir / "s.wav"
        write_wav(p, [0] * 16000)
        r = run_player.analyze_wav(p)
        self.assertAlmostEqual(r["duration"], 2.0)
        self.assertEqual(r["audible"], 0.0)
        self.assertAlmostEqual(r["longest_silence"], 2.0)
        self.assertLessEqual(r["peak_db"], -200)
        out = "\n".join(run_player.audio_summary(None, r, record_path=p, record_requested=True))
        self.assertIn("WARNING", out)
        self.assertIn("silent", out)

    def test_tone_with_gap(self):
        p = self.dir / "t.wav"
        samples = tone(1.0) + [0] * 4000 + tone(0.5, amp=0.25)
        write_wav(p, samples)
        r = run_player.analyze_wav(p)
        self.assertAlmostEqual(r["duration"], 2.0)
        self.assertAlmostEqual(r["audible"], 1.5, places=1)
        self.assertAlmostEqual(r["longest_silence"], 0.5, places=1)
        self.assertAlmostEqual(r["silence_at"], 1.0, places=1)
        self.assertAlmostEqual(r["peak_db"], 20 * math.log10(0.5), places=1)
        out = "\n".join(run_player.audio_summary(None, r, record_path=p, record_requested=True))
        self.assertNotIn("WARNING", out)
        self.assertIn("audible 1.5 s", out)

    def test_stereo_and_quiet_counts_as_silence(self):
        p = self.dir / "q.wav"
        quiet = tone(1.0, amp=0.001)  # about -63 dBFS RMS
        stereo = [v for s in quiet for v in (s, s)]
        write_wav(p, stereo, channels=2)
        r = run_player.analyze_wav(p)
        self.assertEqual(r["channels"], 2)
        self.assertAlmostEqual(r["duration"], 1.0)
        self.assertEqual(r["audible"], 0.0)

    def test_missing_and_broken(self):
        self.assertIsNone(run_player.analyze_wav(self.dir / "nope.wav"))
        bad = self.dir / "bad.wav"
        bad.write_bytes(b"RIFF\x00\x00")
        r = run_player.analyze_wav(bad)
        self.assertIn("error", r)
        out = "\n".join(run_player.audio_summary(None, None, record_path=self.dir / "nope.wav",
                                                 record_requested=True))
        self.assertIn("WARNING", out)
        self.assertIn("missing", out)

    def test_header_shorter_than_data(self):
        # a killed recorder leaves a header that covers the last patched second only
        p = self.dir / "k.wav"
        write_wav(p, tone(1.0))
        with open(p, "ab") as f:
            f.write(b"\x10\x00" * 3000)
        r = run_player.analyze_wav(p)
        self.assertAlmostEqual(r["duration"], 1.0)


class ReportAudioTest(unittest.TestCase):
    def test_report_has_audio_section_when_trace_present(self):
        with tempfile.TemporaryDirectory() as tmp:
            run_dir = Path(tmp)
            (run_dir / "audio.jsonl").write_text('{"t":1,"f":1,"kind":"post","name":"Play_A","result":"ok"}\n')
            write_wav(run_dir / "mix.wav", tone(1.0))
            out = run_player.report(run_dir, 0, audio_trace=True, audio_record=True)
            self.assertIn("audio:", out)
            self.assertIn("posted 1", out)
            self.assertIn("resolved 1", out)
            self.assertIn("mix:", out)

    def test_report_without_audio(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = run_player.report(Path(tmp), 0)
            self.assertNotIn("audio:", out)

    def test_report_warns_when_requested_but_missing(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = run_player.report(Path(tmp), 0, audio_trace=True, audio_record=True)
            self.assertIn("audio:", out)
            self.assertIn("WARNING", out)


if __name__ == "__main__":
    unittest.main()
