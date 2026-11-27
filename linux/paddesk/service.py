"""The background mapping service: reads devices, runs both engines, drives uinput and serves the
state that the configuration UI edits. Everything that touches shared state holds self.lock."""

import logging
import os
import select
import threading
import time

from . import __version__, keys
from .engine import CaptureDetector, InputEngine
from .model import (REMOTE_PROFILE_ID, InvalidProfile, Mapping, Profile, SequenceError, Snapshot, Source,
                    load_hog_config, parse_sequence)

log = logging.getLogger("paddesk")
FRAME = 1 / 125.0
LEARN_SECONDS = 15
EV_SYN, EV_KEY, EV_ABS = 0, 1, 3
SYN_REPORT, SYN_DROPPED = 0, 3
KEY_F12 = 88
CTRL = {29, 97}
# Play/pause and track keys. Desktops such as Raspberry Pi OS (labwc / Wayfire) bind the volume keys but
# leave these unbound, so a key press reaches no player. When the session agent (`paddesk agent`) is
# running, these keys are handed to it and it drives media players over MPRIS on the session bus.
BRIDGED = {164: "play-pause", 163: "next", 165: "previous", 166: "stop"}
AGENT_TIMEOUT = 40.0
ALT = {56, 100}


def create_profile(device_id, name, key_codes, axes):
    """Default profile for a newly seen gamepad. Names are hints to confirm ("待确认"), except the
    d-pad, which keeps stepping the mouse like the Windows build."""
    p = Profile(device_id, name)
    has_stick = keys.ABS["X"] in axes and keys.ABS["Y"] in axes
    p.stick_enabled = has_stick
    if has_stick:
        p.center_x = round(axes[keys.ABS["X"]], 4)
        p.center_y = round(axes[keys.ABS["Y"]], 4)
    dpad = {keys.BTN["DPAD_UP"]: "up", keys.BTN["DPAD_DOWN"]: "down", keys.BTN["DPAD_LEFT"]: "left", keys.BTN["DPAD_RIGHT"]: "right"}
    for code in key_codes:
        m = Mapping(Source("key", code), keys.BUTTON_LABELS.get(code, ""))
        if code in dpad:
            m.target, m.locked, m.confirmed = dpad[code], True, True
        p.mappings.append(m)
    hats = {(keys.ABS["HAT0Y"], -1): ("up", "方向上"), (keys.ABS["HAT0Y"], 1): ("down", "方向下"),
            (keys.ABS["HAT0X"], -1): ("left", "方向左"), (keys.ABS["HAT0X"], 1): ("right", "方向右")}
    for code in sorted(axes):
        if has_stick and code in (keys.ABS["X"], keys.ABS["Y"]):
            continue
        rest = axes[code]
        for sign in (1, -1):
            if (code, sign) in hats:
                target, label = hats[(code, sign)]
                p.mappings.append(Mapping(Source("axis", code, sign, 0.0, 0.5), label, True, target, locked=True))
            elif code not in (keys.ABS["HAT0X"], keys.ABS["HAT0Y"]) and ((1 - rest) if sign > 0 else (rest + 1)) > 0.43:
                label = keys.axis_caption(code, sign) if code in keys.AXIS_LABELS else ""
                p.mappings.append(Mapping(Source("axis", code, sign, round(rest, 4), 0.43), label))
    p.validate()
    return p


class NullOutput:
    owned = set()

    def apply(self, r):
        pass

    def release_all(self):
        pass

    def close(self):
        pass


