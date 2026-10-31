# REST API

Basisadresse beim lokalen Start: **http://localhost:5111**
Im Development-Modus gibt es eine interaktive Übersicht unter http://localhost:5111/scalar.

Alle Daten werden als JSON gesendet und empfangen. IDs sind GUIDs.

## Übersicht der Endpunkte

| Methode | Pfad | Zweck | Erfolg | Fehler |
|---|---|---|---|---|
| GET | `/api/rooms?q=` | Räume auflisten, optional suchen | 200 | – |
| GET | `/api/rooms/{id}` | Einen Raum lesen | 200 | 404 |
| POST | `/api/rooms` | Raum anlegen (**Admin**) | 201 | 400, 401, 403 |
| PUT | `/api/rooms/{id}` | Raum umbenennen (**Admin**) | 200 | 400, 401, 403, 404 |
| DELETE | `/api/rooms/{id}` | Raum löschen (mit Geräten und Ereignissen, **Admin**) | 204 | 401, 403, 404 |
| GET | `/api/devices?q=&roomId=&type=` | Geräte auflisten, suchen, filtern | 200 | 400 |
| GET | `/api/devices/{id}` | Ein Gerät lesen | 200 | 404 |
| POST | `/api/devices` | Gerät anlegen (**Admin**) | 201 | 400, 401, 403 |
| PUT | `/api/devices/{id}` | Gerät ändern (Name, Typ, Raum, **Admin**) | 200 | 400, 401, 403, 404 |
| DELETE | `/api/devices/{id}` | Gerät löschen (mit Ereignissen, **Admin**) | 204 | 401, 403, 404 |
| GET | `/api/devices/{id}/events` | Ereignisverlauf, neueste zuerst | 200 | 404 |
| GET | `/api/devices/{id}/events/{eventId}` | Ein einzelnes Ereignis dieses Geräts | 200 | 404 |
| POST | `/api/devices/{id}/events` | Ereignis speichern (**Admin**) | 201 | 400, 401, 403, 404 |
| GET | `/api/reports/daily?date=JJJJ-MM-TT` | Tagesauswertung (UTC-Tag) | 200 | 400 |
| GET | `/api/tuya/devices` | Alle Geräte aus der Tuya-Cloud mit Verknüpfung zu unseren Geräten (nur lesend) | 200 | 502, 503, 504 |
| GET | `/api/tuya/test-device/status` | Status des Tuya-Testgeräts aus der Tuya-Cloud (nur lesend) | 200 | 502, 503, 504 |
| GET | `/api/tuya/linked-devices/status` | Rohwerte **aller** mit Tuya verknüpften Geräte, speichert nichts (**angemeldet**) | 200 | 401, 503 |
| POST | `/api/tuya/sync` | Alle verknüpften Geräte lesen und `switch_1` / `doorcontact_state` übertragen (schaltet nichts, **angemeldet**) | 200 | 401, 503 |
| POST | `/api/tuya/test-device/sync` | Status lesen und `switch_1` auf das verknüpfte Gerät übertragen (schaltet nichts, **angemeldet**) | 200 | 401, 502, 503, 504 |
| POST | `/api/auth/register` | Benutzer anlegen (erster Benutzer: offen und wird Admin, danach nur **Admin**) | 201 | 400, 401, 403 |
| POST | `/api/auth/login` | Anmelden: Access Token im JSON, Refresh-Token im HTTP-only Cookie | 200 | 400, 401 |
| POST | `/api/auth/refresh` | Neues Access Token, Refresh-Token wird dabei ersetzt (Rotation) | 200 | 401 |
| POST | `/api/auth/logout` | Refresh-Token widerrufen, Cookie löschen | 204 | – |
| GET | `/api/auth/profile` | Eigenes Profil (Access Token nötig) | 200 | 401 |
| GET | `/api/status` | Status-LEDs: Tuya-Cloud erreichbar?, jedes Gerät online/offline/unbekannt (Tuya-Antwort 60 s zwischengespeichert) | 200 | – |

