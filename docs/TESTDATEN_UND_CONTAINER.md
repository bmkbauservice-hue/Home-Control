# Testdaten erhalten und Docker vorbereiten

Arbeitsordner bleibt `Home-Control-Gemeinsam`. Kein weiterer Worktree ist nötig.
Vier Sensoren wurden am 06.11.2026 vom Nutzer im USB-Test bestätigt:
Bad, Gästezimmer, Schlafzimmer und Wohnzimmer. Flur und Küche haben keine Klimasensoren.
Bad Licht, Barlicht und Licht Wohnzimmer melden sich in beiden Tuya-Cloud-Abfragen offline.
Die Tuya-App bietet deren Reparatur an; der Nutzer koppelt sie später selbst neu.

## Den laufenden Test sichern

Solange die bisherige Test-API noch läuft, im gemeinsamen Projektordner:

```bash
python3 tools/live-mit-hausdaten-starten.py --nur-sichern
```

Das Skript sucht ausschließlich einen eigenen Prozess mit Arbeitsordner
`Home-Control-Gemeinsam/backend/HomeControl.Api` und Testadresse `127.0.0.1:5181`.
Es liest nur die benötigten Datenbankpfade aus dessen Umgebung. Geheimnisse werden nicht ausgegeben.
Beide Datenbanken müssen innerhalb von `~/.local/share/home-control/test-snapshots` liegen.
Andere Arbeitsordner, der Dauerdienst und Datenbankpfade außerhalb dieses Ordners werden nicht übernommen.

Die laufenden Testdateien werden in `session.json` vorgemerkt und mit der SQLite-Backup-API
unter `test-snapshots/sicherungen` getrennt gesichert, einschließlich WAL-Inhalt.
Jede Datei ist für sich konsistent; es gibt keine gemeinsame Transaktion über beide Datenbanken.
Der Prozess läuft weiter, die Originaldateien werden nicht beschrieben.
Die Sicherung enthält private Hausdaten und muss außerhalb des Repos bleiben.

Beim späteren normalen Aufruf setzt das Skript dieselbe Sitzung fort:

```bash
python3 tools/live-mit-hausdaten-starten.py
```

Eine noch laufende Test-API wird nur gesichert, kein zweites Backend gestartet.
Ohne laufenden Prozess und ohne Manifest werden ältere Testkopien nicht automatisch ausgewählt.
Bei einer defekten oder fremden Manifest-Referenz hält das Skript an.
`--neu` importiert bewusst neue Kopien aus den anderen Worktrees; vorhandene Sitzungsdaten werden vorher gesichert.
Nicht für einen normalen Neustart verwenden: Der neue Import enthält die nachträglichen Test-Zuordnungen nicht.

## USB-Sensoren im Container

Compose enthält optional den Dienst `sensor-gateway`. Er teilt den Netzwerkraum der API,
damit die Weiterleitung intern über `127.0.0.1:8080` erfolgt. Die Loopback-Prüfung der API bleibt erhalten.
Der Host veröffentlicht den Sensorzugang ausschließlich lokal auf Port 5391, die Anwendung auf 5400.
Der bisherige Host-Gateway auf 5390 bleibt davon unabhängig.

In einer lokal ausgefüllten `.env` setzen:

```text
COMPOSE_PROFILES=betrieb,sensoren
HC_PORT=5400
HC_SENSOR_PORT=5391
Blink__Enabled=false
Tuya__AutoSyncSeconds=0
```

`SensorBridge__Key` muss lokal mit dem Schlüssel der Android-App übereinstimmen, JWT mindestens 32 Zeichen.
Keine Schlüssel in den Chat oder ins Repo kopieren. Anschließend `docker compose up -d --build --wait`.
Das startet zunächst eine eigene Datenbank im Docker-Volume, übernimmt keine Host-Daten automatisch.
Deshalb ist dies noch keine Umstellung der funktionierenden Installation.

Erst beim bewussten Wechsel des Android-Senders zum Container:

```bash
adb -s HANDY_SERIENNUMMER reverse tcp:5390 tcp:5391
```

Der Rückweg zum Host-Test ist `reverse tcp:5390 tcp:5390`.
Die USB-Zuordnung kann immer nur auf ein Ziel zeigen. Der Android-Installer richtet weiterhin den Host-Test ein.
API-Neuerstellung und Gateway müssen gemeinsam über Compose verwaltet werden, nicht einzeln mit `docker restart`.

## Was automatisch geprüft wird

GitHub Actions baut das echte Dockerfile und startet ein zufällig benanntes, getrenntes Compose-Projekt.
Einmalige Testschlüssel und synthetische Klimawerte werden nur in dessen Wegwerf-Volumes verwendet.
Der Test prüft Oberfläche, Anmeldung, falschen/richtigen Sensor-Schlüssel, Raumzuordnung sowie Haus- und
Klima-Daten nach Container-Neuerstellung. Danach entfernt er ausschließlich das eigene Testprojekt.
Das ist kein Hardwaretest und kein Nachweis, dass Host-Daten bereits nach Docker übertragen wurden.

## Blink: vorbereitete Übernahme, noch nicht aktiviert

Kameraseite und Bild-Proxy sind im gemeinsamen Quellcode enthalten. Im Test bleibt Blink aus.
Vor einer Übernahme muss lokal festgestellt werden, welche Instanz aktuell das rotierende Refresh-Token hält.
`GET /api/blink/status` ist dafür keine passive Prüfung: Es kann bereits einen Cloud-Abruf auslösen.

1. Arbeitsordner und tatsächliche Datenbank des laufenden Dauerdienstes feststellen.
2. Vorhandene Data-Protection-Schlüssel unter `~/.local/share/HomeControl/dataprotection-keys` erhalten.
3. Erst bei der bewussten Umstellung den bisherigen tokenführenden Prozess anhalten.
4. Dessen dann aktuelle Hausdatenbank und Schlüssel konsistent in die Ziel-Volumes übernehmen;
   die laufende Test-Klimadatenbank und Raum-IDs müssen dazu passen.
5. `Blink:HardwareId` lokal übernehmen. Alte Bootstrap-Tokens nicht neu einspielen.
6. Genau eine Zielinstanz aktivieren und Kameraabruf prüfen. Bei Fehlern keine parallele Neuanmeldung starten.

Diese Schritte sind noch nicht auf dem Nutzer-PC ausgeführt. Ohne dessen Zugriff ist ein belastbarer
Token- und Datenbankumzug nicht möglich. Der bestehende Dienst wird durch diese Vorbereitung nicht gestoppt.

Quelle zur Compose-Netzwerkfreigabe und Startreihenfolge:
https://docs.docker.com/reference/compose-file/services/
