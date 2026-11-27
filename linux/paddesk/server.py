"""Local HTTP API and static files for the configuration UI.

Bound to loopback only. Requests must name a loopback Host (blocks DNS rebinding) and every change
must carry the X-PadDesk header (a custom header forces a CORS preflight that we never grant, so
web pages in a browser cannot drive the service).
"""

import json
import mimetypes
import os
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

from . import keys

WEB = os.path.join(os.path.dirname(os.path.abspath(__file__)), "web")
DEFAULT_LISTEN = ("127.0.0.1", 7381)
LOOPBACK = {"127.0.0.1", "localhost", "[::1]"}


def make_handler(service):
    class Handler(BaseHTTPRequestHandler):
        server_version = "PadDesk"

        def log_message(self, fmt, *args):
            pass

        def _host_ok(self):
            host = (self.headers.get("Host") or "").rsplit(":", 1)[0].lower()
            return host in LOOPBACK

        def _send(self, code, body, ctype="application/json; charset=utf-8"):
            data = body if isinstance(body, bytes) else json.dumps(body, ensure_ascii=False).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("X-Frame-Options", "DENY")
            self.end_headers()
            self.wfile.write(data)

        def _body(self):
            n = int(self.headers.get("Content-Length") or 0)
            if n > 1 << 20:
                raise ValueError("请求过大。")
            raw = self.rfile.read(n) if n else b"{}"
            data = json.loads(raw.decode("utf-8") or "{}")
            if not isinstance(data, dict):
                raise ValueError("请求格式错误。")
            return data

        def _dispatch(self, method):
            if not self._host_ok():
                return self._send(403, {"error": "forbidden"})
            url = urlparse(self.path)
            q = {k: v[-1] for k, v in parse_qs(url.query).items()}
            path = url.path
            if method == "GET" and not path.startswith("/api/"):
                return self._static(path)
            if method != "GET" and self.headers.get("X-PadDesk") != "1":
                return self._send(403, {"error": "missing X-PadDesk header"})
            try:
                body = self._body() if method != "GET" else {}
                scope = q.get("scope", body.get("scope", "pad"))
                if scope not in ("pad", "remote"):
                    raise ValueError("scope 无效。")
                route = (method, path)
                if route == ("GET", "/api/state"):
                    return self._send(200, service.state())
                if route == ("GET", "/api/profile"):
                    return self._send(200, service.get_profile(scope))
                if route == ("PUT", "/api/profile"):
                    return self._send(200, service.save_profile(scope, body.get("profile") or {}, body.get("rev")))
                if route == ("GET", "/api/keys"):
                    return self._send(200, {"keys": keys.key_list(), "media": [{"code": c, "name": keys.name(c)} for c in keys.MEDIA],
                                            "modifiers": [{"code": c, "name": keys.name(c)} for c in (29, 56, 42, 125)]})
                if route == ("GET", "/api/media/wait"):
                    return self._send(200, service.media_wait(int(q.get("after", -1)), min(25.0, float(q.get("timeout", 20)))))
                if route == ("POST", "/api/media/fallback"):
                    service.media_fallback(int(body.get("code", 0)))
                    return self._send(200, {"ok": True})
                if route == ("GET", "/api/hog"):
                    return self._send(200, service.hog())
                if route == ("GET", "/api/settings"):
                    return self._send(200, service.get_settings())
                if route == ("POST", "/api/settings"):
                    return self._send(200, service.update_settings(body))
                if route == ("POST", "/api/pause"):
                    service.set_paused(body.get("paused", not service.paused))
                elif route == ("POST", "/api/select"):
                    service.select_device(str(body.get("device_id") or ""))
                elif route == ("POST", "/api/learn"):
                    service.start_learn(scope)
                elif route == ("POST", "/api/learn/cancel"):
                    service.cancel_learn()
                elif route == ("POST", "/api/center"):
                    service.record_center()
                elif route == ("POST", "/api/suspend"):
                    service.suspend(bool(body.get("on")))
                elif route == ("POST", "/api/reset"):
                    service.reset_profile()
                else:
                    return self._send(404, {"error": "not found"})
                return self._send(200, service.state())
            except LookupError as ex:
                return self._send(409, {"error": str(ex)})
            except ValueError as ex:
                return self._send(400, {"error": str(ex)})

        def _static(self, path):
            name = "index.html" if path in ("/", "") else path.lstrip("/")
            full = os.path.realpath(os.path.join(WEB, name))
            if not full.startswith(os.path.realpath(WEB) + os.sep) or not os.path.isfile(full):
                return self._send(404, b"not found", "text/plain")
            ctype = mimetypes.guess_type(full)[0] or "application/octet-stream"
            if ctype.startswith("text/") or ctype.endswith("javascript"):
                ctype += "; charset=utf-8"
            with open(full, "rb") as f:
                self._send(200, f.read(), ctype)

        def do_GET(self):
            self._dispatch("GET")

        def do_POST(self):
            self._dispatch("POST")

        def do_PUT(self):
            self._dispatch("PUT")

    return Handler


def parse_listen(text):
    if not text:
        return DEFAULT_LISTEN
    host, _, port = text.rpartition(":")
    host = host or DEFAULT_LISTEN[0]
    if host not in ("127.0.0.1", "localhost"):
        raise SystemExit("PADDESK_LISTEN 只能绑定本机回环地址。")
    return host, int(port)


def start(service, listen=DEFAULT_LISTEN):
    httpd = ThreadingHTTPServer(listen, make_handler(service))
    httpd.daemon_threads = True
    thread = threading.Thread(target=httpd.serve_forever, name="paddesk-http", daemon=True)
    thread.start()
    return httpd
