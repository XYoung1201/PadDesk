"""Linux key codes (linux/input-event-codes.h), display names and key-name parsing.

Kept free of python-evdev so the model, engine and API can be tested anywhere; tests check
these numbers against evdev.ecodes.
"""

CODES = {
    "KEY_ESC": 1, "KEY_1": 2, "KEY_2": 3, "KEY_3": 4, "KEY_4": 5, "KEY_5": 6, "KEY_6": 7, "KEY_7": 8,
    "KEY_8": 9, "KEY_9": 10, "KEY_0": 11, "KEY_MINUS": 12, "KEY_EQUAL": 13, "KEY_BACKSPACE": 14,
    "KEY_TAB": 15, "KEY_Q": 16, "KEY_W": 17, "KEY_E": 18, "KEY_R": 19, "KEY_T": 20, "KEY_Y": 21,
    "KEY_U": 22, "KEY_I": 23, "KEY_O": 24, "KEY_P": 25, "KEY_LEFTBRACE": 26, "KEY_RIGHTBRACE": 27,
    "KEY_ENTER": 28, "KEY_LEFTCTRL": 29, "KEY_A": 30, "KEY_S": 31, "KEY_D": 32, "KEY_F": 33,
    "KEY_G": 34, "KEY_H": 35, "KEY_J": 36, "KEY_K": 37, "KEY_L": 38, "KEY_SEMICOLON": 39,
    "KEY_APOSTROPHE": 40, "KEY_GRAVE": 41, "KEY_LEFTSHIFT": 42, "KEY_BACKSLASH": 43, "KEY_Z": 44,
    "KEY_X": 45, "KEY_C": 46, "KEY_V": 47, "KEY_B": 48, "KEY_N": 49, "KEY_M": 50, "KEY_COMMA": 51,
    "KEY_DOT": 52, "KEY_SLASH": 53, "KEY_RIGHTSHIFT": 54, "KEY_KPASTERISK": 55, "KEY_LEFTALT": 56,
    "KEY_SPACE": 57, "KEY_CAPSLOCK": 58, "KEY_F1": 59, "KEY_F2": 60, "KEY_F3": 61, "KEY_F4": 62,
    "KEY_F5": 63, "KEY_F6": 64, "KEY_F7": 65, "KEY_F8": 66, "KEY_F9": 67, "KEY_F10": 68,
    "KEY_NUMLOCK": 69, "KEY_SCROLLLOCK": 70, "KEY_KP7": 71, "KEY_KP8": 72, "KEY_KP9": 73,
    "KEY_KPMINUS": 74, "KEY_KP4": 75, "KEY_KP5": 76, "KEY_KP6": 77, "KEY_KPPLUS": 78, "KEY_KP1": 79,
    "KEY_KP2": 80, "KEY_KP3": 81, "KEY_KP0": 82, "KEY_KPDOT": 83, "KEY_F11": 87, "KEY_F12": 88,
    "KEY_KPENTER": 96, "KEY_RIGHTCTRL": 97, "KEY_KPSLASH": 98, "KEY_SYSRQ": 99, "KEY_RIGHTALT": 100,
    "KEY_HOME": 102, "KEY_UP": 103, "KEY_PAGEUP": 104, "KEY_LEFT": 105, "KEY_RIGHT": 106,
    "KEY_END": 107, "KEY_DOWN": 108, "KEY_PAGEDOWN": 109, "KEY_INSERT": 110, "KEY_DELETE": 111,
    "KEY_MUTE": 113, "KEY_VOLUMEDOWN": 114, "KEY_VOLUMEUP": 115, "KEY_POWER": 116, "KEY_PAUSE": 119,
    "KEY_LEFTMETA": 125, "KEY_RIGHTMETA": 126, "KEY_COMPOSE": 127, "KEY_STOP": 128, "KEY_SLEEP": 142,
    "KEY_MAIL": 155, "KEY_BACK": 158, "KEY_FORWARD": 159, "KEY_NEXTSONG": 163, "KEY_PLAYPAUSE": 164,
    "KEY_PREVIOUSSONG": 165, "KEY_STOPCD": 166, "KEY_HOMEPAGE": 172, "KEY_REFRESH": 173,
    "KEY_F13": 183, "KEY_F14": 184, "KEY_F15": 185, "KEY_F16": 186, "KEY_F17": 187, "KEY_F18": 188,
    "KEY_F19": 189, "KEY_F20": 190, "KEY_F21": 191, "KEY_F22": 192, "KEY_F23": 193, "KEY_F24": 194,
    "KEY_SEARCH": 217,
}
NAMES_BY_CODE = {v: k for k, v in CODES.items()}

