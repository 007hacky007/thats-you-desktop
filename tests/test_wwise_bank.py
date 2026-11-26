import contextlib
import io
import json
import os
import struct
import tempfile
import unittest
from collections import Counter
from pathlib import Path

from pipeline import wwise_bank as wb
from tests import wwise_bank_builder as B


def parse(type_id, body):
    obj, reason = wb.parse_object(type_id, body)
    if reason:
        raise AssertionError(f"unparsed: {reason}")
    return obj


class ReaderTest(unittest.TestCase):
    def test_var_int_round_trip(self):
        for v in (0, 1, 0x7F, 0x80, 300, 0x3FFF, 0x12345):
            self.assertEqual(wb.Reader(B.var(v)).var(), v)

    def test_read_past_end_raises(self):
        with self.assertRaises(wb.BankError):
            wb.Reader(b"\x01").u32()

    def test_count_guard(self):
        r = wb.Reader(B.u32(1000))
        n = r.u32()
        with self.assertRaises(wb.BankError):
            r.count(n, 4)


class CurveTest(unittest.TestCase):
    def test_db_from_stored(self):
        self.assertEqual(wb.db_from_stored(0.0), 0.0)
        self.assertEqual(wb.db_from_stored(-1.0), wb.DB_FLOOR)
        self.assertAlmostEqual(wb.db_from_stored(1.0), 6.0206, places=3)

    def test_db_curve_is_densified_in_db(self):
        pts = wb.convert_points([(0.0, -1.0, 4), (100.0, 0.0, 4)], wb.CURVE_SCALING_DB, True)
        self.assertEqual(pts[0], [0.0, -200.0, 4])
        self.assertEqual(pts[-1], [100.0, 0.0, 4])
        self.assertEqual(len(pts), wb.DB_SEGMENT_STEPS + 1)
        mid = [p for p in pts if p[0] == 50.0][0]
        self.assertAlmostEqual(mid[1], -6.0206, places=3)  # half linear gain

    def test_constant_segment_not_densified(self):
        pts = wb.convert_points([(0.0, -1.0, 9), (1.0, 0.0, 4)], wb.CURVE_SCALING_DB, True)
        self.assertEqual(pts, [[0.0, -200.0, 9], [1.0, 0.0, 4]])

    def test_non_db_points_unchanged(self):
        self.assertEqual(wb.convert_points([(0.0, 10.0, 4), (1.0, 62.0, 4)], 0, False),
                         [[0.0, 10.0, 4], [1.0, 62.0, 4]])


