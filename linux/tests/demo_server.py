"""Runs the UI against simulated devices (no hardware needed): python3 tests/demo_server.py [port]"""
import os
import sys
import tempfile
import threading
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
sys.path.insert(0, os.path.dirname(__file__))

from test_service import FakeManager, FakeTracked  # noqa: E402
from paddesk import server  # noqa: E402
from paddesk.model import Store  # noqa: E402
from paddesk.output import RecordingOutput  # noqa: E402
from paddesk.service import Service  # noqa: E402

codes = [0x130, 0x131, 0x133, 0x134, 0x136, 0x137, 0x13a, 0x13b, 0x13c, 0x13d, 0x13e]
pad = FakeTracked("gamepad", "045e:028e:Xbox 360 Wireless Receiver", "Xbox 360 Wireless Receiver", codes,
                  {0: 0.0, 1: 0.0, 2: -1.0, 3: 0.0, 4: 0.0, 5: -1.0, 16: 0.0, 17: 0.0})
ir = FakeTracked("keys", "0000:0000:gpio_ir_recv", "gpio_ir_recv", [28, 103, 108, 115, 114])
cec = FakeTracked("keys", "0000:0000:vc4-hdmi-0", "vc4-hdmi-0", [28, 103, 108])
kbd = FakeTracked("keys", "046d:c52b:Logitech USB Receiver", "Logitech USB Receiver", [29, 30])
svc = Service(Store(tempfile.mkdtemp()), FakeManager(pad, ir, cec, kbd), RecordingOutput())
port = int(sys.argv[1]) if len(sys.argv) > 1 else 7381
server.start(svc, ("127.0.0.1", port))


def animate():
    t = 0
    while True:
        t += 1
        with svc.lock:
            pad.keys = {0x130} if t % 20 < 6 else set()
            pad.axes[0] = 0.6 if t % 40 < 10 else 0.0
            svc.frame()
        time.sleep(0.05)


threading.Thread(target=animate, daemon=True).start()
print("demo UI on http://127.0.0.1:%d/" % port, flush=True)
while True:
    time.sleep(3600)
