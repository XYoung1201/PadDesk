"""Hold/release state, media-key repeat, sequence playback, d-pad stepping and stick motion.

A faithful port of Engine.cs. Output tokens: "M:L" / "M:R" / "M:M" mouse buttons, "K:<code>" keys.
"""

import math
from collections import deque

from . import keys


class OutputEvent:
    __slots__ = ("token", "down")

    def __init__(self, token, down):
        self.token, self.down = token, down

    def __repr__(self):
        return "%s %s" % (self.token, "down" if self.down else "up")


class FrameResult:
    def __init__(self):
        self.dx = self.dy = 0
        self.events = []
        self.stick_x = self.stick_y = 0.0


def _priority(token):
    if not token.startswith("K:"):
        return 2
    return 0 if keys.is_modifier(int(token[2:])) else 1


def stick(s, p):
    def axis(code, center, sign):
        v = s.axes.get(code)
        if v is None:
            return 0.0
        delta = v - center
        span = (1 - center) if delta >= 0 else (1 + center)
        return max(-1.0, min(1.0, delta / max(1e-6, span))) * sign
    x, y = axis(p.stick_x, p.center_x, p.sign_x), axis(p.stick_y, p.center_y, p.sign_y)
    mag = math.hypot(x, y)
    if mag <= p.deadzone:
        return 0.0, 0.0
    amount = ((min(1.0, mag) - p.deadzone) / (1 - p.deadzone)) ** p.curve
    return x / mag * amount, y / mag * amount


