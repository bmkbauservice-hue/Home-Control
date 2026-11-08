#!/usr/bin/env python3
"""Zeigt, welche Geräte Muju Home gerade aus Google Home an die Testinstanz meldet. Nur lesend.

Von der Geräte-ID erscheinen nur die letzten vier Zeichen, der Bridge-Schlüssel nie.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

URL = "http://127.0.0.1:5181/api/google-home/bridge/devices"
KEY_FILE = Path.home() / ".config/home-control/sensor-bridge.key"


def describe(device: dict) -> str:
    state = {True: "Ein", False: "Aus", None: "kein Ein/Aus"}[device.get("on")]
    if device.get("open") is not None:
        state = "Tür auf" if device["open"] else "Tür zu"
    online = "online" if device.get("online") else "offline"
    return f"{device.get('name')} | ID ...{str(device.get('id'))[-4:]} | {online} | {state}"


def main() -> int:
    request = Request(URL, headers={"X-Sensor-Key": KEY_FILE.read_text().strip()})
    try:
        with urlopen(request, timeout=5) as response:
            answer = json.load(response)
    except (HTTPError, URLError, TimeoutError) as error:
        print(f"Testinstanz nicht lesbar: {type(error).__name__}")
        return 1

    if not answer["connected"]:
        print("Muju Home ist nicht verbunden. Handy, USB und App prüfen.")
        return 0
    print(f"Muju Home meldet {len(answer['devices'])} Geräte mit Ein/Aus:")
    for device in answer["devices"]:
        print("  " + describe(device))
    return 0


if __name__ == "__main__":
    sys.exit(main())
