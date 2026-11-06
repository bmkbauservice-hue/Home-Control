#!/usr/bin/env python3
"""Nur POST /api/climate/bridge/readings aus dem privaten WLAN an das lokale Backend weiterleiten."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError
import ipaddress
import json

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path != '/healthz':
            return self.send_error(404)
        self.send_response(200)
        self.end_headers()
        self.wfile.write(b'ok')

    def do_POST(self):
        try:
            peer = ipaddress.ip_address(self.client_address[0])
            if not (peer.is_private or peer.is_loopback):
                return self.send_error(403)
            if self.path != '/api/climate/bridge/readings':
                return self.send_error(404)
            n = int(self.headers.get('Content-Length', '0'))
            if n < 1 or n > 4096:
                return self.send_error(413)
            token = self.headers.get('X-Sensor-Key')
            if not token:
                return self.send_error(401)
            data = self.rfile.read(n)
            json.loads(data)
            req = Request('http://127.0.0.1:5290/api/climate/bridge/readings',
                          data=data, headers={'X-Sensor-Key':token,
                          'Content-Type':'application/json'}, method='POST')
            try:
                with urlopen(req, timeout=9) as response:
                    body, status = response.read(4096), response.status
            except HTTPError as exc:
                body, status = exc.read(4096), exc.code
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            self.wfile.write(body)
        except (ValueError, json.JSONDecodeError):
            self.send_error(400)
        except (URLError, TimeoutError):
            self.send_error(502)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, *args):
        pass # Keine Sensor-IDs oder Tokens protokollieren.

if __name__ == '__main__':
    print('Home Control Sensor-Bridge: Port 5390', flush=True)
    ThreadingHTTPServer(('0.0.0.0', 5390), Handler).serve_forever()
