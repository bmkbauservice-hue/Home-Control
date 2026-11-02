# Blink-Anbindung

Home Control liest (nur lesend) Status und Kameras eines Blink-Systems. Wie bei Tuya wird
nie geschaltet – hier zusätzlich: nie scharf-/unscharfgeschaltet und nie ein Clip gelöscht.

## 1. Wichtig: keine offizielle API

Blink (Amazon) veröffentlicht keine öffentliche API. Alles, was hier passiert, basiert auf
reverse engineering. Zwei Teile:

| Teil | Technik | Wofür |
|---|---|---|
| `tools/BlinkSpike` | NuGet-Paket [Blink.NET](https://github.com/bvdcode/Blink.NET), fest auf **1.0.9** | Nur der Erst-Login mit E-Mail, Passwort und 2FA-PIN |
| Backend (`Blink/BlinkHttpCameraClient.cs`) | **eigener schlanker HttpClient** | Token erneuern, Kameras lesen |

**Warum nicht mehr Blink.NET im Backend?** Am 02.11.2026 antwortete Blink auf die Kameraliste von
Blink.NET 1.0.9 (`/network/{id}/cameras` mit altem Android-App-Build im Header) mit
`426 Upgrade Required – An app update is required`. Eine neuere Version von Blink.NET gibt es nicht.
Das aktuelle [blinkpy](https://github.com/fronzbot/blinkpy) liest stattdessen den **Homescreen v3**
und schickt **keinen** App-Build-Header. Genau so arbeitet jetzt das Backend (REVIEW.md M-5):

1. `POST https://api.oauth.blink.com/oauth/token` mit dem Refresh-Token (`client_id` `ios`, HardwareId)
2. `GET https://rest-prod.immedia-semi.com/api/v1/users/tier_info` → Server (Tier) und Account-Id
3. `GET https://rest-{tier}.immedia-semi.com/api/v3/accounts/{account}/homescreen` → alle Kameras

Seit November 2025 läuft der Login über OAuth 2.0 + PKCE mit 2FA (PIN per SMS oder E-Mail),
danach über ein Refresh-Token.

## 2. Einrichtung (Erst-Login mit 2FA)

Der Erst-Login läuft **nicht** im Backend, sondern über das Konsolenprojekt `tools/BlinkSpike`.
Nur hier kommen E-Mail und Passwort vor, und nur ein Mensch kann die PIN eingeben.

```bash
cd ~/Schreibtisch/Umschulung/Home-Control
dotnet run --project tools/BlinkSpike
```

Ablauf:

1. Die erste Zeile zeigt den Stand („BlinkSpike, Stand 02.11.2026 …“). So ist klar, welcher Code läuft.
2. **HardwareId:** Beim allerersten Start erzeugt und **sofort** gespeichert, danach bei jedem Lauf
   dieselbe (auch nach Fehlversuchen). Für Blink ist eine neue HardwareId ein neues Gerät.
3. **E-Mail:** Fehlt sie, fragt der Spike danach und merkt sie sich (über stdin, nicht in der Shell-History).
4. **Passwort:** verdeckte Eingabe (`*`), wird nirgends gespeichert.
5. **PIN** per SMS oder E-Mail eingeben.
6. Bei Erfolg schreibt der Spike `Blink:HardwareId` und `Blink:InitialRefreshToken` in die User
   Secrets des **Backends**, als JSON über stdin an `dotnet user-secrets set --id …`, nie als
   Kommandozeilenargument (REVIEW.md M-4). Der Spike selbst behält **kein** Token.
7. Danach listet er Netzwerke und Kameras auf (Blink.NET; die Kameraliste kann dort mit 426 scheitern,
   das betrifft nur die Ausgabe des Spikes, nicht das Backend).

Optionen:

| Aufruf | Wirkung |
|---|---|
| `dotnet run --project tools/BlinkSpike -- --email-pruefen` | Zeigt nur Länge, Domain und Auffälligkeiten der gespeicherten E-Mail. Kein Login. |
| `dotnet run --project tools/BlinkSpike -- --neue-email` | Fragt die E-Mail neu ab. |

### Fehlversuche

Laut [Blink-Hilfe](https://support.blinkforhome.com/en_US/i-cant-access-my-account) wird das Konto
nach **5 Fehlversuchen 10 Minuten** gesperrt, beim 5. Versuch kommt eine E-Mail zum Zurücksetzen des
Passworts. Blink.NET meldet jede Antwort 400/401/403 der Anmeldeseite als „abgelehnt“, ein falsches
Passwort lässt sich also nicht von einer Sperre unterscheiden. Deshalb: nicht sofort wiederholen,
erst in der Blink-App mit denselben Daten anmelden, dann 15 Minuten warten.

### Übernahme ins Backend

Beim ersten Abruf (Übersicht öffnen oder `GET /api/blink/status`) übernimmt `BlinkCredentialStore`
den Startwert in die Datenbank, verschlüsselt (Abschnitt 3). Bei der ersten Token-Erneuerung tauscht
Blink das Refresh-Token aus. Das neue wird **sofort** verschlüsselt gespeichert, der Startwert ist
danach bei Blink ungültig.

### Startwert entfernen

Sobald `GET /api/blink/status` `connected` meldet, steht im Log einmal die Mahnung mit dem Befehl:

```bash
dotnet user-secrets remove "Blink:InitialRefreshToken" --project backend/HomeControl.Api
```

`Blink:HardwareId` bleibt stehen (wird bei jeder Token-Erneuerung gebraucht, ist kein Geheimnis).

### Neu-Login nach einem Fehler (ohne Neustart)

Zeigt `GET /api/blink/status` `loginRequired`, `tools/BlinkSpike` erneut ausführen. Der neue Startwert
wird beim nächsten Abruf übernommen, ohne Neustart: Der Dienst liest die User Secrets mit
`reloadOnChange`, und `BlinkCredentialStore` erkennt den neuen Wert am Hash.

**Kein Login-Kreisel:** Lehnt Blink ein Refresh-Token ab (401/400 bei der Erneuerung) oder lehnt den
Homescreen auch nach **einer** Erneuerung noch mit 401 ab, merkt sich der Client das Token als
verbraucht und fragt Blink damit nie wieder. Erst ein neues Token (BlinkSpike) löst einen neuen
Versuch aus. `426 Upgrade Required` und andere Fehler gelten dagegen **nicht** als Login-Problem.

## 3. Wo Zugangsdaten liegen

| Wert | Ort | Warum |
|---|---|---|
| E-Mail | User Secrets von `tools/BlinkSpike` (vom Spike abgefragt, über stdin gespeichert), nie im Backend | Nur für den Login im Spike gebraucht |
| Passwort | **Nirgends.** Verdeckte Eingabe im Terminal bei jedem Spike-Lauf | Wird nur für den Moment des Logins gebraucht, nie gespeichert |
| HardwareId | User Secrets von Backend und Spike (`Blink:HardwareId`) | Muss stabil bleiben, ist aber kein Geheimnis |
| Refresh-Token (Bootstrap) | User Secrets von `backend/HomeControl.Api` (`Blink:InitialRefreshToken`), nur bis zur ersten Übernahme in die Datenbank | Startwert für `BlinkCredentialStore` |
| Refresh-Token (laufend) | SQLite, Tabelle `BlinkCredentials`, **verschlüsselt** über die ASP.NET Data Protection API | Rotiert bei jeder Erneuerung und wird sofort gespeichert, gehört deshalb in eine beschreibbare, nicht ins Repo eingecheckte Stelle |
| Access Token | nur im Arbeitsspeicher des Dienstes | Gilt etwa eine Stunde, wird 60 Sekunden vorher erneuert (nur eine Erneuerung gleichzeitig) |

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
| `GET /api/blink/cameras` | Liste der Kameras aus dem Homescreen (`cameras`, `owls`, `doorbells`): Id, Name, Akku (`ok`/`low`). Online-Status und Temperatur stehen vorerst auf `null`, siehe Abschnitt 5 |

Ein Endpunkt für Vorschaubilder (`GET /api/blink/cameras/{id}/thumbnail`) folgt als nächster Schritt
(Branch `feature/blink-bilder`): nur mit JWT-Anmeldung, nicht mit dem Agenten-Schlüssel, und nur das
zuletzt vorhandene Bild, nie ein neues.

**Beide Endpunkte lösen einen echten Abruf bei Blink aus**, auch `GET /api/blink/status` (er
ruft intern denselben Homescreen ab, nur ohne die Kameras zurückzugeben). Das
passiert höchstens alle zwei Minuten (siehe `BlinkHttpCameraClient.MinPollInterval`);
häufigere Aufrufe bekommen den zwischengespeicherten Stand. Das schützt vor einer Sperrung
durch Blink. Ist die Blink-Cloud nicht erreichbar, antwortet nicht rechtzeitig oder lehnt ab (z. B. `426`), liefern
beide Endpunkte `502` bzw. `504` (ProblemDetails) statt eines allgemeinen Serverfehlers.

## 5. Bekannte Grenzen

- **Inoffizielle Schnittstelle.** Blink kann Endpunkte jederzeit ändern (wie am 02.11.2026 mit `426`
  bei der alten Kameraliste). Dann muss `BlinkHttpCameraClient` angepasst werden. Vorbild ist das
  aktive Projekt blinkpy.
- **Feldnamen geprüft (02.11.2026, echtes Konto).** Der Homescreen enthält u. a. `cameras`, `owls`,
  `doorbells`, `sync_modules`, `networks`. Je Kamera: `id`, `name`, `battery`, `status`, `signals`,
  `thumbnail`, `updated_at`, `serial`, `enabled`, `local_storage_enabled` u. a. Beim ersten Abruf nach
  dem Start stehen nur die **Namen** im Log, nie Werte.
- **Online und Temperatur noch `null`.** `status` und `signals` sind vorhanden, ihre Werte (z. B.
  Fahrenheit oder Celsius bei `signals.temp`) sind aber noch nicht an echten Daten geprüft. Nicht raten.
- **Noch kein Thumbnail-Endpunkt.** Siehe Abschnitt 4. Ein neues Bild anfordern weckt Akku-Kameras
  auf und kostet Batterie; der Endpunkt reicht deshalb nur das zuletzt bekannte Bild durch.
- **Clip-IDs verfallen.** Lokale Clip-IDs sind laut Blink.NET-Doku nur rund 10–20 Minuten
  gültig. Betrifft Home Control aktuell nicht (keine Clip-Endpunkte), ist aber relevant,
  falls das später dazukommt.
- **Ein Blink-Account, eine Zeile.** `BlinkCredentials` hat genau eine Zeile (feste `Id = 1`).
  Mehrere Blink-Accounts sind nicht vorgesehen.
