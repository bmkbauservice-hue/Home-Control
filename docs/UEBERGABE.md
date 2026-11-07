# Übergabe: Stand von Home Control und wie es weitergeht

**Stand: 06.11.2026, gemeinsamer Entwicklungsstand in PR #41.** Diese Datei ist der Einstieg für jede neue Arbeitssitzung, egal ob allein,
mit ChatGPT/Codex oder einem anderen KI-Werkzeug. Sie beschreibt, was fertig ist, wie man prüft,
was offen ist und welche Regeln gelten.

**Zum Lernen:** [Lernunterlage-Uebergabe.pdf](Lernunterlage-Uebergabe.pdf)

## Aktueller verbindlicher Arbeitsstand

- Die Energieübersicht zählt Licht, Steckdosen und Schalter. Wattwerte stammen aus bestätigten Online-Abfragen. Die kWh-Schätzung beginnt mit zwei Wattproben und überspringt bekannte Offline-Zeiten und Lücken über 35 Minuten. Im Test auf Port 5181 ist der automatische Tuya-Abgleich aus.
- Ein gemeinsamer lokaler Testordner: `Home-Control-Gemeinsam`, Port 5181. Keine weiteren Home-Control-Worktrees für Teilfunktionen anlegen.
- Nutzer bestätigt: Wetter sichtbar und echte Heizdecke schaltbar. Das bestätigt noch keine anderen Geräte.
- Vier Klimasensoren: Wohnzimmer, Gästezimmer, Schlafzimmer, Bad. Alle vier vom Nutzer im USB-Test bestätigt; Android-Build und Installation erfolgreich. Flur und Küche aus der Klima-Ansicht ausblenden, die Räume selbst erhalten.
- `tools/android-bridge-einrichten.py` ergänzt die vorhandene Android-Test-App nach Quellcode-Prüfung und Sicherung. Android-App im Vordergrund, USB und Gateway-Terminal müssen weiterlaufen.
- ADB-Auswahl berücksichtigt zusätzliche Netzwerkgeräte: ein USB-Handy automatisch, bei mehreren USB-Geräten `--device SERIENNUMMER`. Keine Mi Box trennen und keinen ADB-Server neu starten.
- Klima-Seite zeigt nur Räume mit Zuordnung oder Messwert. Fehlende Messwerte bleiben leer.
- Docker enthält jetzt einen optionalen Sensor-Gateway im Netzwerkraum der API. Neuer CI-Test baut das Image und prüft Gateway und Datenerhaltung nach Container-Neuerstellung. Tatsächlichen CI-Ausgang am neuesten Commit prüfen; auf dem Nutzer-PC noch nicht gestartet.
- Blink ist vorhanden, im Test bewusst aus. Vor dem Übernehmen aktuelle tokenführende Instanz bestimmen und Token/Schlüssel erhalten.
- Anleitung und Grenzen: [ANDROID_SENSOR_BRIDGE.md](ANDROID_SENSOR_BRIDGE.md).
- Nutzer bestätigt inzwischen: Android-App erfolgreich gebaut/installiert und alle vier Klimasensoren in Home Control sichtbar/aktualisiert. Nur USB-Testbetrieb ist damit bestätigt.
- Tuya-Diagnose lokal ausgeführt: Bad Licht, Barlicht, Licht Wohnzimmer in App-Liste UND direkter Geräteinformation offline; `switch_1` vorhanden. Tuya bietet Reparatur dieser drei Geräte an. Nutzer verschiebt Neukopplung ausdrücklich. Kein Online-Schutz umgehen und keine Zuordnung löschen.
- Neue Admin-Seite „Waage“: Gerätezuordnung, Gewichtsfeld bewusst auswählen, alle gemeldeten Cloud-Eigenschaften mit Rohwert, belegter Skalierung/Einheit und eigener Meldezeit. Keine Körperwerte werden lokal gespeichert. Silvergear fehlt bisher in der Cloud-Liste; echte Bluetooth-Daten bleiben unbestätigt. Anleitung: [WAAGE.md](WAAGE.md).
- Teststarter merkt sich nun dieselben Haus-/Klima-Dateien; `--nur-sichern` übernimmt die laufende eigene Test-API ohne Neustart. Zuerst so die aktuellen Zuordnungen sichern. Ohne gültige Sitzung ältere Kopien nicht still ersetzen. Anleitung und Blink-Übernahmegrenzen: [TESTDATEN_UND_CONTAINER.md](TESTDATEN_UND_CONTAINER.md).
- CI auf `7e65ae3b` vollständig erfolgreich: 289 Backend-Tests, 21 Python-Tests, 3 Formatierungstests, Migrationen und Frontend-Build/Lint sowie echter Docker-Bau, Gateway-Schlüsselprüfung und Datenerhaltung nach Neuerstellung. Prüflauf: https://github.com/bmkbauservice-hue/Home-Control/actions/runs/42218272723. Kein Hardwaretest der Waage und keine Docker-/Blink-Umstellung auf dem Nutzer-PC. Browserdarstellung hier wegen Laufzeitbeschränkungen nicht geprüft.
- Validierung: Python-Einbau- und SQLite-Tests lokal. In dieser Bearbeitungsumgebung fehlen .NET SDK, Android SDK und Docker; deshalb sind lokale Gesamtprüfung, Android-Build und Container-Lauf hier nicht nachgewiesen. GitHub-CI separat prüfen.

