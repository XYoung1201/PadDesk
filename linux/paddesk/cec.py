"""Projector / TV power over HDMI-CEC, through cec-ctl (package v4l-utils).

The Raspberry Pi registers as a playback device once after each boot (`cec-ctl --playback`); after that
"Image View On" wakes the display at logical address 0 and "Standby" puts it to sleep. The service runs
these from a worker thread so a slow display never holds up the mapping loop.
"""

import logging
import queue
import re
import shutil
import subprocess
import threading

log = logging.getLogger("paddesk")

DEFAULT_DEVICE = "/dev/cec1"   # HDMI1 on the Raspberry Pi 5 (HDMI0 is /dev/cec0)
ACTIONS = {"on": "开机", "standby": "待机", "toggle": "开关切换"}
POWER_NAMES = {"on": "已开机", "standby": "待机", "to-on": "正在开机", "to-standby": "正在待机", "unknown": "无应答"}
TIMEOUT = 8.0


def valid_device(dev):
    return bool(re.fullmatch(r"/dev/cec\d{1,2}", dev or ""))


def parse_power(text):
    """Power state from `--give-device-power-status` output."""
    m = re.search(r"pwr-state:\s*(.+?)\s*\(0x0?([0-3])\)", text or "")
    if not m:
        return "unknown"
    return {"0": "on", "1": "standby", "2": "to-on", "3": "to-standby"}[m.group(2)]


def _run(args):
    try:
        r = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=TIMEOUT, text=True)
        return r.returncode, r.stdout
    except FileNotFoundError:
        return 127, "找不到 cec-ctl，请安装 v4l-utils。"
    except (OSError, subprocess.SubprocessError) as ex:
        return 1, str(ex)


class CecError(RuntimeError):
    pass


class CecController:
    def __init__(self, device=DEFAULT_DEVICE, runner=_run, target=0):
        self.device = device if valid_device(device) else DEFAULT_DEVICE
        self.runner = runner
        self.target = target
        self.lock = threading.Lock()   # one cec-ctl at a time
        self.ready = False
        self.last = ""                 # human-readable result of the last command
        self.power = None              # last known power state
        self.on_result = None          # callback(text) for the service's status line
        self.jobs = queue.Queue(maxsize=4)
        self.worker = None

    @staticmethod
    def available():
        return shutil.which("cec-ctl") is not None

    def set_device(self, device):
        if not valid_device(device):
            raise ValueError("CEC 设备应为 /dev/cec0、/dev/cec1 这样的路径。")
        if device != self.device:   # no lock: a running command must not stall the caller
            self.device, self.ready = device, False

    # ---- commands (callers may be any thread) --------------------------------------------------

    def _ctl(self, *args):
        code, out = self.runner(["cec-ctl", "-d", self.device, "-s"] + list(args))
        if code == 127:
            raise CecError(out.strip())
        if code != 0:
            raise CecError("cec-ctl 失败（%d）：%s" % (code, out.strip()[-200:]))
        return out

    def _init(self):
        self._ctl("--playback")
        self.ready = True

    def _send(self, *args):
        """Sends a message, registering as a playback device first when the adapter is unconfigured
        (after a reboot, or when another program released it)."""
        if not self.ready:
            self._init()
        out = self._ctl("--to", str(self.target), *args)
        if "unconfigured" in out.lower():
            self._init()
            out = self._ctl("--to", str(self.target), *args)
            if "unconfigured" in out.lower():
                raise CecError("%s 无法配置为播放设备。" % self.device)
        return out

    def init(self):
        with self.lock:
            try:
                self._init()
                self.last = "%s 已初始化" % self.device
            except CecError as ex:
                self.ready = False
                self.last = str(ex)
                raise
            return self.last

    def query(self):
        with self.lock:
            try:
                self.power = parse_power(self._send("--give-device-power-status"))
                self.last = "投影仪状态：" + POWER_NAMES[self.power]
            except CecError as ex:
                self.power = None
                self.last = str(ex)
                raise
            return self.power

    def run(self, action):
        if action not in ACTIONS:
            raise ValueError("未知 CEC 动作：%s" % action)
        with self.lock:
            try:
                if action == "toggle":
                    state = parse_power(self._send("--give-device-power-status"))
                    action = "standby" if state in ("on", "to-on") else "on"
                if action == "on":
                    out = self._send("--image-view-on")
                    self.power = "to-on"
                else:
                    out = self._send("--standby")
                    self.power = "to-standby"
                acked = "nack" not in out.lower() and "not acknowledged" not in out.lower()
                self.last = "已发送投影仪%s" % ACTIONS[action] + ("" if acked else "，但投影仪没有应答")
            except CecError as ex:
                self.last = str(ex)
                raise
            return self.last

    # ---- background queue used by the mapping loop ---------------------------------------------

    def submit(self, action):
        if self.worker is None or not self.worker.is_alive():
            self.worker = threading.Thread(target=self._work, name="paddesk-cec", daemon=True)
            self.worker.start()
        try:
            self.jobs.put_nowait(action)
        except queue.Full:
            pass

    def init_async(self):
        self.submit("init")

    def _work(self):
        while True:
            action = self.jobs.get()
            if action is None:
                return
            try:
                text = self.init() if action == "init" else self.run(action)
                log.info("CEC: %s", text)
            except (CecError, ValueError) as ex:
                text = "CEC：%s" % ex
                log.warning(text)
            if self.on_result:
                self.on_result(text)

    def stop(self):
        try:
            self.jobs.put_nowait(None)
        except queue.Full:
            pass
