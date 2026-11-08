#!/usr/bin/env python3
"""Lauscht passiv auf Tuya-Rundrufe im Heimnetz. Das Skript sendet nichts und schaltet nichts.

Es zeigt, welche Tuya-Geräte im WLAN aktiv sind, und ordnet sie den gespeicherten Geräten zu.
IP-Adressen werden nie ausgegeben, unbekannte Geräte-IDs nur mit den letzten vier Zeichen.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import select
import socket
import sqlite3
import struct
import sys
import time
from contextlib import closing
from pathlib import Path

from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

SESSION = Path.home() / ".local/share/home-control/test-snapshots/session.json"
PORTS = (6666, 6667, 7000)

# Öffentlich dokumentierter Schlüssel für Rundrufe. Er ist für alle Tuya-Geräte gleich
# und erlaubt NICHT das Steuern. Dafür bräuchte man den gerätespezifischen local_key.
UDP_KEY = hashlib.md5(b"yGAdlopoPVldABfn").digest()

PREFIX_55AA = 0x000055AA
PREFIX_6699 = 0x00006699


def parse_json(data: bytes) -> dict | None:
    """Sucht das JSON-Objekt, denn vor dem Inhalt können noch Statusbytes stehen."""
    start = data.find(b"{")
    end = data.rfind(b"}")
    if start < 0 or end < start:
        return None
    try:
        value = json.loads(data[start:end + 1].decode())
    except (UnicodeDecodeError, ValueError):
        return None
    return value if isinstance(value, dict) else None


def decrypt_ecb(data: bytes) -> bytes | None:
    if not data or len(data) % 16:
        return None
    decryptor = Cipher(algorithms.AES(UDP_KEY), modes.ECB()).decryptor()
    return decryptor.update(data) + decryptor.finalize()


def decode(packet: bytes) -> dict | None:
    """Liest einen Rundruf der Protokolle 3.1 bis 3.5. Unlesbares ergibt None."""
    if len(packet) < 28:
        return None
    prefix = struct.unpack(">I", packet[:4])[0]
    if prefix == PREFIX_6699:
        # Aufbau 3.5: 18 Byte Kopf, 12 Byte Nonce, Inhalt, 16 Byte Tag, 4 Byte Ende.
        if len(packet) < 18 + 12 + 16 + 4:
            return None
        try:
            plain = AESGCM(UDP_KEY).decrypt(packet[18:30], packet[30:-4], packet[4:18])
        except InvalidTag:
            return None
        return parse_json(plain)
    if prefix != PREFIX_55AA:
        return None

    # Aufbau bis 3.4: 16 Byte Kopf, 4 Byte Rückgabewert, Inhalt, 8 Byte Ende.
    body = packet[20:-8]
    for candidate in (body, packet[16:-8]):
        found = parse_json(candidate) if candidate[:1] == b"{" else None
        if found is None:
            plain = decrypt_ecb(candidate)
            found = parse_json(plain) if plain else None
        if found is not None:
            return found
    return None


def known_devices() -> dict[str, str]:
    """Liest die gespeicherten Tuya-IDs der Testsitzung nur lesend."""
    if not SESSION.is_file():
        return {}
    house = Path(json.loads(SESSION.read_text())["house"])
    with closing(sqlite3.connect(house.as_uri() + "?mode=ro", uri=True, timeout=4)) as db:
        rows = db.execute("SELECT Name, TuyaDeviceId FROM Devices WHERE TuyaDeviceId IS NOT NULL AND TuyaDeviceId <> ''")
        return {tuya_id: name for name, tuya_id in rows}


def describe(data: dict | None, known: dict[str, str]) -> str:
    if data is None:
        return "Inhalt nicht lesbar"
    device_id = str(data.get("gwId", ""))
    if not device_id:
        return "Rundruf ohne Geräte-ID, kein Gerät zuordenbar"
    name = known.get(device_id)
    label = f"Home Control: {name}" if name else f"unbekannt, ID endet auf {device_id[-4:]}"
    return f"Protokoll {data.get('version', '?')}, {label}"


def listen(seconds: int) -> dict[str, dict | None]:
    sockets = []
    for port in PORTS:
        udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        udp.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        udp.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEPORT, 1)
        try:
            udp.bind(("", port))
            sockets.append(udp)
        except OSError:
            udp.close()
            print(f"UDP-Port {port} ist nicht verfügbar.", file=sys.stderr)

    # Die IP dient nur als interner Schlüssel und wird nicht ausgegeben.
    senders: dict[str, dict | None] = {}
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        ready, _, _ = select.select(sockets, [], [], 1)
        for udp in ready:
            packet, (address, _) = udp.recvfrom(4096)
            senders[address] = decode(packet) or senders.get(address)
    for udp in sockets:
        udp.close()
    return senders


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sekunden", type=int, default=30, help="Lauschzeit, Standard 30")
    parser.add_argument("--zuordnung", action="store_true",
                        help="Gibt Name|ID für TuyaDiagnose --abgleich aus. Nur in eine Pipe erlaubt.")
    args = parser.parse_args()

    if args.zuordnung:
        # Die vollständigen IDs sollen nicht auf dem Bildschirm oder in Mitschnitten landen.
        if sys.stdout.isatty():
            print("Bitte per Pipe an TuyaDiagnose --abgleich weitergeben.", file=sys.stderr)
            return 2
        for tuya_id, name in known_devices().items():
            print(f"{name}|{tuya_id}")
        return 0

    known = known_devices()
    senders = listen(max(1, min(args.sekunden, 300)))
    print(f"{len(senders)} Tuya-Geräte in {args.sekunden} Sekunden gehört.")
    for number, data in enumerate(senders.values(), 1):
        print(f"Gerät {number}: {describe(data, known)}")
    print("Hören belegt nur die WLAN-Aktivität. Lesen und Schalten bräuchten den local_key des Geräts.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
