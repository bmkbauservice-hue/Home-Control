#!/usr/bin/env python3
"""Zieht das Blink-Token zwischen Dauerdienst und Testinstanz um. Das Token wird nie ausgegeben.

Blink tauscht das Refresh-Token bei jeder Erneuerung aus. Nutzen zwei Instanzen dasselbe Token,
sperrt die zweite die erste aus. Deshalb darf immer nur eine Instanz Blink benutzen.

  status      zeigt, welche Datenbank das neuere Token hat
  zu-test     schaltet Blink im Dauerdienst aus und übernimmt das Token in die Testdatenbank
  zum-dienst  gibt das Token zurück und schaltet Blink im Dauerdienst wieder ein
"""
from __future__ import annotations

import argparse
import json
import socket
import sqlite3
import subprocess
import sys
from contextlib import closing
from datetime import datetime
from pathlib import Path

BASE = Path(__file__).resolve().parent.parent
PROJECT = BASE / "backend/HomeControl.Api"
SERVICE_DB = Path.home() / "Schreibtisch/Umschulung/Home-Control/backend/HomeControl.Api/homecontrol.db"
SESSION = Path.home() / ".local/share/home-control/test-snapshots/session.json"
COLUMNS = ("Id", "HardwareId", "EncryptedRefreshToken", "UpdatedAtUtc", "InitialRefreshTokenHash")


def test_db() -> Path:
    return Path(json.loads(SESSION.read_text())["house"])


def read_row(path: Path) -> dict | None:
    with closing(sqlite3.connect(path.as_uri() + "?mode=ro", uri=True, timeout=4)) as db:
        row = db.execute(f"SELECT {', '.join(COLUMNS)} FROM BlinkCredentials WHERE Id = 1").fetchone()
    return dict(zip(COLUMNS, row)) if row else None


def updated_at(row: dict) -> datetime:
    return datetime.fromisoformat(row["UpdatedAtUtc"])


def compare(source: dict | None, target: dict | None) -> str:
    """Entscheidet ohne Schreiben: Nie ein neueres Token mit einem älteren überschreiben."""
    if source is None:
        return "keine-quelle"
    if target is not None and target["EncryptedRefreshToken"] == source["EncryptedRefreshToken"]:
        return "gleich"
    if target is not None and updated_at(target) > updated_at(source):
        return "ziel-neuer"
    return "kopieren"


def write_row(path: Path, row: dict) -> None:
    with closing(sqlite3.connect(path, timeout=10)) as db:
        marks = ", ".join("?" for _ in COLUMNS)
        db.execute(f"INSERT OR REPLACE INTO BlinkCredentials ({', '.join(COLUMNS)}) VALUES ({marks})",
                   [row[name] for name in COLUMNS])
        db.commit()


def transfer(source: Path, target: Path) -> str:
    result = compare(read_row(source), read_row(target))
    if result == "kopieren":
        write_row(target, read_row(source))
    return result


def secrets_text() -> str:
    # Die Ausgabe bleibt hier: Sie enthält auch andere Geheimnisse.
    return subprocess.run(["dotnet", "user-secrets", "list", "--project", str(PROJECT)],
                          capture_output=True, text=True, check=True).stdout


def service_blink_off(text: str | None = None) -> bool:
    lines = (text if text is not None else secrets_text()).splitlines()
    return any(line.replace(" ", "").lower() == "blink:enabled=false" for line in lines)


def set_service_blink(enabled: bool) -> None:
    # Der Dienst liest die User Secrets neu ein und braucht dafür keinen Neustart.
    command = ["remove", "Blink:Enabled"] if enabled else ["set", "Blink:Enabled", "false"]
    subprocess.run(["dotnet", "user-secrets", *command, "--project", str(PROJECT)],
                   capture_output=True, check=True)


def ready_for_test(house: Path) -> str | None:
    """None = die Testinstanz darf Blink benutzen. Sonst der Grund, warum nicht."""
    if not service_blink_off():
        return "Blink ist im Dauerdienst noch an. Zuerst: python3 tools/blink-token-umziehen.py zu-test"
    if compare(read_row(SERVICE_DB), read_row(house)) == "kopieren":
        return "Die Testdatenbank hat ein älteres Token. Zuerst: python3 tools/blink-token-umziehen.py zu-test"
    return None


def port_open(port: int) -> bool:
    with socket.socket() as probe:
        probe.settimeout(1)
        return probe.connect_ex(("127.0.0.1", port)) == 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("befehl", choices=["status", "zu-test", "zum-dienst"])
    args = parser.parse_args()
    house = test_db()

    if args.befehl == "status":
        for label, path in (("Dauerdienst", SERVICE_DB), ("Testinstanz", house)):
            row = read_row(path)
            print(f"{label}: {'kein Token' if row is None else 'Token vom ' + row['UpdatedAtUtc'][:19] + ' UTC'}")
        print("Beide haben dasselbe Token." if compare(read_row(SERVICE_DB), read_row(house)) == "gleich"
              else "Die Token unterscheiden sich.")
        print("Blink im Dauerdienst:", "aus" if service_blink_off() else "an")
        return 0

    if args.befehl == "zu-test":
        # Erst abschalten, dann kopieren: Danach kann der Dienst das Token nicht mehr verändern.
        set_service_blink(enabled=False)
        print("Blink im Dauerdienst ausgeschaltet.")
        print("Token:", transfer(SERVICE_DB, house))
        return 0

    if port_open(5181):
        print("Port 5181 läuft noch. Bitte die Testinstanz zuerst beenden. Nichts verändert.")
        return 1
    print("Token:", transfer(house, SERVICE_DB))
    set_service_blink(enabled=True)
    print("Blink im Dauerdienst wieder eingeschaltet.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
