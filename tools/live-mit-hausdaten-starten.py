#!/usr/bin/env python3
"""Startet die Wetter-/Schalter-Testversion mit einer sicheren Kopie der bestehenden Hausdaten."""
from __future__ import annotations

import argparse
import json
import os
import socket
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
PROC = Path("/proc")


def checked_session(data: dict) -> tuple[Path, Path]:
    """Nur eigene Testkopien zulassen, niemals eine Produktionsdatenbank."""
    if not isinstance(data, dict) or data.get("version") != 1:
        raise ValueError("Unbekanntes Sitzungsformat.")
    paths = []
    for name in ("house", "climate"):
        path = Path(data[name]).resolve(strict=True)
        if not path.is_relative_to(SNAPSHOTS.resolve()) or not path.is_file():
            raise ValueError("Die Sitzung verweist nicht auf eigene Testdaten.")
        paths.append(path)
    if paths[0] == paths[1] or counts(paths[0]) is None:
        raise ValueError("Ungültige Haus- oder Klima-Datenbank.")
    with closing(sqlite3.connect(paths[1].as_uri() + "?mode=ro", uri=True)) as db:
        if db.execute("PRAGMA quick_check").fetchone()[0] != "ok":
            raise ValueError("Klima-Datenbank nicht lesbar.")
    return paths[0], paths[1]


def save_session(house: Path, climate: Path) -> None:
    data = {"version": 1, "house": str(house.resolve()), "climate": str(climate.resolve())}
    checked_session(data)
    temporary = SNAPSHOTS / f"session-{uuid4().hex}.tmp"
    try:
        with temporary.open("x", encoding="utf-8") as file:
            os.chmod(temporary, 0o600)
            json.dump(data, file)
            file.flush()
            os.fsync(file.fileno())
        temporary.replace(SNAPSHOTS / "session.json")
    finally:
        temporary.unlink(missing_ok=True)


def running_session() -> tuple[Path, Path] | None:
    """Linux: Nur Pfade der eigenen Test-API lesen, keine Geheimnisse ausgeben."""
    found = []
    for process in PROC.glob("[0-9]*"):
        try:
            if process.stat().st_uid != os.getuid():
                continue
            if (process / "cwd").resolve(strict=True) != (BASE / "backend/HomeControl.Api").resolve():
                continue
            values = dict(part.split(b"=", 1) for part in (process / "environ").read_bytes().split(b"\0") if b"=" in part)
            if values.get(b"ASPNETCORE_URLS") != b"http://127.0.0.1:5181":
                continue
            connection = values.get(b"ConnectionStrings__DefaultConnection", b"").decode()
            if not connection.startswith("Data Source=") or ";" in connection:
                raise ValueError("Test-Datenbankpfad der laufenden API ist nicht eindeutig.")
            found.append(checked_session({"version": 1, "house": connection[12:],
                                         "climate": values.get(b"Climate__DatabasePath", b"").decode()}))
        except (PermissionError, ProcessLookupError, FileNotFoundError):
            continue
    if len(found) > 1:
        raise ValueError("Mehrere Test-APIs gefunden. Keine Sitzung verändert.")
    return found[0] if found else None


def backup_session(house: Path, climate: Path) -> Path:
    target = SNAPSHOTS / "sicherungen" / f"{datetime.now():%Y%m%d-%H%M%S}-{uuid4().hex[:6]}"
    target.mkdir(mode=0o700, parents=True)
    copy_sqlite(house, target / "homecontrol.db")
    copy_sqlite(climate, target / "homecontrol-climate.db")
    scale_settings = SNAPSHOTS / "scale-settings.json"
    if scale_settings.is_file():
        (target / "scale-settings.json").write_bytes(scale_settings.read_bytes())
        os.chmod(target / "scale-settings.json", 0o600)
    if counts(target / "homecontrol.db") is None:
        raise ValueError("Sicherung der Hausdaten nicht lesbar.")
    print(f"Haus- und Klima-Daten separat gesichert: {target}")
    return target


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


