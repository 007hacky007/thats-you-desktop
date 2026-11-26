import contextlib
import io
import json
import os
import struct
import tempfile
import unittest
from pathlib import Path

from pipeline import audio_graph as ag
from tests import wwise_bank_builder as B

# synthetic ids
EV_PLAY, EV_STOP, EV_CHAIN, EV_MISSING, EV_MUSIC, EV_VOICE, EV_PARAM = 1001, 1002, 1003, 1004, 1005, 1006, 1007
A_PLAY, A_STOP, A_CHAIN, A_MISSING, A_MUSIC, A_VOICE, A_PARAM = 2001, 2002, 2003, 2004, 2005, 2006, 2007
RS, SND1, SND2, SW, SND3, MIX = 3001, 3002, 3003, 3004, 3005, 3006
MSW, MRS, SEG, TRK = 4001, 4002, 4003, 4004
MEDIA1, MEDIA2, MEDIA3, MUS_MEDIA = 5001, 5002, 5003, 5004
L_EV, L_ACT, L_SND, L_STREAM, L_EXT, L_EMB, L_EMB_SND = 6001, 6002, 6003, 6004, 6005, 6006, 6007
MASTER, MUSIC_BUS, METER_FX, SG = 7001, 7002, 7003, 7005
GP_VOL = ag.fnv1("MUSIC_VOL")


def init_bank():
    st = B.stmg(state_groups=[(SG, 1000, [])], game_params=[(GP_VOL, 100.0, 0, 0.0, 0.0, 0)])
    objs = [
        (B.T_BUS, B.bus(MASTER, 0, {26: 0.0}, fx=[(0, METER_FX, 0, 0)])),
        (B.T_BUS, B.bus(MUSIC_BUS, MASTER, {5: -6.0}, ducks=[(MASTER, -4.0, 100, 200, 4, 5)],
                        rtpc_items=[B.rtpc(GP_VOL, 0, [(0.0, -1.0, 4), (100.0, 0.0, 4)], scaling=2)])),
        (B.T_FXSHARE, B.fx(METER_FX, B.METER, B.meter_params(game_param=GP_VOL))),
        (B.T_ATTEN, B.attenuation(7006, curves=[(2, [(0.0, 0.0, 4), (10.0, -1.0, 4)])])),
    ]
    return B.bank(ag.fnv1("Init"), 0, objs, extra=[st])