**Wer darf was:** Alle `GET`-Endpunkte sind standardmäßig ohne Anmeldung erreichbar (Frontend und
n8n-Agent lesen weiter wie bisher). Ist `Auth:RequireLoginForReading` an, verlangen sie ein Access
Token oder den Header `X-Api-Key` des Agenten (sonst 401); der Schlüssel erlaubt nur Lesen. Anlegen, Ändern und Löschen von Räumen, Geräten und Ereignissen
verlangt ein Access Token mit der Rolle **Admin** (`Authorization: Bearer <token>`). Ohne
gültiges Token antwortet die API mit **401**, als angemeldeter User mit **403**.
`POST /api/tuya/test-device/sync` verlangt nur eine Anmeldung (kein Admin), weil es den Gerätezustand in der Datenbank aktualisiert.

Zusätzlich antwortet `GET /` mit dem Text „Hello World!“ (Überbleibsel aus der Projektvorlage).

## DTOs (Contracts)

Die API gibt nie die Datenbank-Klassen direkt heraus, sondern eigene DTOs
(Ordner `Contracts/`). So sieht das Frontend nur, was es braucht.

### Räume

```jsonc
// RoomRequest (POST, PUT)
{ "name": "Küche" }

// RoomResponse
{ "id": "2b7ebae0-…", "name": "Küche", "deviceCount": 0 }
```

### Geräte

```jsonc
// DeviceRequest (POST, PUT) – tuyaDeviceId ist optional, leer = keine Verknüpfung
{ "name": "Wasserkocher", "type": "Socket", "roomId": "2b7ebae0-…", "tuyaDeviceId": "<TUYA-GERAETE-ID>" }

// DeviceResponse
{ "id": "b1cb3a70-…", "name": "Wasserkocher", "type": "Socket",
  "roomId": "2b7ebae0-…", "roomName": "Küche", "lastValue": null, "tuyaDeviceId": "<TUYA-GERAETE-ID>" }
```

`IsActive` ist bewusst nicht enthalten. `lastValue` bleibt `null`, solange kein Abgleich
einen Wert geliefert hat (siehe [DATENMODELL.md](DATENMODELL.md)).

`tuyaDeviceId` muss aus Buchstaben und Ziffern bestehen (höchstens 64 Zeichen), und jedes
Tuya-Gerät darf nur mit **einem** Gerät verknüpft sein. Sonst antwortet die API mit 400 und
einer Feldmeldung bei `tuyaDeviceId`. Wird die Verknüpfung beim Bearbeiten geändert oder
entfernt, setzt die API `lastValue` und `IsActive` zurück, weil der alte Wert nicht mehr zu
diesem Gerät gehört.

### Ereignisse

```jsonc
// DeviceEventRequest (POST)
{ "action": "temperature_changed", "value": "21,5 °C" }   // value ist optional

// DeviceEventResponse
{ "id": "253714e5-…", "timestamp": "2026-10-26T17:45:52.918+00:00",
  "action": "turned_on", "value": null }
```

Den Zeitstempel setzt der Server in UTC. Ein Ereignis wird nur gespeichert. Es schaltet
nichts und verändert das Gerät nicht.

Nach dem Anlegen antwortet die API mit **201** und dem neuen Ereignis. Der `Location`-Header
zeigt auf die Adresse, unter der man genau dieses Ereignis wieder abrufen kann:
`/api/devices/{id}/events/{eventId}`.

Ein einzelnes Ereignis wird nur gefunden, wenn es **zu dem angegebenen Gerät gehört**.
Unbekanntes Gerät, unbekanntes Ereignis oder ein Ereignis eines anderen Geräts → 404.

### Tagesauswertung