def restore_session(name: str) -> tuple[Path, Path]:
    """Ein ausdrücklich ausgewähltes Sicherungspaar prüfen und wieder als Sitzung speichern."""
    if Path(name).name != name or not name.startswith("homecontrol-test-") or not name.endswith(".db"):
        raise ValueError("Bitte den Dateinamen einer Haus-Testkopie angeben.")

    try:
        with socket.create_connection(("127.0.0.1", 5181), timeout=2):
            raise ValueError("Port 5181 läuft noch. Bitte zuerst dessen Startterminal beenden.")
    except ConnectionRefusedError:
        pass

    house, climate = checked_session({
        "version": 1,
        "house": SNAPSHOTS / name,
        "climate": SNAPSHOTS / f"homecontrol-climate-test-{name}",
    })
    with closing(sqlite3.connect(house.as_uri() + "?mode=ro", uri=True)) as db:
        rooms = {str(row[0]).lower() for row in db.execute("SELECT Id FROM Rooms")}
    with closing(sqlite3.connect(climate.as_uri() + "?mode=ro", uri=True)) as db:
        assigned = {str(row[0]).lower() for row in db.execute(
            "SELECT RoomId FROM ClimateSensors WHERE RoomId IS NOT NULL"
        )}
    if not assigned or not assigned <= rooms:
        raise ValueError("Keine passenden Sensor-Raumzuordnungen in diesem Datenbankpaar.")

    backup = backup_session(house, climate)
    manifest = SNAPSHOTS / "session.json"
    if manifest.is_file():
        (backup / "session-vor-wiederherstellung.json").write_bytes(manifest.read_bytes())
    save_session(house, climate)
    room_count, device_count = counts(house)
    print(f"Frühere Sitzung ausgewählt: {room_count} Räume, {device_count} Geräte, {len(assigned)} Klimaräume.")
    return house, climate


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--wiederherstellen", metavar="HAUSDATEI", help="Ein vorhandenes Haus-/Klima-Testpaar wieder öffnen.")
    mode.add_argument("--nur-sichern", action="store_true", help="Aktuelle Testdaten sichern, keinen Dienst starten.")
    mode.add_argument("--neu", action="store_true", help="Bewusst neue Kopien importieren; die bisherige Sitzung bleibt gesichert.")
    args = parser.parse_args(argv or [])
    if not (BASE / "app-starten.sh").is_file():
        print("Fehler: App-Startskript im Projekt nicht gefunden.")
        return 1

    os.umask(0o077)
    try:
        active = running_session()
        if args.wiederherstellen:
            if active:
                raise ValueError("Port 5181 läuft noch. Bitte zuerst dessen Startterminal beenden.")
            return launch(*restore_session(args.wiederherstellen))
        manifest = SNAPSHOTS / "session.json"
        session = active or (checked_session(json.loads(manifest.read_text())) if manifest.exists() else None)
        if session:
            backup_session(*session)
            save_session(*session)
        if active:
            print("Laufende Testversion auf Port 5181 erkannt und gesichert. Kein Neustart ausgeführt.")
            return 0
        if args.nur_sichern:
            if not session:
                print("Keine laufende oder gespeicherte Test-Sitzung gefunden. Nichts verändert.")
                return 2
            return 0
        if session and not args.neu:
            print("Gespeicherte Test-Sitzung wird mit denselben Zuordnungen und Geräten fortgesetzt.")
            return launch(*session)
        if not session and not args.neu and SNAPSHOTS.exists() and any(SNAPSHOTS.glob("*.db")):
            raise ValueError("Ältere Testkopien ohne Sitzung gefunden. Zuerst die bisherige Test-API sichern; --neu importiert bewusst neu.")
    except (OSError, sqlite3.Error, ValueError, KeyError, TypeError) as exc:
        print(f"Sitzung nicht verändert: {type(exc).__name__}: {exc}")
        return 7

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

    try:
        climate_db = climate_snapshot(target.stem)
        save_session(target, climate_db)
    except (OSError, sqlite3.Error) as exc:
        print(f"Klima-Sicherheitskopie fehlgeschlagen: {type(exc).__name__}: {exc}")
        return 6
    return launch(target, climate_db)


def launch(target: Path, climate_db: Path) -> int:
    environment = os.environ.copy()
    environment["ASPNETCORE_ENVIRONMENT"] = "Development"
    environment["ASPNETCORE_URLS"] = "http://127.0.0.1:5181"
    environment["ConnectionStrings__DefaultConnection"] = f"Data Source={target}"
    environment["Climate__DatabasePath"] = str(climate_db)
    environment["Scale__SettingsPath"] = str(SNAPSHOTS / "scale-settings.json")
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
    sys.exit(main(sys.argv[1:]))

