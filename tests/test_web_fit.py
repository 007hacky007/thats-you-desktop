import unittest

from pipeline.web_fit import judge


class JudgeTest(unittest.TestCase):
    def test_clean_screen_passes(self):
        self.assertEqual(judge({"docScroll": [0, 0], "scrollers": [], "cut": [], "rotate": False}), [])

    def test_document_scroll_fails(self):
        self.assertIn("page scrolls by 0x40", judge({"docScroll": [0, 40], "scrollers": [], "cut": []}))

    def test_inner_scroller_and_cut_content_fail(self):
        p = judge({"docScroll": [0, 0], "scrollers": ["div y+20"], "cut": ["img 0,600,10,700"]})
        self.assertEqual(len(p), 2)

    def test_landscape_needs_rotate_hint(self):
        self.assertEqual(judge({"rotate": True}, landscape=True), [])
        self.assertTrue(judge({"rotate": False}, landscape=True))

    def test_keyboard_open_must_not_show_rotate_hint(self):
        self.assertTrue(judge({"rotate": True}, keyboard=True))
        self.assertEqual(judge({"rotate": False, "docScroll": [0, 99]}, keyboard=True), [])

    def test_rotate_hint_in_portrait_fails(self):
        self.assertTrue(judge({"docScroll": [0, 0], "rotate": True}))


if __name__ == "__main__":
    unittest.main()