```jsonc
// DailyReportResponse
{
  "date": "2026-10-26",
  "currentDeviceCount": 1,
  "eventCount": 1,
  "actions": [ { "action": "turned_on", "count": 1 } ],
  "topDevices": [ { "deviceId": "b1cb3a70-…", "deviceName": "Wasserkocher", "eventCount": 1 } ]
}
```

### Tuya-Geräteliste

```jsonc
// TuyaCloudDeviceResponse (GET /api/tuya/devices), sortiert nach Name
[ { "tuyaId": "<TUYA-GERAETE-ID>", "name": "Bad Licht", "productName": "Wi-Fi BLE Socket",
    "category": "cz", "online": true,
    "linkedDeviceId": "b1cb3a70-…", "linkedDeviceName": "Licht Bad" } ]
```

`online` ist das, was Tuya meldet (`null`, wenn Tuya nichts dazu sagt). `linkedDeviceId` und
`linkedDeviceName` sind `null`, solange das Tuya-Gerät mit keinem unserer Geräte verknüpft ist.
Den `local_key` aus der Tuya-Antwort gibt die API **nie** heraus. Braucht nur `Tuya:ClientId`
und `Tuya:ClientSecret`, kein Testgerät.

### Tuya-Testgerät

```jsonc
// TuyaDeviceStatusResponse
{ "retrievedAtUtc": "2026-10-27T08:21:11.921+00:00",
  "status": [ { "code": "switch_1", "value": false } ],
  "note": "Werte, die die Tuya-Cloud zum Abrufzeitpunkt für das Gerät kennt. …" }
```

`value` wird unverändert von Tuya übernommen und kann je nach Code ein Boolean, eine Zahl
oder ein Text sein. Einrichtung, Fehlerfälle und Grenzen stehen in [TUYA.md](TUYA.md).

```jsonc
// TuyaDeviceSyncResponse (POST /api/tuya/test-device/sync)
{ "retrievedAtUtc": "2026-10-27T08:21:11.921+00:00",
  "status": [ { "code": "switch_1", "value": true } ],
  "note": "…",
  "syncResult": "changed" }
```

`syncResult` sagt, was in der Datenbank passiert ist: `changed` (Gerät aktualisiert und
Ereignis gespeichert), `unchanged`, `notLinked` (kein Gerät eingestellt), `deviceNotFound`
oder `unknownValue` (switch_1 fehlt oder ist kein Boolean). Nur bei `changed` wird etwas
gespeichert. Fehlerantworten sind dieselben wie beim GET.

### Status (LEDs)

`GET /api/status` antwortet **immer mit 200**. Ist Tuya nicht erreichbar, steht das in `tuya`
(rote LED) und die Geräte sind `unknown`. Es ist kein Fehler dieses Endpunkts.

```json
{
  "checkedAtUtc": "2026-10-31T13:05:00+00:00",
  "tuya": "online",
  "devicesOnline": 3,
  "devicesOffline": 1,
  "devicesUnknown": 2,
  "devices": [
    { "id": "…", "name": "Licht Wohnzimmer", "state": "online" },
    { "id": "…", "name": "Steckdose Flur", "state": "offline" },
    { "id": "…", "name": "Thermometer Bad", "state": "unknown" }
  ]
}
```

| Feld | Werte |
|---|---|
| `tuya` | `online` (Cloud erreichbar), `offline` (nicht erreichbar), `notConfigured` (Zugangsdaten fehlen) |
| `devices[].state` | `online`/`offline` laut Tuya, `unknown` = nicht verknüpft, Tuya meldet nichts oder ist nicht erreichbar |

Die Tuya-Antwort merkt sich der Server 60 Sekunden (`TuyaOnlineCache`). So entsteht höchstens
ein Aufruf pro Minute bei der Tuya-Cloud, egal wie viele Geräte die App offen haben. Auch für den
KI-Agenten geeignet: „Welche Geräte sind offline?“

## Validierung

