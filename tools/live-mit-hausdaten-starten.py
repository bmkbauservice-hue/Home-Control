#!/usr/bin/env python3
"""Startet die Wetter-/Schalter-Testversion mit einer sicheren Kopie der bestehenden Hausdaten."""
from __future__ import annotations

import os
import sqlite3
import subprocess
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
        with sqlite3.connect(f"file:{path}?mode=ro", uri=True, timeout=4) as db:
            return (
                db.execute("SELECT count(*) FROM Rooms").fetchone()[0],
                db.execute("SELECT count(*) FROM Devices").fetchone()[0],
            )
    except sqlite3.Error:
        return None


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
        with sqlite3.connect(f"file:{source}?mode=ro", uri=True, timeout=10) as original:
            with sqlite3.connect(target, timeout=10) as copy:
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