Die folgenden Tabellen dokumentieren auch frühere Entwicklungsschritte; bei widersprechenden Angaben gilt der obige aktuelle Arbeitsstand.

### Wiedereinstieg nach der Abwesenheit

1. USB-Handy, Android-App und laufendes Gateway-Terminal zunächst weiterlaufen lassen.
2. Im bestehenden `Home-Control-Gemeinsam` den aktuellen PR-41-Stand abrufen und nur per Fast-forward übernehmen. Das startet und stoppt keinen Dienst.
3. Noch vor einem Backend-Neustart `python3 tools/live-mit-hausdaten-starten.py --nur-sichern` aus diesem Ordner ausführen. Nur bei bestätigter Sicherung mit dem Neustart fortfahren. Bei nicht eindeutig erkannter Sitzung keine alte Datenbank auf Verdacht auswählen.
4. Danach die Testversion mit `python3 tools/live-mit-hausdaten-starten.py` fortsetzen; für neue Funktionen ist ein Backend-Neustart nötig. `--neu` ist dafür ausdrücklich nicht gedacht. `app-starten.sh` nicht direkt ohne die Sitzungsumgebung starten.
5. Drei Tuya-Steckdosen bleiben bis zur vom Nutzer gewünschten Neukopplung offen. Silvergear-Cloud-Zugriff und Blink-Übernahme benötigen die tatsächlichen lokalen Verbindungen. Keine zusätzlichen Worktrees und kein paralleles Blink-Login anlegen.