def sfx_bank():
    objs = [
        (B.T_EVENT, B.event(EV_PLAY, [A_PLAY])),
        (B.T_ACTION, B.action(A_PLAY, 0x0403, RS, {14: 500}, tail=B.play_tail(4, 99))),
        (B.T_EVENT, B.event(EV_STOP, [A_STOP])),
        (B.T_ACTION, B.action(A_STOP, 0x0103, RS, {15: 1000}, tail=B.stop_tail(1, [(SND1, 0)]))),
        (B.T_EVENT, B.event(EV_CHAIN, [A_CHAIN])),
        (B.T_ACTION, B.action(A_CHAIN, 0x2103, EV_MUSIC, {14: 4000})),
        (B.T_EVENT, B.event(EV_MISSING, [A_MISSING])),
        (B.T_ACTION, B.action(A_MISSING, 0x0403, 9999, tail=B.play_tail())),
        (B.T_EVENT, B.event(EV_MUSIC, [A_MUSIC])),
        (B.T_ACTION, B.action(A_MUSIC, 0x0403, MSW, tail=B.play_tail())),
        (B.T_EVENT, B.event(EV_PARAM, [A_PARAM])),
        (B.T_ACTION, B.action(A_PARAM, 0x1303, GP_VOL, {15: 1000}, tail=B.set_param_tail(base=80.0))),
        (B.T_ACTOR, B.actor_mixer(MIX, [RS, SW], bus=MUSIC_BUS)),
        (B.T_RANSEQ, B.ranseq(RS, [SND1, SND2], [(SND1, 50000), (SND2, 50000)], loop=0, trans=1000.0,
                              trans_max=8000.0, trans_mode=3, bits=8, parent=MIX)),
        (B.T_SOUND, B.sound(SND1, B.source(sid=MEDIA1, mem=8), parent=RS, values={0: -3.0, 58: 0, 4: 30.0},
                            ranges={2: (-100.0, 100.0)}, pos=B.positioning(0xD9, 1, 7006),
                            adv=B.advanced(1, 1, 2, 0, 0))),
        (B.T_SOUND, B.sound(SND2, B.source(sid=MEDIA2, mem=8), parent=RS)),
        (B.T_SWITCH, B.switch_cntr(SW, 0, 8001, 8002, 0, [SND3], [(8002, [SND3]), (8003, [])],
                                   [(SND3, 0, 1, 0, 0)], parent=MIX)),
        (B.T_SOUND, B.sound(SND3, B.source(sid=MEDIA3, mem=8), parent=SW)),
        (B.T_MSWITCH, B.music_switch(MSW, [B.transition(sync=7, entry=3)], 1, [(8001, 0)],
                                     [B.tree_branch(0, 1, 2), (8002, MRS, 50, 100), (0, 0, 50, 100)],
                                     kids=[MRS])),
        (B.T_MRANSEQ, B.music_ranseq(MRS, [], [(0, 4101, 1, 0, 0, 50000, 0, 0, 0),
                                               (SEG, 4102, 0, 0xFFFFFFFF, 1, 50000, 0, 0, 0)],
                                     kids=[SEG], parent=MSW)),
        (B.T_SEGMENT, B.music_segment(SEG, 2000.0, [(1, 100.0, ""), (2, 1000.0, "1j"), (3, 1900.0, "")],
                                      kids=[TRK], parent=MRS)),
        (B.T_TRACK, B.music_track(TRK, [B.source(sid=MUS_MEDIA, mem=8)], [(0, MUS_MEDIA, -10.0, 0.0, 0.0, 2000.0)],
                                  automation=[(0, 3, [(0.0, 0.0, 4), (0.2, 1.0, 4)])], parent=SEG)),
    ]
    media = [(MEDIA1, b"m1"), (MEDIA2, b"m2"), (MEDIA3, b"m3"), (MUS_MEDIA, b"m4")]
    return B.bank(ag.fnv1("SFX_Bank"), 0, objs, media=media)


def lang_bank(lang_id):
    objs = [
        (B.T_EVENT, B.event(L_EV, [L_ACT])),
        (B.T_ACTION, B.action(L_ACT, 0x0403, L_SND, tail=B.play_tail())),
        (B.T_RANSEQ, B.ranseq(L_SND, [L_STREAM, L_EXT, L_EMB_SND], [(L_STREAM, 0)])),
        (B.T_SOUND, B.sound(L_STREAM, B.source(sid=6100, stream=2, mem=2000, bits=1), parent=L_SND)),
        (B.T_SOUND, B.sound(L_EMB_SND, B.source(sid=L_EMB, mem=2, bits=1), parent=L_SND)),
        (B.T_SOUND, B.sound(L_EXT, B.source(B.EXTERNAL, 6200), parent=L_SND)),
        (B.T_EVENT, B.event(EV_VOICE, [A_VOICE])),
        (B.T_ACTION, B.action(A_VOICE, 0x0403, SND1, tail=B.play_tail())),
    ]
    return B.bank(ag.fnv1("Voice_Bank"), lang_id, objs, media=[(L_EMB, b"vv")])


AK_CS = """public class AK
{
\tpublic class EVENTS
\t{
\t\tpublic static uint %s = %du;
\t\tpublic static uint BOGUS_NAME = 1234u;
\t}
\tpublic class GAME_PARAMETERS
\t{
\t\tpublic static uint MUSIC_VOL = %du;
\t}
}
""" % ("PLAY_THING", ag.fnv1("PLAY_THING"), ag.fnv1("MUSIC_VOL"))


def make_tree(root: Path, sfx_bytes=None):
    li = root / "LocaleIndependent"
    li.mkdir(parents=True)
    (li / "Init.bnk").write_bytes(init_bank())
    (li / "SFX_Bank.bnk").write_bytes(sfx_bytes if sfx_bytes is not None else sfx_bank())
    for folder, lid in (("English(UK)", 11), ("Czech", 6)):
        d = root / folder
        d.mkdir()
        (d / "Voice_Bank.bnk").write_bytes(lang_bank(lid))
        (d / "6100.wem").write_bytes(b"x")


