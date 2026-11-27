import json
import os
import sys
import tempfile
import unittest
import urllib.error
import urllib.request
from collections import namedtuple

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from paddesk import server  # noqa: E402
from paddesk.model import Snapshot, Store  # noqa: E402
from paddesk.output import RecordingOutput  # noqa: E402
from paddesk.service import Service  # noqa: E402

Ev = namedtuple("Ev", "type code value")
A, B = 0x130, 0x131


class FakeTracked:
    def __init__(self, kind, id_, name, key_codes=(), axes=None, path=None):
        self.kind, self.id, self.name, self.path = kind, id_, name, path or "/dev/input/" + id_
        self.key_codes = list(key_codes)
        self.axes = dict(axes or {})
        self.ranges = {c: (-32768, 32767) for c in self.axes}
        self.keys = set()
        self.grabbed = False

    def snapshot(self):
        return Snapshot(True, self.id, self.name, self.keys, self.axes)

    def resync(self):
        pass

    def set_grab(self, on):
        self.grabbed = on
        return True


class FakeManager:
    def __init__(self, *devs):
        self.devices = {d.path: d for d in devs}

    def gamepads(self):
        return [d for d in self.devices.values() if d.kind == "gamepad"]

    def key_devices(self):
        return [d for d in self.devices.values() if d.kind == "keys"]

    def drop(self, path):
        self.devices.pop(path, None)

    def close(self):
        pass


class Clock:
    t = 100.0

    def __call__(self):
        return self.t


def press(svc, dev, code, down=True):
    svc.handle(dev, [Ev(1, code, 1 if down else 0), Ev(0, 0, 0)])


class ServiceTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.pad = FakeTracked("gamepad", "pad1", "Test Pad", [A, B, 0x220, 0x221], {0: 0.0, 1: 0.0, 2: -1.0})
        self.remote = FakeTracked("keys", "ir1", "IR Receiver", [28, 115])
        self.kbd = FakeTracked("keys", "kbd", "Keyboard", [29, 56, 88])
        self.out = RecordingOutput()
        self.clock = Clock()
        self.svc = Service(Store(self.dir.name), FakeManager(self.pad, self.remote, self.kbd), self.out, self.clock)

    def tearDown(self):
        self.dir.cleanup()

    def tick(self, n=1):
        for _ in range(n):
            self.clock.t += 0.01
            self.svc.frame()

    def set_pad_output(self, code, target, keys_=()):
        d = self.svc.get_profile("pad")
        for m in d["profile"]["mappings"]:
            if m["input"] == {"kind": "key", "code": code}:
                m["target"], m["keys"] = target, list(keys_)
        return self.svc.save_profile("pad", d["profile"], d["rev"])

    def test_default_profile_dpad_and_mapping(self):
        d = self.svc.get_profile("pad")["profile"]
        dpad_up = [m for m in d["mappings"] if m["input"]["code"] == 0x220][0]
        self.assertEqual((dpad_up["target"], dpad_up["locked"]), ("up", True))
        self.set_pad_output(A, "keyboard", [30])
        self.tick()
        press(self.svc, self.pad, A)
        press(self.svc, self.pad, A, False)
        self.assertEqual(self.out.log[-2:], [("K:30", True), ("K:30", False)])
        self.assertTrue(os.path.exists(self.svc.store.profile_path("pad1")))

    def test_pause_hotkey_and_release(self):
        self.set_pad_output(A, "mouse_left")
        self.tick()
        press(self.svc, self.pad, A)
        self.assertEqual(self.out.log[-1], ("M:L", True))
        press(self.svc, self.kbd, 29)
        press(self.svc, self.kbd, 56)
        press(self.svc, self.kbd, 88)
        self.assertTrue(self.svc.paused)
        self.assertNotIn("M:L", self.out.owned)
        self.assertEqual(self.svc.status_text(), "已暂停")

    def test_learn_pad_and_remote(self):
        self.svc.start_learn("pad")
        self.assertEqual(self.svc.status_text(), "校准 / 编辑中")
        self.tick()
        press(self.svc, self.pad, B)
        self.assertEqual(self.svc.learn_result, {"scope": "pad", "id": "key:%d" % B})
        self.assertTrue(self.svc.profile_for(self.pad).find("key:%d" % B).confirmed)
        self.svc.start_learn("remote")
        press(self.svc, self.remote, 115)
        rid = "remote:ir1:115"
        self.assertEqual(self.svc.learn_result["id"], rid)
        d = self.svc.get_profile("remote")
        d["profile"]["mappings"][0].update(target="sequence", sequence_mode="hog", sequence_text="*JF")
        saved = self.svc.save_profile("remote", d["profile"], d["rev"])
        self.assertEqual(saved["profile"]["mappings"][0]["steps"], [[29], [36], [33]])
        press(self.svc, self.remote, 115, False)
        self.tick()
        press(self.svc, self.remote, 115)
        self.tick(8)
        downs = [t for t, d_ in self.out.log if d_]
        self.assertEqual(downs, ["K:29", "K:36", "K:33"])

    def test_nintendo_layout_names_and_locate(self):
        d = self.svc.get_profile("pad")
        d["profile"]["visual_layout"] = "nintendo"
        d = self.svc.save_profile("pad", d["profile"], d["rev"])
        names = {m["id"]: m["display"] for m in d["profile"]["mappings"]}
        self.assertEqual((names["key:%d" % A], names["key:%d" % B]), ("B", "A"))
        self.svc.start_learn("pad")
        self.tick()
        press(self.svc, self.pad, B)  # the east button, printed "A" on Nintendo pads
        self.assertEqual(self.svc.message, "已识别：A")

    def test_play_pause_goes_to_session_agent(self):
        self.set_pad_output(A, "media", [164])
        self.tick()
        press(self.svc, self.pad, A)
        press(self.svc, self.pad, A, False)
        self.assertEqual(self.out.log[-2:], [("K:164", True), ("K:164", False)])  # no agent: plain key
        first = self.svc.media_wait(-1, timeout=0)
        self.assertEqual(first["events"], [])
        self.assertTrue(self.svc.agent_connected())
        n = len(self.out.log)
        press(self.svc, self.pad, A)
        press(self.svc, self.pad, A, False)
        self.assertEqual(self.out.log[n:], [])  # handed to the agent instead of the desktop
        got = self.svc.media_wait(first["seq"], timeout=0)
        self.assertEqual([(e["code"], e["action"]) for e in got["events"]], [(164, "play-pause")])
        self.svc.media_fallback(164)  # agent found no player
        self.assertEqual(self.out.log[n:], [("K:164", True), ("K:164", False)])
        self.clock.t += 60
        self.assertFalse(self.svc.agent_connected())

    def test_stale_revision_rejected(self):
        d = self.svc.get_profile("pad")
        self.svc.save_profile("pad", d["profile"], d["rev"])
        with self.assertRaises(ValueError):
            self.svc.save_profile("pad", d["profile"], d["rev"])

    def test_bad_sequence_rejected(self):
        d = self.svc.get_profile("pad")
        d["profile"]["mappings"][0].update(target="sequence", sequence_mode="hog", sequence_text="J1")
        with self.assertRaises(ValueError):
            self.svc.save_profile("pad", d["profile"], d["rev"])

    def test_disconnect_releases(self):
        self.set_pad_output(A, "keyboard", [42])
        self.tick()
        press(self.svc, self.pad, A)
        self.svc.devices.drop(self.pad.path)
        self.tick()
        self.assertEqual(self.out.log[-1], ("K:42", False))
        self.assertEqual(self.svc.status_text(), "等待手柄")

    def test_grab_setting(self):
        self.svc.update_settings({"grab": {"ir1": True}})
        self.assertTrue(self.remote.grabbed)
        self.assertFalse(self.kbd.grabbed)


class ServerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dir = tempfile.TemporaryDirectory()
        pad = FakeTracked("gamepad", "pad1", "Test Pad", [A, B], {0: 0.0, 1: 0.0})
        cls.svc = Service(Store(cls.dir.name), FakeManager(pad), RecordingOutput())
        cls.httpd = server.start(cls.svc, ("127.0.0.1", 0))
        cls.base = "http://127.0.0.1:%d" % cls.httpd.server_address[1]

    @classmethod
    def tearDownClass(cls):
        cls.httpd.shutdown()
        cls.dir.cleanup()

    def req(self, method, path, body=None, headers=None):
        h = {"X-PadDesk": "1", "Content-Type": "application/json"}
        h.update(headers or {})
        data = json.dumps(body).encode() if body is not None else None
        r = urllib.request.Request(self.base + path, data=data, method=method, headers=h)
        try:
            with urllib.request.urlopen(r, timeout=3) as resp:
                return resp.status, resp.read()
        except urllib.error.HTTPError as ex:
            return ex.code, ex.read()

    def test_state_and_static(self):
        code, body = self.req("GET", "/api/state")
        self.assertEqual(code, 200)
        self.assertEqual(json.loads(body)["pad"]["name"], "Test Pad")
        code, body = self.req("GET", "/")
        self.assertEqual(code, 200)
        self.assertIn(b"PadDesk", body)
        self.assertEqual(self.req("GET", "/../server.py")[0], 404)

    def test_csrf_and_rebinding_blocked(self):
        self.assertEqual(self.req("POST", "/api/pause", {}, {"X-PadDesk": ""})[0], 403)
        self.assertEqual(self.req("GET", "/api/state", None, {"Host": "evil.example:7381"})[0], 403)

    def test_profile_roundtrip_and_errors(self):
        code, body = self.req("GET", "/api/profile?scope=pad")
        d = json.loads(body)
        d["profile"]["mappings"][0].update(target="keyboard", keys=[29, 30])
        code, body = self.req("PUT", "/api/profile", {"scope": "pad", "rev": d["rev"], "profile": d["profile"]})
        self.assertEqual(code, 200, body)
        self.assertEqual(json.loads(body)["profile"]["mappings"][0]["output_label"], "Ctrl + A")
        code, body = self.req("PUT", "/api/profile", {"scope": "pad", "rev": d["rev"], "profile": d["profile"]})
        self.assertEqual(code, 400)
        self.assertEqual(self.req("POST", "/api/pause", {"paused": True})[0], 200)
        self.assertTrue(self.svc.paused)
        self.assertEqual(self.req("POST", "/api/learn", {"scope": "remote"})[0], 200)
        self.assertEqual(self.req("POST", "/api/learn/cancel", {})[0], 200)
        self.assertEqual(self.req("GET", "/api/keys")[0], 200)


if __name__ == "__main__":
    unittest.main()