| Feld | Regel |
|---|---|
| Raumname, Gerätename | Pflicht, nach dem Trimmen höchstens 100 Zeichen |
| Gerätetyp (neues Gerät) | Genau einer von: `Light`, `Socket`, `TemperatureSensor`, `DoorSensor`, `Switch` |
| Gerätetyp (Bearbeiten) | Wie oben, oder der bisher gespeicherte Typ bleibt unverändert |
| `roomId` beim Gerät | Der Raum muss existieren |
| Ereignis `action` | Pflicht, genau einer von: `turned_on`, `turned_off`, `temperature_changed`, `door_opened`, `door_closed` |
| Ereignis `value` | Optional, höchstens 200 Zeichen. Leer bedeutet „kein Wert“ |
| Report `date` | Pflicht, genau im Format `JJJJ-MM-TT` und ein gültiges Datum |

Groß- und Kleinschreibung zählt bei Typen und Aktionen: `light` ist ungültig, `Light` ist richtig.

## Fehlerantworten (ProblemDetails)

Alle Fehler kommen im Format **ProblemDetails** (`Content-Type: application/problem+json`):

| Status | Wann | Beispiel `title` |
|---|---|---|
| 400 | Ungültige Eingabe, mit Meldung pro Feld unter `errors` | „One or more validation errors occurred.“ |
| 400 | Kaputtes JSON oder fehlender Body | „Ungültige Anfrage.“ |
| 404 | Unbekannter Raum, unbekanntes Gerät oder Ereignis | „Raum nicht gefunden.“ / „Gerät nicht gefunden.“ / „Ereignis nicht gefunden.“ |
| 401 | Schreibender Endpunkt ohne gültiges Access Token (fehlt, abgelaufen, falsch signiert) | „Unauthorized“ |
| 403 | Angemeldet, aber nicht Admin | „Forbidden“ |
| 404 | Unbekannte Route oder ungültige GUID in der URL | „Not Found“ |
| 500 | Unerwarteter Fehler im Server | „Ein unerwarteter Fehler ist aufgetreten.“ |
| 502, 503, 504 | Nur Tuya: nicht eingerichtet, abgelehnt, nicht erreichbar (siehe [TUYA.md](TUYA.md)) | „Tuya ist nicht eingerichtet.“ / „Tuya hat die Anfrage abgelehnt.“ |

Bei 500 bekommt der Client keine internen Details. Die vollständige Fehlermeldung mit
Stacktrace steht nur im Server-Log.