class InputEngine:
    def __init__(self):
        self.held = set()
        self.armed = False
        self.device = ""
        self.last_time = 0.0
        self.residual_x = self.residual_y = 0.0
        self.next_x = self.next_y = 0.0
        self.old_x = self.old_y = 0
        self.pending = deque()
        self.step = None
        self.step_down = False
        self.was_active = set()
        self.repeat_next = {}
        self.repeat_gap = set()

    @property
    def sequence_pending(self):
        return self.step_down or bool(self.pending)

    def _change(self, desired, result):
        for t in sorted(self.held - desired, key=lambda t: (-_priority(t), t)):
            result.events.append(OutputEvent(t, False))
        for t in sorted(desired - self.held, key=lambda t: (_priority(t), t)):
            result.events.append(OutputEvent(t, True))
        self.held = desired

    def _disarm(self):
        self.armed = False
        self.old_x = self.old_y = 0
        self.next_x = self.next_y = 0.0
        self.residual_x = self.residual_y = 0.0
        self.pending.clear()
        self.step = None
        self.step_down = False
        self.was_active = set()
        self.repeat_next.clear()
        self.repeat_gap.clear()

    def _step(self, direction, axis, now, p):
        prev, nxt = (self.old_x, self.next_x) if axis == "x" else (self.old_y, self.next_y)
        out = 0
        if direction == 0:
            prev, nxt = 0, 0.0
        elif prev != direction:
            prev, nxt, out = direction, now + p.repeat_delay_ms / 1000.0, direction
        elif p.repeat_enabled and now >= nxt:
            nxt, out = now + p.repeat_interval_ms / 1000.0, direction
        if axis == "x":
            self.old_x, self.next_x = prev, nxt
        else:
            self.old_y, self.next_y = prev, nxt
        return out

    def tick(self, s, now, p, enabled, partial=False):
        """partial=True evaluates an in-between state (each evdev report): buttons are mirrored exactly,
        but time-based work (stick motion, repeats, sequence steps) waits for the next full frame."""
        result = FrameResult()
        desired = set()
        fresh = not partial and now > self.last_time
        dt = 0.0 if partial else max(0.0, min(0.05, now - self.last_time))
        if not partial:
            self.last_time = now
        if self.device != s.device_id:
            self._disarm()
            self.device = s.device_id
            self._change(desired, result)
        if not enabled or not s.connected or p is None:
            self._disarm()
            self._change(desired, result)
            return result
        sx, sy = stick(s, p) if p.stick_enabled else (0.0, 0.0)
        result.stick_x, result.stick_y = sx, sy
        if not self.armed:
            if not s.keys and all(not m.input.active(s) for m in p.mappings if m.target != "none") and sx == 0 and sy == 0:
                self.armed = True
            return result
        x = y = 0
        now_active = set()
        for m in p.mappings:
            if not m.input.active(s):
                continue
            t = m.target
            if t in ("up", "down", "left", "right"):
                x += (t == "right") - (t == "left")
                y += (t == "down") - (t == "up")
                continue
            if p.stick_enabled and m.input.kind == "axis" and m.input.code in (p.stick_x, p.stick_y):
                continue
            if t == "mouse_left":
                desired.add("M:L")
            elif t == "mouse_right":
                desired.add("M:R")
            elif t == "mouse_middle":
                desired.add("M:M")
            elif t == "keyboard":
                desired.update("K:%d" % k for k in m.keys)
            elif t == "media" and len(m.keys) == 1:
                sid, token = m.input.id, "K:%d" % m.keys[0]
                now_active.add(sid)
                if not p.repeat_enabled or m.keys[0] not in keys.REPEATING:
                    desired.add(token)
                elif sid not in self.repeat_next:
                    self.repeat_next[sid] = now + p.repeat_delay_ms / 1000.0
                    desired.add(token)
                elif sid in self.repeat_gap:
                    if fresh:
                        self.repeat_gap.discard(sid)
                        desired.add(token)
                elif fresh and now >= self.repeat_next[sid]:
                    self.repeat_next[sid] = now + p.repeat_interval_ms / 1000.0
                    self.repeat_gap.add(sid)
                else:
                    desired.add(token)
            elif t == "sequence" and m.steps:
                sid = m.input.id
                now_active.add(sid)
                if sid not in self.was_active:
                    self.pending.extend(m.steps)
        for sid in [k for k in self.repeat_next if k not in now_active]:
            del self.repeat_next[sid]
            self.repeat_gap.discard(sid)
        self.was_active = now_active
        if fresh:
            if self.step_down:
                self.step_down = False
            elif self.pending:
                self.step = self.pending.popleft()
                self.step_down = True
        if self.step_down:
            desired.update("K:%d" % k for k in self.step)
        result.dx = self._step((x > 0) - (x < 0), "x", now, p) * p.h_step
        result.dy = self._step((y > 0) - (y < 0), "y", now, p) * p.v_step
        if p.stick_enabled and not partial:
            if sx == 0 and sy == 0:
                self.residual_x = self.residual_y = 0.0
            else:
                self.residual_x += sx * p.speed * dt
                self.residual_y += sy * p.speed * dt
                ix, iy = int(self.residual_x), int(self.residual_y)
                self.residual_x -= ix
                self.residual_y -= iy
                result.dx += ix
                result.dy += iy
        self._change(desired, result)
        return result


class CaptureDetector:
    """Finds the first gamepad input pressed after everything was released (按键定位 / learning)."""

    def __init__(self, initial, ignore=()):
        self.baseline = initial.clone()
        self.ready = False
        self.ignore = set(ignore)

    def poll(self, s):
        if not s.connected:
            return None
        from .model import Source
        b = self.baseline.axes
        if not self.ready:
            if not s.keys and all(c in self.ignore or c not in b or abs(v - b[c]) < 0.18 for c, v in s.axes.items()):
                self.ready = True
            return None
        if s.keys:
            return Source("key", min(s.keys))
        best, code = 0.43, None
        for c, v in s.axes.items():
            if c in self.ignore or c not in b:
                continue
            change = abs(v - b[c])
            if change > best:
                best, code = change, c
        if code is None:
            return None
        return Source("axis", code, 1 if s.axes[code] > b[code] else -1, b[code], max(0.21, best * 0.5))