LEFTCTRL, RIGHTCTRL = 29, 97
MODIFIERS = {29, 97, 42, 54, 56, 100, 125, 126}
# Media / system keys offered by the "媒体键" output, in menu order (same set as the Windows build).
MEDIA = [115, 114, 113, 164, 163, 165, 166, 158, 159, 173, 172]
REPEATING = {114, 115}  # volume keys repeat while held

_DISPLAY = {
    1: "Esc", 14: "Backspace", 15: "Tab", 28: "Enter", 57: "Space", 58: "CapsLock",
    29: "Ctrl", 97: "右Ctrl", 42: "Shift", 54: "右Shift", 56: "Alt", 100: "右Alt",
    125: "Super", 126: "右Super", 127: "Menu", 119: "Pause", 99: "PrintScreen", 69: "NumLock", 70: "ScrollLock",
    102: "Home", 107: "End", 104: "PageUp", 109: "PageDown", 110: "Insert", 111: "Delete",
    103: "↑", 108: "↓", 105: "←", 106: "→",
    12: "-", 13: "=", 26: "[", 27: "]", 39: ";", 40: "'", 41: "`", 43: "\\", 51: ",", 52: ".", 53: "/",
    55: "Num*", 74: "Num-", 78: "Num+", 83: "Num.", 96: "NumEnter", 98: "Num/",
    115: "音量+", 114: "音量−", 113: "静音", 164: "播放 / 暂停", 163: "下一曲", 165: "上一曲", 166: "停止播放",
    158: "浏览器后退", 159: "浏览器前进", 173: "浏览器刷新", 172: "浏览器主页", 217: "搜索", 155: "邮件",
    116: "电源", 142: "睡眠", 128: "停止",
}
_KEYPAD = {71: 7, 72: 8, 73: 9, 75: 4, 76: 5, 77: 6, 79: 1, 80: 2, 81: 3, 82: 0}


def name(code):
    if code in _DISPLAY:
        return _DISPLAY[code]
    if code in _KEYPAD:
        return "Num%d" % _KEYPAD[code]
    n = NAMES_BY_CODE.get(code)
    if n:
        return n[4:]
    return "KEY %d" % code


def chord(codes):
    return " + ".join(name(c) for c in codes) if codes else "键盘（未选择）"


def is_modifier(code):
    return code in MODIFIERS


def is_media(code):
    return code in MEDIA


_ALIASES = {
    "CTRL": 29, "CONTROL": 29, "LCTRL": 29, "RCTRL": 97,
    "ALT": 56, "LALT": 56, "OPTION": 56, "RALT": 100, "ALTGR": 100,
    "SHIFT": 42, "LSHIFT": 42, "RSHIFT": 54,
    "WIN": 125, "LWIN": 125, "META": 125, "SUPER": 125, "CMD": 125, "COMMAND": 125, "RWIN": 126, "RSUPER": 126,
    "TAB": 15, "ENTER": 28, "RETURN": 28, "ESC": 1, "ESCAPE": 1, "SPACE": 57,
    "BACKSPACE": 14, "BKSP": 14, "DELETE": 111, "DEL": 111, "INSERT": 110, "INS": 110,
    "HOME": 102, "END": 107, "PAGEUP": 104, "PGUP": 104, "PAGEDOWN": 109, "PGDN": 109,
    "LEFT": 105, "←": 105, "UP": 103, "↑": 103, "RIGHT": 106, "→": 106, "DOWN": 108, "↓": 108,
    "VOLUP": 115, "VOLUMEUP": 115, "音量+": 115, "VOLDOWN": 114, "VOLUMEDOWN": 114, "音量-": 114, "音量−": 114,
    "MUTE": 113, "静音": 113, "PLAY": 164, "PAUSE": 164, "PLAYPAUSE": 164, "播放": 164,
    "NEXT": 163, "下一曲": 163, "PREV": 165, "PREVIOUS": 165, "上一曲": 165, "STOP": 166,
    "PRINTSCREEN": 99, "PRTSC": 99, "MENU": 127, "APPS": 127, "CAPSLOCK": 58,
    "-": 12, "=": 13, "[": 26, "]": 27, ";": 39, "'": 40, "`": 41, "\\": 43, ",": 51, ".": 52, "/": 53,
}


