import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from paddesk import keys  # noqa: E402
from paddesk.engine import CaptureDetector, InputEngine  # noqa: E402
from paddesk.model import (Mapping, Profile, SequenceError, Snapshot, Source, Store, parse_hog,  # noqa: E402
                           parse_hog_config, parse_keys)
from paddesk.service import create_profile  # noqa: E402

A, B, LB = 0x130, 0x131, 0x136


def pad(*pressed, axes=None):
    return Snapshot(True, "pad", "Pad", pressed, axes if axes is not None else {0: 0.0, 1: 0.0})


def profile(*mappings):
    p = Profile("pad", "Pad")
    p.mappings = list(mappings)
    p.validate()
    return p


def evs(r):
    return [(e.token, e.down) for e in r.events]


class KeyTests(unittest.TestCase):
    def test_codes_match_kernel(self):
        try:
            from evdev import ecodes
        except ImportError:
            self.skipTest("python-evdev not installed")
        for name, code in keys.CODES.items():
            self.assertEqual(getattr(ecodes, name), code, name)
        for name, code in keys.BTN.items():
            self.assertEqual(getattr(ecodes, "BTN_" + name), code, name)
        for name, code in keys.ABS.items():
            self.assertEqual(getattr(ecodes, "ABS_" + name), code, name)

    def test_parse(self):
        self.assertEqual(keys.parse("ctrl"), 29)
        self.assertEqual(keys.parse("Super"), 125)
        self.assertEqual(keys.parse("F12"), 88)
        self.assertEqual(keys.parse("←"), 105)
        self.assertEqual(keys.parse("VolUp"), 115)
        self.assertEqual(keys.parse("KEY_A"), 30)
        self.assertEqual(keys.parse("a"), 30)
        self.assertEqual(keys.parse("nonsense"), 0)
        self.assertEqual(keys.name(29), "Ctrl")
        self.assertEqual(keys.chord([29, 30]), "Ctrl + A")


class SequenceTests(unittest.TestCase):
    def test_hog(self):
        self.assertEqual(parse_hog("*jf"), [[29], [36], [33]])
        self.assertEqual(parse_hog("+Q"), [[97], [16]])
        with self.assertRaises(SequenceError):
            parse_hog("J1")
        with self.assertRaises(SequenceError):
            parse_hog("")

    def test_keys(self):
        self.assertEqual(parse_keys("Super+Ctrl+Right Enter"), [[125, 29, 106], [28]])
        self.assertEqual(parse_keys("Ctrl+C, Ctrl+V"), [[29, 46], [29, 47]])
        with self.assertRaises(SequenceError):
            parse_keys("Ctrl+Bogus")

    def test_hog_config(self):
        lines = ["// comment", "JFQR CMD:QRCODE", "*JFYX TEXT:me@example.com // mail", "bad1 CMD:X", "JFX"]
        self.assertEqual(parse_hog_config(lines), [{"hotkey": "JFQR", "action": "CMD:QRCODE"},
                                                   {"hotkey": "*JFYX", "action": "TEXT:me@example.com"}])


