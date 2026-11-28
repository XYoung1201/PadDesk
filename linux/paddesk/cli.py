"""`paddesk` command: open the configuration window or control the running service."""

import fcntl
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

from .server import parse_listen

USAGE = """用法: paddesk [ui|status|pause|resume|toggle|agent|cec]
  ui       打开配置界面（默认）。关闭窗口后后台服务继续运行。
  agent    桌面会话助手：把播放 / 暂停、上一曲、下一曲交给媒体播放器（随桌面自动启动）
  status   显示服务状态
  pause    暂停输出      resume  恢复输出      toggle  切换暂停
  cec on|standby|toggle|status|init   通过 HDMI-CEC 控制投影仪 / 电视
"""


def base():
    host, port = parse_listen(os.environ.get("PADDESK_LISTEN", ""))
    return "http://%s:%d" % (host, port)


def call(method, path, body=None, timeout=3):
    data = json.dumps(body or {}).encode() if method != "GET" else None
    req = urllib.request.Request(base() + path, data=data, method=method,
                                 headers={"X-PadDesk": "1", "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read().decode())


# ---- session agent: media keys over MPRIS ----------------------------------------------------

MPRIS_METHODS = {"play-pause": "PlayPause", "next": "Next", "previous": "Previous", "stop": "Stop"}


def _run(args):
    try:
        return subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=3, text=True)
    except (OSError, subprocess.SubprocessError):
        return None


def mpris_players():
    r = _run(["gdbus", "call", "--session", "--dest", "org.freedesktop.DBus", "--object-path", "/org/freedesktop/DBus",
              "--method", "org.freedesktop.DBus.ListNames"])
    return re.findall(r"'(org\.mpris\.MediaPlayer2\.[^']+)'", r.stdout) if r and r.returncode == 0 else []


def _status(player):
    r = _run(["gdbus", "call", "--session", "--dest", player, "--object-path", "/org/mpris/MediaPlayer2", "--method",
              "org.freedesktop.DBus.Properties.Get", "org.mpris.MediaPlayer2.Player", "PlaybackStatus"])
    out = r.stdout if r and r.returncode == 0 else ""
    return 0 if "Playing" in out else 1 if "Paused" in out else 2


def media_action(action):
    """Sends one media action to the most relevant player. Returns False when no player is running."""
    if shutil.which("playerctl"):
        r = _run(["playerctl", action])
        if r is not None:
            return r.returncode == 0
    players = mpris_players()
    if not players:
        return False
    player = min(players, key=_status)
    r = _run(["gdbus", "call", "--session", "--dest", player, "--object-path", "/org/mpris/MediaPlayer2",
              "--method", "org.mpris.MediaPlayer2.Player." + MPRIS_METHODS[action]])
    return bool(r and r.returncode == 0)


def agent():
    runtime = os.environ.get("XDG_RUNTIME_DIR") or os.path.expanduser("~/.cache")
    os.makedirs(runtime, exist_ok=True)
    lock = open(os.path.join(runtime, "paddesk-agent.lock"), "w")
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        return 0  # already running in this session
    after = -1
    while True:
        try:
            d = call("GET", "/api/media/wait?after=%d&timeout=20" % after, timeout=30)
        except (urllib.error.URLError, OSError, ValueError):
            after = -1
            time.sleep(3)
            continue
        after = d.get("seq", after)
        for ev in d.get("events", []):
            if ev.get("action") in MPRIS_METHODS and not media_action(ev["action"]):
                try:
                    call("POST", "/api/media/fallback", {"code": ev["code"]})
                except (urllib.error.URLError, OSError):
                    pass


def start_agent():
    try:
        subprocess.Popen([sys.executable, "-m", "paddesk", "agent"], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                         stderr=subprocess.DEVNULL, start_new_session=True)
    except OSError:
        pass


def open_ui():
    url = base() + "/"
    try:
        st = call("GET", "/api/state")
    except (urllib.error.URLError, OSError):
        print("PadDesk 后台服务未运行。请执行: sudo systemctl start paddesk", file=sys.stderr)
        return 1
    if not st.get("agent"):
        start_agent()
    profile = os.path.expanduser("~/.config/paddesk/ui-browser")
    for browser in ("chromium-browser", "chromium", "google-chrome"):
        exe = shutil.which(browser)
        if exe:
            subprocess.Popen([exe, "--app=" + url, "--class=PadDesk", "--user-data-dir=" + profile,
                              "--window-size=1280,820", "--no-first-run", "--password-store=basic"],
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
            return 0
    for opener in ("xdg-open", "firefox"):
        exe = shutil.which(opener)
        if exe:
            subprocess.Popen([exe, url], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
            return 0
    print("请在浏览器中打开 " + url)
    return 0


def main(argv=None):
    args = sys.argv[1:] if argv is None else argv
    cmd = args[0] if args else "ui"
    try:
        if cmd == "ui":
            return open_ui()
        if cmd == "agent":
            return agent()
        if cmd == "status":
            s = call("GET", "/api/state")
            print("状态: %s" % s["status"])
            print("手柄: %s" % (s["pad"]["name"] if s["pad"] else "未连接"))
            print("按键外设: %d 个" % len(s["key_devices"]))
            if s["output_error"]:
                print(s["output_error"])
            return 0
        if cmd in ("pause", "resume", "toggle"):
            body = {} if cmd == "toggle" else {"paused": cmd == "pause"}
            print(call("POST", "/api/pause", body)["status"])
            return 0
        if cmd == "cec" and len(args) == 2 and args[1] in ("on", "standby", "toggle", "status", "init"):
            if args[1] == "status":
                r = call("GET", "/api/cec", timeout=20)
            else:
                r = call("POST", "/api/cec", {"action": args[1]}, timeout=20)
            print(r["message"])
            return 0 if args[1] != "status" or r["power"] else 1
    except urllib.error.HTTPError as ex:
        try:
            print(json.loads(ex.read().decode())["error"], file=sys.stderr)
        except (ValueError, KeyError, OSError):
            print("PadDesk 服务返回错误 %d" % ex.code, file=sys.stderr)
        return 1
    except (urllib.error.URLError, OSError) as ex:
        print("无法连接 PadDesk 服务: %s" % ex, file=sys.stderr)
        return 1
    print(USAGE)
    return 2


if __name__ == "__main__":
    sys.exit(main())
