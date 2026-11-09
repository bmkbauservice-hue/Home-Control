# Sensoren mit der vorhandenen Android-Test-App verbinden

Diese Anleitung beschreibt den USB-Weg für Entwicklung und Fehlersuche. Die direkte Verbindung
per WLAN und privatem OVH-Gateway steht in [Google Home schalten](GOOGLE_HOME_SCHALTEN.md).
Das Projekthandbuch und die [Übergabe](UEBERGABE.md) halten die bestätigten Tests und offenen
Abnahmeschritte fest.

## Einbau auf dem Linux-PC

Die vorhandene gemeinsame Testversion auf 5181 muss laufen. Das entsperrte Android-Handy
per USB verbinden und USB-Debugging erlauben. Ein eindeutig erkanntes USB-Handy wird automatisch
ausgewählt. Zusätzliche Netzwerkgeräte wie eine Mi Box bleiben verbunden und werden nicht benutzt.
Bei mehreren USB-Geräten das Handy mit `--device SERIENNUMMER` ausdrücklich auswählen;
alle ADB-Aufrufe verwenden anschließend genau dieses Ziel. Offline- oder nicht freigegebene
Geräte werden abgelehnt.

Im Ordner `Home-Control-Gemeinsam` zunächst nur prüfen:

```bash
python3 tools/android-bridge-einrichten.py
```

Dann einrichten und starten:

```bash
python3 tools/android-bridge-einrichten.py --install
```

Das Skript prüft alle Quellcode-Anker, bevor es Dateien verändert. Abweichende Anker,
eine eigene Bridge oder eigene Netzwerkregeln führen zum Abbruch. Es ergänzt die
vorhandene App, verändert weder OAuth-Konfiguration noch Google-Konten und legt eine
Sicherung der betroffenen Dateien unter `~/.local/share/HomeControl/android-sicherungen/` an.

Der vorhandene Schlüssel unter `~/.config/home-control/sensor-bridge.key` bleibt erhalten.
Fehlt er, wird ein neuer Schlüssel mit Dateirechten 0600 erzeugt. Er wird lokal in die .NET
User Secrets eingetragen und für den Android-Build nur per Umgebung übergeben.
Der Schlüssel steht in den lokalen Build-Ergebnissen und in der installierten Test-APK;
APK und Build-Ordner daher nicht weitergeben oder ins Repository aufnehmen.

Der Verbindungscheck sendet ausschließlich ein absichtlich ungültiges Paket ohne Temperatur
oder Luftfeuchte. Das prüft Schlüssel und Schnittstelle, legt aber keinen Messwert an.
Übernimmt das Backend die Konfiguration noch nicht, gibt das Skript einen Hinweis zum
Neustart der gemeinsamen Testversion aus. Es beendet keinen vorhandenen Dienst.

Danach baut Gradle die App und ADB aktualisiert sie mit `install -r`. Bei einem Fehler wird
nicht deinstalliert oder das Gerät zurückgesetzt. Die Sicherung bleibt verfügbar.

## Datenweg für den ersten Hardwaretest

Android-Test-App → USB-Weiterleitung auf 5390 → lokaler Python-Gateway → Backend auf 5181.

Der Gateway bindet bei diesem Test ausschließlich an 127.0.0.1. HTTP ist im Debug-Build nur
für localhost/127.0.0.1 zugelassen. Die Test-App muss im Vordergrund und das USB-Kabel verbunden
bleiben. Das ist ein überprüfbarer erster Test, noch kein Hintergrundbetrieb oder LAN-Dauerdienst.

Die Bridge greift auf echte Temperatur- und Feuchte-Traits der online gemeldeten Sensoren zu.
Fehlende Werte bleiben null. Sie sendet bei SDK-Geräteereignissen, ohne erfundene Messungen oder
regelmäßiges Wiederholen alter Werte. Zeitstempel im Backend sind Empfangszeiten, kein Beweis
für den Zeitpunkt der physischen Messung. Bei unverändertem SDK-Zustand kann die Anzeige altern.

Die Klima-Seite öffnen und nach dem ersten Empfang die vier Sensoren den vier Räumen zuordnen.
Die Seite zeigt Temperaturkarten nur für Räume mit Messwerten oder Sensorzuordnung.
Flur und Küche werden nicht als zusätzliche Sensoren angelegt.

## Rückkehr zum bisherigen Android-Quellcode

Die Sicherung enthält die Originaldateien und `dateien.json`. Dort ist auch vermerkt,
welche Dateien neu angelegt wurden. Die Originaldateien zurückkopieren und ausschließlich
die im Sicherungsverzeichnis als neu markierten Dateien entfernen. Anschließend die App
wie zuvor bauen. Google Home selbst und die Sensoren werden vom Einbau nicht geändert.

## Docker und Blink im selben Projekt

Dockerfile und Compose sind bereits vorhanden. Die echte Klima-SQLite liegt nun explizit
unter `/data/homecontrol-climate.db` im vorhandenen Daten-Volume. Die Demo verwendet eine
eigene Klima-Datei unter `/data/homecontrol-climate-demo.db` im tmpfs.

Der USB-Installer arbeitet zunächst mit dem Host-Backend auf 5181. Der Weiterleitungsweg in
den Docker-Container muss separat eingerichtet und geprüft werden: Der Klima-Empfang im
Container nimmt absichtlich nur Loopback an. Portweiterleitung allein genügt dafür nicht.
Docker-Build und Volume-Persistenz der neuen Konfiguration sind noch lokal zu prüfen.

Blink ist vollständig im vorhandenen Backend und Frontend vorhanden, bleibt im Test aber
aus. Vor der Übernahme prüfen, welche Instanz aktuell Blink nutzt, und deren aktuellen
Token sowie Data-Protection-Schlüssel erhalten. Keine zweite Instanz mit derselben
Blink-Identität aktivieren. Siehe [BLINK.md](BLINK.md) und [DOCKER.md](DOCKER.md).

## Quellen und Lernunterlage

- [Google: Datenmodell und Traits](https://developers.home.google.com/apis/android/data-model)
- [Google: Device API](https://developers.home.google.com/apis/android/device)
- [Google: Temperatur](https://developers.home.google.com/reference/kotlin/com/google/home/matter/standard/TemperatureMeasurementTrait.Attributes)
- [Google: Luftfeuchte](https://developers.home.google.com/reference/kotlin/com/google/home/matter/standard/RelativeHumidityMeasurementTrait.Attributes)
- [Lernunterlage](Lernunterlage-Android-Sensor-Bridge.pdf)
