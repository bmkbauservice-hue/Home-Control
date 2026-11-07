#!/usr/bin/env python3
"""Liest BLE-Klimasensoren über das standardisierte Environmental Sensing Service."""
import argparse
import asyncio
import json
from pathlib import Path
import re
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener

BASE_UUID = "-0000-1000-8000-00805f9b34fb"
SERVICE = "0000181a" + BASE_UUID
TEMPERATURE = "00002a6e" + BASE_UUID
HUMIDITY = "00002a6f" + BASE_UUID


def decode_value(raw, signed, minimum, maximum):
    if len(raw) != 2:
        return None
    value = int.from_bytes(raw, "little", signed=signed) / 100
    return value if minimum <= value <= maximum else None


async def read_sensor(address):
    from bleak import BleakClient, BleakScanner

    device = await BleakScanner.find_device_by_address(address, timeout=10)
    if device is None:
        raise ValueError("Sensor nicht in Bluetooth-Reichweite gefunden.")
    async with BleakClient(device, timeout=15) as client:
        service = client.services.get_service(SERVICE)
        if service is None:
            raise ValueError("Das Gerät bietet kein standardisiertes Bluetooth-Klimaprofil an.")
        values = {}
        for uuid, field, signed, minimum, maximum in (
            (TEMPERATURE, "temperatureCelsius", True, -40, 60),
            (HUMIDITY, "humidityPercent", False, 0, 100),
        ):
            characteristic = service.get_characteristic(uuid)
            if characteristic is not None and "read" in characteristic.properties:
                raw = await asyncio.wait_for(client.read_gatt_char(characteristic), timeout=8)
                values[field] = decode_value(raw, signed, minimum, maximum)
        if not any(value is not None for value in values.values()):
            raise ValueError("Keine lesbaren Temperatur- oder Feuchtewerte.")
        return values


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def publish(port, key, payload):
    request = Request(
        f"http://127.0.0.1:{port}/api/climate/bridge/readings",
        data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json", "X-Sensor-Key": key},
        method="POST",
    )
    opener = build_opener(ProxyHandler({}), NoRedirect())
    with opener.open(request, timeout=6) as response:
        return json.load(response).get("received") is True


async def scan():
    from bleak import BleakScanner

    for device, advert in (await BleakScanner.discover(timeout=8, return_adv=True)).values():
        supported = " · meldet Klimaprofil" if SERVICE in advert.service_uuids else ""
        print(f"{device.address}  {device.name or advert.local_name or 'Unbenannt'}{supported}")


async def run(args, key, bleak_error):
    payload = {"sensorId": f"ble:{args.address.lower()}", "name": args.name}
    while True:
        received = False
        try:
            values = await read_sensor(args.address)
            received = await asyncio.to_thread(publish, args.port, key, payload | values)
            if received:
                print("Klimawert übernommen. In Home Control unter Klima einem Raum zuordnen.", flush=True)
            else:
                print("Home Control hat den Empfang nicht bestätigt.", flush=True)
        except HTTPError as exc:
            print(f"Home Control antwortet mit HTTP {exc.code}. Port und Bridge-Schlüssel prüfen.", flush=True)
        except (URLError, ValueError, OSError, bleak_error) as exc:
            print(f"Bluetooth-Bridge: {exc}", flush=True)
        if args.once:
            return 0 if received else 1
        await asyncio.sleep(60)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scan", action="store_true", help="Bluetooth-Geräte in Reichweite anzeigen.")
    parser.add_argument("--address", help="Bluetooth-Adresse aus dem Scan.")
    parser.add_argument("--name", help="Anzeigename für den Sensor in Home Control.")
    parser.add_argument("--port", type=int, default=5181)
    parser.add_argument("--once", action="store_true", help="Nur einen Messversuch ausführen.")
    args = parser.parse_args()
    try:
        from bleak.exc import BleakError
    except ImportError:
        parser.exit(1, "Bleak fehlt. Installation steht in docs/DIREKTE_GERAETE.md.\n")

    try:
        if args.scan:
            asyncio.run(scan())
        else:
            if not re.fullmatch(r"(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}", args.address or ""):
                parser.error("Bitte eine Bluetooth-Adresse aus dem Scan angeben.")
            if not args.name or not args.name.strip() or len(args.name.strip()) > 120 or not 1 <= args.port <= 65535:
                parser.error("Bitte einen Sensornamen und einen gültigen lokalen Port angeben.")
            args.name = args.name.strip()
            key = (Path.home() / ".config/home-control/sensor-bridge.key").read_text().strip()
            if len(key) < 32:
                parser.error("Der vorhandene Sensor-Bridge-Schlüssel ist zu kurz.")
            return asyncio.run(run(args, key, BleakError))
    except KeyboardInterrupt:
        pass
    except (BleakError, OSError) as exc:
        parser.exit(1, f"Bluetooth-Bridge: {exc}\n")


if __name__ == "__main__":
    raise SystemExit(main())
