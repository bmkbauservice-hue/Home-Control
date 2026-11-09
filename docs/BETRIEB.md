# Betrieb auf OVH, Linux-PC und iPhone

Stand 09.11.2026. Die Web-App läuft auf OVH als Docker-Dienst. Tailscale stellt den privaten
HTTPS-Zugang bereit. Das [Projekthandbuch](Home-Control-Projektabschluss.pdf) erklärt Aufbau und Bedienung.
Die [Lernunterlage zum Linux-Dauerdienst](Lernunterlage-Betrieb.pdf) beschreibt den früheren lokalen Betrieb.

## Was wo läuft

| Teil | Laufzeit | Abhängigkeit |
|---|---|---|
| Home Control und Web-App | OVH mit Docker Compose | Browser oder iPhone mit Tailscale |
| Tuya und Wetter | OVH spricht direkt mit der jeweiligen Cloud | Konfiguration und erreichbare Anbieter |
| Google Home | Muju Home auf Android und privater Sensor-Gateway auf OVH | WLAN, Tailscale und geöffnete Android-App beim direkten Weg. Der PC ist nur beim älteren USB-Weg Teil der Verbindung. |
| Blink | OVH mit einem auf Blink begrenzten HTTP-Proxy | Linux-PC mit Tailscale und laufendem Heimproxy |
| n8n | Kann die API mit einem lesenden Schlüssel ansprechen | Leseschlüssel und geprüfte Verbindung. Am echten OVH-Agenten noch offen. |

## OVH prüfen und aktualisieren

Im Bereitstellungsverzeichnis die dort verwendete Compose-Konfiguration beibehalten. In dieser
Installation ist `compose.ovh.yml` die lokale Datei für das aktive Image. `.env` enthält die
privaten Einstellungen. Beide Dateien bleiben auf dem Server. Der Projektname bestimmt die
vorhandenen Volumes und muss bei Updates gleich bleiben.

```bash
docker compose -p homecontrol-ovh -f docker-compose.yml -f compose.ovh.yml \
  --profile betrieb --profile sensoren ps
curl -fsS http://127.0.0.1:5400/healthz
curl -fsS http://127.0.0.1:5391/healthz
```

1. Vor einem Update Hausdaten, Klimadaten, Einstellungen und Data-Protection-Schlüssel sichern.
2. Das geprüfte Image bauen und den lokalen Compose-Override auf dieses Image setzen. Mit demselben Projekt und beiden Profilen starten.
3. Die beiden Gesundheitsprüfungen und danach Anmeldung, Tuya, Klima, Google-Home-Schalter und Blink prüfen. Ein GitHub-Merge ändert den laufenden Container erst beim anschließenden Deployment.

Siehe [Docker](DOCKER.md) für Volumes und Wiederherstellung. Ein Update soll dieselben Volumes verwenden.
`docker compose down -v` löscht sie.

## Zugriff vom iPhone

Tailscale verbinden und die eigene Home-Control-Adresse in Safari öffnen. Unter **Teilen → Zum
Home-Bildschirm** lässt sich Home Control als Web-App ablegen. Die PWA speichert Dateien der Oberfläche
zwischen. API-Daten werden live abgefragt.

Der Sensor-Gateway verwendet einen getrennten privaten HTTPS-Port. Die Freigabe erfolgt mit
`tailscale serve` im Tailnet. Die Bridge-API prüft zusätzlich ihren eigenen Schlüssel.
Ein Aufruf von `/healthz` bestätigt die Erreichbarkeit des Gateways. Ein neuer Empfang unter **Klima**
bestätigt Daten vom Android-Handy. Die Stehlampe per Mobilfunk bei ausgeschaltetem PC zu schalten ist
der Endtest für den direkten Weg. [Einrichtung der Android-App](GOOGLE_HOME_SCHALTEN.md)

## Blink auf OVH

Beim Umzug die Datenbank und die Data-Protection-Schlüssel gemeinsam übernehmen. Nur die aktive Instanz
darf das rotierende Blink-Token erneuern. Der Linux-Dauerdienst wurde für Blink deaktiviert.

Beim getesteten Netzwerkweg lieferte Blink für dieselbe OAuth-Anfrage mit Platzhalter von OVH
HTTP 406 und vom Heimanschluss HTTP 401. Die OVH-Anbindung war über den Heimproxy erfolgreich.
`Blink__ProxyUrl` konfiguriert ausschließlich den Blink-HTTP-Client. Der Tinyproxy-Heimdienst
lauscht auf der privaten Tailscale-Adresse, akzeptiert nur den vorgesehenen VPS und filtert die Blink-Ziele.
Wenn der Linux-PC ausgeschaltet ist, kann OVH diesen Proxy nicht erreichen. Die Blink-Oberfläche
unterscheidet Standbilder, gespeicherte Clips und die Livebild-Probe. [Blink](BLINK.md) · [Alarm](ALARM.md)

## Lokaler Linux-Dauerdienst

`./dienst-einrichten.sh` veröffentlicht die App als Benutzer-Systemdienst auf
`127.0.0.1:5200`. Der Entwicklungsstart mit `./app-starten.sh` verwendet Port 5111.
Für echte Zugangsdaten unter Linux nutzt der Dienst die .NET User Secrets. Beim Docker-Betrieb
werden sie über eine geschützte Laufzeitkonfiguration bereitgestellt. Für die isolierte
Schulvorführung eignet sich [die Demo](DEMO.md).

## Lesender Agent

Für einen Agenten kann ein eigener Schlüssel über `Agent__ApiKey` auf dem Server eingerichtet werden.
In n8n den Wert im Credential-Speicher als Header `X-Api-Key` hinterlegen. Mit einer lesenden Abfrage
wie `GET /api/status` prüfen. Der Schlüssel ermöglicht keine Admin-Aktionen. Der Leseschutz bleibt aktiv.
