# Blink-Anbindung

Home Control liest (nur lesend) Status und Kameras eines Blink-Systems. Wie bei Tuya wird
nie geschaltet – hier zusätzlich: nie scharf-/unscharfgeschaltet und nie ein Clip gelöscht.

## 1. Wichtig: keine offizielle API

Blink (Amazon) veröffentlicht keine öffentliche API. Alles, was hier passiert, basiert auf
reverse engineering – u. a. über das NuGet-Paket
[Blink.NET](https://github.com/bvdcode/Blink.NET) (fest auf Version **1.0.9**, siehe
`backend/HomeControl.Api/HomeControl.Api.csproj` und `tools/BlinkSpike/BlinkSpike.csproj`).
Ein automatisches Update auf eine neuere Version könnte unbemerkt Felder oder Endpunkte
ändern, deshalb ist die Version bewusst fixiert und wird nur von Hand angehoben.

Seit November 2025 läuft der Login über OAuth 2.0 + PKCE mit 2FA (PIN per SMS oder E-Mail),
danach über ein Refresh-Token.

## 2. Einrichtung (Erst-Login mit 2FA)

Der Erst-Login läuft **nicht** im Backend, sondern über das separate Konsolenprojekt
`tools/BlinkSpike`. Nur hier kommt E-Mail/Passwort kurzzeitig vor, und nur ein Mensch kann
die PIN eingeben. Das **Passwort wird nirgends gespeichert**: Der Spike fragt es bei jedem
Lauf verdeckt im Terminal ab (Eingabe erscheint als `*`), nie in User Secrets oder einer Datei.

```bash
# Nur die E-Mail vorher setzen (nur lokal, in den User Secrets des Spikes)
dotnet user-secrets set "Blink:Email" "<deine-email>" --project tools/BlinkSpike

# Spike starten – fragt das Passwort verdeckt im Terminal ab
dotnet run --project tools/BlinkSpike
```

> Aus einer früheren Version könnte noch ein gespeichertes Passwort in den Spike-User-Secrets
> stehen. Falls vorhanden, bitte entfernen:
> ```bash
> dotnet user-secrets remove "Blink:Password" --project tools/BlinkSpike
> ```

Ablauf:

1. Login mit E-Mail (User Secrets) und Passwort (verdeckte Eingabe im Terminal).
2. Blink verlangt eine PIN (SMS oder E-Mail) – im Terminal eingeben.
3. Bei Erfolg schreibt der Spike **HardwareId** und **Refresh-Token** direkt in die User
   Secrets (nie als Textausgabe):
   - Backend (`backend/HomeControl.Api`): `Blink:HardwareId`, `Blink:InitialRefreshToken`
   - Spike selbst (`tools/BlinkSpike`): `Blink:HardwareId`, `Blink:RefreshToken` (für einen
     künftigen Lauf ohne erneute PIN-Abfrage)
4. Danach listet der Spike Netzwerke und Kameras auf. Je Kamera werden die vorhandenen
   Feldnamen und die Werte einiger ausgewählter Felder ausgegeben (`battery`, `battery_state`,
   `temp`, `temperature`, `status`, `enabled`, `armed`, `wifi_strength`) – bewusst nicht die
   vollständigen Rohdaten, siehe Abschnitt 5, „Bekannte Grenzen“.

Die **HardwareId** ist eine UUID und muss stabil bleiben – ändert sie sich, verlangt Blink
beim nächsten Login wieder eine neue PIN. Der Spike verwendet deshalb bei einem erneuten
Lauf automatisch die zuvor gespeicherte HardwareId weiter.

### Backend starten

Danach reicht ein normaler Start:

```bash
cd backend/HomeControl.Api
dotnet run
```

Beim ersten Start übernimmt `BlinkCredentialStore` den Bootstrap-Wert
(`Blink:InitialRefreshToken`) aus den User Secrets in die Datenbank (verschlüsselt, siehe
Abschnitt 3). Ab da zählt nur noch die Datenbank.

### Bootstrap-Token entfernen

