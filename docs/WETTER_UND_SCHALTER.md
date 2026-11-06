# Live-Wetter und echte Tuya-Schalter

Stand: 06.11.2026 · Entwicklungsbranch: `feature/live-wetter-tuya-schalten-20261106`.

## Live-Wetter

Die Übersicht ruft `GET /api/weather/current` im eigenen Backend auf. Das Backend holt
Wetterdaten von **Open-Meteo**, ohne API-Schlüssel, und hält erfolgreiche Antworten fünf Minuten
im Cache. Dazu gehören Außentemperatur, WMO-Wettercode und Tag/Nacht. Das Frontend zeigt daraus
Sonne, Wolken, Regen, Schnee oder Gewitter. Die Uhrzeit kommt vom Gerät und wird alle 30 Sekunden
aktualisiert, die Wetterdaten alle fünf Minuten und beim Öffnen der App.

**Wichtig:** Das sind aktuelle **Wettermodellwerte**, keine Thermometer-Messungen aus dem eigenen
Garten. Der Messzeitpunkt des Wetterdiensts wird angezeigt. Bei einem Ausfall zeigt die App eine
Fehlermeldung und kennzeichnet gegebenenfalls noch angezeigte, ältere Werte als letzte Daten.

Der Ort wird unter `Weather` in `backend/HomeControl.Api/appsettings.json` festgelegt.
Vorgabe ist Oranienburg; `Weather__Location`, `Weather__Latitude` und
`Weather__Longitude` können per Umgebungsvariablen überschrieben werden.
**Keine privaten Wohnanschriften oder GPS-Koordinaten ins öffentliche Repository stellen.**

## Ein/Aus über Tuya

Das Frontend schickt als angemeldeter **Admin** `POST /api/tuya/devices/{id}/switch`
mit JSON `{"on":true}` oder `{"on":false}`. Der Server prüft danach:

1. Gerät existiert in der eigenen SQLite-Datenbank, ist mit Tuya verknüpft und vom Typ
   `Light`, `Socket` oder `Switch`.
2. Tuya ist eingerichtet, und die **frische Cloud-Geräteliste** meldet das Gerät online.
3. Der aktuelle Tuya-Status enthält den bekannten booleschen Gerätecode `switch_1`.
   Andere Codes werden **nicht geraten**.
4. Der Backend-Client signiert genau **einen** POST-Befehl an
   `/v1.0/iot-03/devices/{device_id}/commands` mit
   `{"commands":[{"code":"switch_1","value":true}]}` (oder `false`).
5. Der Backend-Client liest den Zustand bis zu dreimal erneut. Erst bei bestätigtem
   Zielzustand aktualisiert das Backend die Gerätedatenbank, legt gegebenenfalls das
   übliche Ereignis an und benachrichtigt offene Browser über den Live-Kanal.

Das vermeidet die gefährliche Verwechslung **„Befehl angenommen“** und **„Licht wirklich an“**.
Kann die Cloud den Zielzustand noch nicht bestätigen, meldet das Frontend einen Fehler,
statt einen falschen Zustand zu behaupten. Der automatische Tuya-Abgleich kann später
einen verzögert umgesetzten Zustand nachtragen.

**Schutzregeln:** Nur Admins, Rate Limiting, keine Steuerung im Demo-Modus, kein Schalten
von Tür- oder Temperatursensoren, nicht von nicht verknüpften oder offline gemeldeten Geräten.
Die Zugangsdaten bleiben ausschließlich auf dem Backend.

## Testversion mit den vorhandenen Geräten starten

Die lokale Testkopie hat normalerweise eine **andere, leere SQLite-Datenbank** als
der bereits laufende Home-Control-Dienst. Dann zeigt das Dashboard zwar das Wetter,
aber null Räume und null Geräte. Nicht alle Geräte neu anlegen!

