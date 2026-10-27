# Home Control

Home Control ist eine Web-App, mit der man Räume und Geräte eines Zuhauses verwaltet.
Zu jedem Gerät kann ein Verlauf von Ereignissen gespeichert werden (zum Beispiel
„eingeschaltet“ oder „Tür geöffnet“). Eine Tagesauswertung zeigt, was an einem Tag passiert ist.

Das Projekt besteht aus zwei Teilen:

- **Backend:** REST API mit ASP.NET Core (Minimal API), Entity Framework Core und SQLite
- **Frontend:** React mit TypeScript und Vite

Weitere Dokumentation liegt im Ordner [docs/](docs/):
[Architektur](docs/ARCHITEKTUR.md) · [Datenmodell](docs/DATENMODELL.md) ·
[API](docs/API.md) · [Tuya](docs/TUYA.md) · [Präsentation](docs/PRAESENTATION.md)

## Projektübersicht

![Home Control – Architektur und Projektidee mit Frontend, Backend, Datenbank und Smart-Home-Geräten](docs/images/home-control-architektur.png)

> Die Infografik zeigt die ursprüngliche Projektidee einschließlich geplanter Erweiterungen.
> Im aktuellen Code sind IDs vom Typ `Guid` und Ereigniszeitstempel vom Typ `DateTimeOffset`.
> Dashboard und Reports sind bereits umgesetzt; echte Geräteanbindungen sind noch geplant
> und werden über das Backend angebunden, nicht direkt über die Datenbank.
> Das aktuelle Datenmodell steht in [docs/DATENMODELL.md](docs/DATENMODELL.md).

## Aktueller Funktionsumfang

| Bereich | Was funktioniert |
|---|---|
| Übersicht | Begrüßung, Anzahl Räume und Geräte, Räume als Kacheln, ersten Raum direkt anlegen |
| Räume | Anlegen, anzeigen, umbenennen, löschen, nach Namen suchen |
| Geräte | Anlegen, anzeigen, bearbeiten (auch Raum wechseln), löschen; Suche nach Namen, Filter nach Raum und Gerätetyp |
| Ereignisse | Verlauf pro Gerät anzeigen, manuelle Testeinträge erfassen |
| Reports | Tagesauswertung für einen UTC-Tag: Anzahl Ereignisse, Aktionen, Geräte mit den meisten Ereignissen |
| API | Validierung mit Feldmeldungen, einheitliche Fehler (ProblemDetails), OpenAPI-Beschreibung mit Scalar-Oberfläche |
| Datenbank | Migrationen werden beim Start automatisch angewendet, optionale Demo-Daten |
| Tuya | Status eines Testgeräts aus der Tuya-Cloud lesen (`GET /api/tuya/test-device/status`), nur lesend; Abgleich (`POST /api/tuya/test-device/sync`) überträgt EIN/AUS auf ein verknüpftes Gerät und speichert Änderungen als Ereignis; Karte „Licht Wohnzimmer“ in der Übersicht zeigt EIN/AUS mit Abrufzeitpunkt |

**Noch nicht vorhanden:**

- Tuya nur lesend und nur für ein Testgerät: kein Schalten; gespeichert wird nur der Zustand des einen verknüpften Geräts
  (siehe [docs/TUYA.md](docs/TUYA.md)).
- Keine Anbindung an Blink. Die Blink-Seite ist nur ein Platzhalter.
- Keine Live-Werte. `IsActive` und `LastValue` werden nicht durch Ereignisse aktualisiert.
  In der Oberfläche steht deshalb „Noch keine Daten“.
- Ereignisse werden nur gespeichert, es wird nichts geschaltet. Woher ein Ereignis kommt
  (manuell oder von einem Gerät), wird noch nicht gespeichert.
- Keine Anmeldung. Automatisierte Tests gibt es bisher nur für den Tuya-Token-Cache.

## Voraussetzungen

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) in Version 20.19 oder neuer bzw. 22.12 oder neuer (Vorgabe von Vite 8)
- npm (wird mit Node.js installiert)

Entwickelt und geprüft mit .NET SDK 10.0.113, Node.js 24.22.1 und npm 11.19.1.

## Installation

Im Projektordner:

```bash
# Lokale .NET-Tools installieren (dotnet-ef für Migrationen, siehe dotnet-tools.json)
dotnet tool restore

# Frontend-Pakete installieren
cd frontend
npm install
```

Die NuGet-Pakete des Backends lädt `dotnet run` bzw. `dotnet build` automatisch.

### Tuya-Zugangsdaten (optional)

