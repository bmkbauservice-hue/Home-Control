# Übergabe für Home Control

Stand 09.11.2026. Das [Projekthandbuch](Home-Control-Projektabschluss.pdf) bündelt Bedienung,
Architektur, Fehlersuche und Schulvortrag. Die einzelnen Lernunterlagen bleiben im [README](../README.md) verlinkt.

## Arbeitsstand

| Bereich | Stand | Details |
|---|---|---|
| OVH | Home Control und Sensor-Gateway laufen in Docker. Der Zugriff erfolgt privat über Tailscale HTTPS. | [Betrieb](BETRIEB.md) |
| Tuya | Anbindung auf OVH vom Nutzer bestätigt. Verknüpfte, unterstützte Geräte können Admins mit Rückbestätigung schalten. | [Tuya](TUYA.md), [Wetter und Schalter](WETTER_UND_SCHALTER.md) |
| Raumklima | Live-Klima auf OVH mit der bisherigen Bridge bestätigt. Die direkte Android-Verbindung ist installiert. Der Empfang ohne USB und der Schaltversuch mit ausgeschaltetem PC stehen als Abnahmetest aus. | [Google Home](GOOGLE_HOME_SCHALTEN.md) |
| Blink | Sichtbare Kameras auf OVH bestätigt. Die HTTP-Anfragen laufen über einen privaten Proxy am Heimanschluss. OVH führt das rotierende Blink-Token. | [Blink](BLINK.md), [Betrieb](BETRIEB.md) |
| Alarm | Scharfschalten beim Öffnen der Tür wurde am echten System bestätigt. Ein USB-Clip wurde mit Wiedergabe und Spulen geprüft. Hörbarer Ton und Live-Wiedergabe im Browser bleiben offen. | [Szenarien](SZENARIEN.md), [Alarm](ALARM.md) |
| Strom und Akkus | Watt und geschätzte kWh aus verfügbaren Messwerten. Shelly-Zählerstände separat. Akkustände nur bei gemeldetem Wert. Die PC-Steckdose wird nie geschaltet. | [Direkte Geräte](DIREKTE_GERAETE.md), [Tuya](TUYA.md) |
| n8n | Lesender API-Zugriff ist implementiert. Der Zugriff des echten Agenten auf die OVH-Instanz wurde nicht geprüft. | [Betrieb](BETRIEB.md), [Authentifizierung](AUTHENTIFIZIERUNG.md) |

## Weiterarbeiten

```bash
./pruefen.sh
./app-starten.sh
docker compose --profile demo up demo
```

- Lokal entwickeln und mit der getrennten Demo vorführen. Vor Änderungen an echten Daten die aktive Instanz bestimmen.
- Haus- und Klima-Datenbanken und Data-Protection-Schlüssel sichern. Bei einem Blink-Umzug zuerst den einzigen aktiven Token-Halter bestimmen.
- Änderungen über Branch und Pull Request veröffentlichen. Der OVH-Container wird gesondert aktualisiert.
- Hardwaretests und automatische Tests getrennt dokumentieren. Bekannte Grenzen und Sicherheitshinweise stehen in [CLAUDE.md](../CLAUDE.md) und [REVIEW.md](../REVIEW.md).
