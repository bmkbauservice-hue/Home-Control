# ASP.NET Core Identity in Home Control

**Zum Lernen:** [Lernunterlage-Identity.pdf](Lernunterlage-Identity.pdf)

Identity übernimmt die Benutzerverwaltung: Benutzer anlegen, Passwörter prüfen und hashen, Rollen,
Sperre nach Fehlversuchen. Passwörter werden **nicht selbst gehasht** (`UserManager.CreateAsync(user, password)`).
Die Anmeldung selbst läuft weiter mit **JWT**, siehe [AUTHENTIFIZIERUNG.md](AUTHENTIFIZIERUNG.md).

> **Stand 31.10.2026:** Gebaut und getestet (`./pruefen.sh`: Build ohne Warnung, 103/103 Tests grün).
> Die Migration `IdentityUmgestellt` ist im Repo. Erklärung: [Lernunterlage-Pruefen-und-Migration.pdf](Lernunterlage-Pruefen-und-Migration.pdf).

## Bausteine

| Baustein | Wo | Aufgabe |
|---|---|---|
| `User : IdentityUser<Guid>` | `Models/User.cs` | Benutzer. Id, UserName, Email, PasswordHash, SecurityStamp, Lockout kommen von Identity. Nur `CreatedAtUtc` und die Refresh-Tokens sind eigene Felder |
| `IdentityDbContext<User, IdentityRole<Guid>, Guid>` | `Data/ApplicationDbContext.cs` | Bringt die Identity-Tabellen mit. Rooms, Devices, DeviceEvents, BlinkCredentials, RefreshTokens bleiben |
| `AddIdentityCore<User>()` `.AddRoles<IdentityRole<Guid>>()` `.AddEntityFrameworkStores<ApplicationDbContext>()` | `Auth/IdentityRegistration.cs` | Registriert `UserManager`, `RoleManager`, `PasswordHasher` und die Regeln |
| `AuthService` | `Auth/AuthService.cs` | Register, Login, Refresh, Logout. Nutzt `UserManager` und `RoleManager`. Ist für den Test von HTTP getrennt |
| `TokenService` | `Auth/TokenService.cs` | JWT (15 Minuten, Claims: sub, email, role) und Refresh-Token (Zufall, in der Datenbank nur der SHA-256-Hash) |
| `AuthEndpoints` | `Endpoints/AuthEndpoints.cs` | HTTP: `register`, `login`, `refresh`, `logout`, `profile` |
| Deutsche Fehlertexte | `GermanIdentityErrorDescriber` | `IdentityResult.Errors` auf Deutsch |

## Regeln (`AddHomeControlIdentity`)

- `RequireUniqueEmail = true`. E-Mail und Benutzername sind dieselbe Adresse, klein geschrieben.
- Passwort: mindestens 8 Zeichen, Groß- und Kleinbuchstabe, Ziffer.
- Sperre: 5 falsche Passwörter, dann 5 Minuten gesperrt. Antwort **429** (ProblemDetails).
- Rollen `Admin` und `User` entstehen beim ersten Gebrauch. Der erste registrierte Benutzer wird Admin.

## Register: IdentityResult → ValidationProblem

```text
RegisterRequest → AuthEndpoints → AuthService → UserManager.CreateAsync → Identity → SQLite
```

`CreateAsync` liefert ein `IdentityResult`. Bei Fehlern werden `result.Errors` zu einer
`ValidationProblem`-Antwort (HTTP 400): Passwortfehler unter `password`, Rest unter `email`.
Doppelte E-Mail-Adresse: ebenfalls 400 unter `email`.

## Tabellen

Identity legt an: `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`,
`AspNetUserLogins`, `AspNetUserTokens`. Unsere `RefreshTokens` zeigen jetzt auf `AspNetUsers`.
Die Rolle steht nicht mehr im Benutzer, sondern in `AspNetUserRoles`.

## Umstellung durchführen (einmalig, am eigenen Rechner)

Die alten Benutzer werden **nicht übernommen** (es gibt nur Testkonten). Alles andere bleibt erhalten.

```bash
cd ~/Schreibtisch/Umschulung/Home-Control
git checkout feature/identity
git pull
./identity-vorbereiten.sh          # sichert die Datenbank, löscht nur alte Benutzer und Refresh-Tokens
dotnet tool restore
dotnet ef migrations add IdentityUmgestellt --project backend/HomeControl.Api
dotnet build
dotnet test
./app-starten.sh                   # wendet die Migration beim Start an
```

Danach im Browser **„Erstes Konto anlegen"** und wieder als Admin starten. Alte Migrationen werden nie verändert.
Der API-Schlüssel für den Agenten (`Agent:ApiKey`) ist davon nicht betroffen.

## Nicht betroffen

Tuya (`TuyaClient`, Token-Cache, Signatur, Abgleich) und Blink (`BlinkCredentialStore`, Data Protection,
`BlinkNetCameraClient`). Das **Blink-Refresh-Token** (gehört zum Blink-Konto) ist ein anderes Token als
das **Home-Control-Refresh-Token** (gehört zu unseren Benutzern).

## Bekannte Grenzen

- Alte Benutzer werden nicht übernommen.
- Die Sperrzeit misst Identity mit der echten Uhr, der Ablauf ist deshalb nicht im Unit-Test geprüft.
- Keine E-Mail-Bestätigung, kein Passwort-Zurücksetzen, keine Zwei-Faktor-Anmeldung.
- Eine Rolle pro Benutzer (Identity könnte mehrere).