class SoundTest(unittest.TestCase):
    def test_vorbis_sound_with_props_ranges_positioning_rtpc(self):
        body = B.sound(
            101, B.source(B.VORBIS, 555, stream=2, mem=2000, bits=1),
            bus=0, parent=7, values={0: -3.0, 2: 100.0, 4: 30.0, 6: 100.0, 13: 90.0, 58: 0, 59: 2.5, 26: 1.0},
            ranges={0: (-2.0, 0.0), 59: (0.0, 5.0), 40: (1.0, 2.0)},
            pos=B.positioning(0xD9, 1, attenuation=900),
            fx=[(0, 77, 1, 0)], fx_bypass=1, override_fx=1,
            aux_=B.aux(8, (1, 2, 3, 4)), adv=B.advanced(12, 1, 3, 0, 1),
            state_groups=[(11, 0, [(12, 13)])],
            rtpc_items=[B.rtpc(400, 3, [(0.0, 0.0, 4), (100.0, 62.0, 4)], scaling=0)])
        o = parse(B.T_SOUND, body)
        self.assertEqual(o["type"], "Sound")
        self.assertEqual(o["id"], 101)
        s = o["source"]
        self.assertEqual((s["kind"], s["stream"], s["id"], s["inMemorySize"], s["languageSpecific"]),
                         ("vorbis", "streamed", 555, 2000, True))
        self.assertEqual(o["parent"], 7)
        self.assertEqual(o["props"]["volume"], -3.0)
        self.assertEqual(o["props"]["loop"], 0)
        self.assertEqual(o["props"]["initialDelay"], 2.5)
        self.assertEqual(o["props"]["unknown"], {"26": 1.0})
        self.assertEqual(o["props"]["volumeRand"], {"min": -2.0, "max": 0.0})
        self.assertEqual(o["rawProps"][58], 0)
        self.assertEqual(o["rawRanges"][40], [1.0, 2.0])
        p = o["positioning"]
        self.assertTrue(p["overrideParent"] and p["threeD"] and p["gameDefined"])
        self.assertEqual(p["attenuation"], 900)
        self.assertEqual(o["fx"], [{"index": 0, "id": 77, "shareSet": True, "rendered": False}])
        self.assertEqual(o["aux"]["auxBusses"], [1, 2, 3, 4])
        self.assertEqual(o["advanced"]["maxInstances"], 3)
        self.assertTrue(o["advanced"]["maxInstancesIgnoreParent"] and o["advanced"]["virtualVoiceOverrideParent"])
        self.assertTrue(o["advanced"]["useVirtualBehavior"] is False and o["advanced"]["killNewest"] is False)
        self.assertEqual(o["states"][0]["states"], [{"state": 12, "instance": 13}])
        r = o["rtpcs"][0]
        self.assertEqual((r["rtpc"], r["param"], r["paramId"], r["accumulation"]), (400, "lpf", 3, "additive"))
        self.assertEqual(r["points"], [[0.0, 0.0, 4], [100.0, 62.0, 4]])

    def test_user_defined_3d_path(self):
        path = (1, 500, [(1.0, 2.0, 3.0, 100), (4.0, 5.0, 6.0, 200)], [(0, 2)], [(1.0, 1.0, 0.0)])
        o = parse(B.T_SOUND, B.sound(1, pos=B.positioning(0xF9, 0, 5, path)))
        p = o["positioning"]
        self.assertFalse(p["gameDefined"])
        self.assertEqual(p["vertices"], [[1.0, 2.0, 3.0, 100], [4.0, 5.0, 6.0, 200]])
        self.assertEqual(p["playlist"], [[0, 2]])
        self.assertEqual(p["ranges"], [[1.0, 1.0, 0.0]])

    def test_external_and_audio_input_sources(self):
        o = parse(B.T_SOUND, B.sound(2, B.source(B.EXTERNAL, 3456)))
        self.assertEqual((o["source"]["kind"], o["source"]["cookie"]), ("external", 3456))
        o = parse(B.T_SOUND, B.sound(3, B.source(B.AUDIO_INPUT, 99, params=b"")))
        self.assertEqual(o["source"]["kind"], "audioInput")
        self.assertEqual(o["source"]["params"], "")

    def test_rtpc_volume_in_db(self):
        o = parse(B.T_SOUND, B.sound(4, rtpc_items=[B.rtpc(9, 0, [(0.0, -1.0, 4), (100.0, 0.0, 4)], scaling=2)]))
        r = o["rtpcs"][0]
        self.assertEqual(r["param"], "volume")
        self.assertEqual(r["points"][0][1], -200.0)
        self.assertEqual(r["points"][-1][1], 0.0)

    def test_trailing_bytes_reported(self):
        obj, reason = wb.parse_object(B.T_SOUND, B.sound(5) + b"\0\0")
        self.assertEqual(obj["id"], 5)
        self.assertEqual(reason, "2 bytes left")

    def test_truncated_reported(self):
        obj, reason = wb.parse_object(B.T_SOUND, B.sound(6)[:-3])
        self.assertIn("read past end", reason)
        self.assertEqual(obj["id"], 6)


