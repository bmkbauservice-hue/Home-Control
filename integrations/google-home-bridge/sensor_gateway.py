#!/usr/bin/env python3
"""Leitet Sensor-Messwerte aus dem privaten LAN an die lokale Home-Control-API weiter."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError
import ipaddress
import json
import os

BACKEND = os.environ.get("HOME_CONTROL_SENSOR_BACKEND", "http://127.0.0.1:5181").rstrip("/")
PORT = int(os.environ.get("HOME_CONTROL_SENSOR_PORT", "5390"))
BIND = os.environ.get("HOME_CONTROL_SENSOR_BIND", "0.0.0.0")

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path != "/healthz":
            return self.send_error(404)
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.end_headers()
        self.wfile.write(b"ok")

    def do_POST(self):
        try:
            peer = ipaddress.ip_address(self.client_address[0])
            if not (peer.is_private or peer.is_loopback):
                return self.send_error(403)
            if self.path != "/api/climate/bridge/readings":
                return self.send_error(404)

            size = int(self.headers.get("Content-Length", "0"))
            if size < 1 or size > 4096:
                return self.send_error(413)
            key = self.headers.get("X-Sensor-Key")
            if not key:
                return self.send_error(401)
            body = self.rfile.read(size)
            if not isinstance(json.loads(body), dict):
                return self.send_error(400)

            req = Request(
                BACKEND + "/api/climate/bridge/readings",
                data=body,
                headers={"Content-Type": "application/json", "X-Sensor-Key": key},
                method="POST",
            )
            try:
                with urlopen(req, timeout=9) as response:
                    data, code = response.read(4096), response.status
            except HTTPError as exc:
                data, code = exc.read(4096), exc.code

            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(data)
        except (ValueError, UnicodeError):
            self.send_error(400)
        except (URLError, TimeoutError):
            self.send_error(502)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, format, *args):
        # Keine Sensor-IDs oder Schlüssel in Protokollen.
        pass

if __name__ == "__main__":
    if not BACKEND.startswith("http://127.0.0.1:") and not BACKEND.startswith("http://localhost:"):
        raise SystemExit("Backend muss an Loopback gebunden sein.")
    print("Sensor-Gateway gestartet (LAN-Port " + str(PORT) + ").", flush=True)
    ThreadingHTTPServer((BIND, PORT), Handler).serve_forever()

