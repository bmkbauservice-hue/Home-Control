# Einstellungsseite

**Zum Lernen:** [Lernunterlage-Einstellungen.pdf](Lernunterlage-Einstellungen.pdf)

Die Seite „Einstellungen“ (in der Handy-Leiste kurz „Admin“) sehen **nur Admins**. Sie bündelt alles, wofür man
sonst ins Terminal müsste. Jeder Bereich lädt und speichert für sich, ein Fehler in einem Bereich stört die
anderen nicht.

| Bereich | Was man dort tut | Endpunkt |
|---|---|---|
| Benutzer | Liste mit Rolle, neuen Benutzer anlegen (mit Startpasswort und Rolle), Rolle ändern, löschen | `GET /api/users`, `POST /api/auth/register`, `PUT /api/users/{id}/role`, `DELETE /api/users/{id}` |
| Mein Passwort | eigenes Passwort ändern | `POST /api/auth/password` |
| Verbindungen | Tuya und Blink mit LED, Zeitpunkt der letzten erfolgreichen Abfrage und Erklärung, was zu tun ist; Knopf „Jetzt prüfen“ | `GET /api/status`, `GET /api/blink/status` (dieselben wie die Status-LEDs) |
| Abgleich mit Tuya | alle verknüpften Geräte in einer Tabelle: LED, Raum, Takt (direkt änderbar), letzte Messung | `GET /api/devices`, `PUT /api/devices/{id}` |
| Info | Version mit Commit, Umgebung (Production, Development, Demo), läuft seit, Datenbankdatei und -größe, Bestand, .NET-Version | `GET /api/system/info` |

## Regeln der Benutzerverwaltung

| Regel | Warum | Antwort |
|---|---|---|
| Nur Admin (Policy `AdminOnly`) | Benutzer anlegen und Rechte vergeben ist die mächtigste Aktion der App | 401 / 403 |
| Das **eigene Konto** lässt sich nicht löschen oder herabstufen | Schutz vor versehentlichem Aussperren. Das macht ein anderer Admin. | 409 |
| Der **letzte Admin** bleibt immer | Ohne Admin kann niemand mehr etwas verwalten | 409 |
| Rollenwechsel und Löschen nacheinander (`SemaphoreSlim`) | Zwei Admins, die sich gleichzeitig gegenseitig herabstufen, sähen beide „es gibt noch einen“, am Ende gäbe es keinen | – |
| Nach Rollenwechsel: Sitzungen des Benutzers beenden | Das JWT enthält die Rolle. Es läuft nach höchstens 15 Minuten ab, danach braucht der Benutzer ein neues Token und bekommt dabei die neue Rolle. | – |
| Jede Änderung im Log | Nachvollziehbar, wer was geändert hat (REVIEW.md N-9), nur mit Ids, nie mit Passwörtern | – |

Die Oberfläche sperrt die betroffenen Knöpfe schon vorher („du“, letzter Admin). **Die eigentliche Prüfung macht
aber immer der Server.** Ein Test prüft deshalb auch den Fall, dass jemand mit einem noch gültigen, alten
Admin-Token den letzten Admin löschen will.

## Passwort ändern

- Das **bisherige Passwort** muss stimmen. Sonst antwortet der Server mit 400 und einem Fehler am Feld
  `currentPassword`. Die Regeln für das neue Passwort prüft Identity (8 Zeichen, Groß-, Kleinbuchstabe, Ziffer).
- Nur mit JWT (Policy `SignedInUser`), nicht mit dem Agenten-Schlüssel. Wer, steht im Token, nie im Body.
- Rate Limit wie beim Login: Mit einem gestohlenen Access Token soll niemand das alte Passwort beliebig oft raten.
- Danach sind **alle anderen Geräte abgemeldet**. Das aktuelle bleibt angemeldet: Der Endpunkt liegt unter
  `/api/auth`, dorthin schickt der Browser das Refresh-Cookie mit, und genau dieses Token bleibt.

**Gefundener Fehler beim Testen:** Zuerst wurden die anderen Refresh-Tokens nur *widerrufen*. Das andere Gerät
schickt sein widerrufenes Token aber noch einmal, und das hält die **Reuse Detection** für einen Diebstahl: Sie
beendet dann *alle* Sitzungen, auch die gerade benutzte. Jetzt werden die fremden Tokens **gelöscht**. Ein
unbekanntes Token heißt nur „abgemeldet“. Test: `Neues_Passwort_gilt_und_andere_Sitzungen_werden_beendet`.

## Verbindungen

Die Texte erklären jeden Zustand in einem Satz und sagen, was zu tun ist. Bei Blink „Neu anmelden nötig“
stehen die drei Schritte direkt auf der Seite (`dotnet run --project tools/BlinkSpike`, Passwort und PIN, dann
übernimmt der Dienst die neue Anmeldung ohne Neustart). Ausführlich: [BLINK.md](BLINK.md).

## Info

`GET /api/system/info` (nur Admin):

- **Version und Commit:** Das .NET-SDK schreibt beim Bauen in einem Git-Repo `1.0.0+<Commit>` in die
  Assembly. Die Seite zeigt die ersten 7 Zeichen. Ohne Git (z. B. im Docker-Build) fehlt der Commit.
- **Datenbank:** nur der **Dateiname**, nie der Pfad (er enthielte den Benutzernamen des Rechners), und die
  Größe von Datei plus Write-Ahead-Log (`-wal`).

## Tests

`SettingsTests`: Liste nur für Admin, Rolle ändern und zurück, sich selbst nicht, letzter Admin auch mit altem
Token, gelöschter Benutzer kann sich nicht anmelden, Passwortregeln, andere Sitzungen beendet (ohne Reuse
Detection), Info ohne Pfad.
