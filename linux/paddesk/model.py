"""Inputs, mappings, sequences, HOG config reading and profile storage (Linux port of Model.cs)."""

import json
import os
import re

from . import keys

TARGETS = ["none", "mouse_left", "mouse_right", "mouse_middle", "keyboard", "media", "sequence",
           "up", "down", "left", "right"]
STEP_TARGETS = {"up", "down", "left", "right"}
KINDS = ["key", "axis", "remote"]
REMOTE_PROFILE_ID = "remotes"


class Snapshot:
    """State of one gamepad (keys held, normalized axes) or of all remotes (held ids)."""

    def __init__(self, connected=False, device_id="", name="", pressed=None, axes=None, remote=None):
        self.connected = connected
        self.device_id = device_id
        self.name = name
        self.keys = set(pressed or ())
        self.axes = dict(axes or {})  # code -> value in [-1, 1]
        self.remote = set(remote or ())

    def clone(self):
        return Snapshot(self.connected, self.device_id, self.name, self.keys, self.axes, self.remote)


class Source:
    def __init__(self, kind="key", code=0, direction=1, rest=0.0, threshold=0.43, device="", device_name=""):
        self.kind = kind
        self.code = int(code)
        self.direction = int(direction)
        self.rest = float(rest)
        self.threshold = float(threshold)
        self.device = device
        self.device_name = device_name

    @property
    def id(self):
        if self.kind == "remote":
            return "remote:%s:%d" % (self.device, self.code)
        if self.kind == "axis":
            return "axis:%d:%s" % (self.code, "+" if self.direction > 0 else "-")
        return "key:%d" % self.code

    @property
    def caption(self):
        if self.kind == "remote":
            return "按键 %s · %s" % (keys.name(self.code), self.device_name or "外设")
        if self.kind == "axis":
            base = keys.AXIS_LABELS.get(self.code, "轴 %d" % self.code)
            if self.code in (0x10, 0x12):
                return "方向帽 " + ("→" if self.direction > 0 else "←")
            if self.code in (0x11, 0x13):
                return "方向帽 " + ("↓" if self.direction > 0 else "↑")
            return base + (" +" if self.direction > 0 else " −")
        for nm, code in keys.BTN.items():
            if code == self.code:
                return "按钮 BTN_" + nm
        return "按钮 0x%03X" % self.code

    def active(self, s):
        if not s.connected:
            return False
        if self.kind == "remote":
            return self.id in s.remote
        if self.kind == "key":
            return self.code in s.keys
        v = s.axes.get(self.code)
        return v is not None and (v - self.rest) * self.direction >= self.threshold

    def to_json(self):
        d = {"kind": self.kind, "code": self.code}
        if self.kind == "axis":
            d.update(direction=self.direction, rest=round(self.rest, 4), threshold=round(self.threshold, 4))
        if self.kind == "remote":
            d.update(device=self.device, device_name=self.device_name)
        return d

    @classmethod
    def from_json(cls, d):
        return cls(d.get("kind", "key"), d.get("code", 0), d.get("direction", 1), d.get("rest", 0.0),
                   d.get("threshold", 0.43), d.get("device", "") or "", d.get("device_name", "") or "")


class Mapping:
    def __init__(self, source=None, name="", confirmed=False, target="none", codes=None, locked=False,
                 steps=None, sequence_text="", sequence_mode="hog"):
        self.input = source or Source()
        self.name = name
        self.confirmed = confirmed
        self.target = target
        self.keys = list(codes or [])
        self.locked = locked
        self.steps = [list(s) for s in (steps or [])]
        self.sequence_text = sequence_text
        self.sequence_mode = sequence_mode

    @property
    def physical(self):
        return self.name or self.input.caption

    @property
    def output_label(self):
        t = self.target
        if t in STEP_TARGETS:
            return {"up": "鼠标上移", "down": "鼠标下移", "left": "鼠标左移", "right": "鼠标右移"}[t] + (" · 固定" if self.locked else "")
        if t == "media":
            return "媒体 · " + (keys.name(self.keys[0]) if len(self.keys) == 1 else "未选择")
        if t == "sequence":
            return ("HOG · " if self.sequence_mode == "hog" else "序列 · ") + (self.sequence_text or describe(self.steps))
        return {"mouse_left": "鼠标左键", "mouse_right": "鼠标右键", "mouse_middle": "鼠标中键",
                "keyboard": keys.chord(self.keys)}.get(t, "未分配")

    def to_json(self):
        return {"input": self.input.to_json(), "name": self.name, "confirmed": self.confirmed, "target": self.target,
                "keys": self.keys, "locked": self.locked, "steps": self.steps,
                "sequence_text": self.sequence_text, "sequence_mode": self.sequence_mode}

    @classmethod
    def from_json(cls, d):
        return cls(Source.from_json(d.get("input") or {}), d.get("name", "") or "", bool(d.get("confirmed")),
                   d.get("target", "none") or "none", d.get("keys") or [], bool(d.get("locked")),
                   d.get("steps") or [], d.get("sequence_text", "") or "", d.get("sequence_mode", "hog") or "hog")


