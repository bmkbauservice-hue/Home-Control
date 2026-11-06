import contextlib
import importlib.util
import io
import sqlite3
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


MODULE_FILE = Path(__file__).resolve().parents[1] / "live-mit-hausdaten-starten.py"
spec = importlib.util.spec_from_file_location("live_hausdaten", MODULE_FILE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class LiveStartTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.school = Path(self.temp.name)
        self.test_repo = self.school / "Home-Control-LiveTest"
        self.test_repo.mkdir()
        (self.test_repo / "app-starten.sh").write_text("#!/bin/bash\nexit 0\n")
        self.snapshots = self.school / "snapshots"
        self.main_db = self.school / "Home-Control" / "backend" / "HomeControl.Api" / "homecontrol.db"

    def set_db(self, rooms=2, devices=3):
        self.main_db.parent.mkdir(parents=True)
        with sqlite3.connect(self.main_db) as db:
            db.execute("CREATE TABLE Rooms (Name TEXT)")
            db.execute("CREATE TABLE Devices (Name TEXT)")
            db.executemany("INSERT INTO Rooms VALUES (?)", [("Raum",)] * rooms)
            db.executemany("INSERT INTO Devices VALUES (?)", [("Gerät",)] * devices)

    def run_start(self):
        output = io.StringIO()
        with patch.object(module, "BASE", self.test_repo), patch.object(module, "SCHOOL", self.school), \
             patch.object(module, "SNAPSHOTS", self.snapshots), \
             patch.object(module.subprocess, "call", return_value=0) as start, \
             contextlib.redirect_stdout(output):
            result = module.main()
        return result, output.getvalue(), start

    def test_kopiert_nur_in_den_testordner(self):
        self.set_db()
        result, output, start = self.run_start()
        self.assertEqual(0, result)
        self.assertIn("2/3", output)
        snapshot = next(self.snapshots.glob("*.db"))
        self.assertEqual((2, 3), module.counts(snapshot))
        self.assertEqual((2, 3), module.counts(self.main_db))
        self.assertNotEqual(snapshot, self.main_db)
        self.assertEqual(0o600, snapshot.stat().st_mode & 0o777)
        self.assertEqual("http://127.0.0.1:5181", start.call_args.kwargs["env"]["ASPNETCORE_URLS"])
        self.assertEqual("false", start.call_args.kwargs["env"]["Blink__Enabled"])

    def test_ohne_geraete_wird_nicht_gestartet(self):
        self.set_db(devices=0)
        result, output, start = self.run_start()
        self.assertEqual(2, result)
        self.assertIn("Keine andere Home-Control-Datenbank", output)
        start.assert_not_called()
        self.assertFalse(self.snapshots.exists())


if __name__ == "__main__":
    unittest.main()
