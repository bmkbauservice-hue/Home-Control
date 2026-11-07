# Docker

**Zum Lernen:** [Lernunterlage-Docker.pdf](Lernunterlage-Docker.pdf)

Home Control lässt sich als **Container** starten, z. B. auf einem anderen Rechner oder später auf einem Server.
Der Dienst auf dem PC (`./dienst-einrichten.sh`, Port 5200) bleibt davon **unberührt**: Docker ist eine
zusätzliche Möglichkeit, kein Ersatz.

| Datei | Aufgabe |
|---|---|
| `Dockerfile` | Bauplan des Images in drei Stufen: Oberfläche bauen (Node), API veröffentlichen (.NET SDK), schlankes Laufzeit-Image (aspnet) |
| `.dockerignore` | Was beim Bauen nicht in den Container kommt: `.env`, Datenbanken, `bin/`, `obj/`, `node_modules/`, `.git/`, Doku |
| `docker-compose.yml` | Zwei Dienste: `homecontrol` (Profil `betrieb`) und `demo` (Profil `demo`), Volumes, Ports |
| `.env.example` | Vorlage für die Geheimnisse, **ohne Werte**. Die echte `.env` steht in `.gitignore` und `.dockerignore`. |

## Demo im Container

Braucht keine `.env` und keine Geheimnisse:

```bash
docker compose --profile demo up
```

Dann `http://localhost:5300` öffnen, Konto `demo@example.de`, Passwort `Demo-Haus-2026` (änderbar mit
`DEMO_PASSWORT=…`). Datenbank und Schlüssel liegen nur im Arbeitsspeicher (`tmpfs`): Jeder Start beginnt
frisch. Beenden mit Strg+C, aufräumen mit `docker compose --profile demo down`.

## Betrieb im Container

```bash
cp .env.example .env          # dann .env öffnen und die Werte eintragen
docker compose up -d          # baut beim ersten Mal das Image und startet im Hintergrund
docker compose ps             # Zustand, nach etwa 20 s "healthy"
docker compose logs -f        # Logs ansehen
```

Danach `http://localhost:5400` öffnen. Beim allerersten Start ist die Datenbank leer: Dort das erste Konto
anlegen (wird Admin). Pflicht in `.env` ist nur `Jwt__SigningKey` (mindestens 32 Zeichen). Ohne ihn startet die
App absichtlich nicht.

| Aufgabe | Befehl |
|---|---|
| Update nach `git pull` | `docker compose build` und `docker compose up -d` (Daten bleiben im Volume) |
| Commit in der Info anzeigen | `GIT_COMMIT=<kurzer Commit> docker compose build` |
| Stoppen | `docker compose down` (Volumes bleiben) |
| Alles löschen, auch die Daten | `docker compose down -v` (**Vorsicht:** Datenbank weg) |
| Sicherung der Datenbank | `docker compose stop`, dann `docker compose cp homecontrol:/data/homecontrol.db ./homecontrol-sicherung.db`, dann `docker compose start`. Gestoppt, damit SQLite alles aus dem `-wal` in die Datei geschrieben hat. Die Sicherung enthält echte Daten: nicht ins Repo (`*.db` steht in `.gitignore`). |

## Wie das Image gebaut ist

```text
Stufe 1  node:22-alpine            npm ci, npm run build           → frontend/dist
Stufe 2  dotnet/sdk:10.0           dotnet restore, dotnet publish  → /app
Stufe 3  dotnet/aspnet:10.0        nur /app + wwwroot              → fertiges Image (409 MB, davon 342 MB aspnet)
```

- **Multi-Stage:** Nur die letzte Stufe wird zum Image. Node, das .NET SDK und der Quellcode bleiben in den
  Bau-Stufen zurück. Das Image ist kleiner und enthält keine Werkzeuge, die ein Angreifer nutzen könnte.
- **Layer und Cache:** Jede Zeile im Dockerfile ist eine Schicht. Erst werden nur `package.json` bzw. die
  `.csproj` kopiert und die Pakete geholt, dann der restliche Code. Ändert sich nur Code, nimmt Docker die
  Paket-Schicht aus dem Cache, und der Build geht schnell.