class ActionEventTest(unittest.TestCase):
    def test_event(self):
        self.assertEqual(parse(B.T_EVENT, B.event(10, [1, 2, 3]))["actions"], [1, 2, 3])

    def test_play_with_delay_and_transition(self):
        o = parse(B.T_ACTION, B.action(20, 0x0403, 101, {14: 4000, 15: 2000, 16: 50.0}, {14: (0, 500)},
                                       B.play_tail(4, 77)))
        self.assertEqual((o["action"], o["targetMode"], o["gameObjectScoped"]), ("play", "Element", True))
        self.assertEqual(o["props"]["delay"], 4000)
        self.assertEqual(o["props"]["transition"], 2000)
        self.assertEqual(o["props"]["probability"], 50.0)
        self.assertEqual(o["rawRanges"][14], [0, 500])
        self.assertEqual((o["bank"], o["fadeCurve"]), (77, 4))

    def test_stop_pause_resume_exceptions(self):
        o = parse(B.T_ACTION, B.action(21, 0x0103, 101, tail=B.stop_tail(4, [(5, 0), (6, 1)])))
        self.assertEqual(o["action"], "stop")
        self.assertEqual(o["exceptions"], [{"id": 5, "isBus": False}, {"id": 6, "isBus": True}])
        o = parse(B.T_ACTION, B.action(22, 0x0208, 0, tail=B.pause_tail(4, 1, [(7, 0)])))
        self.assertEqual((o["action"], o["targetMode"], o["gameObjectScoped"]), ("pause", "AllExcept", False))
        self.assertTrue(o["includePendingResume"])
        self.assertEqual(o["exceptions"], [{"id": 7, "isBus": False}])
        o = parse(B.T_ACTION, B.action(23, 0x0304, 0, tail=B.pause_tail(4, 0)))
        self.assertEqual((o["action"], o["targetMode"]), ("resume", "All"))
        self.assertFalse(o["masterResume"])

    def test_set_game_parameter(self):
        o = parse(B.T_ACTION, B.action(24, 0x1303, 300, {15: 1000},
                                       tail=B.set_param_tail(4, 1, 2, 80.0, -1.0, 1.0)))
        self.assertEqual(o["action"], "setGameParameter")
        self.assertTrue(o["bypassTransition"])
        self.assertEqual((o["valueMeaning"], o["value"], o["valueMin"], o["valueMax"]), ("offset", 80.0, -1.0, 1.0))
        self.assertEqual(o["exceptions"], [])

    def test_set_state_switch_and_play_event(self):
        o = parse(B.T_ACTION, B.action(25, 0x1204, 0, tail=B.group_tail(40, 41)))
        self.assertEqual((o["action"], o["stateGroup"], o["state"]), ("setState", 40, 41))
        o = parse(B.T_ACTION, B.action(26, 0x1901, 0, tail=B.group_tail(50, 51)))
        self.assertEqual((o["action"], o["switchGroup"], o["switch"]), ("setSwitch", 50, 51))
        o = parse(B.T_ACTION, B.action(27, 0x2103, 10, {14: 7000}))
        self.assertEqual((o["action"], o["target"], o["props"]["delay"]), ("playEvent", 10, 7000))

    def test_unsupported_action_is_unparsed(self):
        _o, reason = wb.parse_object(B.T_ACTION, B.action(28, 0x1E03, 1))
        self.assertIn("not supported", reason)


