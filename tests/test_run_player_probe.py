import json
import tempfile
import unittest
from pathlib import Path

from pipeline import run_player


def state(page="MainMenu", index=0, item="Play", active=True, labels=("Play", "Options"), logo=False):
    return {
        "scene": "Home",
        "menu": {"active": active, "transition": False, "ignoreInput": "None", "loading": False,
                 "page": None if page is None else {"name": page, "object": page, "class": "MenuPage",
                                                    "type": "kOptions", "index": index, "count": 3,
                                                    "item": item, "itemObject": item, "itemClass": "MenuOnlineGame"}},
        "display": None,
        "frontend": {"in": True, "starting": False, "home": "Center", "transition": "None"},
        "logo": logo,
        "pause": {"canPause": False, "paused": False},
        "labels": list(labels),
    }


def probe_line(t, reason, st, msg="menu=on"):
    return json.dumps({"t": t, "kind": "probe", "msg": msg, "reason": reason, "state": st})


class ParseProbeEventsTests(unittest.TestCase):
    def test_probes_collected_in_order(self):
        text = "\n".join([
            probe_line(1.0, "scene Home", state()),
            json.dumps({"t": 1.5, "kind": "key", "msg": "key up: down"}),
            probe_line(2.0, "key down", state(index=1, item="Options")),
            json.dumps({"t": 2.5, "kind": "probe", "msg": "x", "state": "not an object"}),
            "{broken",
        ])
        r = run_player.parse_events(text)
        self.assertEqual([p["reason"] for p in r["probes"]], ["scene Home", "key down"])
        self.assertEqual(r["probes"][1]["state"]["menu"]["page"]["index"], 1)
        self.assertEqual(r["probes"][0]["t"], 1.0)

    def test_no_probes(self):
        self.assertEqual(run_player.parse_events("")["probes"], [])

    def test_flatten(self):
        flat = run_player.flatten_state(state())
        self.assertEqual(flat["menu.page.index"], 0)
        self.assertEqual(flat["menu.page.item"], "Play")
        self.assertIsNone(flat["display"])
        self.assertEqual(flat["labels"], ["Play", "Options"])
        flat = run_player.flatten_state(state(page=None))
        self.assertIsNone(flat["menu.page"])


class ProbeChangesTests(unittest.TestCase):
    def probes(self, *states):
        return [{"t": float(i), "reason": f"r{i}", "msg": "", "state": s} for i, s in enumerate(states)]

    def test_first_probe_described_unchanged_skipped(self):
        lines = run_player.probe_changes(self.probes(state(), state()))
        self.assertEqual(len(lines), 1)
        self.assertTrue(lines[0].startswith("t=0.00 [r0] "))
        self.assertIn("menu.page.item='Play'", lines[0])
        self.assertIn("labels=['Play', 'Options']", lines[0])
        self.assertNotIn("display", lines[0])  # null fields are left out

    def test_selection_change(self):
        lines = run_player.probe_changes(self.probes(state(), state(index=1, item="Options")))
        self.assertEqual(len(lines), 2)
        self.assertIn("menu.page.index: 0 -> 1", lines[1])
        self.assertIn("menu.page.item: 'Play' -> 'Options'", lines[1])
        self.assertIn("menu.page.itemObject: 'Play' -> 'Options'", lines[1])
        self.assertNotIn("labels", lines[1])

    def test_menu_closed(self):
        lines = run_player.probe_changes(self.probes(state(), state(page=None, active=False, labels=())))
        self.assertIn("menu.active: true -> false", lines[1])
        self.assertIn("menu.page.name: 'MainMenu' -> null", lines[1])
        self.assertIn("labels: -['Play', 'Options']", lines[1])

    def test_label_diff_and_reorder(self):
        lines = run_player.probe_changes(self.probes(
            state(labels=("A", "B")), state(labels=("B", "C")), state(labels=("C", "B"))))
        self.assertIn("labels: +['C'] -['A']", lines[1])
        self.assertIn("labels: reordered", lines[2])

    def test_label_list_capped(self):
        many = [f"L{i}" for i in range(12)]
        line = run_player.probe_changes(self.probes(state(labels=()), state(labels=many)))[1]
        self.assertIn("'L7', +4 more]", line)
        self.assertNotIn("'L8'", line)

    def test_line_width(self):
        long = ["x" * 80 for _ in range(30)]
        line = run_player.probe_changes(self.probes(state(labels=long)), width=100)[0]
        self.assertEqual(len(line), 100)
        self.assertTrue(line.endswith("..."))


class ReportProbeTests(unittest.TestCase):
    def test_report_lists_probe_changes(self):
        with tempfile.TemporaryDirectory() as tmp:
            run_dir = Path(tmp)
            (run_dir / "shots").mkdir()
            (run_dir / "shots" / "log.jsonl").write_text("\n".join([
                probe_line(1.0, "scene Home", state(), msg="menu=on page=MainMenu"),
                probe_line(3.0, "timer", state()),
                probe_line(4.0, "key down", state(index=1, item="Options"), msg="menu=on sel=1"),
            ]))
            out = run_player.report(run_dir, None)
        self.assertIn("probe changes (2 of 3 probes):", out)
        self.assertIn("  t=4.00 [key down] menu.page.index: 0 -> 1", out)
        self.assertIn("last probe: t=4.00 menu=on sel=1", out)


if __name__ == "__main__":
    unittest.main()
