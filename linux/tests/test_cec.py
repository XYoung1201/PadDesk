import os
import sys
import tempfile
import time
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from paddesk.cec import CecController, CecError, parse_power  # noqa: E402
from paddesk.engine import InputEngine  # noqa: E402
from paddesk.model import Mapping, Profile, Snapshot, Source, Store  # noqa: E402
from paddesk.output import RecordingOutput  # noqa: E402
from paddesk.service import Service  # noqa: E402
from test_service import A, FakeManager, FakeTracked, press  # noqa: E402

STATUS = "GIVE_DEVICE_POWER_STATUS (0x8f)\n    REPORT_POWER_STATUS (0x90):\n\tpwr-state: %s\n"


class FakeCecCtl:
    """Pretends to be cec-ctl: unconfigured until --playback, then answers with a power state."""

    def __init__(self, power="standby (0x01)"):
        self.calls = []
        self.configured = False
        self.power = power

    def __call__(self, args):
        self.calls.append(args[4:])
        if "--playback" in args:
            self.configured = True
            return 0, "Logical Address : 8 (Playback Device 2)"
        if not self.configured:
            return 0, "\nAdapter is unconfigured, please configure it first.\n"
        if "--give-device-power-status" in args:
            return 0, STATUS % self.power
        return 0, "Transmit from Playback Device 2 to TV (8 to 0):\n"


class CecTests(unittest.TestCase):
    def test_parse_power(self):
        self.assertEqual(parse_power(STATUS % "standby (0x01)"), "standby")
        self.assertEqual(parse_power(STATUS % "on (0x00)"), "on")
        self.assertEqual(parse_power(STATUS % "in transition standby to on (0x02)"), "to-on")
        self.assertEqual(parse_power("Timed out"), "unknown")

    def test_commands_and_device(self):
        ctl = FakeCecCtl()
        c = CecController("/dev/cec1", runner=ctl)
        c.run("on")
        self.assertEqual(ctl.calls, [["--playback"], ["--to", "0", "--image-view-on"]])
        c.run("standby")
        self.assertEqual(ctl.calls[-1], ["--to", "0", "--standby"])
        c.set_device("/dev/cec0")
        c.run("on")
        self.assertEqual(ctl.calls[-2], ["--playback"])   # a new device is registered first
        with self.assertRaises(ValueError):
            c.set_device("/etc/passwd")

    def test_reconfigures_when_adapter_lost_its_address(self):
        ctl = FakeCecCtl()
        c = CecController(runner=ctl)
        c.init()
        ctl.configured = False   # e.g. after a reboot of the HDMI side
        self.assertEqual(c.query(), "standby")
        self.assertEqual(ctl.calls[-3:], [["--to", "0", "--give-device-power-status"], ["--playback"],
                                          ["--to", "0", "--give-device-power-status"]])

    def test_toggle_follows_power_state(self):
        ctl = FakeCecCtl("standby (0x01)")
        c = CecController(runner=ctl)
        c.run("toggle")
        self.assertEqual(ctl.calls[-1], ["--to", "0", "--image-view-on"])
        ctl.power = "on (0x00)"
        c.run("toggle")
        self.assertEqual(ctl.calls[-1], ["--to", "0", "--standby"])

    def test_missing_tool(self):
        c = CecController(runner=lambda a: (127, "找不到 cec-ctl"))
        with self.assertRaises(CecError):
            c.run("on")


class CecMappingTests(unittest.TestCase):
    def test_engine_fires_once_per_press(self):
        p = Profile("pad", "Pad")
        p.mappings = [Mapping(Source("key", A), target="cec", cec_action="toggle")]
        p.validate()
        e = InputEngine()
        e.tick(Snapshot(True, "pad", "Pad", (), {}), 0.01, p, True)
        r1 = e.tick(Snapshot(True, "pad", "Pad", (A,), {}), 0.02, p, True)
        r2 = e.tick(Snapshot(True, "pad", "Pad", (A,), {}), 0.03, p, True)
        e.tick(Snapshot(True, "pad", "Pad", (), {}), 0.04, p, True)
        r3 = e.tick(Snapshot(True, "pad", "Pad", (A,), {}), 0.05, p, True)
        self.assertEqual((r1.actions, r2.actions, r3.actions), ([("cec", "toggle")], [], [("cec", "toggle")]))
        self.assertEqual(r1.events, [])

    def test_validation_and_label(self):
        m = Mapping(Source("key", A), target="cec", cec_action="bogus")
        p = Profile("pad", "Pad")
        p.mappings = [m]
        with self.assertRaises(ValueError):
            p.validate()
        m.cec_action = "standby"
        p.validate()
        self.assertEqual(m.output_label, "投影仪 · 待机")
        self.assertEqual(Mapping.from_json(m.to_json()).cec_action, "standby")

    def test_service_press_and_settings(self):
        with tempfile.TemporaryDirectory() as d:
            ctl = FakeCecCtl()
            pad = FakeTracked("gamepad", "pad1", "Test Pad", [A], {0: 0.0, 1: 0.0})
            svc = Service(Store(d), FakeManager(pad), RecordingOutput(), cec=CecController(runner=ctl))
            svc.cec.on_result = svc._cec_result
            prof = svc.get_profile("pad")
            prof["profile"]["mappings"][0].update(target="cec", cec_action="on")
            svc.save_profile("pad", prof["profile"], prof["rev"])
            svc.frame()
            press(svc, pad, A)
            for _ in range(100):
                if ["--to", "0", "--image-view-on"] in ctl.calls:
                    break
                time.sleep(0.01)
            self.assertIn(["--to", "0", "--image-view-on"], ctl.calls)
            self.assertEqual(svc.get_settings()["cec_device"], "/dev/cec1")
            self.assertEqual(svc.update_settings({"cec_device": "0"})["cec_device"], "/dev/cec0")
            self.assertEqual(svc.store.load_settings()["cec_device"], "/dev/cec0")
            with self.assertRaises(ValueError):
                svc.update_settings({"cec_device": "/dev/sda"})
            self.assertEqual(svc.cec.query(), "standby")
            svc.cec.stop()


if __name__ == "__main__":
    unittest.main()