Beispiel für einen Validierungsfehler:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "name": ["Bitte einen Namen eingeben."] },
  "traceId": "00-…"
}
```

## Suche und Filter

**Räume:** `GET /api/rooms?q=küche` findet alle Räume, deren Name den Text enthält.

**Geräte:** `GET /api/devices?q=…&roomId=…&type=…`

- `q`: Text im Gerätenamen
- `roomId`: nur Geräte in diesem Raum
- `type`: nur Geräte dieses Typs

Regeln:

- Alle Filter sind **optional** und lassen sich **kombinieren**. Sie gelten gleichzeitig (UND).
- Fehlende oder leere Filter (auch nur Leerzeichen) bedeuten: keine Einschränkung.
- Der Suchtext wird an den Rändern getrimmt.
- `%` und `_` im Suchtext werden wörtlich gesucht, nicht als Platzhalter.
- Ungültige `roomId` (keine GUID) → 400 mit Feldmeldung `roomId`.
- Gültige, aber unbekannte `roomId` → 200 mit leerer Liste.
- Unbekannter `type` → 400 mit Feldmeldung `type`.

Die Abfrage läuft mit LINQ und `EF.Functions.Like`. EF Core schickt die Werte als
Parameter an die Datenbank (`LIKE @pattern`), nicht als zusammengesetzten SQL-Text.

### Einschränkung von SQLite bei Groß-/Kleinschreibung

SQLite ignoriert bei `LIKE` die Groß-/Kleinschreibung **nur für die Buchstaben A–Z**.
Bei Umlauten und ß muss die Schreibweise genau stimmen:

| Suche | Findet | Ergebnis |
|---|---|---|
| `lampe` | „Deckenlampe“ | ja |
| `LAMPE` | „Deckenlampe“ | ja |
| `küche` | „Küche“ | ja (gleiche Schreibweise beim ü) |
| `KÜCHE` | „Küche“ | **nein** (Ü ≠ ü) |
| `ölradiator` | „Ölradiator“ | **nein** (ö ≠ Ö) |
| `FUSSBODEN` | „Fußbodenheizung“ | **nein** (SS ≠ ß) |

Die Suche ist also **nicht vollständig Unicode-fähig**.

Sortierung: Räume nach Name, Geräte nach Raumname und dann Gerätename.

## Tagesauswertung

`GET /api/reports/daily?date=2026-10-26`

- Ausgewertet wird ein **UTC-Tag**: von **00:00 UTC (einschließlich)** bis
  **00:00 UTC am Folgetag (ausschließlich)**. In Deutschland beginnt dieser Tag
  also um 01:00 Uhr (Winterzeit) bzw. 02:00 Uhr (Sommerzeit).
- `currentDeviceCount` ist der **aktuelle** Gerätebestand, nicht der Bestand am gewählten Tag.
  Gelöschte Geräte und ihre Ereignisse fehlen auch in früheren Tagen.
- `eventCount`: Anzahl der Ereignisse an diesem Tag.
- `actions`: jede Aktion mit Anzahl, häufigste zuerst.
- `topDevices`: höchstens fünf Geräte mit den meisten Ereignissen. Geräte ohne Ereignisse
  fehlen. Bei Gleichstand wird nach Name, danach nach ID sortiert.
- Fehlendes oder ungültiges Datum → 400 mit Feldmeldung `date`.

## Beispiele mit curl

Für Bash (Linux, macOS, Git Bash). `<ROOM-ID>`, `<DEVICE-ID>` und `<EVENT-ID>` durch die IDs
aus den Antworten ersetzen.

```bash
# Raum anlegen
curl -X POST http://localhost:5111/api/rooms \
  -H "Content-Type: application/json" -d '{"name":"Küche"}'

# Räume suchen (--data-urlencode kodiert Umlaute richtig)
curl -G http://localhost:5111/api/rooms --data-urlencode "q=kü"

# Gerät anlegen
curl -X POST http://localhost:5111/api/devices \
  -H "Content-Type: application/json" \
  -d '{"name":"Wasserkocher","type":"Socket","roomId":"<ROOM-ID>"}'

# Geräte filtern: Typ Steckdose UND Name enthält "wasser"
curl "http://localhost:5111/api/devices?type=Socket&q=wasser"

# Ereignis speichern (-i zeigt den Location-Header), einzeln abrufen und Verlauf lesen
curl -i -X POST http://localhost:5111/api/devices/<DEVICE-ID>/events \
  -H "Content-Type: application/json" -d '{"action":"turned_on"}'
curl http://localhost:5111/api/devices/<DEVICE-ID>/events/<EVENT-ID>
curl http://localhost:5111/api/devices/<DEVICE-ID>/events

# Tagesauswertung
curl "http://localhost:5111/api/reports/daily?date=2026-10-26"

# Status des Tuya-Testgeräts lesen (schaltet nichts)
curl -i http://localhost:5111/api/tuya/test-device/status

# Validierungsfehler ansehen (-i zeigt Status und Header)
curl -i -X POST http://localhost:5111/api/rooms \
  -H "Content-Type: application/json" -d '{"name":""}'
```

## Bekannte Grenzen

- 404 für unbekannte Routen hat den englischen Standardtitel „Not Found“.
- Es gibt keine Anmeldung. Jeder, der die API erreicht, kann Daten ändern.
