# Google Home → Home Control: Live-Klima

Dieser Integrationszweig ergänzt den **separaten Wetter-Testworktree** `feature/wetter-himmel`. `main`, der produktive Dienst auf Port 5200 und die Produktivdatenbank werden nicht verändert.

## Installation auf Muju-Linux-PC

```bash
cd ~/Schreibtisch/Umschulung/Home-Control-Wetter
git fetch origin feature/live-google-home-klima
git show FETCH_HEAD:integrations/google-home-bridge/install_live.py > /tmp/home-control-live-install.py
python3 /tmp/home-control-live-install.py
```

**Vorher:** Xiaomi Android-Handy mit USB und aktiviertem USB-Debugging verbinden, falls die APK gleich installiert werden soll. Der Installer sucht nach `c84bca2`. Wenn es nicht angeschlossen ist, bleibt die APK lokal und kann später installiert werden.

**Danach:** Google-Home-Test-App auf dem Handy öffnen und angemeldet lassen. Unter `http://127.0.0.1:5190` → **Klima** erscheinen erkannte Sensoren. Jeden Sensor einmal seinem Raum zuordnen. Die Zuordnung und die echten Messwerte bleiben in der separaten Klima-SQLite-Datenbank gespeichert.

## Architektur

- Android-Debug-Build, Google Home APIs: `TemperatureMeasurement`, `RelativeHumidityMeasurement`, alle 60 Sekunden solange die App läuft.
- Gateway `0.0.0.0:5390`: Nur `POST /api/climate/bridge/readings`, Beschränkung auf private IPs.
- .NET Backend `127.0.0.1:5290`: Erfordert in jedem Bridge-POST einen geheimen `X-Sensor-Key` mit zeitkonstantem Vergleich, validiert Werte.
- Klimadatenbank: separate `ClimateSensors`-Tabelle; Messwerte werden bei Zuordnung automatisch in `RoomClimate` übertragen.
- Die eigentliche C#-API, Benutzerverwaltung und Tuya-Steuerung bleiben ausschließlich auf Loopback.

Schlüssel: `~/.config/home-control/sensor-bridge.key`, Rechte 0600. Android-`local.properties` enthält nur die lokale Debug-Konfiguration, **nicht committen**.

## Tests

```bash
curl -i http://127.0.0.1:5390/healthz
curl -i -X POST http://127.0.0.1:5390/api/climate/bridge/readings
tail -30 /tmp/home-control-wetter-api.log
tail -30 /tmp/home-control-sensor-gateway.log
adb -s c84bca2 logcat -d -s HomeControlBridge:V
```

Erwartet: `healthz = 200`, unauthentifizierter POST = `401`. Die Android-Logs zeigen, ob Sensorwerte gesendet wurden.

**Einschränkungen:** Kein garantierter Android-Hintergrundbetrieb: Android kann die Test-App im Hintergrund stoppen. Die API-Zeitstempel bedeuten **zuletzt übertragen**, nicht sicher den Zeitpunkt der tatsächlichen Messung. Für Dauerbetrieb braucht es später eine verlässliche Bridge mit dauerhaftem Prozess und HTTPS statt Debug-HTTP. Die automatische Tuya-Steuerung braucht verknüpfte Geräte mit funktionsfähigen Tuya-Zugangsdaten.

## Wiederherstellung

Das Installationsskript sichert geänderte Quelldateien unter `~/.local/share/home-control/bridge-backups/<Zeitstempel>/`. Bei Build-Fehlern werden veränderte Quelldateien zurückgesetzt. Die separate Klima-Datenbank wird nicht gelöscht.