Das Hilfsskript `tools/live-mit-hausdaten-starten.py` sucht die bestehende Hausdatenbank
im üblichen Umschulungs-Ordner, liest nur die Anzahlen der Räume und Geräte und legt
mit der SQLite-Backup-API eine separate, geschützte **Kopie** an. Die Originaldatenbank
wird nicht beschrieben; laufender Dienst, Passwörter und Live-Tuya-Verknüpfungen bleiben
unangetastet. In der Testversion sind alle Räume und Geräte aus der Kopie vorhanden.

```bash
cd ~/Schreibtisch/Umschulung/Home-Control
git fetch origin
git worktree add ../Home-Control-MitGeraeten origin/feature/live-wetter-tuya-schalten-20261106
cd ../Home-Control-MitGeraeten
python3 tools/live-mit-hausdaten-starten.py
```

Die Testversion wird unter **http://127.0.0.1:5181** geöffnet. Auf der Geräteseite
`Mit Tuya abgleichen` drücken, um aktuelle Schalterzustände zu holen. Mit einem
**unkritischen** Licht oder einer Steckdose erstmals EIN/AUS testen. Die Cloud-
Verknüpfung muss bestehen und den Schaltcode `switch_1` unterstützen.
Die Testversion verwendet echte Tuya-Cloud-Zugangsdaten aus den vorhandenen .NET
User Secrets; deshalb bewirkt EIN/AUS auch einen **wirklichen** Schaltversuch.

Blink und automatische Tuya-Abgleiche sind in dieser Testinstanz deaktiviert, um
den produktiven Betrieb nicht durch parallele Hintergrunddienste zu stören. Manuelles
Abgleichen ist möglich. Die Test-SQLite-Dateien liegen mit Benutzerrechten 0600
unter `~/.local/share/home-control/test-snapshots`. Auf dem Bildschirm werden keine
Geräte-IDs, Tokens oder anderen Zugangsdaten ausgegeben.

**Wenn keine befüllte Ursprungsdatenbank gefunden wird:** Das Skript startet nicht
und verändert nichts. Dann zuerst den tatsächlichen Datenbankpfad des laufenden
Dienstes feststellen, nicht irgendwelche Datenbanken vermischen.

## Lokal kontrollieren (Linux Mint)

Das bisherige `Home-Control-Wetter` ist ein **separater lokaler Worktree**. Dieser PR verändert
den laufenden Dienst nicht selbst. Zuerst eine Testkopie im eigenen Worktree anlegen:

```bash
cd ~/Schreibtisch/Umschulung/Home-Control
git fetch origin
git worktree add ../Home-Control-Wettertest origin/feature/live-wetter-tuya-schalten-20261106
cd ../Home-Control-Wettertest
./pruefen.sh
./app-starten.sh
```

Die Startseite der Testkopie liegt bei `http://localhost:5111`, solange kein anderer Prozess
den Port belegt. Der vorhandene produktive Dienst läuft separat auf Port 5200.
Eine Testkopie benutzt außerdem eine **eigene Datenbankdatei**; dort müssen die Tuya-Geräte
gegebenenfalls erneut verknüpft werden. Ein wirkliches Schalten darf erst an einem
unproblematischen Gerät (z. B. einer Lampe) getestet werden, niemals an sicherheitskritischer
Technik. Nach dem Test kann die Funktion per PR nach `main` und anschließend mit
`./dienst-einrichten.sh` in den Dauerdienst übernommen werden.

## Was noch nicht enthalten ist

- **Innenraumtemperaturen** aus Google Home/Xiaomi brauchen eine laufende Sensor-Bridge
  bzw. eine geeignete Geräte-Integration. Dieser Branch erfindet keine Sensorwerte.
- Dimmen, Farbwechsel und Schaltercodes außer `switch_1` sind nicht implementiert.
- Echte Hardwaretests auf dem Linux-Rechner waren bei der GitHub-Bearbeitung nicht möglich.
  Die GitHub-CI prüft Build, Migrationen, Tests und Lint, ersetzt aber keinen Hardwaretest.

Quellen: Open-Meteo Forecast API (`current=temperature_2m,weather_code,is_day`) und
Tuya Cloud „Send commands“ (`POST /v1.0/iot-03/devices/{device_id}/commands`).
