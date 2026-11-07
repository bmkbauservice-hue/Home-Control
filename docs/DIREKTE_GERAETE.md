# Geräte direkt verbinden

Der Tuya-Energiezähler bleibt unter **Übersicht → Auf einen Blick**. Er schätzt kWh aus aufeinanderfolgenden Wattproben. Ein direkt abgefragter Shelly-Zähler steht separat darunter, weil sein Zählerstand einen anderen Zeitraum abdeckt.

## WLAN: Shelly Gen2 oder neuer

Unterstützt werden Shelly-Schaltkanäle mit lokaler HTTP-RPC-Schnittstelle. Modelle mit Leistungsmessung wie der Shelly Plus Plug S können Watt und den eigenen Energiezähler melden. Das Gerät muss schon im Heimnetz angemeldet sein. Seine private IPv4-Adresse steht im Router oder in der Geräteverwaltung.

Beispiel aus dem gemeinsamen Testordner, mit eigener Geräte-IP statt der Beispieladresse:

```bash
LocalDevices__Shelly__0__Name='Steckdose Werkbank' \
LocalDevices__Shelly__0__Ip='192.168.1.42' \
LocalDevices__Shelly__0__Channel=0 \
python3 tools/live-mit-hausdaten-starten.py
```

Die Konfiguration wird beim Start des Backends übernommen. Läuft die Test-App bereits, sie vorher im zugehörigen Terminal beenden. Für weitere Geräte die laufende Nummer im Konfigurationsnamen erhöhen. Die Ansicht **Direkt verbundene WLAN-Zähler** erscheint bei konfigurierten Geräten. Geräte, deren lokale RPC-Schnittstelle eine Anmeldung verlangt, zeigen diesen Status an. Diese Version unterstützt den lesenden Zugriff ohne RPC-Anmeldung und sendet keine Schaltbefehle.

Home Control fragt ausschließlich hinterlegte private IP-Adressen ab. Die Geräte-IP wird nicht an die Browseransicht übertragen. Der Shelly-Stand ist der vom Gerät gemeldete Zählerwert in kWh, auch nach einem Zählerreset. Werte ohne Messdaten bleiben leer. In der Demo erfolgt kein Geräteabruf.

## Bluetooth: BLE-Klimasensoren unter Linux

Unterstützt werden Geräte mit dem standardisierten BLE-Klimadienst **Environmental Sensing** und lesbaren Merkmalen für Temperatur oder relative Luftfeuchte. Bluetooth und BlueZ müssen am Linux-PC bereitstehen. Falls das Gerät eine Kopplung verlangt, erfolgt diese in den Linux-Bluetooth-Einstellungen.

Im bestehenden gemeinsamen Projektordner:

```bash
python3 -m venv "$HOME/.local/share/home-control/ble-venv"
"$HOME/.local/share/home-control/ble-venv/bin/python" -m pip install bleak
"$HOME/.local/share/home-control/ble-venv/bin/python" integrations/bluetooth/klima_bridge.py --scan
```

Die Adresse des eigenen Sensors aus dem Scan einsetzen. Beispiel für die lokale App auf Port 5181:

```bash
"$HOME/.local/share/home-control/ble-venv/bin/python" integrations/bluetooth/klima_bridge.py \
  --address AA:BB:CC:DD:EE:FF --name 'Klimasensor Werkbank'
```

Die Bridge läuft in diesem Terminal und nutzt den vorhandenen lokalen Sensor-Bridge-Schlüssel. Nach der ersten Übertragung das Gerät unter **Klima → Verbundene Klimasensoren** einem Raum zuordnen. Mit `--once` lässt sich ein einzelner Versuch ausführen. Der BLE-Prozess benötigt keinen Android-Gateway. Die bestehenden Google-Home-Sensoren laufen weiterhin über ihren eigenen Gateway.

Tuya-Bluetooth-Schalter, Silvergear-Waagen und Sensoren mit proprietären Datenformaten benötigen jeweils eine passende Integration. Ein Eintrag im Bluetooth-Scan allein belegt keine Unterstützung.

## Technische Quellen

- [Shelly Gen2 Switch-API](https://shelly-api-docs.shelly.cloud/gen2/ComponentsAndServices/Switch/)
- [Bluetooth SIG GATT Specification Supplement](https://btprodspecificationrefs.blob.core.windows.net/gatt-specification-supplement/GATT_Specification_Supplement.pdf)
- [Bleak](https://bleak.readthedocs.io/)