def parse(text):
    """Parses one key name as typed in a sequence ("Ctrl", "Tab", "VolUp", "F5", "←", "KEY_A", "KEY 30")."""
    t = (text or "").strip()
    if not t:
        return 0
    u = t.upper()
    if len(u) == 1 and ("A" <= u <= "Z" or "0" <= u <= "9"):
        return CODES["KEY_" + u]
    if len(u) >= 2 and u[0] == "F" and u[1:].isdigit() and 1 <= int(u[1:]) <= 24:
        return CODES["KEY_" + u]
    if u in CODES:
        return CODES[u]
    if u.startswith("KEY") and u[3:].strip().isdigit():
        v = int(u[3:].strip())
        return v if 1 <= v <= 255 else 0
    if u in _ALIASES:
        return _ALIASES[u]
    if t in _ALIASES:
        return _ALIASES[t]
    for code, label in _DISPLAY.items():
        if label.lower() == t.lower():
            return code
    return 0


def key_list():
    """All keys the keyboard output may use, for the UI's key picker."""
    return [{"code": c, "name": name(c)} for c in sorted(NAMES_BY_CODE)]


# Gamepad codes (EV_KEY / EV_ABS) used for default names and the controller drawing.
BTN = {
    "SOUTH": 0x130, "EAST": 0x131, "C": 0x132, "NORTH": 0x133, "WEST": 0x134, "Z": 0x135,
    "TL": 0x136, "TR": 0x137, "TL2": 0x138, "TR2": 0x139, "SELECT": 0x13a, "START": 0x13b,
    "MODE": 0x13c, "THUMBL": 0x13d, "THUMBR": 0x13e,
    "DPAD_UP": 0x220, "DPAD_DOWN": 0x221, "DPAD_LEFT": 0x222, "DPAD_RIGHT": 0x223,
}
ABS = {"X": 0, "Y": 1, "Z": 2, "RX": 3, "RY": 4, "RZ": 5, "GAS": 9, "BRAKE": 10,
       "HAT0X": 0x10, "HAT0Y": 0x11, "HAT1X": 0x12, "HAT1Y": 0x13}

BUTTON_LABELS = {
    0x130: "A", 0x131: "B", 0x133: "Y", 0x134: "X", 0x132: "C", 0x135: "Z",
    0x136: "L1 / LB", 0x137: "R1 / RB", 0x138: "L2 / LT", 0x139: "R2 / RT",
    0x13a: "返回 / Select", 0x13b: "开始 / Start", 0x13c: "Home", 0x13d: "左摇杆按下", 0x13e: "右摇杆按下",
    0x220: "方向上", 0x221: "方向下", 0x222: "方向左", 0x223: "方向右",
}
# Stick directions follow evdev: X grows to the right, Y grows downwards.
STICKS = {0: "左摇杆", 1: "左摇杆", 3: "右摇杆", 4: "右摇杆"}
TRIGGERS = {2, 5, 9, 10}
HATS_X, HATS_Y = {0x10, 0x12}, {0x11, 0x13}

AXIS_LABELS = {0: "左摇杆 X", 1: "左摇杆 Y", 2: "L2 / LT", 3: "右摇杆 X", 4: "右摇杆 Y", 5: "R2 / RT",
               9: "R2 / RT", 10: "L2 / LT", 0x10: "方向帽 X", 0x11: "方向帽 Y"}


def axis_caption(code, direction):
    """Readable name of one axis direction, e.g. "右摇杆 ↑" (ABS_RY −) or "方向帽 →"."""
    plus = direction > 0
    if code in STICKS:
        arrow = ("→" if plus else "←") if code in (0, 3) else ("↓" if plus else "↑")
        return STICKS[code] + " " + arrow
    if code in HATS_X:
        return "方向帽 " + ("→" if plus else "←")
    if code in HATS_Y:
        return "方向帽 " + ("↓" if plus else "↑")
    base = AXIS_LABELS.get(code, "轴 %d" % code)
    if code in TRIGGERS:
        return base if plus else base + " −"
    return base + (" +" if plus else " −")


def legacy_axis_name(code, direction):
    """The default name earlier versions stored for an axis direction ("右摇杆 Y +"), so it can be renamed."""
    label = AXIS_LABELS.get(code, "")
    if not label or code in TRIGGERS:
        return None
    return label + (" +" if direction > 0 else " −")


# Face buttons are named by position in evdev (SOUTH/EAST/NORTH/WEST); the printed letter depends on the layout.
FACE_LABELS = {
    "xbox": {0x130: "A", 0x131: "B", 0x133: "Y", 0x134: "X"},
    "nintendo": {0x130: "B", 0x131: "A", 0x133: "X", 0x134: "Y"},
}


def button_label(code, layout="xbox"):
    faces = FACE_LABELS.get(layout, FACE_LABELS["xbox"])
    return faces.get(code) or BUTTON_LABELS.get(code, "")
