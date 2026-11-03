# CLAUDE.md – Hinweise für Claude Code in diesem Repo

Home Control: Smart-Home-Übersicht (Umschulungsprojekt von Mario, wird in der Schule präsentiert).
ASP.NET Core 10 Minimal API + EF Core/SQLite + Identity/JWT, React 19 + TypeScript + Vite.
Tuya und Blink sind **nur lesend** angebunden, es wird nie etwas geschaltet. **Einzige bewusste Ausnahme:**
Blink-Knopf „Neues Bild“ (nur Admin, höchstens 1× pro Minute und Kamera, kostet Akku, schaltet nichts),
siehe `docs/BLINK.md`. Weitere Ausnahmen nur nach ausdrücklicher Zustimmung.

**Zuerst lesen:** `docs/UEBERGABE.md` (Stand, offene Punkte, Regeln) und die passende Datei in `docs/`.
Sicherheitsthemen: `REVIEW.md` (Abschnitt „Umsetzungsstand“).

## Sprache und Stil

- Antworten, Kommentare, Commit-Nachrichten und Doku auf **Deutsch**.
- Code muss für einen Anfänger erklärbar sein: kurze deutsche Kommentare (Was und Warum), keine unnötig
  komplexe Architektur.
- Nach jedem Schritt kurz erklären: Was wurde gemacht? Warum? Was muss ich für die Schule wissen?
- Linux Mint. Befehle für den Nutzer einzeln schicken, nicht als langen Block (eingefügte Blöcke fressen
  `read`-Eingaben).

## Arbeitsweise (verbindlich)

- **Nie direkt auf `main`.** Branch (`feature/…`, `fix/…`, `docs/…`), dann Pull Request. Kein Force-Push.
- **Vor jedem Commit `./pruefen.sh`** (Build, Migrationen vollständig, Tests, Frontend-Build, Lint →
  `pruefen.log`). Ein Schritt gilt erst als fertig, wenn alles grün ist. Keine erfundenen Erfolgsmeldungen.
- **Kleine Commits**, eine Sache pro Branch.
- **Alte Migrationen nie ändern.** Modell geändert → `dotnet ef migrations add <SprechenderName> --project backend/HomeControl.Api`.
- **Zu jedem Schritt eine Lern-PDF** in `docs/`, erzeugt mit `tools/lernpdf` (Muster:
  `tools/lernpdf/lernunterlage_production.py`), in der README-Tabelle verlinkt:
  `PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_<thema>.py`
- `docs/UEBERGABE.md` aktuell halten, wenn sich Stand oder offene Punkte ändern.

## Geheimnisse

- Nie ins Repo: JWT-Schlüssel, Tuya, Blink, Agent-Schlüssel liegen in den **.NET User Secrets**
  (Skripte `*-einrichten.sh`). `homecontrol.db` und `*.bak` sind in `.gitignore`.
- Geheimnisse nie im Klartext ausgeben oder in die Shell-History schreiben (`KEY=$(…)` statt Zwischenablage).
- Keine Hostnamen, Tailnet-Namen oder IP-Adressen ins öffentliche Repo schreiben.

## Wichtige Befehle

```bash
./pruefen.sh                     # alles prüfen
./app-starten.sh                 # Entwicklung: http://localhost:5111 (Development, /scalar an)
./dienst-einrichten.sh           # Dauerdienst auf 127.0.0.1:5200 (Production) aktualisieren
./demo-starten.sh                # Demo-Modus: Beispielhaus auf http://localhost:5300, keine echten Daten
HC_API=http://127.0.0.1:5200 ./tuya-rohwerte-anzeigen.sh   # echte Tuya-Codes ansehen
```

## Tests

- xUnit in `backend/HomeControl.Api.Tests`, `HomeControlApiFactory` startet die API im Arbeitsspeicher.
- Die Fabrik läuft als Development und legt feste Werte fest (Leseschutz aus, kein Agent-Schlüssel), damit
  die echten User Secrets das Ergebnis nicht beeinflussen.
- Werte, die `Program.cs` schon beim Aufbau liest (z. B. `Jwt:SigningKey`), im Test mit `UseSetting` setzen,
  nicht über `ConfigureAppConfiguration`.
- Tuya-Werte nie raten: Statuscodes und Skalierung erst an echten Rohwerten prüfen.
