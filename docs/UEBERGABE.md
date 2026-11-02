# Übergabe: Stand von Home Control und wie es weitergeht

**Stand: 31.10.2026, 14:40 Uhr.** Diese Datei ist der Einstieg für jede neue Arbeitssitzung, egal ob allein,
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
| API-Schlüssel für den KI-Agenten (nur lesen) | fertig, getestet, in n8n noch nicht eingetragen | [AUTHENTIFIZIERUNG.md](AUTHENTIFIZIERUNG.md) |
| Tuya: Geräteliste, Status, Abgleich aller verknüpften Geräte (nur lesend) | fertig, getestet | [TUYA.md](TUYA.md) |
| Blink: Status und Kameras (nur lesend, inoffizielle API) | Backend fertig, Oberfläche zeigt nur den Status | [BLINK.md](BLINK.md) |
| Frontend: Login, Admin-Buttons, Token-Refresh | fertig | [AUTHENTIFIZIERUNG.md](AUTHENTIFIZIERUNG.md) |
| Status-LEDs: Gesamtzustand, Server, Tuya, Blink, Geräte online/offline | fertig, getestet (PR #16–#18) | [API.md](API.md) (`/api/status`) |
| Validierung mit `AddValidation` + DataAnnotations (Anmeldung), OpenAPI-Tags | fertig, getestet (113 Tests) | [Lernunterlage-Validierung-und-Unterricht.pdf](Lernunterlage-Validierung-und-Unterricht.pdf) |
| PWA (App auf dem Home-Bildschirm) | vorbereitet | [BETRIEB.md](BETRIEB.md) |
| Dauerbetrieb: Dienst + HTTPS über Tailscale | **läuft**: Dienst auf 127.0.0.1:5200, https://muju-ms-7c95.tail0b69aa.ts.net | [BETRIEB.md](BETRIEB.md) |

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

1. **Geräte mit Tuya verknüpfen** (als Nächstes, ohne Code): App öffnen → Geräte → Bearbeiten → Feld
   „Tuya-Gerät“ wählen → Speichern, für jedes Gerät aus der Tuya-App. Danach werden die LEDs grün/rot statt grau.
2. **Rohwerte ansehen:** `HC_API=http://127.0.0.1:5200 ./tuya-rohwerte-anzeigen.sh` (Anmeldung mit dem
   Home-Control-Konto). Die Ausgabe zeigt die echten Tuya-Codes für Punkt 4.
3. **iPhone/iPad und KI-Agent:** Link in Safari öffnen → „Zum Home-Bildschirm“. Für n8n:
   `./agent-schluessel-einrichten.sh`, danach `./dienst-einrichten.sh`, in n8n Header `X-Api-Key` und die
   https-Adresse eintragen, testen, dann `Auth:RequireLoginForReading` auf `true`.
4. **Weitere Gerätetypen:** `./tuya-rohwerte-anzeigen.sh` ausführen, Codes für Temperatur, Watt und Akku
   ansehen, dann im `TuyaDeviceSyncer` übertragen (siehe [TUYA.md](TUYA.md), „Bekannte Grenzen“).
5. **Blink-Seite:** Kameraliste aus `GET /api/blink/cameras` anzeigen (Backend ist fertig, die Seite ist
   noch ein Platzhalter). Vorher mit `tools/BlinkSpike` die echten Feldnamen prüfen.
6. **TypedResults** statt `IResult` in allen Endpunkten (wie im Unterricht): Der Compiler prüft die
   erlaubten Antworten, Scalar zeigt sie automatisch. Eigener Branch, eigener PR.
7. **Endpunktfilter** `RejectEmptyIdFilter` für `{id:guid}`-Routen (leere GUID → 400 statt 404).
8. **Variante B:** Betrieb auf dem OVH-Server mit Docker, Geheimnisse als Umgebungsvariablen,
   `ASPNETCORE_ENVIRONMENT=Production`.

## 4. Regeln für die Arbeit am Projekt

- **Nie direkt auf `main`.** Immer ein Branch (`feature/...`, `fix/...`), dann ein Pull Request, Merge-Commit.
- **Vor jedem Commit `./pruefen.sh`.** Ein Schritt gilt erst als fertig, wenn alles grün ist.
  Wer ohne Build arbeitet (z. B. KI ohne .NET SDK), schreibt das ehrlich in die Commit-Nachricht.
- **Alte Migrationen nie ändern.** Modell geändert → `dotnet ef migrations add <SprechenderName> --project backend/HomeControl.Api`.
- **Geheimnisse nie ins Repo:** JWT-Schlüssel, Tuya, Blink und Agent-Schlüssel liegen in den User Secrets
  (Skripte `*-einrichten.sh`). `homecontrol.db` und `*.bak` sind in `.gitignore`.
- **Zu jedem Schritt eine Lern-PDF** in `docs/`, erzeugt mit `tools/lernpdf` (Beispiel unten), in der README verlinkt.
- **Kommentare auf Deutsch, kurz, für Anfänger erklärbar.** Keine unnötig komplexe Architektur.
- **Tuya und Blink bleiben nur lesend.** Es wird nichts geschaltet.

Lern-PDF erzeugen (Muster: `tools/lernpdf/lernunterlage_status_leds.py` kopieren und anpassen):

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
keine Geheimnisse ins Repo; Tuya/Blink nur lesend; kurze deutsche Kommentare; zu jedem Schritt eine
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
| `backend/HomeControl.Api/Migrations/` | Datenbankänderungen (nie alte ändern) |
| `backend/HomeControl.Api.Tests/` | xUnit-Tests, `Fakes/` spielt Tuya und die Uhr |
| `frontend/src/api/` | Aufrufe ans Backend (`http.ts`: Bearer + Refresh) |
| `frontend/src/auth/`, `frontend/src/status/` | Context-Provider für Anmeldung und Status-LEDs |
| `frontend/src/pages/`, `components/` | Seiten und Bausteine |
| `docs/` | Doku und Lern-PDFs |
| `tools/BlinkSpike/` | Erst-Login bei Blink mit 2FA |
| `tools/lernpdf/` | Skripte für die Lern-PDFs |
