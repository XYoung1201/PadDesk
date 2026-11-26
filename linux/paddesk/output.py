"""Mouse and keyboard output through /dev/uinput (works under Wayland, X11 and the text console)."""

from . import keys

BTN_LEFT, BTN_RIGHT, BTN_MIDDLE = 0x110, 0x111, 0x112
EV_SYN, EV_KEY, EV_REL = 0, 1, 2
REL_X, REL_Y = 0, 1
MOUSE_BUTTONS = {"M:L": BTN_LEFT, "M:R": BTN_RIGHT, "M:M": BTN_MIDDLE}


class UInputOutput:
    """Two virtual devices so desktops classify them cleanly: a keyboard and a relative mouse."""

    def __init__(self):
        from evdev import UInput  # imported lazily so tests can run without python-evdev
        self.kbd = UInput({EV_KEY: sorted(keys.NAMES_BY_CODE)}, name="PadDesk Virtual Keyboard")
        self.mouse = UInput({EV_KEY: [BTN_LEFT, BTN_RIGHT, BTN_MIDDLE], EV_REL: [REL_X, REL_Y, 8]},
                            name="PadDesk Virtual Mouse")
        self.owned = set()

    def _emit(self, token, down):
        if token in MOUSE_BUTTONS:
            self.mouse.write(EV_KEY, MOUSE_BUTTONS[token], 1 if down else 0)
            self.mouse.syn()
        else:
            self.kbd.write(EV_KEY, int(token[2:]), 1 if down else 0)
            self.kbd.syn()
        if down:
            self.owned.add(token)
        else:
            self.owned.discard(token)

    def apply(self, r):
        try:
            if r.dx or r.dy:
                if r.dx:
                    self.mouse.write(EV_REL, REL_X, int(r.dx))
                if r.dy:
                    self.mouse.write(EV_REL, REL_Y, int(r.dy))
                self.mouse.syn()
            for ev in r.events:
                self._emit(ev.token, ev.down)
        except OSError:
            self.release_all()
            raise

    def release_all(self):
        def order(t):
            return 1 if t.startswith("K:") and keys.is_modifier(int(t[2:])) else 0
        for t in sorted(self.owned, key=order):
            try:
                self._emit(t, False)
            except OSError:
                pass

    def close(self):
        self.release_all()
        for d in (self.kbd, self.mouse):
            try:
                d.close()
            except OSError:
                pass


class RecordingOutput:
    """Test double: records what would be sent."""

    def __init__(self):
        self.log = []
        self.owned = set()

    def apply(self, r):
        if r.dx or r.dy:
            self.log.append(("move", r.dx, r.dy))
        for ev in r.events:
            self.log.append((ev.token, ev.down))
            (self.owned.add if ev.down else self.owned.discard)(ev.token)

    def release_all(self):
        for t in sorted(self.owned):
            self.log.append((t, False))
        self.owned.clear()

    def close(self):
        self.release_all()