Nur nötig, wenn der Tuya-Statusabruf genutzt werden soll. Die Zugangsdaten kommen in die
.NET User Secrets, nicht ins Repository. Im Projektordner:

```bash
./tuya-secrets-einrichten.sh
```

Das Skript fragt Client-ID, Client-Secret und TestDeviceId verdeckt ab. Ohne diese Werte
läuft die App normal, nur der Tuya-Abruf antwortet mit 503. Details in [docs/TUYA.md](docs/TUYA.md).

Optional kann der Zustand des Testgeräts auf ein Gerät in der Datenbank übertragen werden
(`Tuya:LinkedDeviceId`, siehe [docs/TUYA.md](docs/TUYA.md#3-konfiguration)).

## Starten

Backend und Frontend laufen in **zwei getrennten Terminals**.

**Terminal 1 – Backend:**

```bash
cd backend/HomeControl.Api
dotnet run
```

Die API läuft dann unter **http://localhost:5111** (Profil `http` aus
`Properties/launchSettings.json`, Umgebung `Development`).

**Terminal 2 – Frontend:**

```bash
cd frontend
npm run dev
```

Die App öffnet man unter **http://localhost:5173**.

### Wie Frontend und Backend zusammenhängen

Das Frontend ruft nur Adressen wie `/api/rooms` auf. Der Vite-Entwicklungsserver leitet alles,
was mit `/api` beginnt, an `http://localhost:5111` weiter (siehe `frontend/vite.config.ts`).
Deshalb braucht das Frontend keine feste Backend-Adresse und es gibt keine CORS-Probleme.

Ist Port 5173 schon belegt, weicht Vite automatisch auf 5174 usw. aus (steht im Terminal).

### API-Dokumentation (Scalar)

Nur im Development-Modus:

- Scalar-Oberfläche: **http://localhost:5111/scalar**
- OpenAPI-Dokument: http://localhost:5111/openapi/v1.json

Scalar läuft direkt am Backend (Port 5111), nicht über den Vite-Port.

## Datenbank und Migrationen

Die SQLite-Datei `homecontrol.db` liegt in `backend/HomeControl.Api/`. Sie wird beim ersten
Start angelegt. Fehlende Migrationen wendet das Backend beim Start automatisch an
(`Database.Migrate()` in `Program.cs`). Die Datei ist in `.gitignore` eingetragen.

Migrationen kann man auch von Hand prüfen oder anwenden (im Projektordner):

```bash
# Welche Migrationen gibt es, welche sind noch offen?
dotnet ef migrations list --project backend/HomeControl.Api

# Migrationen auf die Datenbank anwenden
dotnet ef database update --project backend/HomeControl.Api
```

Bisher gibt es eine Migration: `InitialCreate`.

## Demo-Daten (Seeding)

Das Seeding ist **standardmäßig ausgeschaltet** (`"SeedDemoData": false` in `appsettings.json`).
Es läuft nur, wenn alle drei Bedingungen erfüllt sind:

1. Umgebung ist `Development`
2. `SeedDemoData` ist ausdrücklich `true`
3. Die Datenbank hat noch keine Räume, Geräte und Ereignisse

Es legt drei Räume, vier Geräte und sieben Ereignisse für den heutigen UTC-Tag an.
Alle Namen enden auf „(Demo)“. Vorhandene Daten werden nie verändert oder gelöscht.

Einschalten für einen Start:

```bash
cd backend/HomeControl.Api
dotnet run -- --SeedDemoData=true
```

Hat `homecontrol.db` schon Daten, passiert nichts. Für eine getrennte Demo-Datenbank:

```bash
dotnet run -- --SeedDemoData=true --ConnectionStrings:DefaultConnection="Data Source=demo.db"
```

## Builds und Linter

```bash
# Backend (im Projektordner, baut HomeControl.slnx)
dotnet build
dotnet test     # Tests in backend/HomeControl.Api.Tests

# Frontend (im Ordner frontend)
npm run build   # TypeScript prüfen und Produktions-Build nach frontend/dist
npm run lint    # Linter oxlint
```

## Projektstruktur (Kurzfassung)

```
backend/HomeControl.Api/   REST API (Details in docs/ARCHITEKTUR.md)
backend/HomeControl.Api.Tests/  xUnit-Tests (bisher Tuya-Token-Cache)
frontend/src/              React-Oberfläche
docs/                      Dokumentation
HomeControl.slnx           .NET-Lösung
dotnet-tools.json          lokales Tool dotnet-ef
tuya-secrets-einrichten.sh Tuya-Zugangsdaten verdeckt in die User Secrets eintragen
```
