import contextlib
import importlib.util
import io
import json
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

    def run_start(self, *args, active=None):
        output = io.StringIO()
        with patch.object(module, "BASE", self.test_repo), patch.object(module, "SCHOOL", self.school), \
             patch.object(module, "SNAPSHOTS", self.snapshots), \
             patch.object(module, "running_session", return_value=active), \
             patch.object(module.subprocess, "call", return_value=0) as start, \
             contextlib.redirect_stdout(output):
            result = module.main(list(args))
        return result, output.getvalue(), start

    def test_kopiert_nur_in_den_testordner(self):
        self.set_db()
        result, output, start = self.run_start()
        self.assertEqual(0, result)
        self.assertIn("2/3", output)
        snapshot = next(self.snapshots.glob("homecontrol-test-*.db"))
        self.assertEqual((2, 3), module.counts(snapshot))
        self.assertEqual((2, 3), module.counts(self.main_db))
        self.assertNotEqual(snapshot, self.main_db)
        self.assertEqual(0o600, snapshot.stat().st_mode & 0o777)
        self.assertEqual("http://127.0.0.1:5181", start.call_args.kwargs["env"]["ASPNETCORE_URLS"])
        self.assertEqual("false", start.call_args.kwargs["env"]["Blink__Enabled"])
        self.assertTrue(Path(start.call_args.kwargs["env"]["Climate__DatabasePath"]).is_file())

    def test_ohne_geraete_wird_nicht_gestartet(self):
        self.set_db(devices=0)
        result, output, start = self.run_start()
        self.assertEqual(2, result)
        self.assertIn("Keine andere Home-Control-Datenbank", output)
        start.assert_not_called()
        self.assertFalse(self.snapshots.exists())

    def test_neustart_erhaelt_neue_geraete_und_sensorzuordnung(self):
        self.set_db()
        self.run_start()
        session = json.loads((self.snapshots / "session.json").read_text())
        with sqlite3.connect(session["house"]) as db:
            db.execute("INSERT INTO Devices VALUES ('Neues Badlicht')")
        with sqlite3.connect(session["climate"]) as db:
            db.execute("CREATE TABLE ClimateSensors (Room TEXT)")
            db.execute("INSERT INTO ClimateSensors VALUES ('Bad')")
        result, _, start = self.run_start()
        self.assertEqual(0, result)
        self.assertEqual(f'Data Source={session["house"]}', start.call_args.kwargs["env"]["ConnectionStrings__DefaultConnection"])
        self.assertEqual((2, 4), module.counts(Path(session["house"])))
        self.assertEqual(1, len(list(self.snapshots.glob("homecontrol-test-*.db"))))
        backup = next((self.snapshots / "sicherungen").glob("*/homecontrol-climate.db"))
        with sqlite3.connect(backup) as db:
            self.assertEqual("Bad", db.execute("SELECT Room FROM ClimateSensors").fetchone()[0])
        self.assertEqual((2, 3), module.counts(self.main_db))

    def test_laufende_sitzung_wird_uebernommen_ohne_zweiten_start(self):
        self.set_db()
        self.run_start()
        data = json.loads((self.snapshots / "session.json").read_text())
        (self.snapshots / "session.json").unlink()
        result, _, start = self.run_start("--nur-sichern", active=(Path(data["house"]), Path(data["climate"])))
        self.assertEqual(0, result)
        start.assert_not_called()
        self.assertTrue((self.snapshots / "session.json").is_file())

    def test_manifest_darf_keine_produktionsdaten_uebernehmen(self):
        self.set_db()
        self.run_start()
        manifest = self.snapshots / "session.json"
        data = json.loads(manifest.read_text())
        data["house"] = str(self.main_db)
        manifest.write_text(json.dumps(data))
        before = manifest.read_bytes()
        result, _, start = self.run_start()
        self.assertEqual(7, result)
        start.assert_not_called()
        self.assertEqual(before, manifest.read_bytes())

    def test_alte_kopien_ohne_sitzung_werden_nicht_still_ersetzt(self):
        self.set_db()
        self.run_start()
        (self.snapshots / "session.json").unlink()
        result, _, start = self.run_start()
        self.assertEqual(7, result)
        start.assert_not_called()

    def test_wal_inhalt_wird_mitgesichert(self):
        self.set_db()
        self.run_start()
        session = json.loads((self.snapshots / "session.json").read_text())
        with sqlite3.connect(session["house"]) as db:
            db.execute("PRAGMA journal_mode=WAL")
            db.execute("INSERT INTO Devices VALUES ('Im WAL')")
            db.commit()
            result, _, start = self.run_start("--nur-sichern")
            self.assertEqual(0, result)
            start.assert_not_called()
            backup = next((self.snapshots / "sicherungen").glob("*/homecontrol.db"))
            self.assertEqual((2, 4), module.counts(backup))

    def test_proc_liest_nur_den_eigenen_testprozess(self):
        self.set_db()
        self.run_start()
        data = json.loads((self.snapshots / "session.json").read_text())
        api = self.test_repo / "backend/HomeControl.Api"
        api.mkdir(parents=True)
        proc = self.school / "proc"
        process = proc / "42"
        process.mkdir(parents=True)
        (process / "cwd").symlink_to(api, target_is_directory=True)
        environment = (f'ASPNETCORE_URLS=http://127.0.0.1:5181\0ConnectionStrings__DefaultConnection=Data Source={data["house"]}\0Climate__DatabasePath={data["climate"]}\0SECRET=bleibt-privat\0')
        (process / "environ").write_bytes(environment.encode())
        with patch.object(module, "BASE", self.test_repo), patch.object(module, "SNAPSHOTS", self.snapshots), patch.object(module, "PROC", proc):
            self.assertEqual((Path(data["house"]), Path(data["climate"])), module.running_session())
            (process / "environ").write_bytes(environment.replace(":5181", ":5200").encode())
            self.assertIsNone(module.running_session())


if __name__ == "__main__":
    unittest.main()