**Neuer Entwurf (PR #40, noch nicht auf `main` oder im Dauerdienst):** Live-Wetter/Open-Meteo,
Uhr und Tuya-EIN/AUS für Admins mit Online-Prüfung und Readback, neue GitHub-CI.
Build/Tests/Migrationen/Frontend/Lint wurden für den bisherigen Stand in CI grün geprüft. Die Heizdecke wurde inzwischen vom Nutzer geschaltet; weitere Hardwaretests sind offen.
Beschreibung: [WETTER_UND_SCHALTER.md](WETTER_UND_SCHALTER.md).

## 1. Was fertig ist

| Bereich | Stand | Doku |
|---|---|---|
| Räume, Geräte, Ereignisse, Reports | fertig, getestet | [API.md](API.md), [DATENMODELL.md](DATENMODELL.md) |
| Anmeldung: JWT, Refresh-Token-Rotation, Reuse Detection, HTTP-only Cookie | fertig, getestet | [AUTHENTIFIZIERUNG.md](AUTHENTIFIZIERUNG.md) |
| ASP.NET Core Identity: Benutzer, Rollen Admin/User, Sperre nach 5 Fehlversuchen | fertig, getestet, Migration `IdentityUmgestellt` | [IDENTITY.md](IDENTITY.md) |
| Rechte: Schreiben nur Admin, Lesen optional nur angemeldet | fertig, getestet | [AUTHENTIFIZIERUNG.md](AUTHENTIFIZIERUNG.md) |
| API-Schlüssel für den KI-Agenten (nur lesen) | fertig, getestet; **Leseschutz im Dienst an** (seit 02.11.), Schlüssel in n8n noch nicht eingetragen | [AUTHENTIFIZIERUNG.md](AUTHENTIFIZIERUNG.md) |
| Tuya: Geräteliste, Status, Abgleich aller verknüpften Geräte (nur lesend) | fertig, getestet; verknüpft: Bar Licht, Heizdecke, Stehlampe, Tür | [TUYA.md](TUYA.md) |
| Tuya-Messwerte: Watt, Volt, Akku (letzter Stand, Offline-Schutz) | fertig, getestet (124 Tests), Migration `TuyaMesswerteErgaenzt` | [TUYA.md](TUYA.md) „Messwerte“ |
| Blink: Status und Kameras (nur lesend, inoffizielle API) | **eingerichtet und verbunden** (02.11.): eigener Client statt Blink.NET (426 bei Blink.NET), 2 Kameras (Wohnzimmer, Flur) | [BLINK.md](BLINK.md) |
| Blink-Vorschaubilder: Proxy-Endpunkt (nur JWT, no-store, 60 s Cache, Rate Limit), Kamerakarten in Übersicht und Blink-Seite; beim Entwickeln Blink aus (ein Prozess pro Datenbank) | getestet (189 Tests), am 02.11. live gegen das echte Konto geprüft (Bilder von Wohnzimmer und Flur sichtbar); Branch `feature/blink-bilder` | [BLINK.md](BLINK.md) „Vorschaubilder“ |
| Blink „Neues Bild“: bewusste Ausnahme von „nur lesend“ (nur Admin, 1× pro Minute und Kamera, kostet Akku), Aufnahmezeit groß und gelb ab 1 Tag | getestet, live noch nicht ausgelöst; Branch `feature/blink-neues-bild` | [BLINK.md](BLINK.md) „Neues Bild“ |
| Demo-Modus: zweite Instanz mit Beispielhaus (5 Räume, 12 Geräte, 7 Tage Verlauf), Fake-Tuya und Fake-Blink, Banner, Port 5300; startet nicht, wenn DemoMode im Dienst an wäre | fertig, getestet; Branch `feature/demo-modus` | [DEMO.md](DEMO.md) |
| Einstellungsseite (nur Admin): Benutzer anlegen, Rolle ändern, löschen (nie sich selbst, nie den letzten Admin), eigenes Passwort ändern, Verbindungen Tuya/Blink mit Erklärung, Abgleich-Takt als Tabelle, Info (Version, Commit, Umgebung, Datenbankgröße) | fertig, getestet; Branch `feature/einstellungen` | [EINSTELLUNGEN.md](EINSTELLUNGEN.md) |
| Docker: mehrstufiges Image (Node → SDK → aspnet), nicht als root, Healthcheck `/healthz`, Compose mit Volumes für Datenbank und Schlüssel, Geheimnisse über `.env`, Demo per Profil | fertig, getestet (Image gebaut, Demo und Betrieb gestartet, Daten überleben neuen Container, Sicherung geprüft); Branch `feature/docker` | [DOCKER.md](DOCKER.md) |
| Feinschliff: REVIEW N-1 (Rechnername und IP aus Repo und Lern-PDF), einheitliche Lade-/Fehlerzustände mit „Erneut versuchen“, Gerätekacheln ohne Überlappung, „Erstes Konto anlegen“ nur ohne Benutzer (`GET /api/auth/setup`), README als Produktseite mit Demo-Screenshots, Skripte laufen auch auf macOS | fertig, getestet (238 Tests), im Demo auf Desktop und 390 px durchgeklickt; Branch `fix/feinschliff` | [README](../README.md), [Lernunterlage-Feinschliff.pdf](Lernunterlage-Feinschliff.pdf) |
| Präsentationsmaterial: 15-Minuten-Ablauf, Demo-Drehbuch (im Demo durchgespielt), Architekturbild (Mermaid), Fachbegriffe, 30 Rückfragen, „Was ich gelernt habe“, Ausblick; Spickzettel als PDF (4 Seiten) | fertig; Branch `docs/praesentation` | [PRAESENTATION.md](PRAESENTATION.md), [Lernunterlage-Praesentation.pdf](Lernunterlage-Praesentation.pdf) |
| Frontend: Login, Admin-Buttons, Token-Refresh | fertig | [AUTHENTIFIZIERUNG.md](AUTHENTIFIZIERUNG.md) |
| Design „Nachtlicht“: alle Seiten (Übersicht, Räume, Geräte, Reports, Blink, Anmeldung), Design-Tokens, Schrift lokal, „Energie jetzt“ und Batterien statt Karte „Licht (Tuya)“ | fertig, Screenshots Desktop und 390 px; Branch `design/nachtlicht` | [DESIGN.md](DESIGN.md) |
| Status-LEDs: Gesamtzustand, Server, Tuya, Blink, Geräte online/offline | fertig, getestet (PR #16–#18) | [API.md](API.md) (`/api/status`) |
| Validierung mit `AddValidation` + DataAnnotations (Anmeldung), OpenAPI-Tags | fertig, getestet (113 Tests) | [Lernunterlage-Validierung-und-Unterricht.pdf](Lernunterlage-Validierung-und-Unterricht.pdf) |
| Rate Limiting: Login pro Gerät, Tuya gemeinsam, 429 mit Retry-After; Fehlversuche im Log | fertig, getestet (139 Tests), Branch `fix/rate-limiting` | [RATE-LIMITING.md](RATE-LIMITING.md) |
| Echtzeit Backend → Browser: Server-Sent Events (`GET /api/live/devices`), Dashboard und Geräteseite aktualisieren sich ohne Neuladen, Polling bleibt Rückfallebene. **Tuya → Backend bleibt Polling**, kein Tuya-Push | getestet (Tests und im Browser im Demo-Modus), an echten Geräten und über `tailscale serve` noch nicht; Branch `feature/echtzeit-sse` | [ECHTZEIT.md](ECHTZEIT.md) |
| Tuya-Hintergrund-Abgleich mit Takt pro Gerät: Standard 30 min, Tür 3 min (~20 500 Aufrufe/Monat), Backoff bis 1 h, Status-LEDs lesen nur den Speicher, Seiten laden jede Minute neu | fertig, getestet; Takt pro Gerät seit 02.11. abends (Branch `feature/tuya-takt-pro-geraet`). **Nach dem Deploy auf der Geräteseite bei der Tür „Abgleich: Alle 3 Minuten“ einstellen.** | [TUYA.md](TUYA.md) „Hintergrund-Abgleich“, „Kontingent“, „Ausblick: Push“ |
| PWA (App auf dem Home-Bildschirm) | vorbereitet | [BETRIEB.md](BETRIEB.md) |
| Dauerbetrieb: Dienst + HTTPS über Tailscale | **läuft als Production** (seit 02.11.): Dienst auf 127.0.0.1:5200, `https://<rechner>.<tailnet>.ts.net` | [BETRIEB.md](BETRIEB.md) |

## 2. Prüfen, starten, betreiben

```bash
cd ~/Schreibtisch/Umschulung/Home-Control
./pruefen.sh                     # Build, Migrationen, Tests, Frontend-Build, Lint → pruefen.log
./app-starten.sh                 # alles unter http://localhost:5111 (Entwicklung)
./dienst-einrichten.sh           # Dauerbetrieb als Dienst auf 127.0.0.1:5200 (erneut = Update)
./demo-starten.sh                # Demo-Modus für die Präsentation: http://localhost:5300 (Beispieldaten)
docker compose --profile demo up # dasselbe im Container (docs/DOCKER.md)
./tailscale-https-einrichten.sh  # https://<rechner>.<tailnet>.ts.net für iPhone, iPad, Agent
```

Entwickeln wie bisher: Backend `dotnet run` im Ordner `backend/HomeControl.Api` (Port 5111),
Frontend `npm run dev` im Ordner `frontend` (Port 5173). Gegen den Dienst statt gegen das Entwicklungs-Backend:
`HC_BACKEND=http://127.0.0.1:5200 npm run dev` (Vorsicht: echte Daten).

**Auf dem MacBook** (zweiter Clone, ohne User Secrets, ohne Dienst, ohne Tuya/Blink): `./pruefen.sh` und
`./demo-starten.sh` laufen unverändert. Für die Lern-PDFs `reportlab` in einer venv und die Schrift DejaVu
(`brew install --cask font-dejavu`). Alles, was man sehen muss, im Demo-Modus ansehen.

## 3. Offene Punkte (Reihenfolge = Empfehlung)

1. **Präsentation am Montag 09.11.** Design „Nachtlicht“, Blink-Knopf „Neues Bild“ (einmal live ausprobieren) und
   Demo-Modus, Einstellungsseite, Docker und Feinschliff sind fertig. **Achtung:** Einstellungen und Docker (PR #33,
   #34) waren in Feature-Branches gemergt statt in `main`; PR #35 holt sie nach und muss **vor** `fix/feinschliff`
   gemergt werden. Präsentationsmaterial ist fertig (`docs/praesentation`, PR nach `fix/feinschliff` mergen).
   Vor Montag: Drehbuch aus [PRAESENTATION.md](PRAESENTATION.md) einmal laut mit Stoppuhr durchspielen. Danach, wenn Zeit bleibt: Clips vom USB-Stick
   (`feature/blink-lokale-clips`, Recherche liegt vor). Barlicht-Steckdose bis Montag einstecken, sonst bleibt
   „Energie jetzt“ leer (zeigt dann ehrlich „Kein Gerät mit Strommessung online“).
   - Offen: Backend-Teil des alten Tuya-Testgeräts (`/api/tuya/test-device/*`, `TuyaSwitchRecorder`,
     `Tuya:TestDeviceId`/`LinkedDeviceId`) wird im Frontend nicht mehr benutzt. Erst entfernen, wenn klar ist, dass
     der n8n-Agent ihn nicht abfragt; die Konstanten aus `TuyaSwitchRecorder` braucht `TuyaDeviceSyncer`.
2. **Security-Review abarbeiten** ([REVIEW.md](../REVIEW.md), Abschnitt „Umsetzungsstand“): H-1, M-1 bis M-5 sind
   erledigt, N-1 auch (Historie bewusst nicht umgeschrieben). Nach dem Merge der offenen PRs einmal
   `./dienst-einrichten.sh` ausführen. Als Nächstes N-4 (erster Admin nur lokal), N-3 (Sicherheitsheader), N-8 (CI).
   Jeweils eigener Branch.
3. **Tuya-Kontingent prüfen:** platform.tuya.com → Cloud → My Services → IoT Core: Edition, Ablaufdatum und
   Kontingent ansehen. Der Hintergrund-Abgleich braucht mit Tür 3 min und Rest 30 min etwa 20 500 Aufrufe/Monat
   (Rechnung in [TUYA.md](TUYA.md) „Kontingent“). Später günstiger: Tuyas Sammel-Abruf für den Status mehrerer
   Geräte (1 Aufruf statt 4), vorher an den echten Geräten prüfen. **Nach der Präsentation:** Push über den Tuya
   Message Service als Spike (TUYA.md „Ausblick: Push statt Polling“, Message Service im Portal ist freigeschaltet).
4. **KI-Agent:** neuen Schlüssel mit `KEY=$(./agent-schluessel-einrichten.sh)` erzeugen (erscheint nicht auf dem
   Bildschirm), `systemctl --user restart home-control`, mit `echo "$KEY"` einmal in n8n eintragen (Header `X-Api-Key`).
5. **iPhone/iPad:** https-Link in Safari öffnen → „Zum Home-Bildschirm“. Im Chrome am PC lud der https-Link
   am 02.11. nicht (vermutlich „Sicheres DNS“ in Chrome, `chrome://settings/security`), lokal geht `http://127.0.0.1:5200`.
6. **Messwerte prüfen:** Watt-Faktor (÷ 10) einmal mit eingeschalteter Last am Barlicht gegenprüfen. Barlicht
   war am 02.11. offline. Temperatursensoren sind nicht im Tuya-Projekt (klären, wo sie eingebunden sind).
7. **TypedResults** statt `IResult` in allen Endpunkten (wie im Unterricht). Eigener Branch, eigener PR.
8. **Endpunktfilter** `RejectEmptyIdFilter` für `{id:guid}`-Routen (leere GUID → 400 statt 404).
9. **Variante B:** Betrieb auf dem OVH-Server mit Docker, Geheimnisse als Umgebungsvariablen,
   `ASPNETCORE_ENVIRONMENT=Production`.
10. Kleinigkeiten: `agent-schluessel-einrichten.sh` zeigt „jetzt kopieren“ auch bei `KEY=$(…)` (Lösung: `[ -t 1 ]`),
   Gerätename „PC Moonligth“ → „PC Moonlight“.

## 4. Regeln für die Arbeit am Projekt

- **Nie direkt auf `main`.** Immer ein Branch (`feature/...`, `fix/...`), dann ein Pull Request, Merge-Commit.
- **Vor jedem Commit `./pruefen.sh`.** Ein Schritt gilt erst als fertig, wenn alles grün ist.
  Wer ohne Build arbeitet (z. B. KI ohne .NET SDK), schreibt das ehrlich in die Commit-Nachricht.
- **Alte Migrationen nie ändern.** Modell geändert → `dotnet ef migrations add <SprechenderName> --project backend/HomeControl.Api`.
- **Geheimnisse nie ins Repo:** JWT-Schlüssel, Tuya, Blink und Agent-Schlüssel liegen in den User Secrets
  (Skripte `*-einrichten.sh`). `homecontrol.db` und `*.bak` sind in `.gitignore`.
- **Zu jedem Schritt eine Lern-PDF** in `docs/`, erzeugt mit `tools/lernpdf` (Beispiel unten), in der README verlinkt.
- **Kommentare auf Deutsch, kurz, für Anfänger erklärbar.** Keine unnötig komplexe Architektur.
- **Tuya bleibt für den automatischen Abgleich lesend.** Auf ausdrücklichen Auftrag gibt es neu
  Admin-Schalter für bekannte Tuya-`switch_1`-Geräte mit Readback (Entwicklungs-PR #40, siehe
  [WETTER_UND_SCHALTER.md](WETTER_UND_SCHALTER.md)). Blink „Neues Bild“ bleibt die weitere Ausnahme.
  Für zusätzliche Schreibfunktionen ist weiterhin Zustimmung nötig.

Lern-PDF erzeugen (Muster: `tools/lernpdf/lernunterlage_rate_limiting.py` kopieren und anpassen). Braucht `reportlab`
(`pip install reportlab`, am besten in einer venv). Fehlt die Kursivschrift (`fonts-dejavu-extra`), nimmt
`lernpdf.py` die normale Schrift:

```bash
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_<thema>.py
```

## 5. Vorlage für eine neue KI-Sitzung

```text
Projekt: Home Control (GitHub-Repo bmkbauservice-hue/Home-Control), lokaler Pfad
~/Schreibtisch/Umschulung/Home-Control. ASP.NET Core 10 Minimal API, EF Core + SQLite, Identity + JWT,
React + TypeScript + Vite. Ich bin in der Umschulung und muss den Code in der Präsentation erklären.

Lies zuerst docs/UEBERGABE.md und die zum Thema passende Datei in docs/.
Regeln: nie direkt auf main, Branch + PR; vor jedem Commit ./pruefen.sh; alte Migrationen nie ändern;
keine Geheimnisse ins Repo; Tuya/Blink nur lesend (Ausnahme nur Blink „Neues Bild“); kurze deutsche Kommentare; zu jedem Schritt eine
Lern-PDF mit tools/lernpdf in docs/ und in der README verlinken. Erkläre nach jedem Schritt:
Was wurde gemacht? Warum? Was muss ich für die Schule wissen?

Aufgabe: <hier die Aufgabe, z. B. Punkt 4 aus docs/UEBERGABE.md>
```

## 6. Wo was liegt

| Ordner/Datei | Inhalt |
|---|---|
| `backend/HomeControl.Api/Program.cs` | Dienste, Middleware-Reihenfolge, Endpunkte |
| `backend/HomeControl.Api/Auth/` | Identity, JWT, Refresh-Token, API-Schlüssel, Rechte (Policies) |
| `backend/HomeControl.Api/Endpoints/` | Ein File pro Bereich: Räume, Geräte, Ereignisse, Reports, Tuya, Blink, Anmeldung, Status |
| `backend/HomeControl.Api/Tuya/`, `Blink/`, `Status/` | Anbindungen und Status-LEDs |
| `backend/HomeControl.Api/Demo/` | Demo-Modus: Beispielhaus, Fake-Tuya, Fake-Blink, Demo-Bilder ([DEMO.md](DEMO.md)) |
| `backend/HomeControl.Api/RateLimiting/` | Rate-Limit-Regeln `login` und `tuya`, Grenzen aus appsettings.json |
| `backend/HomeControl.Api/Migrations/` | Datenbankänderungen (nie alte ändern) |
| `backend/HomeControl.Api.Tests/` | xUnit-Tests, `Fakes/` spielt Tuya und die Uhr |
| `frontend/src/api/` | Aufrufe ans Backend (`http.ts`: Bearer + Refresh) |
| `frontend/src/auth/`, `frontend/src/status/` | Context-Provider für Anmeldung und Status-LEDs |
| `frontend/src/pages/`, `components/` | Seiten und Bausteine |
| `docs/` | Doku und Lern-PDFs |
| `tools/BlinkSpike/` | Erst-Login bei Blink mit 2FA |
| `tools/lernpdf/` | Skripte für die Lern-PDFs |
| `tools/demo-bilder/` | Zeichnet die Demo-Kamerabilder (Pillow) |


