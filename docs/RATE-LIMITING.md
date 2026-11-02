# Rate Limiting

**Zum Lernen:** [Lernunterlage-Rate-Limiting.pdf](Lernunterlage-Rate-Limiting.pdf)

Rate Limiting begrenzt, wie viele Anfragen in einer bestimmten Zeit erlaubt sind. Wer mehr schickt, bekommt
`429 Too Many Requests` und im Header `Retry-After` die Wartezeit in Sekunden. Umgesetzt mit dem eingebauten
Rate Limiter von ASP.NET Core (`AddRateLimiter`, `UseRateLimiter`, `RequireRateLimiting`), siehe
[REVIEW.md](../REVIEW.md) Findings M-2 und M-3.

## Die Regeln

| Regel | Endpunkte | Gezählt | Standard | Schützt vor |
|---|---|---|---|---|
| `login` | `POST /api/auth/login`, `/register`, `/refresh` (ein gemeinsamer Zähler) | pro Gerät (Client-IP) | 10 pro 60 s | Passwort-Raten, Massen-Registrierung |
| `tuya` | alle Endpunkte unter `/api/tuya` | **ein** Zähler für alle | 20 pro 60 s | Aufbrauchen des Tuya-Kontingents |
| `blink-bilder` | `GET /api/blink/cameras/{id}/thumbnail` | pro Gerät (Client-IP) | 30 pro 60 s | Flut von Bildanfragen (Bilder liegen zusätzlich 60 s im Server-Cache) |
| `blink-neues-bild` | `POST /api/blink/cameras/{id}/snapshot` | pro **Kamera** (Id aus der Adresse) | 1 pro 60 s | leerer Akku durch wiederholtes „Neues Bild“, egal von welchem Gerät |

- `GET /api/status` hat **kein** Rate Limit: Er liest die Online-Liste aus dem `TuyaOnlineCache`, den der
  Tuya-Hintergrund-Abgleich füllt. Ohne Hintergrund-Abgleich fragt er Tuya höchstens alle 5 Minuten,
  egal wie oft die Status-LEDs abgefragt werden.
- `POST /api/auth/logout` hat kein Rate Limit: Abmelden soll nie scheitern.
- Verfahren: **Fixed Window** ohne Warteschlange (überzählige Anfragen bekommen sofort 429).

## Einstellen

In `backend/HomeControl.Api/appsettings.json`:

```json
"RateLimiting": {
  "Login": { "PermitLimit": 10, "WindowSeconds": 60 },
  "Tuya":  { "PermitLimit": 20, "WindowSeconds": 60 }
}
```

Die Blink-Regeln (`BlinkThumbnails` 30 pro 60 s, `BlinkSnapshot` 1 pro 60 s) stehen nur als Standardwert im Code
und lassen sich genauso überschreiben, z. B. `RateLimiting__BlinkSnapshot__WindowSeconds=120`.

Werte 0 oder kleiner verhindern den Start (Prüfung mit `ValidateOnStart`). Im Dienst lässt sich ein Wert auch
per Umgebungsvariable überschreiben, z. B. `RateLimiting__Login__PermitLimit=5`.

## Pro Gerät hinter tailscale serve

`tailscale serve` nimmt die HTTPS-Verbindung an und leitet sie an `http://127.0.0.1:5200` weiter. Für das
Backend kommt deshalb **jede** Anfrage von `127.0.0.1`. Ohne weitere Maßnahme stünden alle Geräte im selben Topf:
Ein Gerät mit vielen Fehlversuchen würde alle anderen vom Login aussperren.

Geprüft am 02.11.2026 (Tailscale 1.104.4): `tailscale serve` schickt `X-Forwarded-For` mit der Tailscale-IP
des Geräts mit und **ersetzt** dabei einen vom Client mitgeschickten Wert. Dasselbe gilt für
`Tailscale-User-Login` (fehlt bei getaggten Geräten).

Deshalb in `Program.cs`:

- `UseForwardedHeaders` ganz vorne, nur `X-Forwarded-For`, `ForwardLimit = 1`.
- `KnownProxies` = nur `127.0.0.1` und `::1`. Kommt eine Verbindung von woanders, wird der Header ignoriert,
  sonst könnte jeder mit einem erfundenen Header ein anderes Gerät spielen.
- Gezählt wird pro `Connection.RemoteIpAddress` (nach `UseForwardedHeaders` = Tailscale-IP des Geräts).

Die Regel `tuya` zählt bewusst für alle gemeinsam: Das Tuya-Kontingent gibt es nur einmal, egal von wie
vielen Geräten die Anfragen kommen.

## Reihenfolge der Middleware

```text
UseForwardedHeaders → UseExceptionHandler → UseStatusCodePages → statische Dateien
→ UseAuthentication → UseAuthorization → UseRateLimiter → Endpunkt
```

`UseRateLimiter` steht **nach** `UseAuthorization`: Eine Anfrage ohne Anmeldung an z. B. `POST /api/tuya/sync`
bekommt schon 401 und verbraucht keine der gemeinsamen Tuya-Anfragen.

## Was Rate Limiting nicht löst

Die Kontosperre (5 Fehlversuche, 5 Minuten) lässt sich weiterhin auslösen: 5 Versuche liegen unter der
Login-Grenze von 10. Rate Limiting bremst Passwort-Raten über viele Konten stark, ein gezieltes Sperren eines
einzelnen Kontos verhindert es nicht. Eine Grenze unter 5 wäre für echte Tippfehler zu streng.

## Logs

- Fehlgeschlagener Login (Warnung): `Anmeldung fehlgeschlagen (falsche Zugangsdaten), Benutzer <Id>, von <IP>`.
  Bei unbekannter E-Mail steht `Benutzer unbekannt`. Nie Passwort, Token oder die eingegebene E-Mail.
- Überschrittene Grenze (Warnung): `Rate Limit login überschritten: POST /api/auth/login von <IP>`.

Ansehen: `journalctl --user -u home-control -f`

## Tests

`backend/HomeControl.Api.Tests/RateLimitingTests.cs` (kleine Grenzen: login 3, tuya 2) und
`LoginLoggingTests.cs`. „Neues Bild“ 1× pro Minute und Kamera: `BlinkThumbnailEndpointTests.cs` (dort gilt der
Standardwert). Die normale Testfabrik setzt großzügige Grenzen (100 000), damit die vielen Logins der
übrigen Tests nicht in 429 laufen. `FakeRemoteIpStartupFilter` spielt die Verbindungsadresse, die der
TestServer sonst nicht hat.
