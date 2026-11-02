# Übergabe: Stand von Home Control und wie es weitergeht

**Stand: 02.11.2026, 15 Uhr.** Diese Datei ist der Einstieg für jede neue Arbeitssitzung, egal ob allein,
mit ChatGPT/Codex oder einem anderen KI-Werkzeug. Sie beschreibt, was fertig ist, wie man prüft,
was offen ist und welche Regeln gelten.

**Zum Lernen:** [Lernunterlage-Uebergabe.pdf](Lernunterlage-Uebergabe.pdf)

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
| Frontend: Login, Admin-Buttons, Token-Refresh | fertig | [AUTHENTIFIZIERUNG.md](AUTHENTIFIZIERUNG.md) |
| Design „Nachtlicht“: alle Seiten (Übersicht, Räume, Geräte, Reports, Blink, Anmeldung), Design-Tokens, Schrift lokal, „Energie jetzt“ und Batterien statt Karte „Licht (Tuya)“ | fertig, Screenshots Desktop und 390 px; Branch `design/nachtlicht` | [DESIGN.md](DESIGN.md) |
| Status-LEDs: Gesamtzustand, Server, Tuya, Blink, Geräte online/offline | fertig, getestet (PR #16–#18) | [API.md](API.md) (`/api/status`) |
| Validierung mit `AddValidation` + DataAnnotations (Anmeldung), OpenAPI-Tags | fertig, getestet (113 Tests) | [Lernunterlage-Validierung-und-Unterricht.pdf](Lernunterlage-Validierung-und-Unterricht.pdf) |
| Rate Limiting: Login pro Gerät, Tuya gemeinsam, 429 mit Retry-After; Fehlversuche im Log | fertig, getestet (139 Tests), Branch `fix/rate-limiting` | [RATE-LIMITING.md](RATE-LIMITING.md) |
| Tuya-Hintergrund-Abgleich mit Takt pro Gerät: Standard 30 min, Tür 3 min (~20 500 Aufrufe/Monat), Backoff bis 1 h, Status-LEDs lesen nur den Speicher, Seiten laden jede Minute neu | fertig, getestet; Takt pro Gerät seit 02.11. abends (Branch `feature/tuya-takt-pro-geraet`). **Nach dem Deploy auf der Geräteseite bei der Tür „Abgleich: Alle 3 Minuten“ einstellen.** | [TUYA.md](TUYA.md) „Hintergrund-Abgleich“, „Kontingent“, „Ausblick: Push“ |
| PWA (App auf dem Home-Bildschirm) | vorbereitet | [BETRIEB.md](BETRIEB.md) |
| Dauerbetrieb: Dienst + HTTPS über Tailscale | **läuft als Production** (seit 02.11.): Dienst auf 127.0.0.1:5200, `https://<rechner>.<tailnet>.ts.net` | [BETRIEB.md](BETRIEB.md) |

## 2. Prüfen, starten, betreiben

```bash
cd ~/Schreibtisch/Umschulung/Home-Control
./pruefen.sh                     # Build, Migrationen, Tests, Frontend-Build, Lint → pruefen.log
./app-starten.sh                 # alles unter http://localhost:5111 (Entwicklung)
./dienst-einrichten.sh           # Dauerbetrieb als Dienst auf 127.0.0.1:5200 (erneut = Update)
./tailscale-https-einrichten.sh  # https://<rechner>.<tailnet>.ts.net für iPhone, iPad, Agent
```

Entwickeln wie bisher: Backend `dotnet run` im Ordner `backend/HomeControl.Api` (Port 5111),
Frontend `npm run dev` im Ordner `frontend` (Port 5173).

## 3. Offene Punkte (Reihenfolge = Empfehlung)

1. **Präsentation am Montag 09.11.** Design „Nachtlicht“ ist fertig (Branch `design/nachtlicht`), Blink-Knopf
   „Neues Bild“ ebenfalls (Branch `feature/blink-neues-bild`, nach dem Merge einmal live ausprobieren). Danach,
   wenn Zeit bleibt: Clips vom USB-Stick
   (`feature/blink-lokale-clips`, Recherche liegt vor). Barlicht-Steckdose bis Montag einstecken, sonst bleibt
   „Energie jetzt“ leer (zeigt dann ehrlich „Kein Gerät mit Strommessung online“).
   - Offen: Backend-Teil des alten Tuya-Testgeräts (`/api/tuya/test-device/*`, `TuyaSwitchRecorder`,
     `Tuya:TestDeviceId`/`LinkedDeviceId`) wird im Frontend nicht mehr benutzt. Erst entfernen, wenn klar ist, dass
     der n8n-Agent ihn nicht abfragt; die Konstanten aus `TuyaSwitchRecorder` braucht `TuyaDeviceSyncer`.
   - Offen: Die Anmeldeseite zeigt „Noch kein Konto? Erstes Konto anlegen“ immer, auch wenn es schon Konten gibt
     (das Backend lehnt dann ab). Nur anzeigen, solange es keinen Benutzer gibt (passt zu REVIEW.md N-4).
2. **Security-Review abarbeiten** ([REVIEW.md](../REVIEW.md), Abschnitt „Umsetzungsstand“): H-1, M-1 bis M-5 sind
   erledigt. Nach dem Merge von `feature/tuya-hintergrund-abgleich` und `fix/blink-einrichtung` einmal
   `./dienst-einrichten.sh` ausführen. Als Nächstes N-1 (Tailscale-IP aus `tuya-rohwerte-anzeigen.sh` und
   Rechnername aus `tools/lernpdf/lernunterlage_betrieb.py` entfernen). Jeweils eigener Branch.
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
- **Tuya und Blink bleiben nur lesend.** Es wird nichts geschaltet. Einzige bewusste Ausnahme: Blink „Neues Bild“
  (nur Admin, 1× pro Minute und Kamera, siehe [BLINK.md](BLINK.md)). Weitere nur nach Zustimmung.

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
| `backend/HomeControl.Api/RateLimiting/` | Rate-Limit-Regeln `login` und `tuya`, Grenzen aus appsettings.json |
| `backend/HomeControl.Api/Migrations/` | Datenbankänderungen (nie alte ändern) |
| `backend/HomeControl.Api.Tests/` | xUnit-Tests, `Fakes/` spielt Tuya und die Uhr |
| `frontend/src/api/` | Aufrufe ans Backend (`http.ts`: Bearer + Refresh) |
| `frontend/src/auth/`, `frontend/src/status/` | Context-Provider für Anmeldung und Status-LEDs |
| `frontend/src/pages/`, `components/` | Seiten und Bausteine |
| `docs/` | Doku und Lern-PDFs |
| `tools/BlinkSpike/` | Erst-Login bei Blink mit 2FA |
| `tools/lernpdf/` | Skripte für die Lern-PDFs |