# ---- key sequences --------------------------------------------------------------------------

MAX_STEPS, MAX_CHORD = 64, 6


class SequenceError(ValueError):
    pass


def parse_hog(text):
    """HOG hotkeys: letters A–Z tapped in order; '*' taps left Ctrl and '+' right Ctrl (HOG's prefixes)."""
    steps = []
    for ch in (text or "").strip():
        c = ch.upper()
        if "A" <= c <= "Z":
            steps.append([keys.CODES["KEY_" + c]])
        elif c == "*":
            steps.append([keys.LEFTCTRL])
        elif c == "+":
            steps.append([keys.RIGHTCTRL])
        elif c.isspace():
            continue
        else:
            raise SequenceError("HOG 热键只能包含字母 A–Z、* 和 +。")
    if not steps:
        raise SequenceError("请输入 HOG 热键，例如 JFQR 或 *JFYX。")
    if len(steps) > MAX_STEPS:
        raise SequenceError("序列最多 %d 步。" % MAX_STEPS)
    return steps


def parse_keys(text):
    """Key sequences: steps separated by spaces or commas, keys inside a step joined with '+'."""
    steps = []
    for part in re.split(r"[ ,，\t]+", (text or "").strip()):
        if not part:
            continue
        chord = []
        for nm in [p for p in part.split("+") if p]:
            code = keys.parse(nm)
            if not code:
                raise SequenceError("无法识别按键“%s”。" % nm)
            if code not in chord:
                chord.append(code)
        if not chord:
            continue
        if len(chord) > MAX_CHORD:
            raise SequenceError("每一步最多同时按 %d 个键。" % MAX_CHORD)
        steps.append(chord)
    if not steps:
        raise SequenceError("请输入按键序列，例如 Super+Tab 或 Ctrl+C Ctrl+V。")
    if len(steps) > MAX_STEPS:
        raise SequenceError("序列最多 %d 步。" % MAX_STEPS)
    return steps


def parse_sequence(mode, text):
    return parse_hog(text) if mode == "hog" else parse_keys(text)


def describe(steps):
    return "  ".join(keys.chord(s) for s in steps) if steps else "未设置"


def valid_steps(steps):
    return (isinstance(steps, list) and 1 <= len(steps) <= MAX_STEPS and
            all(isinstance(s, list) and 1 <= len(s) <= MAX_CHORD and all(isinstance(k, int) and 1 <= k <= 255 for k in s) for s in steps))


# ---- HOG configuration ----------------------------------------------------------------------

def parse_hog_config(lines):
    out = []
    for raw in lines:
        line = (raw or "").strip()
        if not line or line.startswith("//"):
            continue
        sp = line.find(" ")
        if sp <= 0:
            continue
        hotkey, action = line[:sp], line[sp + 1:].strip()
        c = action.find(" //")
        if c > 0:
            action = action[:c].strip()
        try:
            parse_hog(hotkey)
        except SequenceError:
            continue
        if action:
            out.append({"hotkey": hotkey.upper(), "action": action})
    return out


def find_hog_config(configured=""):
    candidates = [configured] if configured else []
    home = os.path.expanduser("~")
    candidates += [os.path.join(home, ".config", "HOG", "HOG.conf"), "/etc/HOG/HOG.conf"]
    for p in candidates:
        if p and os.path.isfile(p):
            return p
    return None


def load_hog_config(configured=""):
    path = find_hog_config(configured)
    if not path:
        return None, []
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            return path, parse_hog_config(f.read().splitlines())
    except OSError:
        return path, []


# ---- profiles -------------------------------------------------------------------------------

class InvalidProfile(ValueError):
    pass