class Service:
    def __init__(self, store, devices, output, clock=time.monotonic, output_error=""):
        self.lock = threading.RLock()
        self.store = store
        self.devices = devices
        self.output = output or NullOutput()
        self.output_error = output_error
        self.clock = clock
        self.settings = store.load_settings()
        self.paused = False
        self.profiles = {}
        self.revs = {}
        self.engine = InputEngine()
        self.remote_engine = InputEngine()
        self.learn = None
        self.learn_result = None
        self.suspend_until = 0.0
        self.message = ""
        self.stopping = False
        self.last_stick = (0.0, 0.0)
        self.media_cond = threading.Condition()
        self.media_seq = 0
        self.media_events = []   # (seq, code), newest last
        self.agent_seen = -1e9
        self.agent_waiting = 0
        try:
            self.remote_profile = store.load_profile(REMOTE_PROFILE_ID)
        except (InvalidProfile, ValueError, OSError) as ex:
            self.remote_profile = None
            self.message = "遥控配置未载入：%s" % ex
        if self.remote_profile is None:
            self.remote_profile = Profile(REMOTE_PROFILE_ID, "遥控 / 蓝牙外设")
            self.remote_profile.stick_enabled = False

    # ---- devices and profiles ----------------------------------------------------------------

    def active_pad(self):
        pads = sorted(self.devices.gamepads(), key=lambda t: t.path)
        for t in pads:
            if t.id == self.settings.get("selected_device"):
                return t
        return pads[0] if pads else None

    def profile_for(self, t):
        if t is None:
            return None
        p = self.profiles.get(t.id)
        if p is None:
            try:
                p = self.store.load_profile(t.id)
            except (InvalidProfile, ValueError, OSError) as ex:
                self.message = "手柄配置无法读取，已使用默认配置：%s" % ex
                p = None
            if p is None:
                p = create_profile(t.id, t.name, t.key_codes, t.axes)
            self.profiles[t.id] = p
        return p

    def _apply_grabs(self):
        grabs = self.settings.get("grab", {})
        for t in self.devices.key_devices():
            want = bool(grabs.get(t.id))
            if t.grabbed != want and not t.set_grab(want):
                self.message = "无法独占 %s" % t.name

    def busy(self, now=None):
        now = self.clock() if now is None else now
        return self.learn is not None or now < self.suspend_until

    def _remote_snapshot(self):
        ids = set()
        for t in self.devices.key_devices():
            for k in t.keys:
                ids.add("remote:%s:%d" % (t.id, k))
        return Snapshot(True, REMOTE_PROFILE_ID, self.remote_profile.device_name, remote=ids)

    def agent_connected(self, now=None):
        now = self.clock() if now is None else now
        return self.agent_waiting > 0 or now - self.agent_seen < AGENT_TIMEOUT

    def _bridge(self, result):
        """Takes play/pause and track keys out of the output and queues them for the session agent."""
        if not self.agent_connected():
            return
        kept = []
        for ev in result.events:
            code = int(ev.token[2:]) if ev.token.startswith("K:") else 0
            if code in BRIDGED:
                if ev.down:
                    with self.media_cond:
                        self.media_seq += 1
                        self.media_events = (self.media_events + [(self.media_seq, code)])[-32:]
                        self.media_cond.notify_all()
            else:
                kept.append(ev)
        result.events = kept

    def media_wait(self, after, timeout=20.0):
        """Long poll used by the session agent. Returns media actions newer than `after`."""
        with self.media_cond:
            self.agent_waiting += 1
            self.agent_seen = self.clock()
            try:
                if after < 0 or after > self.media_seq:
                    after = self.media_seq   # first call, or the service restarted: skip old presses
                self.media_cond.wait_for(lambda: self.media_seq > after or self.stopping, timeout)
                events = [{"seq": q, "code": c, "action": BRIDGED[c]} for q, c in self.media_events if q > after]
                return {"seq": self.media_seq, "events": events}
            finally:
                self.agent_waiting -= 1
                self.agent_seen = self.clock()

    def media_fallback(self, code):
        """The agent found no media player: send the key itself after all, as a tap."""
        if code not in BRIDGED:
            raise ValueError("不是媒体键。")
        with self.lock:
            from .engine import FrameResult, OutputEvent
            r = FrameResult()
            r.events = [OutputEvent("K:%d" % code, True), OutputEvent("K:%d" % code, False)]
            self._apply(r, bridge=False)

    def _apply(self, result, bridge=True):
        if bridge:
            self._bridge(result)
        try:
            self.output.apply(result)
        except OSError as ex:
            self.paused = True
            self.message = "输出失败，已暂停：%s" % ex
            self._reset_engines()

    def _reset_engines(self):
        self.output.release_all()
        self.engine = InputEngine()
        self.remote_engine = InputEngine()

    def frame(self, partial=False, pad_only=False, remote_only=False):
        now = self.clock()
        if self.learn is not None and now > self.learn["deadline"]:
            self.learn = None
            self.message = "学习已超时。"
        enabled = not self.paused and not self.busy(now)
        pad = self.active_pad()
        s = pad.snapshot() if pad else Snapshot(False)
        if not remote_only:
            r = self.engine.tick(s, now, self.profile_for(pad), enabled, partial)
            self.last_stick = (r.stick_x, r.stick_y)
            self._apply(r)
            if self.learn and self.learn["scope"] == "pad" and pad is not None:
                found = self.learn["detector"].poll(s)
                if found is not None:
                    self._finish_learn(found)
        if not pad_only:
            rp = self.remote_profile
            ready = any(m.target != "none" for m in rp.mappings)
            self._apply(self.remote_engine.tick(self._remote_snapshot(), now, rp, enabled and ready, partial))

    # ---- event handling ------------------------------------------------------------------------

    def handle(self, t, events):
        from .devices import normalize
        is_pad = t.kind == "gamepad"
        for ev in events:
            if ev.type == EV_KEY:
                if ev.value:
                    if ev.value == 1 and not is_pad:
                        self._key_down(t, ev.code)
                    t.keys.add(ev.code)
                else:
                    t.keys.discard(ev.code)
            elif ev.type == EV_ABS and is_pad and ev.code in t.ranges:
                lo, hi = t.ranges[ev.code]
                t.axes[ev.code] = normalize(ev.value, lo, hi)
            elif ev.type == EV_SYN:
                if ev.code == SYN_DROPPED:
                    t.resync()
                elif ev.code == SYN_REPORT:
                    if is_pad:
                        if t is self.active_pad():
                            self.frame(partial=True, pad_only=True)
                    else:
                        self.frame(partial=True, remote_only=True)

    def _key_down(self, t, code):
        if code == KEY_F12 and t.keys & CTRL and t.keys & ALT:
            self.set_paused(not self.paused)
            return
        if self.learn and self.learn["scope"] == "remote":
            self._finish_learn(Source("remote", code, device=t.id, device_name=t.name))

    def read_device(self, t):
        try:
            self.handle(t, list(t.dev.read()))
        except BlockingIOError:
            pass
        except OSError:
            was_active = t is self.active_pad()
            self.devices.drop(t.path)
            if was_active:
                self.frame()

    def run(self):
        next_scan = next_tick = 0.0
        while not self.stopping:
            now = self.clock()
            if now >= next_scan:
                with self.lock:
                    if self.devices.rescan():
                        self._apply_grabs()
                next_scan = now + 1.5
            fds = self.devices.by_fd()
            try:
                ready, _, _ = select.select(list(fds), [], [], max(0.0, min(next_tick - now, 0.5)))
            except (OSError, ValueError):
                ready = []
                next_scan = 0
            with self.lock:
                for fd in ready:
                    t = fds.get(fd)
                    if t is not None and t.path in self.devices.devices:
                        self.read_device(t)
                now = self.clock()
                if now >= next_tick:
                    self.frame()
                    next_tick = now + FRAME

    def shutdown(self):
        with self.lock:
            self.stopping = True
            with self.media_cond:
                self.media_cond.notify_all()
            self.output.release_all()
            self.output.close()
            self.devices.close()

    # ---- API used by the configuration UI ------------------------------------------------------

    def _scope_profile(self, scope):
        if scope == "remote":
            return self.remote_profile
        p = self.profile_for(self.active_pad())
        if p is None:
            raise LookupError("没有连接手柄。")
        return p

    def _bump(self, p):
        self.revs[p.device_id] = self.revs.get(p.device_id, 0) + 1

    def _save(self, p):
        try:
            if p is not self.remote_profile or p.mappings or os.path.exists(self.store.profile_path(p.device_id)):
                self.store.save_profile(p)
            self.message = "已保存 · " + time.strftime("%H:%M:%S")
        except (OSError, InvalidProfile) as ex:
            self.message = "保存失败：%s" % ex

    def status_text(self):
        if self.busy():
            return "校准 / 编辑中"
        if self.paused:
            return "已暂停"
        if self.active_pad() is None:
            return "遥控待命" if any(m.target != "none" for m in self.remote_profile.mappings) else "等待手柄"
        return "正在控制"

    def state(self):
        with self.lock:
            pad = self.active_pad()
            s = pad.snapshot() if pad else Snapshot(False)
            rs = self._remote_snapshot()
            p = self.profile_for(pad)
            active = [m.input.id for m in (p.mappings if p else []) if m.input.active(s)]
            active += [m.input.id for m in self.remote_profile.mappings if m.input.active(rs)]
            if pad:
                active += ["key:%d" % k for k in s.keys if "key:%d" % k not in active]
            now = self.clock()
            return {
                "version": __version__, "status": self.status_text(), "paused": self.paused, "busy": self.busy(now),
                "message": self.message, "output_error": self.output_error, "agent": self.agent_connected(now),
                "learning": None if self.learn is None else {"scope": self.learn["scope"], "remaining": max(0, round(self.learn["deadline"] - now))},
                "learn_result": self.learn_result,
                "pad": {"id": pad.id, "name": pad.name} if pad else None,
                "pads": [{"id": t.id, "name": t.name} for t in sorted(self.devices.gamepads(), key=lambda t: t.path)],
                "key_devices": [{"id": t.id, "name": t.name, "grabbed": t.grabbed} for t in sorted(self.devices.key_devices(), key=lambda t: t.name)],
                "active": active, "stick": [round(v, 3) for v in self.last_stick],
                "axes": {str(k): round(v, 3) for k, v in s.axes.items()},
                "revs": {"pad": self.revs.get(pad.id, 0) if pad else 0, "remote": self.revs.get(REMOTE_PROFILE_ID, 0)},
            }

    def get_profile(self, scope):
        with self.lock:
            p = self._scope_profile(scope)
            d = p.to_json()
            for m, j in zip(p.mappings, d["mappings"]):
                j.update(id=m.input.id, caption=m.input.caption, output_label=m.output_label,
                         display=m.display_name(p.visual_layout))
            return {"rev": self.revs.get(p.device_id, 0), "profile": d}

    def save_profile(self, scope, data, rev):
        with self.lock:
            cur = self._scope_profile(scope)
            if rev is not None and rev != self.revs.get(cur.device_id, 0):
                raise ValueError("配置已在别处更新，请刷新后再改。")
            p = Profile.from_json(data)
            p.device_id, p.device_name = cur.device_id, cur.device_name
            for m in p.mappings:
                if m.target == "sequence":
                    try:
                        m.steps = parse_sequence(m.sequence_mode, m.sequence_text)
                    except SequenceError as ex:
                        raise ValueError("%s：%s" % (m.physical, ex))
                if m.target not in ("keyboard", "media"):
                    m.keys = []
                if m.target != "sequence":
                    m.steps = []
            p.validate()
            if scope == "remote":
                p.stick_enabled = False
                self.remote_profile = p
            else:
                self.profiles[p.device_id] = p
            self._reset_engines()
            self._bump(p)
            self._save(p)
            return self.get_profile(scope)

    def reset_profile(self):
        with self.lock:
            pad = self.active_pad()
            if pad is None:
                raise LookupError("没有连接手柄。")
            p = create_profile(pad.id, pad.name, pad.key_codes, pad.axes)
            self.profiles[pad.id] = p
            self._reset_engines()
            self._bump(p)
            self._save(p)

    def set_paused(self, paused):
        with self.lock:
            self.paused = bool(paused)
            if self.paused:
                self._reset_engines()
            self.message = "已暂停" if self.paused else "已恢复"

    def select_device(self, device_id):
        with self.lock:
            self.settings["selected_device"] = device_id or ""
            self.store.save_settings(self.settings)
            self._reset_engines()

    def start_learn(self, scope):
        with self.lock:
            now = self.clock()
            self.learn_result = None
            if scope == "pad":
                pad = self.active_pad()
                if pad is None:
                    raise LookupError("没有连接手柄。")
                p = self.profile_for(pad)
                ignore = (p.stick_x, p.stick_y) if p.stick_enabled else ()
                self.learn = {"scope": "pad", "deadline": now + LEARN_SECONDS, "detector": CaptureDetector(pad.snapshot(), ignore)}
            else:
                self.learn = {"scope": "remote", "deadline": now + LEARN_SECONDS}
            self._reset_engines()

    def cancel_learn(self):
        with self.lock:
            self.learn = None

    def _finish_learn(self, src):
        scope = self.learn["scope"]
        self.learn = None
        p = self.remote_profile if scope == "remote" else self.profile_for(self.active_pad())
        m = p.find(src.id)
        if m is None:
            label = keys.name(src.code) if scope == "remote" else keys.BUTTON_LABELS.get(src.code, "") if src.kind == "key" else ""
            m = Mapping(src, label, True)
            p.mappings.append(m)
        else:
            m.confirmed = True
        self.learn_result = {"scope": scope, "id": src.id}
        self._bump(p)
        self._save(p)
        if not self.message.startswith("保存失败"):
            self.message = "已识别：%s" % m.display_name(p.visual_layout)

    def record_center(self):
        with self.lock:
            pad = self.active_pad()
            if pad is None:
                raise LookupError("没有连接手柄。")
            p = self.profile_for(pad)
            p.center_x = round(pad.axes.get(p.stick_x, 0.0), 4)
            p.center_y = round(pad.axes.get(p.stick_y, 0.0), 4)
            self._bump(p)
            self._save(p)

    def suspend(self, on):
        with self.lock:
            self.suspend_until = self.clock() + 60 if on else 0.0
            if on:
                self._reset_engines()

    def hog(self):
        with self.lock:
            path, entries = load_hog_config(self.settings.get("hog_config", ""))
            return {"path": path, "entries": entries, "configured": self.settings.get("hog_config", "")}

    def get_settings(self):
        with self.lock:
            return {"hog_config": self.settings.get("hog_config", ""), "grab": dict(self.settings.get("grab", {}))}

    def update_settings(self, data):
        with self.lock:
            if "hog_config" in data:
                self.settings["hog_config"] = str(data["hog_config"] or "").strip()
            if "grab" in data and isinstance(data["grab"], dict):
                self.settings["grab"] = {str(k): True for k, v in data["grab"].items() if v}
            self.store.save_settings(self.settings)
            self._apply_grabs()
            return self.get_settings()