Sobald das Backend einmal erfolgreich gestartet ist (Log-Zeile beim ersten `/api/blink/...`-
Aufruf, oder einfach prüfen, dass `GET /api/blink/status` nicht mehr `notConfigured` meldet),
kann der Bootstrap-Wert wieder aus den Backend-User-Secrets entfernt werden:

```bash
dotnet user-secrets remove "Blink:InitialRefreshToken" --project backend/HomeControl.Api
```

`Blink:HardwareId` bleibt stehen (wird bei jedem Login gebraucht, ist aber kein Geheimnis).

### Neu-Login nach einem Fehler (ohne Neustart)

Zeigt `GET /api/blink/status` `loginRequired`, reicht es, `tools/BlinkSpike` erneut
auszuführen (es verwendet automatisch dieselbe HardwareId weiter). Der neue
`Blink:InitialRefreshToken` wird **ohne Neustart des Backends** übernommen: Beim nächsten
Aufruf von `/api/blink/status` oder `/api/blink/cameras` erkennt `BlinkCredentialStore` am
Hash, dass sich der Wert in den User Secrets geändert hat, übernimmt ihn in die Datenbank und
versucht den Login erneut. Ein unveränderter Bootstrap-Wert überschreibt dabei nie ein
zwischenzeitlich über `OnTokenRefreshed` rotiertes, neueres Token.

**Kein Login-Kreisel:** Scheitert ein Abruf *nach* erfolgreichem Login (Blink lehnt die Sitzung ab),
merkt sich `BlinkNetCameraClient` das gerade benutzte Token als verbraucht (`activeRefreshToken` →
`lastFailedRefreshToken`). Weitere Aufrufe melden dann nur `loginRequired`, ohne Blink erneut mit
demselben Token zu fragen. Erst ein neues Token (BlinkSpike) löst wieder einen Login aus. Wichtig,
seit die Status-Leiste im Dashboard `/api/blink/status` bei jedem Öffnen der App abfragt.

## 3. Wo Zugangsdaten liegen

| Wert | Ort | Warum |
|---|---|---|
| E-Mail | User Secrets von `tools/BlinkSpike`, nie im Backend | Nur für den Login im Spike gebraucht |
| Passwort | **Nirgends.** Verdeckte Eingabe im Terminal bei jedem Spike-Lauf | Wird nur für den Moment des Logins gebraucht, nie gespeichert |
| HardwareId | User Secrets von `backend/HomeControl.Api` (`Blink:HardwareId`) | Muss stabil bleiben, ist aber kein Geheimnis |
| Refresh-Token (Bootstrap) | User Secrets von `backend/HomeControl.Api` (`Blink:InitialRefreshToken`), nur bis zur ersten Übernahme in die Datenbank | Startwert für `BlinkCredentialStore` |
| Refresh-Token (laufend) | SQLite, Tabelle `BlinkCredentials`, **verschlüsselt** über die ASP.NET Data Protection API | Rotiert bei jedem Login/Refresh (`OnTokenRefreshed`), gehört deshalb in eine beschreibbare, nicht ins Repo eingecheckte Stelle |

Die Data-Protection-Schlüssel (ohne die ist der verschlüsselte Wert in der Datenbank
wertlos) liegen **außerhalb** des Repos, fest unter:

```
~/.local/share/HomeControl/dataprotection-keys
```

(`Environment.SpecialFolder.LocalApplicationData` + `HomeControl/dataprotection-keys`, siehe
`Program.cs`). `SetApplicationName("HomeControl")` sorgt dafür, dass der Schlüssel unabhängig
vom Pfad des Checkouts gleich bleibt – ein Branch-Wechsel oder ein Git-Worktree ändert daran
nichts.

## 4. Endpunkte

Nur lesend, beide unter `/api/blink`:

| Endpunkt | Zweck |
|---|---|
| `GET /api/blink/status` | `state`: `connected`, `loginRequired` oder `notConfigured`, plus Zeitpunkt des letzten erfolgreichen Abrufs |
| `GET /api/blink/cameras` | Liste der Kameras: Name, Akku (falls vorhanden). Online-Status und Temperatur stehen vorerst immer auf `null`, siehe Abschnitt 5 |