class ContainerTest(unittest.TestCase):
    def test_ranseq(self):
        o = parse(B.T_RANSEQ, B.ranseq(30, [1, 2], [(1, 50000), (2, 50000)], loop=0, trans=1000.0,
                                       trans_min=-500.0, trans_max=8000.0, avoid=1, trans_mode=3,
                                       random_mode=1, mode=0, bits=8 | 1, parent=7))
        self.assertEqual(o["children"], [1, 2])
        self.assertEqual(o["playlist"], [{"id": 1, "weight": 50000}, {"id": 2, "weight": 50000}])
        self.assertEqual((o["loopCount"], o["transitionTime"], o["transitionTimeModMin"]), (0, 1000.0, -500.0))
        self.assertEqual((o["transitionMode"], o["randomMode"], o["mode"]), ("delay", "shuffle", "random"))
        self.assertTrue(o["continuous"] and o["usingWeight"])
        self.assertFalse(o["global"])

    def test_switch_container(self):
        o = parse(B.T_SWITCH, B.switch_cntr(31, 1, 500, 501, 1, [1, 2], [(501, [1]), (502, [2])],
                                            [(1, 1, 0, 100, 200), (2, 2, 1, 0, 0)]))
        self.assertEqual((o["groupType"], o["group"], o["defaultSwitch"]), ("state", 500, 501))
        self.assertTrue(o["continuousValidation"])
        self.assertEqual(o["assignments"], [{"switch": 501, "nodes": [1]}, {"switch": 502, "nodes": [2]}])
        p = o["childParams"]
        self.assertEqual((p[0]["firstOnly"], p[0]["continuePlayback"], p[0]["onSwitchMode"]), (True, False, "playToEnd"))
        self.assertEqual((p[0]["fadeOutMs"], p[0]["fadeInMs"]), (100, 200))
        self.assertEqual((p[1]["firstOnly"], p[1]["continuePlayback"], p[1]["onSwitchMode"]), (False, True, "stop"))

    def test_actor_mixer_and_layer(self):
        self.assertEqual(parse(B.T_ACTOR, B.actor_mixer(32, [5, 6]))["children"], [5, 6])
        o = parse(B.T_LAYER, B.layer_cntr(33, [5], [(1, [], 600, 0, [(5, [(0.0, 1.0, 4), (100.0, 0.0, 4)])])]))
        layer = o["layers"][0]
        self.assertEqual((layer["id"], layer["crossfadeRtpc"]), (1, 600))
        self.assertEqual(layer["associations"][0]["child"], 5)


class BusTest(unittest.TestCase):
    def test_bus_with_ducks_fx_rtpc(self):
        o = parse(B.T_BUS, B.bus(40, 1, {5: -11.0, 26: 0.0}, bits=8, recovery=300, max_duck=-96.0,
                                 ducks=[(41, -7.0, 500, 1500, 4, 5)], fx=[(0, 70, 0, 0), (1, 71, 1, 0)], fx_bypass=1,
                                 rtpc_items=[B.rtpc(800, 0, [(0.0, -1.0, 9), (100.0, 0.0, 4)], scaling=2)]))
        self.assertEqual(o["type"], "Bus")
        self.assertEqual((o["parent"], o["props"]["busVolume"]), (1, -11.0))
        self.assertTrue(o["backgroundMusic"])
        self.assertTrue(o["hdrReleaseExponential"])
        self.assertEqual((o["recoveryTimeMs"], o["maxDuckVolume"]), (300, -96.0))
        self.assertEqual(o["ducks"], [{"bus": 41, "volume": -7.0, "fadeOutMs": 500, "fadeInMs": 1500, "curve": 4,
                                       "targetProp": 5}])
        self.assertEqual([f["id"] for f in o["fx"]], [70, 71])
        self.assertEqual(o["bypassBits"], 1)
        self.assertEqual(o["rtpcs"][0]["points"][0], [0.0, -200.0, 9])

    def test_aux_bus(self):
        o = parse(B.T_AUXBUS, B.bus(42, 40, {5: -12.0}, hdr=0, recovery=0))
        self.assertEqual(o["type"], "AuxBus")
        self.assertFalse(o["hdrReleaseExponential"])


