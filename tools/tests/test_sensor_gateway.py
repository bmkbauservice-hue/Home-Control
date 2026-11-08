import runpy
import unittest
from pathlib import Path

gateway = runpy.run_path(str(Path(__file__).resolve().parents[2] / "integrations/google-home-bridge/sensor_gateway.py"))
is_bridge_path = gateway["is_bridge_path"]
COMMAND = "/api/google-home/bridge/commands/3f2504e0-4f89-11d3-9a0c-0305e82c3301/result"


class GatewayTests(unittest.TestCase):
    def test_bridge_adressen_werden_weitergeleitet(self):
        for path in ("/api/climate/bridge/readings", "/api/google-home/bridge/poll", COMMAND):
            self.assertTrue(is_bridge_path(path), path)

    def test_uebrige_api_bleibt_gesperrt(self):
        for path in ("/api/devices", "/api/auth/login", "/api/google-home/devices/abc/switch",
                     "/api/google-home/bridge/poll/../../devices", COMMAND + "/extra"):
            self.assertFalse(is_bridge_path(path), path)


if __name__ == "__main__":
    unittest.main()
