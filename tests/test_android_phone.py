import struct
import unittest

from pipeline import android_phone as ap


class FrameTest(unittest.TestCase):
    def test_client_frame_is_masked_text(self):
        f = ap.encode_frame(b"hello", mask=b"\x01\x02\x03\x04")
        self.assertEqual(f[0], 0x81)
        self.assertEqual(f[1], 0x80 | 5)
        self.assertEqual(f[2:6], b"\x01\x02\x03\x04")
        self.assertEqual(bytes(b ^ f[2 + (i % 4)] for i, b in enumerate(f[6:])), b"hello")

    def test_long_payload_uses_extended_length(self):
        f = ap.encode_frame(b"x" * 70000, mask=b"\0\0\0\0")
        self.assertEqual(f[1] & 0x7F, 127)
        self.assertEqual(struct.unpack(">Q", f[2:10])[0], 70000)

    def test_decode_server_frames_with_fragments(self):
        a = bytes([0x01, 3]) + b"abc"        # text, not final
        b = bytes([0x80, 126]) + struct.pack(">H", 2) + b"de"  # continuation, final
        dec = ap.FrameDecoder()
        self.assertEqual(dec.feed(a[:2]), [])
        self.assertEqual(dec.feed(a[2:] + b), [(1, b"abcde")])

    def test_decode_close(self):
        self.assertEqual(ap.FrameDecoder().feed(bytes([0x88, 0])), [(8, b"")])


class TouchTest(unittest.TestCase):
    def test_mouse_sequence_maps_to_touch(self):
        self.assertIsNone(ap.touch_for("mouseMoved", 1, 2, 0))
        self.assertEqual(ap.touch_for("mousePressed", 1, 2, 1),
                         ("touchStart", [{"x": 1, "y": 2, "id": 0}]))
        self.assertEqual(ap.touch_for("mouseMoved", 3, 4, 1), ("touchMove", [{"x": 3, "y": 4, "id": 0}]))
        self.assertEqual(ap.touch_for("mouseReleased", 3, 4, 0), ("touchEnd", []))


class TargetTest(unittest.TestCase):
    def test_picks_page_with_url_prefix(self):
        targets = [{"type": "page", "url": "chrome://newtab/", "webSocketDebuggerUrl": "ws://a"},
                   {"type": "service_worker", "url": "http://127.0.0.1:5/sw.js", "webSocketDebuggerUrl": "ws://b"},
                   {"type": "page", "url": "http://127.0.0.1:5/#j=1", "webSocketDebuggerUrl": "ws://c"}]
        self.assertEqual(ap.pick_target(targets, "http://127.0.0.1:5/")["webSocketDebuggerUrl"], "ws://c")
        self.assertIsNone(ap.pick_target(targets, "http://x/"))


class PromptTest(unittest.TestCase):
    XML = ('<hierarchy><node text="http://127.0.0.1:5 wants to use your camera" bounds="[0,0][10,10]"/>'
           '<node text="Block" bounds="[500,1312][700,1438]"/>'
           '<node text="Allow" bounds="[780,1312][1011,1438]"/></hierarchy>')

    def test_taps_allow_of_a_site_prompt(self):
        self.assertEqual(ap.prompt_tap(self.XML), (895, 1375))

    def test_android_runtime_prompt(self):
        xml = '<node text="While using the app" bounds="[100,200][300,400]"/>'
        self.assertEqual(ap.prompt_tap(xml), (200, 300))

    def test_ignores_screens_without_a_prompt(self):
        self.assertIsNone(ap.prompt_tap('<node text="Allow" bounds="[0,0][2,2]"/>'))
        self.assertIsNone(ap.prompt_tap('<node text="PLAY" bounds="[0,0][2,2]"/>'))


class StaleTest(unittest.TestCase):
    def test_closes_every_page_but_the_one_kept(self):
        targets = [{"id": "1", "type": "page", "url": "http://127.0.0.1:1/#x"},
                   {"id": "2", "type": "service_worker", "url": "http://127.0.0.1:5/sw.js"},
                   {"id": "3", "type": "page", "url": "http://127.0.0.1:5/#j"}]
        self.assertEqual(ap.pages_to_close(targets), ["1", "3"])
        self.assertEqual(ap.pages_to_close(targets, keep="3"), ["1"])


if __name__ == "__main__":
    unittest.main()
