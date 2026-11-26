"""`paddesk` command: open the configuration window or control the running service."""

import json
import os
import shutil
import subprocess
import sys
import urllib.error
import urllib.request

from .server import parse_listen

USAGE = """用法: paddesk [ui|status|pause|resume|toggle]
  ui       打开配置界面（默认）。关闭窗口后后台服务继续运行。
  status   显示服务状态
  pause    暂停输出      resume  恢复输出      toggle  切换暂停
"""


def base():
    host, port = parse_listen(os.environ.get("PADDESK_LISTEN", ""))
    return "http://%s:%d" % (host, port)


def call(method, path, body=None):
    data = json.dumps(body or {}).encode() if method != "GET" else None
    req = urllib.request.Request(base() + path, data=data, method=method,
                                 headers={"X-PadDesk": "1", "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=3) as r:
        return json.loads(r.read().decode())


def open_ui():
    url = base() + "/"
    try:
        call("GET", "/api/state")
    except (urllib.error.URLError, OSError):
        print("PadDesk 后台服务未运行。请执行: sudo systemctl start paddesk", file=sys.stderr)
        return 1
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
    except (urllib.error.URLError, OSError) as ex:
        print("无法连接 PadDesk 服务: %s" % ex, file=sys.stderr)
        return 1
    print(USAGE)
    return 2


if __name__ == "__main__":
    sys.exit(main())