class MusicTest(unittest.TestCase):
    def test_segment_markers(self):
        o = parse(B.T_SEGMENT, B.music_segment(50, 4000.0, [(1, 0.0, ""), (2, 1000.0, "1j"), (3, 3500.0, "")],
                                               kids=[60], grid=(500.0, 0.0), tempo=120.0, sig=(4, 4),
                                               stingers=[(9, 51, 3, 0, 1000, 1)]))
        self.assertEqual(o["children"], [60])
        self.assertEqual(o["durationMs"], 4000.0)
        self.assertEqual([m["kind"] for m in o["markers"]], ["entry", "custom", "exit"])
        self.assertEqual(o["markers"][1]["name"], "1j")
        self.assertEqual((o["tempo"], o["timeSigBeats"], o["gridPeriodMs"]), (120.0, 4, 500.0))
        self.assertEqual(o["stingers"][0]["sync"], "nextBeat")
        self.assertEqual(o["stingers"][0]["lookAhead"], 1)

    def test_track_clips_and_fades(self):
        body = B.music_track(60, [B.source(B.VORBIS, 700, bits=0, mem=100)],
                             [(0, 700, -50.0, 100.0, -200.0, 2000.0)],
                             automation=[(0, 3, [(0.0, 0.0, 1), (0.5, 1.0, 9)]),
                                         (0, 4, [(0.0, 1.0, 9), (1.2, 1.0, 7), (1.7, 0.0, 9)]),
                                         (0, 2, [(0.0, 0.0, 4)])],
                             look_ahead=100, parent=50)
        o = parse(B.T_TRACK, body)
        self.assertEqual(o["sources"][0]["id"], 700)
        c = o["clips"][0]
        self.assertEqual((c["playAtMs"], c["beginTrimMs"], c["endTrimMs"], c["sourceDurationMs"]),
                         (-50.0, 100.0, -200.0, 2000.0))
        self.assertEqual(o["subTrackCount"], 1)
        fin, fout, other = o["automation"]
        self.assertEqual(fin["type"], "fadeIn")
        self.assertEqual(fin["points"], [[0.0, 0.0, 1], [500.0, 1.0, 9]])
        self.assertEqual(fout["type"], "fadeOut")
        # clip length = 2000 - 100 - 200 = 1700 ms; fadeOut x relative to the clip end
        self.assertEqual([p[0] for p in fout["points"]], [-1700.0, -500.0, 0.0])
        self.assertEqual(other["type"], "other")
        self.assertEqual((o["trackType"], o["lookAheadMs"]), ("normal", 100))

    def test_switch_track(self):
        o = parse(B.T_TRACK, B.music_track(61, track_type=3, switch=(0, 900, 901, [901, 902])))
        self.assertEqual(o["trackType"], "switch")
        self.assertEqual((o["switchGroup"], o["switchAssociations"]), (900, [901, 902]))
        self.assertEqual(o["switchTransition"]["dstFade"]["timeMs"], 200)

    def test_music_switch_tree_and_rules(self):
        rule = B.transition((-1,), (52,), (1000, 4, 0), 7, 0, 1, (500, 1, 100), 0, 0, 3, 1, 0,
                            tobj=(53, (100, 4, 0), (200, 4, 0), 0, 1))
        # depth 2: root -> A(2 children) , B(1 child)
        tree = [B.tree_branch(0, 1, 2),
                B.tree_branch(10, 3, 2), B.tree_branch(0, 5, 1),
                (100, 70, 50, 100), (0, 71, 50, 100), (0, 72, 50, 100)]
        o = parse(B.T_MSWITCH, B.music_switch(54, [rule], 1, [(800, 0), (810, 1)], tree, 0, kids=[70, 71, 72]))
        self.assertTrue(o["continuePlayback"])
        self.assertEqual(o["arguments"], [{"group": 800, "groupType": "switch"}, {"group": 810, "groupType": "state"}])
        self.assertEqual(o["paths"], [{"keys": [10, 100], "node": 70, "weight": 50, "probability": 100},
                                      {"keys": [10, 0], "node": 71, "weight": 50, "probability": 100},
                                      {"keys": [0, 0], "node": 72, "weight": 50, "probability": 100}])
        r = o["transitions"][0]
        self.assertEqual((r["src"], r["dst"]), ([-1], [52]))
        self.assertEqual(r["srcRule"]["sync"], "exitMarker")
        self.assertTrue(r["srcRule"]["playPostExit"])
        self.assertEqual(r["srcRule"]["fade"], {"timeMs": 1000, "curve": 4, "offsetMs": 0})
        self.assertEqual((r["dstRule"]["entry"], r["dstRule"]["playPreEntry"]), ("randomUserMarker", True))
        self.assertEqual(r["dstRule"]["fade"], {"timeMs": 500, "curve": 1, "offsetMs": 100})
        self.assertEqual(r["transitionObject"]["segment"], 53)

    def test_bad_tree_size_unparsed(self):
        body = B.music_switch(55, tree=[(0, 1, 0, 0)])
        body = body[:-12 - 4 - 1] + B.u32(13) + B.u8(0) + b"\0" * 13
        _o, reason = wb.parse_object(B.T_MSWITCH, body)
        self.assertIn("multiple of 12", reason)

    def test_music_ranseq_playlist_tree(self):
        items = [(0, 1, 2, 0, 0, 50000, 1, 1, 0),
                 (90, 2, 0, 0xFFFFFFFF, 1, 50000, 0, 0, 0),
                 (0, 3, 1, 3, 2, 50000, 0, 0, 1),
                 (91, 4, 0, 0xFFFFFFFF, 1, 50000, 0, 0, 0)]
        o = parse(B.T_MRANSEQ, B.music_ranseq(56, [B.transition()], items, kids=[90, 91]))
        root = o["playlist"][0]
        self.assertEqual((root["id"], root["type"], root["loop"]), (1, "sequenceContinuous", 0))
        self.assertEqual([c["id"] for c in root["children"]], [2, 3])
        self.assertIsNone(root["children"][0]["type"])
        self.assertEqual(root["children"][1]["type"], "randomStep")
        self.assertEqual(root["children"][1]["children"][0]["segment"], 91)
        self.assertEqual(o["transitions"][0]["srcRule"]["sync"], "immediate")