Ein Thumbnail-Endpunkt (`GET /api/blink/cameras/{id}/thumbnail`) ist **bewusst noch nicht**
gebaut: Home Control ist über Tailscale erreichbar, und ohne eigenen API-Schlüssel für
Home Control könnte jedes Gerät im Tailnet Kamerabilder abrufen. Das folgt erst, wenn es
eine Authentifizierung für die Home-Control-API selbst gibt.

**Beide Endpunkte lösen einen echten Abruf bei Blink aus**, auch `GET /api/blink/status` (er
ruft intern dieselbe Netzwerk-/Kameraliste ab, nur ohne die Kameras zurückzugeben). Das
passiert höchstens alle zwei Minuten (siehe `BlinkNetCameraClient.MinPollInterval`);
häufigere Aufrufe bekommen den zwischengespeicherten Stand. Das schützt vor einer Sperrung
durch Blink. Ist die Blink-Cloud nicht erreichbar oder antwortet nicht rechtzeitig, liefern
beide Endpunkte `502` bzw. `504` (ProblemDetails) statt eines allgemeinen Serverfehlers.

## 5. Bekannte Grenzen

- **Kein offizielles Feldformat.** Blink.NET liefert Kameradaten teils als rohes
  `Dictionary<string, JsonElement>` ohne festes Schema. `BlinkNetCameraClient.MapCameras`
  liest Id, Name und Akku über plausible, aus öffentlichen Quellen (u. a. `blinkpy`) bekannte
  Feldnamen – das ist **„best effort“ und noch nicht gegen einen echten Account geprüft**.
  **Online-Status und Temperatur liefert `GET /api/blink/cameras` deshalb vorerst immer als
  `null`**: Die naheliegenden Felder `enabled`/`armed` sind bei Blink Einstellungen
  (Bewegungserkennung bzw. scharf/unscharf), kein Verbindungsstatus der Kamera – sie als
  „online“ auszugeben wäre irreführend. Der Spike (`tools/BlinkSpike`) gibt je Kamera die
  vorhandenen Feldnamen sowie die Werte einiger Kandidaten aus (`battery`, `battery_state`,
  `temp`, `temperature`, `status`, `enabled`, `armed`, `wifi_strength`), damit sich
  `MapCameras` anhand echter Daten korrigieren lässt.
- **Kein HTTP-Statuscode von Blink.NET.** Die Bibliothek wirft bei Fehlern nur eine
  `BlinkClientException` mit Text, ohne Statuscode. Ein echtes 401 lässt sich deshalb nicht
  sicher von einem kurzen Netzwerkfehler unterscheiden. Home Control behandelt beides gleich:
  Status `loginRequired`, **keine automatische Wiederholung bei Blink**. Ein neuer Lauf von
  `tools/BlinkSpike` (mit 2FA) behebt das – **ohne dass das Backend neu gestartet werden muss**,
  siehe „Neu-Login nach einem Fehler“ in Abschnitt 2. Auch ein nicht mehr entschlüsselbares
  Token (z. B. fehlender Data-Protection-Schlüssel, `CryptographicException`) führt zu
  `loginRequired` statt zu einem Serverfehler.
- **Kein Thumbnail-Endpunkt.** Siehe Abschnitt 4 – folgt erst mit einem API-Schlüssel für
  Home Control. Ohnehin gilt: Ein neues Bild anfordern (`RequestNewImageAsync`) weckt
  Akku-Kameras auf und kostet Batterie; ein künftiger Thumbnail-Endpunkt soll deshalb nur das
  zuletzt bekannte Bild durchreichen, nie ein neues anfordern.
- **Clip-IDs verfallen.** Lokale Clip-IDs sind laut Blink.NET-Doku nur rund 10–20 Minuten
  gültig. Betrifft Home Control aktuell nicht (keine Clip-Endpunkte), ist aber relevant,
  falls das später dazukommt.
- **Ein Blink-Account, eine Zeile.** `BlinkCredentials` hat genau eine Zeile (feste `Id = 1`).
  Mehrere Blink-Accounts sind nicht vorgesehen.
