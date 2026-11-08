import runpy
import unittest
from pathlib import Path

describe = runpy.run_path(str(Path(__file__).resolve().parents[1] / "geraete-daten-anzeigen.py"))["describe"]


class DatenAnzeigeTests(unittest.TestCase):
    def test_id_erscheint_nur_gekuerzt(self):
        text = describe({"id": "geheimeLangeId9eee", "name": "Stehlampe", "online": True, "on": False})
        self.assertIn("...9eee", text)
        self.assertNotIn("geheimeLange", text)
        self.assertIn("online", text)
        self.assertIn("Aus", text)

    def test_fehlender_zustand_wird_benannt(self):
        self.assertIn("kein Ein/Aus", describe({"id": "abcd", "name": "Sensor", "online": False, "on": None}))


if __name__ == "__main__":
    unittest.main()