class AttenuationFxTest(unittest.TestCase):
    def test_attenuation(self):
        o = parse(B.T_ATTEN, B.attenuation(80, cone=(90.0, 180.0, -6.0, 20.0), uses=(0, 0, 0, -1, 1, -1, -1),
                                           curves=[(2, [(0.0, 0.0, 4), (21.0, -1.0, 4)]),
                                                   (0, [(0.0, 0.0, 4), (21.0, 100.0, 4)])]))
        self.assertTrue(o["coneEnabled"])
        self.assertEqual(o["cone"]["outsideVolume"], -6.0)
        uses = [c["use"] for c in o["curves"]]
        self.assertEqual(uses, ["volumeDry", "volumeWetGame", "volumeWetUser", "other"])
        self.assertEqual(o["curves"][0]["points"][-1], [21.0, -200.0, 4])
        self.assertEqual(o["curves"][3]["points"], [[0.0, 0.0, 4], [21.0, 100.0, 4]])

    def test_meter_and_gain(self):
        o = parse(B.T_FXSHARE, B.fx(81, B.METER, B.meter_params(0.05, 0.5, -44.5, 0.0, 0.0, 1, 1234)))
        self.assertEqual(o["meter"], {"attack": 0.05, "release": 0.5, "min": -44.5, "max": 0.0, "hold": 0.0,
                                      "mode": "rms", "gameParameter": 1234})
        o = parse(B.T_FXCUSTOM, B.fx(82, B.GAIN, struct.pack("<ff", -4.0, -4.0),
                                     rtpc_items=[B.rtpc(5, 3, [(0.0, 1.0, 4)])]))
        self.assertEqual(o["gain"], {"fullband": -4.0, "lfe": -4.0})
        self.assertEqual(o["rtpcs"][0]["paramId"], 3)
        o = parse(B.T_FXCUSTOM, B.fx(83, 0x00730003, b"\1\2", media=[(0, 9)]))
        self.assertEqual((o["params"], o["media"]), ("0102", [{"index": 0, "source": 9}]))


