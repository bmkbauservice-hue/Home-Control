#!/usr/bin/env python3
"""Isolierter Container-Test mit Wegwerfdaten, niemals mit lokalen Zugangsdaten."""
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from uuid import uuid4

BASE = Path(__file__).resolve().parents[2]


def main():
    project = "hc-ci-" + uuid4().hex[:12]
    with tempfile.TemporaryDirectory(prefix="hc-container-test-") as folder:
        environment_file = Path(folder) / "test.env"
        key = secrets.token_hex(32)
        environment_file.write_text(
            "COMPOSE_PROFILES=betrieb,sensoren\nHC_PORT=0\nHC_SENSOR_PORT=0\n"
            "Jwt__SigningKey=" + secrets.token_hex(48) + "\nSensorBridge__Key=" + key + "\n"
            "Auth__RequireLoginForReading=true\nBlink__Enabled=false\nTuya__AutoSyncSeconds=0\n"
            "Tuya__ClientId=\nTuya__ClientSecret=\n", encoding="utf-8")
        environment_file.chmod(0o600)
        # Keine geerbten App-, Port- oder Compose-Einstellungen übernehmen.
        environment = {k: v for k, v in os.environ.items()
                       if not k.startswith(("HC_", "COMPOSE_")) and "__" not in k}
        environment["HC_ENV_FILE"] = str(environment_file)
        compose = ["docker", "compose", "--project-name", project, "--env-file", str(environment_file),
                   "-f", str(BASE / "docker-compose.yml")]

        def run(*args):
            return subprocess.run(compose + list(args), cwd=BASE, env=environment, check=True,
                                  capture_output=True, text=True).stdout.strip()

        def address(port):
            return "http://" + run("port", "homecontrol", str(port)).splitlines()[0]

        def request(base, path, data=None, token=None, sensor_key=None, expected=200, method=None):
            headers = {"Content-Type": "application/json"}
            if token:
                headers["Authorization"] = "Bearer " + token
            if sensor_key:
                headers["X-Sensor-Key"] = sensor_key
            req = Request(base + path, data=json.dumps(data).encode() if data is not None else None,
                          headers=headers, method=method)
            try:
                response = urlopen(req, timeout=15)
            except HTTPError as error:
                response = error
            with response:
                assert response.code == expected, f"{path}: HTTP {response.code}, erwartet {expected}"
                body = response.read()
            return json.loads(body) if body and expected < 400 else None

        try:
            run("up", "-d", "--wait", "--wait-timeout", "150", "--no-build")
            api, gateway = address(8080), address(5390)
            with urlopen(api, timeout=10) as response:
                assert b"<html" in response.read().lower(), "Oberfläche fehlt im Image"
            request(api, "/api/climate", expected=401)
            password = "Ci-Pruefung-" + secrets.token_hex(12)
            request(api, "/api/auth/register", {"email": "container@example.invalid", "password": password}, expected=201)
            token = request(api, "/api/auth/login", {"email": "container@example.invalid", "password": password})["accessToken"]
            room = request(api, "/api/rooms", {"name": "Nur CI-Test"}, token=token, expected=201)
            # Ausschließlich synthetische CI-Daten in den eigenen Wegwerf-Volumes.
            reading = {"sensorId": "ci-only", "name": "CI-Sensor", "temperatureCelsius": 20.5, "humidityPercent": 40}
            request(gateway, "/api/climate/bridge/readings", reading, sensor_key="falsch", expected=401)
            request(gateway, "/api/climate/bridge/readings", reading, sensor_key=key)
            request(api, "/api/climate/bridge/sensors/ci-only/room", {"roomId": room["id"]}, token=token, method="PUT")
            before = request(api, "/api/climate", token=token)
            assert len(before) == 1 and before[0]["temperatureCelsius"] == 20.5
            run("up", "-d", "--wait", "--wait-timeout", "150", "--no-build", "--force-recreate")
            api, gateway = address(8080), address(5390)
            token = request(api, "/api/auth/login", {"email": "container@example.invalid", "password": password})["accessToken"]
            assert request(api, "/api/climate", token=token) == before, "Klima nach Neuerstellung verändert"
            assert any(r["id"] == room["id"] for r in request(api, "/api/rooms", token=token)), "Hausdaten fehlen"
            reading["temperatureCelsius"] = 21
            request(gateway, "/api/climate/bridge/readings", reading, sensor_key=key)
            assert request(api, "/api/climate", token=token)[0]["temperatureCelsius"] == 21
            print("Container geprüft: Oberfläche, Anmeldung, Sensor-Schlüssel, Gateway, Haus-/Klima-Volumes und Neuerstellung.")
        finally:
            # Ausschließlich unser zufälliges Testprojekt aufräumen.
            run("down", "--volumes", "--remove-orphans")


if __name__ == "__main__":
    main()