- **Nicht als root:** `USER $APP_UID` (Benutzer `app`, UID 1654, vom aspnet-Image mitgebracht). Port 8080
  liegt über 1024, dafür braucht man kein root. `/data` und `/keys` gehören `app`.
- **Geheimnisse nie im Image:** Kein `COPY .env`, keine Secrets in `ENV`. Sie kommen erst beim Start über
  `env_file: .env` in den Container. Ein Image kann man weitergeben, ohne etwas zu verraten.
- **Healthcheck:** Das aspnet-Image hat kein `curl`. Deshalb prüft sich die App selbst:
  `dotnet HomeControl.Api.dll --healthcheck` fragt `GET /healthz` (App läuft, Datenbank erreichbar) und endet
  mit 0 oder 1. `/healthz` braucht keine Anmeldung und verrät nur „Healthy“ oder „Unhealthy“.
- **Commit:** Im Container gibt es kein `.git`. Der Build-Parameter `GIT_COMMIT` wird zu
  `-p:SourceRevisionId=…`, daraus macht das SDK `1.0.0+<Commit>`.

## Volumes

Seit der Klima-Integration liegt zusätzlich `homecontrol-climate.db` unter `/data`.
`Climate__DatabasePath` ist im Dockerfile und in der Konfigurationsvorlage gesetzt.
Die Demo überschreibt den Pfad mit `/data/homecontrol-climate-demo.db` im tmpfs.
Bei einer Sicherung müssen Haus- und Klima-Datenbank erhalten werden.
Der bestätigte USB-Sensor-Test läuft gegen das Host-Backend auf 5181.
Der optionale Compose-Dienst `sensor-gateway` leitet intern über Loopback weiter (Profil `sensoren`,
Host-Port 5391). Ablauf, Datensicherung und Grenzen: [TESTDATEN_UND_CONTAINER.md](TESTDATEN_UND_CONTAINER.md).

| Volume | Im Container | Inhalt |
|---|---|---|
| `homecontrol-daten` | `/data` | `homecontrol.db` (SQLite) |
| `homecontrol-schluessel` | `/keys` | Data-Protection-Schlüssel (`HomeControl:DataProtectionKeysPath`) |

Ohne Volume wären die Daten beim nächsten `docker compose up` nach einem neuen Build weg, denn ein Container
ist zum Wegwerfen gedacht. Die Schlüssel brauchen ein eigenes Volume: Ohne sie ließe sich das gespeicherte
Blink-Token nach einem Update nicht mehr entschlüsseln.

## Umzug vom PC-Dienst in den Container (später)

1. **Blink:** Blink erlaubt nur **eine** Stelle, die das Token erneuert. Deshalb steht in `.env.example`
   `Blink__Enabled=false`. Erst einschalten, wenn der PC-Dienst gestoppt ist
   (`systemctl --user stop home-control`), sonst melden sich beide gegenseitig ab.
2. **Tuya:** Zwei laufende Instanzen verbrauchen doppelt Kontingent.
3. Datenbank übernehmen: die `homecontrol.db` in das Volume kopieren, **und** die Data-Protection-Schlüssel
   (`~/.local/share/HomeControl/dataprotection-keys`) nach `/keys`. Sonst ist das Blink-Token unlesbar und
   BlinkSpike muss neu laufen.

## Grenzen

- **HTTPS** macht der Container nicht selbst. Das Refresh-Cookie ist im Production-Modus `Secure`: Über
  `http://localhost` geht das (Browser behandeln localhost als sicher), von anderen Geräten nur über HTTPS,
  z. B. `tailscale serve` vor Port 5400.
- **Rate Limiting pro Gerät:** Hinter der Port-Weiterleitung von Docker sieht die App bei Zugriffen über
  `localhost` die Adresse des Docker-Netzes, nicht die des Geräts. Alle lokalen Zugriffe teilen sich dann einen
  Zähler.
- Ports nur auf `127.0.0.1`. Von außen ist der Container nicht direkt erreichbar.