class KeysTest(unittest.TestCase):
    def test_lang_key(self):
        self.assertEqual(ag.lang_key("English(UK)"), "english_uk")
        self.assertEqual(ag.lang_key("Czech"), "czech")
        self.assertEqual(ag.lang_key("  Foo--Bar! (x) "), "foo_bar_x")
        self.assertEqual(ag.lang_key("Portugu\u00eas (BR)"), "portugu_s_br")

    def test_fnv1(self):
        self.assertEqual(ag.fnv1(""), 0)
        self.assertEqual(ag.fnv1("Init"), 1355168291)   # Init.bnk bank id
        self.assertEqual(ag.fnv1("init"), ag.fnv1("INIT"))

    def test_load_names_keeps_verified_only(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "AK.cs"
            p.write_text(AK_CS)
            names = ag.load_names(p)
        self.assertEqual(names, {ag.fnv1("PLAY_THING"): "PLAY_THING", ag.fnv1("MUSIC_VOL"): "MUSIC_VOL"})
        self.assertEqual(ag.load_names(None), {})


class PutTest(unittest.TestCase):
    def test_defaults_omitted(self):
        d = {}
        ag._put(d, "a", 0)
        ag._put(d, "b", False)
        ag._put(d, "c", [])
        ag._put(d, "d", None)
        ag._put(d, "e", "")
        ag._put(d, "loop", -1, -1)
        ag._put(d, "loop2", 0, -1)
        ag._put(d, "p", 100.0, 100.0)
        ag._put(d, "t", True)
        self.assertEqual(d, {"loop2": 0, "t": True})

    def test_props_json(self):
        p = ag.props_json({0: -3.0, 58: 0, 6: 50.0, 16: 100.0, 4: 30.0, 59: 1.5},
                          {2: (-100.0, 100.0), 40: (1.0, 2.0), 0: (0.0, 0.0)})
        self.assertEqual(p, {"volume": -3.0, "loop": 0, "initialDelay": 1.5,
                             "pitchRand": {"min": -100.0, "max": 100.0}, "unknown": {"4": 30.0}})

    def test_nan_becomes_zero(self):
        self.assertEqual(json.loads(ag.dumps({"x": float("nan"), "y": [float("inf")]})), {"x": 0.0, "y": [0.0]})


class BuildTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.src = self.root / "PS4"
        make_tree(self.src)
        self.out = self.root / "PortAudio"
        self.reports = self.root / "reports"
        self.ak = self.root / "AK.cs"
        self.ak.write_text(AK_CS)
        self.log = []
        self.report = ag.build(self.src, self.out, report_dir=self.reports, names=self.ak, log=self.log.append)

    def tearDown(self):
        self.tmp.cleanup()

    def load(self, rel):
        return json.loads((self.out / rel).read_text())

    def test_layout_and_manifest(self):
        files = sorted(str(p.relative_to(self.out)) for p in self.out.rglob("*.json"))
        self.assertEqual(files, ["banks/Init.json", "banks/SFX_Bank.json", "banks/czech/Voice_Bank.json",
                                 "banks/english_uk/Voice_Bank.json", "manifest.json"])
        m = self.load("manifest.json")
        self.assertEqual((m["format"], m["wwiseBankVersion"], m["defaultLanguage"]), (1, 113, "English(UK)"))
        self.assertEqual(m["languages"], [{"name": "Czech", "key": "czech", "bankLanguageId": 6},
                                          {"name": "English(UK)", "key": "english_uk", "bankLanguageId": 11}])
        self.assertEqual(m["banks"], [
            {"name": "Init", "id": 1355168291, "file": "banks/Init.json"},
            {"name": "SFX_Bank", "id": ag.fnv1("SFX_Bank"), "file": "banks/SFX_Bank.json"},
            {"name": "Voice_Bank", "id": ag.fnv1("Voice_Bank"), "localized": True,
             "file": "banks/{lang}/Voice_Bank.json", "languages": ["czech", "english_uk"]}])
        self.assertEqual(m["names"], sorted([[ag.fnv1("PLAY_THING"), "PLAY_THING"],
                                             [ag.fnv1("MUSIC_VOL"), "MUSIC_VOL"]]))
        self.assertTrue((self.reports / "graph-report.json").is_file())
        self.assertIn("4 banks", self.log[0])

    def test_json_is_compact_utf8(self):
        raw = (self.out / "banks/Init.json").read_bytes()
        self.assertNotIn(b": ", raw)
        self.assertNotIn(b", ", raw)

    def test_init_bank(self):
        b = self.load("banks/Init.json")
        self.assertEqual((b["format"], b["name"], b["language"], b["languageKey"]), (1, "Init", "", ""))
        master, music = b["busses"]
        self.assertEqual(master, {"id": MASTER, "props": {"unknown": {"26": 0.0}}, "duckRecoveryMs": 1000,
                                  "maxDuckDb": -96.0, "fx": [{"effect": METER_FX}]})
        self.assertEqual(music["parent"], MASTER)
        self.assertEqual(music["props"], {"busVolume": -6.0})
        self.assertEqual(music["ducks"], [{"bus": MASTER, "volumeDb": -4.0, "fadeOutMs": 100, "fadeInMs": 200,
                                           "curve": 4, "targetProp": "busVolume"}])
        r = music["rtpcs"][0]
        self.assertEqual((r["rtpc"], r["param"], r["accumulation"], r["scaling"]), (GP_VOL, "volume", "additive", "db"))
        self.assertEqual(r["curve"][0], [0.0, -200.0, 4])
        self.assertEqual(r["curve"][-1], [100.0, 0.0, 4])
        self.assertEqual(b["effects"][0]["pluginName"], "meter")
        self.assertTrue(b["effects"][0]["shareSet"])
        self.assertEqual(b["effects"][0]["meter"], {"attackSec": 0.1, "releaseSec": 1.0, "minDb": -48.0,
                                                    "mode": "rms", "gameParameter": GP_VOL})
        self.assertEqual(b["gameParameters"], [{"id": GP_VOL, "name": "MUSIC_VOL", "rampType": "none", "default": 100.0}])
        self.assertEqual(b["stateGroups"], [{"id": SG, "defaultTransitionMs": 1000}])
        att = b["attenuations"][0]
        self.assertEqual([c["use"] for c in att["curves"]], ["volumeDry", "volumeWetGame", "volumeWetUser"])
        self.assertEqual(att["curves"][0]["points"][-1], [10.0, -200.0, 4])

    def test_actions_and_events(self):
        b = self.load("banks/SFX_Bank.json")
        acts = {a["id"]: a for a in b["actions"]}
        self.assertEqual(acts[A_PLAY], {"id": A_PLAY, "target": RS, "bankId": 99, "type": "Play", "rawType": 0x0403,
                                        "gameObjectScoped": True, "delayMs": 500})
        self.assertEqual(acts[A_STOP]["type"], "Stop")
        self.assertEqual(acts[A_STOP]["transitionMs"], 1000)
        self.assertEqual(acts[A_STOP]["curve"], 1)
        self.assertEqual(acts[A_STOP]["exceptions"], [{"id": SND1}])
        self.assertEqual((acts[A_CHAIN]["type"], acts[A_CHAIN]["target"], acts[A_CHAIN]["delayMs"]),
                         ("PlayEvent", EV_MUSIC, 4000))
        self.assertEqual((acts[A_PARAM]["type"], acts[A_PARAM]["value"]), ("SetGameParameter", 80.0))
        self.assertNotIn("valueRelative", acts[A_PARAM])
        self.assertEqual(b["events"][0], {"id": EV_PLAY, "actions": [A_PLAY]})

    def test_nodes(self):
        b = self.load("banks/SFX_Bank.json")
        nodes = {n["id"]: n for n in b["nodes"]}
        s1 = nodes[SND1]
        self.assertNotIn("type", s1)  # Sound is the PaNodeType default
        self.assertEqual(s1["props"], {"volume": -3.0, "loop": 0, "pitchRand": {"min": -100.0, "max": 100.0},
                                       "unknown": {"4": 30.0}})
        self.assertEqual(s1["positioning"], {"threeD": True, "spatialize": True, "attenuation": 7006})
        self.assertEqual(s1["advanced"], {"maxInstances": 2, "virtualBehavior": 1, "killNewest": True,
                                          "globalLimit": True})
        self.assertEqual(s1["source"], {"plugin": B.VORBIS, "media": MEDIA1, "inMemorySize": 8, "kind": "vorbis",
                                        "stream": "embedded"})
        self.assertNotIn("positioning", nodes[SND2])
        rs = nodes[RS]
        self.assertEqual(rs["type"], "RanSeq")
        self.assertEqual(rs["ranSeq"], {"continuous": True, "loopCount": 0, "transition": "delay", "transitionSec": 1.0,
                                        "transitionMaxSec": 8.0, "playlist": [[SND1, 50000], [SND2, 50000]]})
        sw = nodes[SW]["switch"]
        self.assertEqual(sw, {"group": 8001, "default": 8002, "assignments": [{"value": 8002, "nodes": [SND3]},
                                                                             {"value": 8003}],
                              "childParams": [{"node": SND3, "onSwitchMode": 1}]})
        self.assertEqual(nodes[MIX], {"id": MIX, "bus": MUSIC_BUS, "type": "ActorMixer", "children": [RS, SW]})

    def test_music_nodes(self):
        nodes = {n["id"]: n for n in self.load("banks/SFX_Bank.json")["nodes"]}
        msw = nodes[MSW]["music"]
        self.assertEqual(msw["mode"], "bestMatch")
        self.assertTrue(msw["continuePlaying"])
        self.assertEqual(msw["arguments"], [{"group": 8001}])
        self.assertEqual(msw["paths"], [{"keys": [8002], "node": MRS, "weight": 50}, {"keys": [0], "weight": 50}])
        self.assertEqual(msw["transitions"], [{"src": [-1], "dst": [-1],
                                               "srcRule": {"syncRaw": 7, "sync": "exitMarker"},
                                               "dstRule": {"entryRaw": 3, "entry": "randomUserMarker"}}])
        self.assertEqual((msw["tempo"], msw["timeSigNum"], msw["timeSigDen"]), (120.0, 4, 4))
        pl = nodes[MRS]["music"]["playlist"]
        self.assertEqual(pl, {"id": 4101, "type": "sequenceContinuous", "loop": 0, "weight": 50000,
                              "children": [{"id": 4102, "segment": SEG, "weight": 50000}]})
        seg = nodes[SEG]["music"]
        self.assertEqual((seg["durationMs"], seg["entryMs"], seg["exitMs"]), (2000.0, 100.0, 1900.0))
        self.assertEqual(seg["markers"], [{"id": 1, "posMs": 100.0, "kind": "entry"},
                                          {"id": 2, "posMs": 1000.0, "name": "1j", "kind": "custom"},
                                          {"id": 3, "posMs": 1900.0, "kind": "exit"}])
        trk = nodes[TRK]["music"]
        self.assertEqual(trk["trackType"], "normal")
        self.assertEqual(trk["clips"], [{"media": MUS_MEDIA, "playAtMs": -10.0, "srcDurationMs": 2000.0}])
        self.assertEqual(trk["automation"], [{"type": "fadeIn", "points": [[0.0, 0.0, 4], [200.0, 1.0, 4]]}])
        self.assertEqual(trk["sources"][0]["media"], MUS_MEDIA)

    def test_localized_bank(self):
        b = self.load("banks/english_uk/Voice_Bank.json")
        self.assertEqual((b["language"], b["languageKey"]), ("English(UK)", "english_uk"))
        nodes = {n["id"]: n for n in b["nodes"]}
        self.assertEqual(nodes[L_STREAM]["source"], {"plugin": B.VORBIS, "media": 6100, "inMemorySize": 2000,
                                                     "kind": "vorbis", "stream": "streamed", "languageSpecific": True})
        self.assertEqual(nodes[L_EXT]["source"]["kind"], "external")
        self.assertEqual(nodes[L_EXT]["source"]["media"], 6200)

    def test_report(self):
        r = json.loads((self.reports / "graph-report.json").read_text())
        self.assertEqual(r, self.report)
        banks = {(b["name"], b["language"]): b for b in r["banks"]}
        self.assertEqual(banks[("SFX_Bank", "")]["objects"]["Sound"], 3)
        self.assertEqual(banks[("SFX_Bank", "")]["unparsed"], [])
        ev = {(e["id"], e["language"]): e for e in r["events"]}
        play = ev[(EV_PLAY, "")]
        self.assertEqual(play["bank"], "SFX_Bank")
        self.assertEqual(play["media"], [{"id": MEDIA1, "scope": "li"}, {"id": MEDIA2, "scope": "li"}])
        self.assertFalse(play["reachesNothing"])
        self.assertTrue(ev[(EV_STOP, "")]["reachesNothing"])
        self.assertTrue(ev[(EV_PARAM, "")]["reachesNothing"])
        self.assertEqual(ev[(EV_MISSING, "")]["missingTargets"], [9999])
        self.assertEqual(ev[(EV_MISSING, "")]["deadTargets"], [9999])
        self.assertFalse(ev[(EV_MISSING, "")]["controlOnly"])
        self.assertTrue(ev[(EV_STOP, "")]["controlOnly"])
        self.assertTrue(ev[(EV_PARAM, "")]["controlOnly"])
        for eid in (EV_PLAY, EV_CHAIN, EV_MUSIC):
            self.assertFalse(ev[(eid, "")]["controlOnly"])
            self.assertEqual(ev[(eid, "")]["deadTargets"], [])
        self.assertEqual(ev[(EV_MUSIC, "")]["media"], [{"id": MUS_MEDIA, "scope": "li"}])
        self.assertEqual(ev[(EV_CHAIN, "")]["media"], [{"id": MUS_MEDIA, "scope": "li"}])  # via PlayEvent
        cz = ev[(L_EV, "Czech")]
        self.assertEqual(cz["media"], [{"id": L_EMB, "scope": "czech"}, {"id": 6100, "scope": "czech"},
                                       {"id": 6200, "scope": "external"}])
        self.assertEqual(ev[(EV_VOICE, "English(UK)")]["media"], [{"id": MEDIA1, "scope": "li"}])
        self.assertEqual(r["names"], {"events": 8, "resolved": 0})

    def test_referenced_media(self):
        ref = ag.referenced_media(self.report)
        self.assertEqual(ref["li"], {MEDIA1, MEDIA2, MEDIA3, MUS_MEDIA})
        self.assertEqual(ref["czech"], {6100, L_EMB})
        self.assertEqual(ref["english_uk"], {6100, L_EMB})
        self.assertNotIn("external", ref)

    def test_stale_bank_json_removed_and_rebuild_is_stable(self):
        stale = self.out / "banks" / "old" / "Gone.json"
        stale.parent.mkdir(parents=True)
        stale.write_text("{}")
        before = (self.out / "banks/SFX_Bank.json").read_bytes()
        ag.build(self.src, self.out, report_dir=self.reports, names=self.ak, log=lambda m: None)
        self.assertFalse(stale.exists())
        self.assertFalse(stale.parent.exists())
        self.assertEqual((self.out / "banks/SFX_Bank.json").read_bytes(), before)


def _bank(folder, name, objects):
    language = "" if folder == ag.LI_FOLDER else folder
    return {"folder": folder, "name": name, "language": language,
            "scope": "li" if not language else ag.lang_key(language),
            "bank": {"header": {"id": 1}, "objects": objects, "didx": [], "unparsed": []}}


class ReportClassificationTest(unittest.TestCase):
    """controlOnly and deadTargets on synthetic parsed banks."""

    def setUp(self):
        ev = lambda i, acts: {"type": "Event", "id": i, "actions": acts}
        act = lambda i, kind, target: {"type": "Action", "id": i, "action": kind, "target": target}
        li = [
            ev(1, [11]), act(11, "play", 500),            # 500 exists only in the Czech bank
            ev(2, [21]), act(21, "play", 600),            # 600 exists in no bank
            ev(3, [31, 32]), act(31, "stop", 600), act(32, "pause", 0),
            ev(4, []),
            ev(5, [51]),                                  # action 51 is in no bank
            ev(6, [61]), act(61, "playEvent", 700),       # chained event in no bank
            ev(7, [71]), act(71, "resume", 0),
            ev(8, [81, 82]), act(81, "setGameParameter", 0), act(82, "playEvent", 7),  # chain to control only
            ev(9, [91]), act(91, "playEvent", 1),          # chain to a Play
            ev(10, [101]), act(101, "playEvent", 10),      # self loop, plays nothing
        ]
        cz = [{"type": "Sound", "id": 500, "source": {"kind": "vorbis", "id": 9, "stream": "embedded"}}]
        banks = [_bank(ag.LI_FOLDER, "SFX", li), _bank("Czech", "Voice", cz)]
        self.ev = {e["id"]: e for e in ag.build_report(banks, {}, "English(UK)")["events"]}

    def test_control_only(self):
        self.assertTrue(self.ev[3]["controlOnly"])
        self.assertTrue(self.ev[4]["controlOnly"])
        self.assertTrue(self.ev[7]["controlOnly"])
        self.assertTrue(self.ev[8]["controlOnly"])
        self.assertTrue(self.ev[10]["controlOnly"])
        self.assertFalse(self.ev[9]["controlOnly"])
        for i in (1, 2, 6):  # 6 chains to an event in no bank
            self.assertFalse(self.ev[i]["controlOnly"])
        self.assertFalse(self.ev[5]["controlOnly"])  # unknown action: not provably control-only

    def test_dead_targets(self):
        self.assertEqual(self.ev[1]["missingTargets"], [500])
        self.assertEqual(self.ev[1]["deadTargets"], [])  # present in another language's bank
        self.assertEqual(self.ev[2]["deadTargets"], [600])
        self.assertEqual(self.ev[5]["deadTargets"], [51])
        self.assertEqual(self.ev[6]["deadTargets"], [700])
        self.assertEqual(self.ev[3]["deadTargets"], [])


class UnparsedTest(unittest.TestCase):
    def test_unparsed_object_still_emitted_and_reported(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            bad = B.bank(ag.fnv1("SFX_Bank"), 0, [(B.T_SOUND, B.sound(42, B.source(sid=1)) + b"\0")])
            make_tree(root / "PS4", sfx_bytes=bad)
            report = ag.build(root / "PS4", root / "out", report_dir=root / "rep", log=lambda m: None)
            sfx = [b for b in report["banks"] if b["name"] == "SFX_Bank"][0]
            self.assertEqual(sfx["unparsed"], [{"id": 42, "type": "Sound", "reason": "1 bytes left"}])
            nodes = json.loads((root / "out/banks/SFX_Bank.json").read_text())["nodes"]
            self.assertEqual(nodes[0]["id"], 42)

    def test_cli(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            make_tree(root / "PS4")
            with contextlib.redirect_stdout(io.StringIO()):
                rc = ag.main(["--source", str(root / "PS4"), "--out", str(root / "w/PortAudio")])
            self.assertEqual(rc, 0)
            self.assertTrue((root / "w/PortAudio/manifest.json").is_file())
            self.assertTrue((root / "w/reports/graph-report.json").is_file())

    def test_empty_source_fails(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "LocaleIndependent").mkdir()
            with self.assertRaises(FileNotFoundError):
                ag.build(Path(d), Path(d) / "o", report_dir=Path(d) / "r", log=lambda m: None)


REAL_ROOT = Path(__file__).resolve().parent.parent / "work/merged/Media/StreamingAssets/Audio/GeneratedSoundBanks/PS4"
REAL_AK = Path(__file__).resolve().parent.parent / "work/project/Assets/Scripts/Assembly-CSharp/AK.cs"


@unittest.skipUnless(os.environ.get("PORT_AUDIO_REAL") == "1", "set PORT_AUDIO_REAL=1 to build from the real banks")
class RealGraphTest(unittest.TestCase):
    def test_real_build(self):
        with tempfile.TemporaryDirectory() as d:
            out = Path(d) / "PortAudio"
            report = ag.build(REAL_ROOT, out, report_dir=Path(d) / "reports",
                              names=REAL_AK if REAL_AK.is_file() else None, log=lambda m: None)
            self.assertEqual(len(report["banks"]), 103)
            self.assertEqual(sum(len(b["unparsed"]) for b in report["banks"]), 0)
            manifest = json.loads((out / "manifest.json").read_text())
            self.assertEqual(len(manifest["languages"]), 9)
            self.assertEqual(manifest["defaultLanguage"], "English(UK)")
            self.assertEqual(len(manifest["banks"]), 23)
            en_li = {e["id"] for e in report["events"] if e["language"] in ("", "English(UK)")}
            self.assertEqual(len(en_li), 639)
            ref = ag.referenced_media(report)
            self.assertEqual(set(ref), {"li"} | {l["key"] for l in manifest["languages"]})


if __name__ == "__main__":
    unittest.main()
