import runpy
import sqlite3
import tempfile
import unittest
from contextlib import closing
from pathlib import Path
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / "blink-token-umziehen.py"
tool = runpy.run_path(str(SCRIPT))
compare, service_blink_off = tool["compare"], tool["service_blink_off"]


def row(token, updated):
    return {"Id": 1, "HardwareId": "hw", "EncryptedRefreshToken": token,
            "UpdatedAtUtc": updated, "InitialRefreshTokenHash": "hash"}


OLD = "2026-11-05 16:15:59.8345734+00:00"
NEW = "2026-11-08 21:00:00.0000000+00:00"


class CompareTests(unittest.TestCase):
    def test_gleiches_token_wird_nicht_kopiert(self):
        self.assertEqual("gleich", compare(row("a", OLD), row("a", NEW)))

    def test_neueres_ziel_wird_nie_ueberschrieben(self):
        self.assertEqual("ziel-neuer", compare(row("a", OLD), row("b", NEW)))

    def test_neuere_quelle_wird_kopiert(self):
        self.assertEqual("kopieren", compare(row("b", NEW), row("a", OLD)))

    def test_leeres_ziel_wird_gefuellt(self):
        self.assertEqual("kopieren", compare(row("a", OLD), None))

    def test_ohne_quelle_passiert_nichts(self):
        self.assertEqual("keine-quelle", compare(None, row("a", OLD)))


class SecretTests(unittest.TestCase):
    def test_erkennt_ausgeschalteten_dienst(self):
        self.assertTrue(service_blink_off("Agent:ApiKey = x\nBlink:Enabled = false\n"))

    def test_ohne_eintrag_gilt_blink_als_an(self):
        self.assertFalse(service_blink_off("Blink:HardwareId = x\n"))


class TransferTests(unittest.TestCase):
    def database(self, folder, name, values):
        path = Path(folder) / name
        with closing(sqlite3.connect(path)) as db:
            db.execute("CREATE TABLE BlinkCredentials (Id INTEGER PRIMARY KEY, HardwareId TEXT, "
                       "EncryptedRefreshToken TEXT, UpdatedAtUtc TEXT, InitialRefreshTokenHash TEXT)")
            db.execute("INSERT INTO BlinkCredentials VALUES (:Id, :HardwareId, :EncryptedRefreshToken, "
                       ":UpdatedAtUtc, :InitialRefreshTokenHash)", values)
            db.commit()
        return path

    def test_kopiert_das_neuere_token(self):
        with tempfile.TemporaryDirectory() as folder:
            source = self.database(folder, "quelle.db", row("neu", NEW))
            target = self.database(folder, "ziel.db", row("alt", OLD))
            self.assertEqual("kopieren", tool["transfer"](source, target))
            self.assertEqual("neu", tool["read_row"](target)["EncryptedRefreshToken"])

    def test_laesst_neueres_ziel_unveraendert(self):
        with tempfile.TemporaryDirectory() as folder:
            source = self.database(folder, "quelle.db", row("alt", OLD))
            target = self.database(folder, "ziel.db", row("neu", NEW))
            self.assertEqual("ziel-neuer", tool["transfer"](source, target))
            self.assertEqual("neu", tool["read_row"](target)["EncryptedRefreshToken"])


class ScriptTextTests(unittest.TestCase):
    def test_token_wird_nie_ausgegeben(self):
        for line in SCRIPT.read_text(encoding="utf-8").splitlines():
            if "print(" in line:
                self.assertNotIn('["EncryptedRefreshToken"]', line)


if __name__ == "__main__":
    unittest.main()
