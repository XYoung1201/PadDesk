"""evdev input: finds gamepads and key-sending devices (keyboards, IR receivers, Bluetooth remotes,
presenters, HDMI-CEC), tracks their state and handles hot-plugging."""

import glob
import os

import evdev
from evdev import ecodes as e

from .model import Snapshot

OWN_PREFIX = "PadDesk"


def identity(dev):
    i = dev.info
    return "%04x:%04x:%s" % (i.vendor, i.product, dev.name or "unknown")


def _is_key_code(k):
    # Real keys: KEY_* below the BTN_ ranges, and the consumer/remote KEY_* codes above them.
    return (1 <= k < 0x100) or (0x160 <= k < 0x220) or (0x224 <= k < 0x2c0)


def classify(dev):
    if (dev.name or "").startswith(OWN_PREFIX):
        return None
    caps = dev.capabilities(absinfo=False)
    key_codes = caps.get(e.EV_KEY, [])
    abs_codes = caps.get(e.EV_ABS, [])
    if e.BTN_TOUCH in key_codes or e.ABS_MT_POSITION_X in abs_codes:
        return None  # touchscreens, touchpads, tablets
    pad_buttons = [k for k in key_codes if 0x120 <= k < 0x140 or 0x220 <= k <= 0x223 or 0x2c0 <= k < 0x2e8]
    if pad_buttons and (e.ABS_X in abs_codes or len(pad_buttons) >= 4):
        return "gamepad"
    if any(_is_key_code(k) for k in key_codes):
        return "keys"
    return None


def normalize(value, lo, hi):
    if hi <= lo:
        return 0.0
    return max(-1.0, min(1.0, 2.0 * (value - lo) / (hi - lo) - 1.0))


class Tracked:
    def __init__(self, dev, kind):
        self.dev = dev
        self.kind = kind
        self.path = dev.path
        self.id = identity(dev)
        self.name = dev.name or "未知设备"
        self.ranges = {}
        self.keys = set()
        self.axes = {}
        self.grabbed = False
        self.key_codes = sorted(dev.capabilities(absinfo=False).get(e.EV_KEY, []))
        if kind == "gamepad":
            for code, info in dev.capabilities(absinfo=True).get(e.EV_ABS, []):
                self.ranges[code] = (info.min, info.max)
        self.resync()

    def resync(self):
        try:
            self.keys = set(self.dev.active_keys())
            if self.kind == "gamepad":
                for code, (lo, hi) in self.ranges.items():
                    self.axes[code] = normalize(self.dev.absinfo(code).value, lo, hi)
        except OSError:
            pass

    def snapshot(self):
        return Snapshot(True, self.id, self.name, self.keys, self.axes)

    def set_grab(self, on):
        if on == self.grabbed:
            return True
        try:
            if on:
                self.dev.grab()
            else:
                self.dev.ungrab()
            self.grabbed = on
            return True
        except OSError:
            return False

    def close(self):
        try:
            if self.grabbed:
                self.dev.ungrab()
        except OSError:
            pass
        try:
            self.dev.close()
        except OSError:
            pass


class DeviceManager:
    def __init__(self, pattern="/dev/input/event*"):
        self.pattern = pattern
        self.devices = {}   # path -> Tracked
        self.ignored = {}   # path -> st_rdev/ctime signature of devices that are neither pads nor key devices
        self.errors = {}

    def rescan(self):
        """Opens new devices and forgets vanished ones. Returns True if the set changed."""
        changed = False
        present = set(glob.glob(self.pattern))
        for path in list(self.devices):
            if path not in present:
                self.drop(path)
                changed = True
        for path in sorted(present - set(self.devices)):
            try:
                st = os.stat(path)
                sig = (st.st_rdev, st.st_ctime)
            except OSError:
                continue
            if self.ignored.get(path) == sig:
                continue
            try:
                dev = evdev.InputDevice(path)
            except OSError as ex:
                self.errors[path] = str(ex)
                continue
            self.errors.pop(path, None)
            kind = classify(dev)
            if kind is None:
                dev.close()
                self.ignored[path] = sig
                continue
            self.devices[path] = Tracked(dev, kind)
            changed = True
        return changed

    def drop(self, path):
        t = self.devices.pop(path, None)
        if t:
            t.close()

    def gamepads(self):
        return [t for t in self.devices.values() if t.kind == "gamepad"]

    def key_devices(self):
        return [t for t in self.devices.values() if t.kind == "keys"]

    def by_fd(self):
        return {t.dev.fd: t for t in self.devices.values()}

    def close(self):
        for path in list(self.devices):
            self.drop(path)
