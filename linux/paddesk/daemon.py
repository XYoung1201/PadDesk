"""Entry point of the background service (systemd runs `python3 -m paddesk.daemon`)."""

import logging
import os
import signal
import sys

from . import __version__, server
from .devices import DeviceManager
from .model import Store
from .service import Service


def main():
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
    log = logging.getLogger("paddesk")
    state = os.environ.get("PADDESK_STATE") or os.environ.get("STATE_DIRECTORY") or os.path.expanduser("~/.local/state/paddesk")
    listen = server.parse_listen(os.environ.get("PADDESK_LISTEN", ""))
    store = Store(state)
    output, error = None, ""
    try:
        from .output import UInputOutput
        output = UInputOutput()
    except Exception as ex:  # keep serving the UI so the problem is visible there
        error = "无法打开 /dev/uinput，鼠标键盘输出不可用：%s" % ex
        log.error(error)
    service = Service(store, DeviceManager(), output, output_error=error)
    httpd = server.start(service, listen)
    log.info("PadDesk %s · state %s · UI http://%s:%d/", __version__, state, listen[0], listen[1])

    def stop(*_):
        service.stopping = True
    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    try:
        service.run()
    finally:
        httpd.shutdown()
        service.shutdown()
        log.info("PadDesk stopped, all keys released")
    return 0


if __name__ == "__main__":
    sys.exit(main())