class EngineTests(unittest.TestCase):
    def test_hold_and_release_with_modifier_order(self):
        p = profile(Mapping(Source("key", LB), target="keyboard", codes=[56]),
                    Mapping(Source("key", A), target="keyboard", codes=[15]))
        e = InputEngine()
        e.tick(pad(), 1.0, p, True)          # arms
        self.assertEqual(evs(e.tick(pad(LB), 1.01, p, True)), [("K:56", True)])
        self.assertEqual(evs(e.tick(pad(LB, A), 1.02, p, True)), [("K:15", True)])
        self.assertEqual(evs(e.tick(pad(LB), 1.03, p, True)), [("K:15", False)])
        self.assertEqual(evs(e.tick(pad(LB, A), 1.04, p, True)), [("K:15", True)])
        # both released at once: the normal key goes up before the modifier
        self.assertEqual(evs(e.tick(pad(), 1.05, p, True)), [("K:15", False), ("K:56", False)])

    def test_not_armed_until_released(self):
        p = profile(Mapping(Source("key", A), target="mouse_left"))
        e = InputEngine()
        self.assertEqual(evs(e.tick(pad(A), 1.0, p, True)), [])
        self.assertEqual(evs(e.tick(pad(A), 1.01, p, True)), [])
        e.tick(pad(), 1.02, p, True)
        self.assertEqual(evs(e.tick(pad(A), 1.03, p, True)), [("M:L", True)])

    def test_disable_releases(self):
        p = profile(Mapping(Source("key", A), target="mouse_right"))
        e = InputEngine()
        e.tick(pad(), 1.0, p, True)
        e.tick(pad(A), 1.01, p, True)
        self.assertEqual(evs(e.tick(pad(A), 1.02, p, False)), [("M:R", False)])
        self.assertEqual(evs(e.tick(Snapshot(False), 1.03, p, True)), [])

    def test_quick_tap_in_partial_frames(self):
        p = profile(Mapping(Source("key", A), target="keyboard", codes=[30]))
        e = InputEngine()
        e.tick(pad(), 1.0, p, True)
        self.assertEqual(evs(e.tick(pad(A), 1.001, p, True, partial=True)), [("K:30", True)])
        self.assertEqual(evs(e.tick(pad(), 1.002, p, True, partial=True)), [("K:30", False)])

    def test_sequence_plays_one_step_per_frame(self):
        m = Mapping(Source("key", A), target="sequence", steps=[[36], [36], [33]], sequence_text="JJF")
        p = profile(m)
        e = InputEngine()
        e.tick(pad(), 1.0, p, True)
        out, t = [], 1.0
        for i in range(10):
            t += 0.01
            out += evs(e.tick(pad(A) if i < 2 else pad(), t, p, True))
        self.assertEqual(out, [("K:36", True), ("K:36", False), ("K:36", True), ("K:36", False), ("K:33", True), ("K:33", False)])

    def test_volume_repeats_while_held(self):
        p = profile(Mapping(Source("key", A), target="media", codes=[115]))
        e = InputEngine()
        e.tick(pad(), 0.0, p, True)
        downs, t = 0, 0.0
        while t < 1.0:
            t += 0.01
            downs += sum(1 for tok, d in evs(e.tick(pad(A), t, p, True)) if d)
        self.assertGreaterEqual(downs, 5)  # first press + repeats every 90 ms after 350 ms

    def test_dpad_steps_with_repeat(self):
        up = Mapping(Source("axis", 0x11, -1, 0.0, 0.5), target="up", locked=True)
        p = profile(up)
        e = InputEngine()
        e.tick(pad(axes={0x11: 0.0}), 0.0, p, True)
        r = e.tick(pad(axes={0x11: -1.0}), 0.01, p, True)
        self.assertEqual((r.dx, r.dy), (0, -20))
        self.assertEqual(e.tick(pad(axes={0x11: -1.0}), 0.2, p, True).dy, 0)
        self.assertEqual(e.tick(pad(axes={0x11: -1.0}), 0.4, p, True).dy, -20)

    def test_stick_moves_mouse(self):
        p = profile()
        e = InputEngine()
        e.tick(pad(), 0.0, p, True)
        total, t = 0, 0.0
        for _ in range(50):
            t += 0.02
            total += e.tick(pad(axes={0: 1.0, 1: 0.0}), t, p, True).dx
        self.assertTrue(700 <= total <= 820, total)  # ~800 px/s at full tilt

    def test_capture_detector(self):
        base = pad(axes={0: 0.0, 1: 0.0, 2: -1.0})
        d = CaptureDetector(base, ignore=(0, 1))
        self.assertIsNone(d.poll(pad(A, axes={0: 0.0, 1: 0.0, 2: -1.0})))  # held at start: not ready
        self.assertIsNone(d.poll(pad(axes={0: 0.0, 1: 0.0, 2: -1.0})))
        self.assertIsNone(d.poll(pad(axes={0: 0.9, 1: 0.0, 2: -1.0})))  # stick ignored
        s = d.poll(pad(axes={0: 0.0, 1: 0.0, 2: 0.6}))
        self.assertEqual((s.kind, s.code, s.direction), ("axis", 2, 1))
        self.assertEqual(d.poll(pad(B)).id, "key:%d" % B)


