import json
import os
import runpy
import struct
import unittest
from pathlib import Path

from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

tool = runpy.run_path(str(Path(__file__).resolve().parents[1] / "tuya-lan-lauschen.py"))
decode = tool["decode"]
describe = tool["describe"]
KEY = tool["UDP_KEY"]
CONTENT = {"gwId": "abc123wxyz", "version": "3.4"}


def frame_55aa(payload: bytes) -> bytes:
    # Kopf, Rückgabewert, Inhalt, Prüfsumme und Ende wie im Tuya-Rahmen.
    return struct.pack(">IIII", 0x55AA, 0, 19, len(payload) + 12) + b"\0\0\0\0" + payload + b"\0" * 4 + struct.pack(">I", 0xAA55)


def ecb(data: bytes) -> bytes:
    pad = 16 - len(data) % 16
    encryptor = Cipher(algorithms.AES(KEY), modes.ECB()).encryptor()
    return encryptor.update(data + bytes([pad]) * pad) + encryptor.finalize()


class TuyaLanTests(unittest.TestCase):
    def test_unverschluesselter_rundruf(self):
        self.assertEqual(CONTENT, decode(frame_55aa(json.dumps(CONTENT).encode())))

    def test_verschluesselter_rundruf_bis_3_4(self):
        self.assertEqual(CONTENT, decode(frame_55aa(ecb(json.dumps(CONTENT).encode()))))

    def test_rundruf_3_5(self):
        header = struct.pack(">IHIII", 0x6699, 0, 1, 19, 0)
        nonce = os.urandom(12)
        secret = AESGCM(KEY).encrypt(nonce, b"\0\0\0\0" + json.dumps(CONTENT).encode(), header[4:])
        self.assertEqual(CONTENT, decode(header + nonce + secret + struct.pack(">I", 0x9966)))

    def test_fremde_daten_ergeben_nichts(self):
        self.assertIsNone(decode(b"kein tuya paket"))
        self.assertIsNone(decode(os.urandom(64)))

    def test_ausgabe_zeigt_keine_vollstaendige_unbekannte_id(self):
        text = describe(CONTENT, {})
        self.assertIn("wxyz", text)
        self.assertNotIn("abc123", text)

    def test_rundruf_ohne_id_wird_nicht_als_geraet_ausgegeben(self):
        self.assertIn("ohne Geräte-ID", describe({"from": "app"}, {}))

    def test_bekanntes_geraet_erscheint_mit_namen(self):
        self.assertIn("Stehlampe", describe(CONTENT, {"abc123wxyz": "Stehlampe"}))


if __name__ == "__main__":
    unittest.main()
