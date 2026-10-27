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
`tools/BlinkSpike`. Nur hier kommen E-Mail/Passwort kurzzeitig vor, und nur ein Mensch kann
die PIN eingeben.

```bash
# E-Mail und Passwort für den Spike setzen (nur lokal, in den User Secrets des Spikes)
dotnet user-secrets set "Blink:Email" "<deine-email>" --project tools/BlinkSpike
dotnet user-secrets set "Blink:Password" "<dein-passwort>" --project tools/BlinkSpike

# Spike starten
dotnet run --project tools/BlinkSpike
```

Ablauf:

1. Login mit E-Mail/Passwort.
2. Blink verlangt eine PIN (SMS oder E-Mail) – im Terminal eingeben.
3. Bei Erfolg schreibt der Spike **HardwareId** und **Refresh-Token** direkt in die User
   Secrets (nie als Textausgabe):
   - Backend (`backend/HomeControl.Api`): `Blink:HardwareId`, `Blink:InitialRefreshToken`
   - Spike selbst (`tools/BlinkSpike`): `Blink:HardwareId`, `Blink:RefreshToken` (für einen
     künftigen Lauf ohne erneute PIN-Abfrage)
4. Danach listet der Spike Netzwerke und Kameras auf, inklusive Rohdaten je Kamera (siehe
   Abschnitt 5, „Bekannte Grenzen“).

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

## 3. Wo Zugangsdaten liegen

| Wert | Ort | Warum |
|---|---|---|
| E-Mail, Passwort | Nur User Secrets von `tools/BlinkSpike`, nie im Backend | Werden nur für den Erst-Login gebraucht |
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
| `GET /api/blink/cameras` | Liste der Kameras: Name, Akku, Temperatur (falls vorhanden), Online-Status |

Ein Thumbnail-Endpunkt (`GET /api/blink/cameras/{id}/thumbnail`) ist **bewusst noch nicht**
gebaut: Home Control ist über Tailscale erreichbar, und ohne eigenen API-Schlüssel für
Home Control könnte jedes Gerät im Tailnet Kamerabilder abrufen. Das folgt erst, wenn es
eine Authentifizierung für die Home-Control-API selbst gibt.

Beide Endpunkte rufen Blink höchstens alle zwei Minuten wirklich ab (siehe
`BlinkNetCameraClient.MinPollInterval`); häufigere Klicks auf „Aktualisieren“ bekommen den
zwischengespeicherten Stand. Das schützt vor einer Sperrung durch Blink.

## 5. Bekannte Grenzen

- **Kein offizielles Feldformat.** Blink.NET liefert Kameradaten teils als rohes
  `Dictionary<string, JsonElement>` ohne festes Schema. `BlinkNetCameraClient.MapCameras`
  liest Akku, Temperatur und Online-Status über plausible, aus öffentlichen Quellen (u. a.
  `blinkpy`) bekannte Feldnamen – das ist **„best effort“ und noch nicht gegen einen echten
  Account geprüft**. Der Spike (`tools/BlinkSpike`) gibt zusätzlich zur kurzen Zusammenfassung
  die Rohdaten jeder Kamera aus, damit sich die Zuordnung bei Bedarf korrigieren lässt.
- **Kein HTTP-Statuscode von Blink.NET.** Die Bibliothek wirft bei Fehlern nur eine
  `BlinkClientException` mit Text, ohne Statuscode. Ein echtes 401 lässt sich deshalb nicht
  sicher von einem kurzen Netzwerkfehler unterscheiden. Home Control behandelt beides gleich:
  Status `loginRequired`, **keine automatische Wiederholung**. Erst ein neuer Lauf von
  `tools/BlinkSpike` (mit 2FA) behebt das.
- **Kein Thumbnail-Endpunkt.** Siehe Abschnitt 4 – folgt erst mit einem API-Schlüssel für
  Home Control. Ohnehin gilt: Ein neues Bild anfordern (`RequestNewImageAsync`) weckt
  Akku-Kameras auf und kostet Batterie; ein künftiger Thumbnail-Endpunkt soll deshalb nur das
  zuletzt bekannte Bild durchreichen, nie ein neues anfordern.
- **Clip-IDs verfallen.** Lokale Clip-IDs sind laut Blink.NET-Doku nur rund 10–20 Minuten
  gültig. Betrifft Home Control aktuell nicht (keine Clip-Endpunkte), ist aber relevant,
  falls das später dazukommt.
- **Ein Blink-Account, eine Zeile.** `BlinkCredentials` hat genau eine Zeile (feste `Id = 1`).
  Mehrere Blink-Accounts sind nicht vorgesehen.