class ProfileTests(unittest.TestCase):
    def test_default_profile_and_roundtrip(self):
        axes = {0: 0.0, 1: 0.0, 2: -1.0, 3: 0.0, 4: 0.0, 5: -1.0, 0x10: 0.0, 0x11: 0.0}
        p = create_profile("045e:028e:Xbox", "Xbox", [A, B, 0x133, 0x134, LB], axes)
        ids = {m.input.id: m for m in p.mappings}
        self.assertEqual(ids["key:%d" % A].name, "A")
        self.assertFalse(ids["key:%d" % A].confirmed)
        self.assertEqual(ids["axis:17:-"].target, "up")
        self.assertIn("axis:2:+", ids)
        self.assertNotIn("axis:2:-", ids)  # trigger rests at -1
        self.assertNotIn("axis:0:+", ids)  # left stick is the mouse
        with tempfile.TemporaryDirectory() as d:
            st = Store(d)
            st.save_profile(p)
            q = st.load_profile(p.device_id)
            self.assertEqual(q.to_json(), p.to_json())

    def test_axis_names_say_the_direction(self):
        axes = {0: 0.0, 1: 0.0, 2: -1.0, 3: 0.0, 4: 0.0, 5: -1.0, 0x10: 0.0, 0x11: 0.0}
        p = create_profile("pad", "Pad", [A], axes)
        ids = {m.input.id: m for m in p.mappings}
        # evdev Y grows downwards: ABS_RY − is pushing the right stick up
        self.assertEqual(ids["axis:4:-"].name, "右摇杆 ↑")
        self.assertEqual(ids["axis:4:+"].name, "右摇杆 ↓")
        self.assertEqual(ids["axis:3:+"].name, "右摇杆 →")
        self.assertEqual(ids["axis:4:-"].input.caption, "右摇杆 ↑")
        self.assertEqual(ids["axis:2:+"].name, "L2 / LT")
        # profiles saved by earlier versions get the ambiguous "右摇杆 Y +" names replaced
        d = p.to_json()
        for m in d["mappings"]:
            if m["input"]["kind"] == "axis" and m["input"]["code"] == 4:
                m["name"] = "右摇杆 Y +" if m["input"]["direction"] > 0 else "右摇杆 Y −"
        q = {m.input.id: m for m in Profile.from_json(d).mappings}
        self.assertEqual((q["axis:4:-"].name, q["axis:4:+"].name), ("右摇杆 ↑", "右摇杆 ↓"))

    def test_face_names_follow_layout(self):
        p = create_profile("pad", "Pad", [A, B, 0x133, 0x134], {})
        m = {x.input.id: x for x in p.mappings}
        self.assertEqual([m["key:%d" % c].display_name("xbox") for c in (A, B, 0x133, 0x134)], ["A", "B", "Y", "X"])
        self.assertEqual([m["key:%d" % c].display_name("nintendo") for c in (A, B, 0x133, 0x134)], ["B", "A", "X", "Y"])
        m["key:%d" % B].name = "跳跃"
        self.assertEqual(m["key:%d" % B].display_name("nintendo"), "跳跃")

    def test_validation(self):
        p = profile()
        p.mappings = [Mapping(Source("key", A), target="keyboard", codes=[])]
        with self.assertRaises(ValueError):
            p.validate()
        p.mappings = [Mapping(Source("key", A), target="media", codes=[30])]
        with self.assertRaises(ValueError):
            p.validate()
        p.mappings = [Mapping(Source("key", A)), Mapping(Source("key", A))]
        with self.assertRaises(ValueError):
            p.validate()


if __name__ == "__main__":
    unittest.main()