class Profile:
    FIELDS = {  # name: (default, min, max)
        "h_step": (30, 1, 5000), "v_step": (20, 1, 5000),
        "repeat_delay_ms": (350, 100, 5000), "repeat_interval_ms": (90, 30, 5000),
        "deadzone": (0.16, 0.05, 0.5), "speed": (800.0, 50, 3000), "curve": (1.6, 1, 3),
        "center_x": (0.0, -1, 1), "center_y": (0.0, -1, 1),
    }

    def __init__(self, device_id="", device_name=""):
        self.version = 3
        self.device_id = device_id
        self.device_name = device_name
        self.visual_layout = "xbox"
        for k, (d, _, _) in self.FIELDS.items():
            setattr(self, k, d)
        self.repeat_enabled = True
        self.stick_enabled = True
        self.stick_x, self.stick_y = keys.ABS["X"], keys.ABS["Y"]
        self.sign_x = self.sign_y = 1
        self.mappings = []

    def validate(self):
        if self.version != 3 or not self.device_id or not isinstance(self.mappings, list):
            raise InvalidProfile("配置参数无效。")
        for k, (_, lo, hi) in self.FIELDS.items():
            v = getattr(self, k)
            if not isinstance(v, (int, float)) or isinstance(v, bool) or not lo <= v <= hi:
                raise InvalidProfile("配置参数无效：%s" % k)
        if self.stick_x == self.stick_y or self.stick_x < 0 or self.stick_y < 0 or abs(self.sign_x) != 1 or abs(self.sign_y) != 1:
            raise InvalidProfile("摇杆设置无效。")
        if self.visual_layout not in ("xbox", "nintendo"):
            raise InvalidProfile("布局无效。")
        ids = set()
        for m in self.mappings:
            s = m.input
            bad = (s.kind not in KINDS or s.code < 0 or s.id in ids or m.target not in TARGETS
                   or any(not isinstance(k, int) or k < 1 or k > 255 for k in m.keys)
                   or (s.kind == "remote" and not s.device)
                   or (s.kind == "axis" and (abs(s.direction) != 1 or not 0.1 <= s.threshold <= 2.0))
                   or (m.target == "keyboard" and not m.keys)
                   or (m.target == "media" and (len(m.keys) != 1 or not keys.is_media(m.keys[0])))
                   or (m.target == "sequence" and not valid_steps(m.steps))
                   or (m.target in STEP_TARGETS and s.kind == "remote")
                   or (m.locked and m.target not in STEP_TARGETS)
                   or m.sequence_mode not in ("hog", "keys"))
            if bad:
                raise InvalidProfile("按键映射无效：%s" % m.physical)
            ids.add(s.id)

    def to_json(self):
        d = {"version": self.version, "device_id": self.device_id, "device_name": self.device_name,
             "visual_layout": self.visual_layout, "repeat_enabled": self.repeat_enabled,
             "stick_enabled": self.stick_enabled, "stick_x": self.stick_x, "stick_y": self.stick_y,
             "sign_x": self.sign_x, "sign_y": self.sign_y}
        for k in self.FIELDS:
            d[k] = getattr(self, k)
        d["mappings"] = [m.to_json() for m in self.mappings]
        return d

    @classmethod
    def from_json(cls, d):
        p = cls(d.get("device_id", ""), d.get("device_name", ""))
        p.version = d.get("version", 3)
        p.visual_layout = d.get("visual_layout", "xbox")
        for k in cls.FIELDS:
            if k in d:
                setattr(p, k, d[k])
        for k in ("repeat_enabled", "stick_enabled"):
            if k in d:
                setattr(p, k, bool(d[k]))
        for k in ("stick_x", "stick_y", "sign_x", "sign_y"):
            if k in d:
                setattr(p, k, int(d[k]))
        p.mappings = [Mapping.from_json(m) for m in d.get("mappings") or []]
        return p

    def find(self, source_id):
        for m in self.mappings:
            if m.input.id == source_id:
                return m
        return None


def safe_id(s):
    return "".join(c if c.isalnum() or c in "-_" else "_" for c in s)[:120]


class Store:
    """Profiles and settings under the state directory (/var/lib/paddesk for the service)."""

    def __init__(self, root):
        self.root = root
        self.profiles = os.path.join(root, "profiles")
        os.makedirs(self.profiles, exist_ok=True)

    def _write(self, path, data):
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
            f.flush()
            os.fsync(f.fileno())
        if os.path.exists(path):
            try:
                os.replace(path, path + ".bak")
            except OSError:
                pass
        os.replace(tmp, path)

    def profile_path(self, device_id):
        return os.path.join(self.profiles, safe_id(device_id) + ".json")

    def load_profile(self, device_id):
        path = self.profile_path(device_id)
        if not os.path.exists(path):
            return None
        with open(path, encoding="utf-8") as f:
            p = Profile.from_json(json.load(f))
        p.validate()
        if p.device_id != device_id:
            raise InvalidProfile("手柄配置身份不匹配。")
        return p

    def save_profile(self, p):
        p.validate()
        self._write(self.profile_path(p.device_id), p.to_json())

    def load_settings(self):
        path = os.path.join(self.root, "settings.json")
        try:
            with open(path, encoding="utf-8") as f:
                d = json.load(f)
        except (OSError, ValueError):
            d = {}
        d.setdefault("selected_device", "")
        d.setdefault("hog_config", "")
        d.setdefault("grab", {})
        d.setdefault("paused", False)
        return d

    def save_settings(self, d):
        self._write(os.path.join(self.root, "settings.json"), d)
