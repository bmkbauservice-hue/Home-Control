# Home Control: gemeinsamer Stand mit Live-Wetter, Tuya und Klima

Stand: 06.11.2026 · Ergänzung zu [WETTER_UND_SCHALTER.md](WETTER_UND_SCHALTER.md)

## Warum „Klima“ gefehlt hat

Der neue Wetter-/Tuya-Branch hatte weder Klima-Seite noch Klima-API.
Die ursprüngliche Google-Home-Bridge lag nur als Integrationsdatei und
als Patch für den lokalen `Home-Control-Wetter`-Worktree auf einem
divergierten Feature-Branch. Ein blindes Merge hätte das Problem nicht gelöst.

Dieser Integrationszweig ergänzt **dieselbe App** um:

- Menüpunkt `Klima` und die Raumzuordnung der erkannten Sensoren
- Klima-Messwerte auf den Raumkarten und auf der Klima-Seite
- Markierung von Messwerten, deren letzter Empfang über 20 Minuten zurückliegt
- `GET /api/climate`, `GET /api/climate/bridge/status`,
  `GET /api/climate/bridge/sensors`,
  `PUT /api/climate/bridge/sensors/{sensorId}/room` (Admin)
  und `POST /api/climate/bridge/readings` (Bridge-Schlüssel)
- den vorhandenen Android-Bridge-Quellcode und einen konfigurierbaren WLAN-Gateway

**Keine fingierten Messwerte.** Open-Meteo bleibt ausschließlich das Außenwetter.

## Datenbanken

Hausdaten (`Rooms`, `Devices` usw.) bleiben in der existierenden
`homecontrol.db`. Sensor-Messwerte liegen separat in der konfigurierbaren
Klima-Datenbank (`Climate__DatabasePath`). Ohne Konfiguration lautet der
Pfad `~/.local/share/HomeControl/homecontrol-climate.db`.

Beim Start werden die erforderlichen Klima-Tabellen **additiv** angelegt,
bestehende Hausmigrationen werden nicht verändert.

Für eine **isolierte Testversion** dient
`tools/live-mit-hausdaten-starten.py`. Es kopiert die vorhandene Haus-SQLite
und sucht außerdem nach einer bestehenden Klima-SQLite in den bisherigen
Worktrees. Jede gefundene Datenbank wird mit der SQLite-Backup-API separat
kopiert. Es wird nicht auf die produktiven Dateien geschrieben. Falls keine
Sensorwerte vorhanden sind, bleibt die Klima-Anzeige ausdrücklich leer.

## Android-Sensor-Bridge / Gateway

Der ursprüngliche Android-Kotlin-Bridge-Quellcode liegt unter
`integrations/google-home-bridge/HomeControlBridge.kt`. Er muss in die
bereits installierte Google-Home-Test-App integriert und auf die
richtige LAN-Adresse des Gateways konfiguriert sein.

Das Gateway läuft auf dem Linux-PC und leitet nur authentifizierte JSON-
Messwerte aus dem privaten LAN zum lokalen Backend weiter. Konfiguration:

```bash
HOME_CONTROL_SENSOR_BACKEND=http://127.0.0.1:5181 \
HOME_CONTROL_SENSOR_PORT=5390 \
python3 integrations/google-home-bridge/sensor_gateway.py
```

**Wichtig:** Erst prüfen, ob Port 5390 schon durch den bisherigen Gateway
belegt ist; niemals einen unbekannten Prozess ohne Prüfung beenden.

Der gemeinsame geheime Schlüssel kommt lokal aus
`~/.config/home-control/sensor-bridge.key` und wird durch das Test-Startskript
in die Backend-Umgebung übernommen, falls er existiert. Er darf nie ins Repo,
in den Chat oder in Logs geschrieben werden.

`POST /api/climate/bridge/readings` nimmt nur von Loopback und nur bei
korrektem `X-Sensor-Key` Werte an. Der Gateway leitet genau dorthin weiter.

**Test ohne private Daten preiszugeben:**

```bash
curl -i http://127.0.0.1:5181/healthz
curl -i http://127.0.0.1:5390/healthz
```

Die Klima-Seite zeigt Sensoranzahl, Anzahl zugeordneter Sensoren, letzten
Empfang und den Zustand `aktuell/veraltet`. Die Datenübertragung ist nur
so zuverlässig wie die Android-App: Im Hintergrund kann Android sie stoppen.

## Noch manuell vor Ort zu verifizieren

1. Lokale Worktrees, aktives Backend und alle benutzten Datenbanken erfassen.
2. Android-Test-App öffnen, Gateway-Port und Schlüssel kontrollieren.
3. Prüfen, dass reale Werte samt Zeitstempel ankommen und einem Raum zugeordnet sind.
4. Mit einer **unkritischen Lampe** einen Tuya-EIN/AUS-Test mit Readback durchführen.
5. Bei erfolgreichem Test die PRs integrieren und erst dann die
   produktive Anwendung mit Sicherung und Rückfallmöglichkeit aktualisieren.

**Kein automatisches Deployment aus GitHub** und keine Umstellung
des laufenden Dienstes auf Port 5200 durch diesen PR.
