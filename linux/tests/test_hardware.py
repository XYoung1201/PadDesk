"""End-to-end test on a real Linux machine (e.g. the Raspberry Pi): creates a virtual gamepad with
uinput, runs the real service on it and reads back what PadDesk's virtual keyboard sends.
Skipped where /dev/uinput is not writable. Stop the installed service first:
    sudo systemctl stop paddesk && sudo python3 -m unittest tests.test_hardware -v
"""

import os
import select
import sys
import tempfile
import threading
import time
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

try:
    import evdev
    from evdev import ecodes as e
except ImportError:
    evdev = None

from paddesk.devices import DeviceManager, classify, normalize  # noqa: E402

HAVE_UINPUT = evdev is not None and os.access("/dev/uinput", os.W_OK)


class FakeCaps:
    def __init__(self, name, caps):
        self.name, self._caps = name, caps

    def capabilities(self, absinfo=False):
        return self._caps


@unittest.skipIf(evdev is None, "python-evdev not installed")
class ClassifyTests(unittest.TestCase):
    def test_classify(self):
        pad = FakeCaps("Xbox", {e.EV_KEY: [e.BTN_SOUTH, e.BTN_EAST], e.EV_ABS: [e.ABS_X, e.ABS_Y]})
        kbd = FakeCaps("Keyboard", {e.EV_KEY: [e.KEY_A, e.KEY_ENTER]})
        ir = FakeCaps("gpio_ir_recv", {e.EV_KEY: [e.KEY_VOLUMEUP, e.KEY_OK]})
        mouse = FakeCaps("Mouse", {e.EV_KEY: [e.BTN_LEFT, e.BTN_RIGHT], e.EV_REL: [e.REL_X]})
        touch = FakeCaps("Touch", {e.EV_KEY: [e.BTN_TOUCH], e.EV_ABS: [e.ABS_X, e.ABS_MT_POSITION_X]})
        own = FakeCaps("PadDesk Virtual Keyboard", {e.EV_KEY: [e.KEY_A]})
        self.assertEqual([classify(d) for d in (pad, kbd, ir, mouse, touch, own)], ["gamepad", "keys", "keys", None, None, None])
        self.assertEqual(normalize(0, 0, 255), -1.0)
        self.assertAlmostEqual(normalize(0, -32768, 32767), 0.0, places=3)


@unittest.skipUnless(HAVE_UINPUT, "needs write access to /dev/uinput")
class HardwareTests(unittest.TestCase):
    def test_button_to_key_through_real_devices(self):
        from paddesk.model import Store
        from paddesk.output import UInputOutput
        from paddesk.service import Service
        absinfo = evdev.AbsInfo(0, -32768, 32767, 16, 128, 0)
        hat = evdev.AbsInfo(0, -1, 1, 0, 0, 0)
        vpad = evdev.UInput({e.EV_KEY: [e.BTN_SOUTH, e.BTN_EAST, e.BTN_START],
                             e.EV_ABS: [(e.ABS_X, absinfo), (e.ABS_Y, absinfo), (e.ABS_HAT0X, hat), (e.ABS_HAT0Y, hat)]},
                            name="Virtual Test Gamepad", vendor=0x1234, product=0x5678)
        out = UInputOutput()
        with tempfile.TemporaryDirectory() as d:
            svc = Service(Store(d), DeviceManager(), out)
            t = threading.Thread(target=svc.run, daemon=True)
            t.start()
            try:
                deadline = time.time() + 5
                while time.time() < deadline and not svc.devices.gamepads():
                    time.sleep(0.1)
                pads = [p for p in svc.devices.gamepads() if p.name == "Virtual Test Gamepad"]
                self.assertTrue(pads, "virtual gamepad not found")
                svc.select_device(pads[0].id)
                prof = svc.get_profile("pad")
                for m in prof["profile"]["mappings"]:
                    if m["input"] == {"kind": "key", "code": e.BTN_SOUTH}:
                        m["target"], m["keys"] = "keyboard", [e.KEY_A]
                svc.save_profile("pad", prof["profile"], prof["rev"])
                kbd = evdev.InputDevice(out.kbd.device.path)
                time.sleep(0.3)
                vpad.write(e.EV_KEY, e.BTN_SOUTH, 1)
                vpad.syn()
                vpad.write(e.EV_KEY, e.BTN_SOUTH, 0)
                vpad.syn()
                got = []
                end = time.time() + 2
                while time.time() < end and len(got) < 2:
                    if select.select([kbd.fd], [], [], 0.2)[0]:
                        got += [(ev.code, ev.value) for ev in kbd.read() if ev.type == e.EV_KEY]
                self.assertEqual(got[:2], [(e.KEY_A, 1), (e.KEY_A, 0)])
            finally:
                svc.stopping = True
                t.join(3)
                svc.shutdown()
                vpad.close()


if __name__ == "__main__":
    unittest.main()