class BankTest(unittest.TestCase):
    def make(self):
        st = B.stmg(state_groups=[(500, 1000, [(1, 2, 300)])], switch_groups=[(600, 601, [(0.0, 1.0, 9)])],
                    game_params=[(700, 50.0, 1, 2.0, 3.0, 0)])
        return B.bank(1234, 11, [(B.T_EVENT, B.event(1, [2])), (B.T_ACTION, B.action(2, 0x0403, 3, tail=B.play_tail())),
                                 (B.T_SOUND, B.sound(3, B.source(sid=9, mem=4))), (21, b"\x05\0\0\0\1")],
                      media=[(9, b"abcd"), (10, b"xy")], extra=[st, B.chunk("ENVS", b"\0" * 8)])

    def test_read_bank(self):
        b = wb.read_bank(self.make())
        self.assertEqual(b["header"]["id"], 1234)
        self.assertEqual(b["header"]["language"], 11)
        self.assertEqual([c[0] for c in b["chunks"]], ["BKHD", "DIDX", "DATA", "STMG", "ENVS", "HIRC", "PLAT"])
        self.assertEqual(b["didx"], [{"id": 9, "offset": 0, "size": 4}, {"id": 10, "offset": 16, "size": 2}])
        data = self.make()
        self.assertEqual(data[b["dataOffset"]:b["dataOffset"] + 4], b"abcd")
        self.assertEqual([o["type"] for o in b["objects"]], ["Event", "Action", "Sound", "LFO"])
        self.assertEqual(b["unparsed"], [{"id": 5, "type": "LFO", "reason": "type LFO not supported"}])
        self.assertEqual(b["stateGroups"], [{"id": 500, "defaultTransitionMs": 1000,
                                             "transitions": [{"from": 1, "to": 2, "timeMs": 300}]}])
        self.assertEqual(b["switchGroups"][0]["rtpc"], 601)
        self.assertEqual(b["gameParameters"], [{"id": 700, "default": 50.0, "rampType": "slewRate",
                                                "rampUp": 2.0, "rampDown": 3.0, "builtIn": 0}])
        self.assertEqual(b["settings"], {"volumeThreshold": -80.0, "maxVoices": 256})
        self.assertEqual(b["platform"], "PS4")

    def test_wrong_version(self):
        with self.assertRaises(wb.BankError):
            wb.read_bank(B.bank(1, version=88))

    def test_cli_summary_and_dump(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "x.bnk"
            p.write_bytes(self.make())
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertEqual(wb.main([str(p)]), 0)
            summary = json.loads(out.getvalue())
            self.assertEqual(summary["objects"], {"Event": 1, "Action": 1, "Sound": 1, "LFO": 1})
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertEqual(wb.main([str(p), "--dump", "3"]), 0)
            self.assertEqual(json.loads(out.getvalue())["source"]["id"], 9)
            with contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(wb.main([str(p), "--dump", "99"]), 1)


REAL_ROOT = Path(__file__).resolve().parent.parent / "work/merged/Media/StreamingAssets/Audio/GeneratedSoundBanks/PS4"


@unittest.skipUnless(os.environ.get("PORT_AUDIO_REAL") == "1", "set PORT_AUDIO_REAL=1 to parse the real banks")
class RealBanksTest(unittest.TestCase):
    def test_all_banks_parse_fully(self):
        banks = sorted(REAL_ROOT.glob("*/*.bnk"))
        self.assertEqual(len(banks), 103)
        counts = Counter()
        unparsed = []
        events = set()
        sync, entry = Counter(), Counter()
        for p in banks:
            b = wb.read_bank(p.read_bytes())
            for o in b["objects"]:
                counts[o["type"]] += 1
                if p.parent.name == "LocaleIndependent" and o["type"] in ("MusicSwitch", "MusicRanSeq"):
                    for r in o["transitions"]:
                        sync[r["srcRule"]["sync"]] += 1
                        entry[r["dstRule"]["entry"]] += 1
                if o["type"] == "Event" and p.parent.name in ("LocaleIndependent", "English(UK)"):
                    events.add(o["id"])
            unparsed += b["unparsed"]
        self.assertEqual(unparsed, [])
        expected = {"Sound": 16186, "RanSeqCntr": 7461, "Action": 3013, "Event": 2679, "SwitchCntr": 2036,
                    "ActorMixer": 116, "Bus": 118, "AuxBus": 1, "LayerCntr": 1, "MusicSegment": 189,
                    "MusicTrack": 228, "MusicSwitch": 22, "MusicRanSeq": 134, "Attenuation": 142,
                    "FxShareSet": 15, "FxCustom": 21}
        self.assertEqual(dict(counts), expected)
        self.assertEqual(len(events), 639)
        self.assertEqual(dict(sync), {"exitMarker": 222, "nextBeat": 61, "immediate": 34, "nextBar": 2})
        self.assertEqual(dict(entry), {"entryMarker": 234, "randomUserMarker": 43, "sameTime": 26, "randomMarker": 16})


if __name__ == "__main__":
    unittest.main()
