# Präsentation (ca. 5–7 Minuten)

Ein Leitfaden zum Vorstellen von Home Control. Die Zeiten sind Richtwerte.

## Vorbereitung (vor der Präsentation)

1. Backend mit einer eigenen Demo-Datenbank starten, damit schon Beispieldaten da sind:

   ```bash
   cd backend/HomeControl.Api
   dotnet run -- --SeedDemoData=true --ConnectionStrings:DefaultConnection="Data Source=demo.db"
   ```

2. In einem zweiten Terminal das Frontend starten:

   ```bash
   cd frontend
   npm run dev
   ```

3. Im Browser öffnen: http://localhost:5173 (App) und http://localhost:5111/scalar (API).
4. Ein drittes Terminal für den curl-Befehl bereithalten.

## Ablauf

### 1. Projektziel (ca. 30 Sekunden)

> „Home Control ist eine Web-App zum Verwalten von Räumen und Geräten im Zuhause.
> Zu jedem Gerät gibt es einen Ereignisverlauf, und eine Tagesauswertung fasst zusammen,
> was an einem Tag passiert ist. Von Tuya wird bereits der Zustand eines Testgeräts
> gelesen und in der Übersicht angezeigt. Schalten und die Blink-Anbindung folgen noch.“

### 2. Architektur (ca. 1 Minute)

Das Diagramm aus [ARCHITEKTUR.md](ARCHITEKTUR.md) zeigen.

> „Im Browser läuft eine React-App. Sie schickt HTTP-Anfragen an eine REST API mit
> ASP.NET Core. Die API benutzt Entity Framework Core, und EF Core speichert alles in
> einer SQLite-Datei. Während der Entwicklung leitet Vite alle `/api`-Aufrufe an das
> Backend weiter.“

Kurz die Ordner nennen: `Models`, `Data`, `Contracts`, `Endpoints` im Backend und
`pages`, `components`, `api` im Frontend.

### 3. Datenmodell (ca. 1 Minute)

Das Diagramm aus [DATENMODELL.md](DATENMODELL.md) zeigen.

> „Es gibt drei Tabellen. Ein Raum hat viele Geräte, ein Gerät hat viele Ereignisse.
> Wenn man einen Raum löscht, werden seine Geräte und deren Ereignisse mitgelöscht.
> Das nennt man kaskadierendes Löschen. Die Oberfläche warnt vorher ausdrücklich.
> Zeitstempel speichert der Server immer in UTC.“

### 4. Live-Demo: Raum → Gerät → Ereignis → Report (ca. 2–3 Minuten)

1. **Übersicht** zeigen: Begrüßung, Anzahl Räume und Geräte, Raumkacheln, Blink-Bereich
   („Noch nicht verbunden“).
2. **Räume** → bei „Neuen Raum anlegen“ zum Beispiel „Küche“ eingeben → **Anlegen**.
3. **Geräte** → bei „Neues Gerät anlegen“: Name „Wasserkocher“, Typ „Steckdose“,
   Raum „Küche“ → **Anlegen**.
4. Beim Wasserkocher auf **Ereignisse** klicken → im Kasten „Manueller Testeintrag“
   die Aktion „Eingeschaltet“ wählen → **Testeintrag speichern**.
   Den Hinweis zeigen: Das Gerät wird nicht geschaltet, es wird nur ein Eintrag gespeichert.
5. **Reports** → heutiges Datum ist vorausgewählt. Zeigen: Ereignisanzahl, Aktionen,
   Rangliste der Geräte. Auf den Hinweis „Auswertung nach UTC“ und
   „Aktueller Gerätebestand“ eingehen.

### 5. Suche und Validierung (ca. 1 Minute)

**Suche** auf der Seite Geräte:

- Bei „Suche“ zum Beispiel „lampe“ eingeben.
- Zusätzlich bei „Raum“ einen Raum wählen: Die Filter gelten zusammen.
- **Filter zurücksetzen** klicken.
- Wenn nichts passt, steht dort „Keine passenden Geräte gefunden“.

**Validierungsfehler** im Terminal zeigen. In der Oberfläche ist „Anlegen“ bei leerem
Namen gesperrt, deshalb direkt an der API:

```bash
curl -i -X POST http://localhost:5111/api/rooms \
  -H "Content-Type: application/json" -d '{"name":""}'
```

> „Die API antwortet mit 400 und einer Meldung für das Feld `name`, im Format ProblemDetails.“

Optional: In Scalar (http://localhost:5111/scalar) die Endpunkt-Gruppen zeigen.

### 6. Grenzen und Ausblick (ca. 30 Sekunden)

> „Was noch fehlt:
> - Tuya nur lesend für ein Testgerät, noch kein Schalten; keine Blink-Anbindung,
>   die Blink-Seite ist ein Platzhalter,
> - keine Live-Werte: `IsActive` und `LastValue` werden noch nicht aktualisiert,
> - bei Ereignissen wird noch nicht gespeichert, ob sie manuell erfasst wurden
>   oder von einem Gerät kommen,
> - die Suche unterscheidet bei Umlauten noch Groß- und Kleinschreibung (Grenze von SQLite).“

## Kurze Erklärungen für Rückfragen

**CRUD**
Create, Read, Update, Delete: anlegen, lesen, ändern, löschen. Bei Räumen und Geräten
entspricht das POST, GET, PUT und DELETE.

**DTO (Data Transfer Object)**
Eine einfache Klasse nur für den Datenaustausch über die API, zum Beispiel `RoomRequest` und
`RoomResponse`. Damit gibt die API nicht die Datenbank-Klassen heraus und zeigt nur, was nötig ist.

**Dependency Injection (DI)**
Klassen erzeugen ihre Hilfsmittel nicht selbst, sondern bekommen sie gereicht.
In `Program.cs` wird der `ApplicationDbContext` registriert. Jeder Endpunkt bekommt ihn
als Parameter. Der `DatabaseSeeder` bekommt DbContext und Logger über den Konstruktor.

**Migration**
Eine von EF Core erzeugte Datei, die beschreibt, wie die Datenbank aufgebaut oder geändert
wird. Hier gibt es `InitialCreate`. Beim Start wendet das Backend fehlende Migrationen
automatisch an.

**Seeding**
Automatisches Befüllen mit Beispieldaten. Hier optional und standardmäßig aus. Es läuft nur
in Development mit `SeedDemoData=true` und nur bei leerer Datenbank, deshalb entstehen
keine Duplikate.

**ProblemDetails**
Ein Standardformat (RFC 9457) für Fehlerantworten mit `status`, `title` und bei
Validierungsfehlern `errors`. Alle Fehler der API haben dieses Format. Bei einem 500er sieht
der Client nur eine allgemeine Meldung, die Details stehen im Server-Log.

**Minimal API und MapGroup**
Endpunkte werden direkt mit `MapGet`, `MapPost` usw. definiert, ohne Controller-Klassen.
`MapGroup("/api/rooms")` fasst alle Raum-Endpunkte unter einem gemeinsamen Pfad zusammen.
