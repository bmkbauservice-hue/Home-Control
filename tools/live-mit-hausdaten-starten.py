#!/usr/bin/env python3
"""Startet die Wetter-/Schalter-Testversion mit einer sicheren Kopie der bestehenden Hausdaten."""
from __future__ import annotations

import os
import sqlite3
import subprocess
from contextlib import closing
import sys
from datetime import datetime
from pathlib import Path
from uuid import uuid4

BASE = Path(__file__).resolve().parent.parent
SCHOOL = Path.home() / "Schreibtisch" / "Umschulung"
SNAPSHOTS = Path.home() / ".local" / "share" / "home-control" / "test-snapshots"


def counts(path: Path) -> tuple[int, int] | None:
    """Nur die Anzahlen lesen, keine persönlichen Daten ausgeben."""
    try:
        with closing(sqlite3.connect(f"file:{path}?mode=ro", uri=True, timeout=4)) as db:
            return (
                db.execute("SELECT count(*) FROM Rooms").fetchone()[0],
                db.execute("SELECT count(*) FROM Devices").fetchone()[0],
            )
    except sqlite3.Error:
        return None


def climate_counts(path: Path) -> tuple[int, int] | None:
    """Erkennt die bisherigen Klima-Tabellen; der Inhalt bleibt privat."""
    try:
        with closing(sqlite3.connect(f"file:{path}?mode=ro", uri=True, timeout=4)) as db:
            tables = {row[0] for row in db.execute(
                "SELECT name FROM sqlite_master WHERE type='table'"
            )}
            if not ({"ClimateSensors", "RoomClimate"} & tables):
                return None
            sensors = db.execute("SELECT count(*) FROM ClimateSensors").fetchone()[0] if "ClimateSensors" in tables else 0
            rooms = db.execute("SELECT count(*) FROM RoomClimate").fetchone()[0] if "RoomClimate" in tables else 0
            return sensors, rooms
    except sqlite3.Error:
        return None


def copy_sqlite(source: Path, target: Path) -> None:
    """Konsistente SQLite-Kopie inklusive WAL, ohne das Original zu verändern."""
    with closing(sqlite3.connect(f"file:{source}?mode=ro", uri=True, timeout=10)) as original:
        with closing(sqlite3.connect(target, timeout=10)) as copy:
            original.backup(copy)
    os.chmod(target, 0o600)


def climate_snapshot(label: str) -> Path:
    """Kopiert die befüllteste vorhandene Klima-Datenbank; sonst eigene Testdatei."""
    sources: list[tuple[Path, tuple[int, int]]] = []
    for pattern in ("Home-Control*/backend/HomeControl.Api/**/*.db",
                    "Home-Control*/backend/HomeControl.Api/**/*.sqlite"):
        for file in SCHOOL.glob(pattern):
            if file.is_file():
                info = climate_counts(file)
                if info is not None and any(info):
                    sources.append((file, info))

    default_path = Path.home() / ".local" / "share" / "HomeControl" / "homecontrol-climate.db"
    if default_path.is_file():
        info = climate_counts(default_path)
        if info is not None and any(info):
            sources.append((default_path, info))

    target = SNAPSHOTS / f"homecontrol-climate-test-{label}.db"
    if sources:
        sources.sort(key=lambda entry: (-(entry[1][0] + entry[1][1]), str(entry[0])))
        source, (sensors, rooms) = sources[0]
        copy_sqlite(source, target)
        print(f"Klima-Datenbank mit {sensors} Sensoren und {rooms} Raumwerten sicher kopiert.")
    else:
        # Create a private, empty climate database; the API initializes its tables.
        target.touch(mode=0o600, exist_ok=False)
        print("Bisher keine befüllte Klima-Datenbank gefunden. Sensor-Bridge muss Werte liefern.")
    return target


