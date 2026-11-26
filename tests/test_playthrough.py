import random
import unittest

from pipeline.playthrough import decide


def d(visible, screen="x", phone=0, mem=None, **kw):
    return decide({"screen": screen, "variant": "", "visible": visible, **kw}, phone, {} if mem is None else mem,
                  random.Random(1))


class DecideTest(unittest.TestCase):
    def test_popups_and_leave_dialog_are_dismissed_first(self):
        self.assertEqual(d(["popup-close", "connect-play"]), [("tap", "popup-close")])
        self.assertEqual(d(["leave-cancel", "leave-confirm"]), [("tap", "leave-cancel")])

    def test_join_and_name(self):
        self.assertEqual(d(["connect-play"]), [("tap", "connect-play")])
        self.assertEqual(d(["name-input", "name-next"], inputValue=""), [("type", "name-input", "Bot1")])
        self.assertEqual(d(["name-input", "name-next"], inputValue="Bot1"), [("tap", "name-next")])

    def test_card_pick_uses_own_index(self):
        self.assertEqual(d(["card-1", "card-2", "card-3", "card-confirm"], phone=1),
                         [("tap", "card-2"), ("tap", "card-confirm")])

    def test_camera_takes_then_uses(self):
        self.assertEqual(d(["shutter", "flip"]), [("tap", "shutter")])
        self.assertEqual(d(["use", "retake"]), [("tap", "use")])

    def test_only_first_phone_holds_start(self):
        self.assertEqual(d(["start"], phone=0), [("hold", "start", 2600)])
        self.assertEqual(d(["start"], phone=2), [])

    def test_answers_once_per_question(self):
        mem = {}
        first = d(["answer-1", "answer-2", "answer-confirm"], mem=mem)
        self.assertEqual(first[0][0], "tap")
        self.assertIn(first[0][1], ("answer-1", "answer-2"))
        self.assertEqual(d(["answer-1", "answer-2", "answer-confirm"], mem=mem), [("tap", "answer-confirm")])

    def test_draw_then_confirm(self):
        mem = {}
        self.assertEqual(d(["draw-canvas", "draw-confirm"], mem=mem), [("draw", "draw-canvas"), ("tap", "draw-confirm")])
        self.assertEqual(d(["draw-canvas", "draw-confirm"], mem=mem), [("tap", "draw-confirm")])

    def test_text_entry(self):
        self.assertEqual(d(["text-input", "text-confirm"], inputValue=""), [("type", "text-input", "Answer from bot 1")])
        self.assertEqual(d(["text-input", "text-confirm"], inputValue="x"), [("tap", "text-confirm")])

    def test_waiting_screens_do_nothing(self):
        self.assertEqual(d([], screen="holding"), [])


if __name__ == "__main__":
    unittest.main()
