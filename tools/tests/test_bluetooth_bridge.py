import runpy
import unittest
from pathlib import Path

bridge = runpy.run_path(str(Path(__file__).resolve().parents[2] / "integrations/bluetooth/klima_bridge.py"))
decode = bridge["decode_value"]


class BluetoothBridgeTests(unittest.TestCase):
    def test_temperatur_kann_negativ_sein(self):
        self.assertEqual(-2.5, decode((-250).to_bytes(2, "little", signed=True), True, -40, 60))

    def test_luftfeuchte_und_unbekannter_wert(self):
        self.assertEqual(45.67, decode((4567).to_bytes(2, "little"), False, 0, 100))
        self.assertIsNone(decode(b"\xff\xff", False, 0, 100))
        self.assertIsNone(decode(b"\x00", False, 0, 100))