def main() -> int:
    if not (BASE / "app-starten.sh").is_file():
        print("Fehler: App-Startskript im Projekt nicht gefunden.")
        return 1

    candidates: list[tuple[Path, tuple[int, int]]] = []
    for folder in SCHOOL.glob("Home-Control*"):
        source = folder / "backend" / "HomeControl.Api" / "homecontrol.db"
        if not source.is_file() or folder.resolve() == BASE.resolve():
            continue
        result = counts(source)
        if result is not None and result[0] > 0 and result[1] > 0:
            candidates.append((source, result))

    if not candidates:
        print("Keine andere Home-Control-Datenbank mit Räumen UND Geräten gefunden.")
        print("Deine Daten wurden nicht verändert. Bitte die Terminalausgabe hier zeigen.")
        return 2

    # In der Regel liegt die richtige Datenbank im Hauptprojekt, nicht im Test-Worktree.
    main_source = SCHOOL / "Home-Control" / "backend" / "HomeControl.Api" / "homecontrol.db"
    candidates.sort(key=lambda item: (item[0] != main_source, -(item[1][1]), str(item[0])))
    source, (rooms, devices) = candidates[0]

    os.umask(0o077)
    SNAPSHOTS.mkdir(mode=0o700, parents=True, exist_ok=True)
    target = SNAPSHOTS / f"homecontrol-test-{datetime.now():%Y%m%d-%H%M%S}-{uuid4().hex[:6]}.db"

    try:
        # SQLite-Backup-API: kopiert konsistent auch bei aktiver WAL-Datenbank.
        # Das Original bleibt offen nur zum LESEN und wird niemals verändert.
        with closing(sqlite3.connect(f"file:{source}?mode=ro", uri=True, timeout=10)) as original:
            with closing(sqlite3.connect(target, timeout=10)) as copy:
                original.backup(copy)
        os.chmod(target, 0o600)
    except (OSError, sqlite3.Error) as exc:
        target.unlink(missing_ok=True)
        print(f"Fehler beim Kopieren: {type(exc).__name__}: {exc}")
        return 3

    copied = counts(target)
    if copied != (rooms, devices):
        target.unlink(missing_ok=True)
        print("Datenbank-Kontrolle fehlgeschlagen. Test nicht gestartet.")
        return 4

    print(f"Vorhandene Räume/Geräte gefunden: {rooms}/{devices} ({source.parent.parent.parent.name}).")
    print("SQLite-Sicherheitskopie für die Testversion angelegt.")
    print("Original-Datenbank und laufender Dienst bleiben unverändert.")
    print("Testadresse: http://127.0.0.1:5181")
    print("Achtung: Die Schalter können ECHTE Tuya-Geräte bedienen. Nur unkritische Lampen testen.")
    print("Zum Beenden im Terminal Strg+C drücken.\n", flush=True)

    environment = os.environ.copy()
    environment["ASPNETCORE_ENVIRONMENT"] = "Development"
    environment["ASPNETCORE_URLS"] = "http://127.0.0.1:5181"
    environment["ConnectionStrings__DefaultConnection"] = f"Data Source={target}"
    try:
        climate_db = climate_snapshot(target.stem)
    except (OSError, sqlite3.Error) as exc:
        print(f"Klima-Sicherheitskopie fehlgeschlagen: {type(exc).__name__}: {exc}")
        return 6
    environment["Climate__DatabasePath"] = str(climate_db)
    key_file = Path.home() / ".config" / "home-control" / "sensor-bridge.key"
    if key_file.is_file():
        key = key_file.read_text(encoding="utf-8").strip()
        if len(key) >= 32:
            environment["SensorBridge__Key"] = key
            print("Vorhandener Sensor-Bridge-Schlüssel für die Testinstanz eingebunden.")
    environment["Blink__Enabled"] = "false"
    environment["Tuya__AutoSyncSeconds"] = "0"
    try:
        return subprocess.call(["bash", "./app-starten.sh"], cwd=BASE, env=environment)
    except KeyboardInterrupt:
        return 130
    except OSError as exc:
        print(f"Start fehlgeschlagen: {exc}")
        return 5


if __name__ == "__main__":
    sys.exit(main())
