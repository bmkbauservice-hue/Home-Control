#!/usr/bin/env python3
"""Ergänzt die vorhandene Google-Home-Test-App; USB-Test ohne LAN-Freigabe."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET

BASE = Path(__file__).resolve().parents[1]
PACKAGE = Path("app/src/main/java/com/example/googlehomeapisampleapp")
ANDROID = "http://schemas.android.com/apk/res/android"
TAG = "// Home Control: Sensor-Bridge"
# Woran eine frühere Fassung unserer eigenen Bridge zu erkennen ist. Sie darf ersetzt werden.
OWN_BRIDGE = "object HomeControlBridge"


def select_device(output: str, requested: str | None = None) -> str:
    """Ein USB-Handy auswählen; zusätzliche Netzwerkgeräte bleiben verbunden."""
    ready = [line.split() for line in output.splitlines()
             if len(line.split()) >= 2 and line.split()[1] == "device"]
    if requested:
        if any(fields[0] == requested for fields in ready):
            return requested
        raise ValueError("Das mit --device gewählte Gerät ist nicht verbunden oder noch nicht freigegeben.")
    usb = [fields[0] for fields in ready if any(field.startswith("usb:") for field in fields[2:])]
    if len(usb) == 1:
        return usb[0]
    if len(usb) > 1:
        raise ValueError("Mehrere USB-Geräte erkannt. Das Handy mit --device SERIENNUMMER auswählen.")
    raise ValueError("Kein freigegebenes USB-Handy erkannt. USB verbinden, entsperren und Debugging zulassen.")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if text.count(old) != 1:
        raise ValueError(f"{label}: Quellcode weicht ab. Keine Datei wurde geändert.")
    return text.replace(old, new, 1)


def plan_changes(root: Path, bridge: str) -> dict[Path, str]:
    """Erst alle Anker prüfen, dann schreiben. Keine OAuth-Einstellung verändern."""
    bridge_file = root / PACKAGE / "bridge/HomeControlBridge.kt"
    if bridge_file.exists() and bridge_file.read_text() != bridge and OWN_BRIDGE not in bridge_file.read_text():
        raise ValueError("Andere Bridge-Datei vorhanden; keine Änderung.")
    vm_path = PACKAGE / "viewmodel/devices/DeviceViewModel.kt"
    module_path = PACKAGE / "HomeModule.kt"
    gradle_path = Path("app/build.gradle.kts")
    main_manifest = ET.fromstring((root / "app/src/main/AndroidManifest.xml").read_text())
    main_application = main_manifest.find("application")
    if main_application is None:
        raise ValueError("Android-Manifest enthält keine Anwendung; keine Änderung.")
    if main_application.get(f"{{{ANDROID}}}networkSecurityConfig"):
        raise ValueError("Eigene Netzwerk-Konfiguration vorhanden; zuerst manuell prüfen.")
    vm = (root / vm_path).read_text()
    module = (root / module_path).read_text()
    gradle = (root / gradle_path).read_text()
    hook = "com.example.googlehomeapisampleapp.bridge.HomeControlBridge.publishDevice(id, name.value, mainTypeSet)"
    if hook not in vm:
        if "HomeControlBridge" in vm:
            raise ValueError("Andere Bridge-Verknüpfung vorhanden; zuerst manuell prüfen.")
        vm = replace_once(vm, "        traits.emit(combinedTraits)",
                          "        traits.emit(combinedTraits)\n        " + TAG + "\n        " + hook,
                          "DeviceViewModel")
    for symbol, anchor in (("RelativeHumidityMeasurement", "TemperatureMeasurement"),
                           ("HumiditySensorDevice", "TemperatureSensorDevice")):
        line = f"import com.google.home.matter.standard.{symbol}\n"
        if line not in module:
            module = replace_once(module, f"import com.google.home.matter.standard.{anchor}\n",
                                  f"import com.google.home.matter.standard.{anchor}\n" + line, "HomeModule-Import")
        if not re.search(rf"^\s*{symbol},\s*$", module, re.MULTILINE):
            # Der vorhandene Eintrag bestimmt auch die Einrückung.
            pattern = re.compile(rf"^(\s*){anchor},\s*$", re.MULTILINE)
            if len(pattern.findall(module)) != 1:
                raise ValueError("HomeModule-Liste weicht ab; keine Änderung.")
            module = pattern.sub(lambda m: m.group(0).rstrip() + "\n" + m.group(1) + symbol + ",", module)
    if TAG not in gradle:
        if "HOME_CONTROL_SENSOR_" in gradle:
            raise ValueError("Andere Bridge-Buildkonfiguration vorhanden; zuerst prüfen.")
        fields = '''
    // Home Control: Sensor-Bridge
    // Der Schlüssel kommt nur aus der Build-Umgebung, nie aus dem Quellcode.
    val bridgeUrl = providers.environmentVariable("HOME_CONTROL_SENSOR_URL").orElse("").get()
    val bridgeKey = providers.environmentVariable("HOME_CONTROL_SENSOR_KEY").orElse("").get()
    buildConfigField("String", "HOME_CONTROL_SENSOR_URL", "\\\"$bridgeUrl\\\"")
    buildConfigField("String", "HOME_CONTROL_SENSOR_KEY", "\\\"$bridgeKey\\\"")
'''
        gradle = replace_once(gradle, "  defaultConfig {", "  defaultConfig {" + fields, "Gradle")
        if not re.search(r"buildConfig\s*=\s*true", gradle):
            raise ValueError("BuildConfig ist nicht aktiviert; keine Änderung.")
    debug_path = Path("app/src/debug/AndroidManifest.xml")
    if (root / debug_path).exists():
        debug = ET.fromstring((root / debug_path).read_text())
    else:
        debug = ET.Element("manifest")
    application = debug.find("application")
    if application is None:
        application = ET.SubElement(debug, "application")
    current = application.get(f"{{{ANDROID}}}networkSecurityConfig")
    if current and current != "@xml/home_control_bridge_network":
        raise ValueError("Eigene Debug-Netzwerkregeln vorhanden; keine Änderung.")
    application.set(f"{{{ANDROID}}}networkSecurityConfig", "@xml/home_control_bridge_network")
    ET.register_namespace("android", ANDROID)
    return {
        vm_path: vm, module_path: module, gradle_path: gradle,
        PACKAGE / "bridge/HomeControlBridge.kt": bridge,
        debug_path: ET.tostring(debug, encoding="unicode") + "\n",
        Path("app/src/debug/res/xml/home_control_bridge_network.xml"): '''<?xml version="1.0" encoding="utf-8"?>
<network-security-config>
  <base-config cleartextTrafficPermitted="false" />
  <domain-config cleartextTrafficPermitted="true">
    <domain>127.0.0.1</domain>
    <domain>localhost</domain>
  </domain-config>
</network-security-config>
''',
    }


def apply_changes(root: Path, changes: dict[Path, str], backup_root: Path) -> Path:
    backup_root.mkdir(parents=True, exist_ok=True, mode=0o700)
    backup = Path(tempfile.mkdtemp(prefix="sensor-bridge-", dir=backup_root))
    manifest = {}
    for path in changes:
        source = root / path
        manifest[str(path)] = source.exists()
        if source.exists():
            target = backup / path
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
    (backup / "dateien.json").write_text(json.dumps(manifest, indent=2))
    for path, content in changes.items():
        target = root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)
    return backup


def read_key(path: Path) -> str:
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    try:
        with path.open("x") as stream:
            stream.write(secrets.token_hex(32) + "\n")
    except FileExistsError:
        pass
    key = path.read_text().strip()
    if not re.fullmatch(r"[A-Za-z0-9_+=/.-]{32,256}", key):
        raise ValueError("Vorhandener Bridge-Schlüssel ist ungültig; er wurde nicht ersetzt.")
    os.chmod(path, 0o600)
    return key


def probe(url: str, key: str) -> int:
    # Absichtlich ungültiges, leeres Paket: prüft Authentifizierung, legt keinen Messwert an.
    body = json.dumps({"sensorId": "home-control-connection-check", "name": "Verbindungsprüfung",
                       "temperatureCelsius": None, "humidityPercent": None}).encode()
    request = Request(url + "/api/climate/bridge/readings", data=body, method="POST",
                      headers={"Content-Type": "application/json", "X-Sensor-Key": key})
    try:
        with urlopen(request, timeout=3) as response:
            return response.status
    except HTTPError as error:
        return error.code


def run() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--android", type=Path,
                        default=Path.home() / "Schreibtisch/Umschulung/google-home-api-sample-app-android")
    parser.add_argument("--device", help="ADB-Seriennummer des gewünschten Handys")
    parser.add_argument("--install", action="store_true", help="Sichern, ergänzen, bauen, per USB installieren und Gateway starten")
    options = parser.parse_args()
    root = options.android.resolve()
    bridge = (BASE / "integrations/google-home-bridge/HomeControlBridge.kt").read_text()
    changes = plan_changes(root, bridge)
    print("Quellcode geprüft: Temperatur, Luftfeuchte und Sender können verbunden werden.", flush=True)
    if not options.install:
        print("Nur geprüft. Mit --install wird die vorhandene Android-App ergänzt.")
        return 0
    adb = shutil.which("adb")
    if not adb:
        candidate = Path.home() / "Android/Sdk/platform-tools/adb"
        if candidate.is_file():
            adb = str(candidate)
    if not adb or not shutil.which("dotnet"):
        raise ValueError("ADB oder dotnet nicht gefunden. Keine Quelldateien wurden geändert.")
    devices = subprocess.check_output([adb, "devices", "-l"], text=True)
    adb_command = [adb, "-s", select_device(devices, options.device)]
    # Eigenen Port vor dem Build prüfen. Keinen vorhandenen Gateway beenden.
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 5390))
    key = read_key(Path.home() / ".config/home-control/sensor-bridge.key")
    payload = json.dumps({"SensorBridge:Key": key})
    result = subprocess.run(["dotnet", "user-secrets", "set", "--project", str(BASE / "backend/HomeControl.Api")],
                            input=payload, text=True, capture_output=True)
    if result.returncode:
        raise ValueError("Bridge-Schlüssel konnte nicht in den lokalen User Secrets eingerichtet werden.")
    for _ in range(12):
        if probe("http://127.0.0.1:5181", key) == 400:
            break
        time.sleep(0.25)
    else:
        raise ValueError("Backend auf 5181 übernimmt den Schlüssel noch nicht. Gemeinsame Testversion neu starten, dann wiederholen.")
    backup = apply_changes(root, changes, Path.home() / ".local/share/HomeControl/android-sicherungen")
    print(f"Android-Quellcode gesichert: {backup}", flush=True)
    environment = os.environ.copy()
    environment["HOME_CONTROL_SENSOR_URL"] = "http://127.0.0.1:5390"
    environment["HOME_CONTROL_SENSOR_KEY"] = key
    print("Bestehende Android-App bauen. Google-Anmeldung und SDK bleiben erhalten.", flush=True)
    # Kein Schlüssel als CLI-Argument oder in Gradle-Ausgaben mit --info/--debug.
    subprocess.run(["bash", "./gradlew", ":app:assembleDebug", "--console=plain", "--no-daemon"],
                   cwd=root, env=environment, check=True)
    apk = root / "app/build/outputs/apk/debug/app-debug.apk"
    if not apk.is_file():
        raise ValueError("Build beendet, aber Debug-APK nicht am erwarteten Ort. Kein APK installiert.")
    subprocess.run(adb_command + ["install", "-r", str(apk)], check=True)
    subprocess.run(adb_command + ["reverse", "tcp:5390", "tcp:5390"], check=True)
    gateway_environment = os.environ.copy()
    gateway_environment["HOME_CONTROL_SENSOR_BACKEND"] = "http://127.0.0.1:5181"
    gateway_environment["HOME_CONTROL_SENSOR_BIND"] = "127.0.0.1"
    gateway = subprocess.Popen([sys.executable, str(BASE / "integrations/google-home-bridge/sensor_gateway.py")],
                               env=gateway_environment)
    try:
        for _ in range(20):
            if gateway.poll() is not None:
                raise ValueError("Gateway konnte nicht starten. Vorhandene Prozesse bleiben unberührt.")
            try:
                if probe("http://127.0.0.1:5390", key) == 400:
                    break
            except (URLError, TimeoutError):
                pass
            time.sleep(0.2)
        else:
            raise ValueError("Gateway-Test fehlgeschlagen.")
        subprocess.run(adb_command + ["shell", "am", "start", "-n",
                       "com.example.googlehomeapisampleapp/.MainActivity"], check=True)
        print("USB-Bridge läuft. Test-App öffnen, Haus auswählen und im Vordergrund lassen.", flush=True)
        print("Home Control > Klima: Empfang prüfen, dann die vier Sensoren ihren Räumen zuordnen.", flush=True)
        print("Dieses Terminal und USB-Verbindung offen lassen. Strg+C beendet nur diesen Gateway.", flush=True)
        return gateway.wait()
    finally:
        gateway.terminate()
        try:
            gateway.wait(timeout=5)
        except subprocess.TimeoutExpired:
            gateway.kill()
            gateway.wait()


if __name__ == "__main__":
    os.umask(0o077)
    try:
        sys.exit(run())
    except KeyboardInterrupt:
        print("\nSensor-Gateway beendet.")
        sys.exit(130)
    except (ValueError, OSError, ET.ParseError, subprocess.SubprocessError) as error:
        print(f"Einrichtung angehalten: {type(error).__name__}: {error}", file=sys.stderr)
        sys.exit(1)
