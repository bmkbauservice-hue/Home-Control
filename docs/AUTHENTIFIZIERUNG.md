# Authentifizierung und Autorisierung

> **In Arbeit** auf dem Branch `feature/authentication`. Die ausführliche Erklärung
> (JWT, Refresh-Token-Rotation, Passwort-Hashing, XSS, CSRF, Auth-Flow) folgt, sobald der
> Code steht.

**Zum Lernen:** [Lernunterlage-Authentifizierung.pdf](Lernunterlage-Authentifizierung.pdf)
(11 Seiten: Grundbegriffe, JWT, Rotation, Passwort-Hashing, Rollen, XSS/CSRF, Ablauf, Code,
Grenzen, Kurzantworten für Rückfragen, Merksätze).

## Überblick in Bildern

![Home Control – Login mit JWT: Anmeldung, Prüfung in der Datenbank, Access- und Refresh-Token, geschützte Endpunkte, Refresh, 401 und 403](images/auth-login-mit-jwt.png)

![Authentifizierung und Autorisierung: Wer bist du, was darfst du, Ablauf in Home Control, Rollen und wichtige Sicherheitsbegriffe](images/auth-authentifizierung-autorisierung.png)

> Die Infografiken zeigen das Prinzip. In einigen Punkten weicht Home Control davon ab:
>
> - **Refresh-Token ist kein JWT.** Nur das Access Token ist ein JWT. Das Refresh-Token ist
>   eine lange Zufallszeichenkette. In der Datenbank steht nur ihr SHA-256-Hash.
> - **Passwort-Hashing mit PBKDF2, nicht bcrypt.** Home Control nutzt den `PasswordHasher`
>   von ASP.NET Core (PBKDF2 mit Salt). Das Prinzip ist dasselbe: langsamer Hash, nie Klartext.
> - **Nur das Refresh-Token liegt im HTTP-only Cookie.** Das Access Token speichert das
>   Frontend im Local Storage und schickt es als `Authorization: Bearer <token>` mit.
> - **Anmeldung mit E-Mail**, nicht mit Benutzername.
> - **Geräte werden nicht gesteuert.** Tuya und Blink sind nur lesend angebunden. Rollen
>   regeln vorerst, wer Räume und Geräte in Home Control anlegen, ändern und löschen darf
>   (Admin) und wer nur lesen darf (User).

## Wer darf was (Stand jetzt)

| Bereich | Ohne Anmeldung | User | Admin |
|---|---|---|---|
| Räume, Geräte, Ereignisse, Reports, Tuya, Blink lesen (`GET`) | ja | ja | ja |
| Räume und Geräte anlegen, ändern, löschen | 401 | 403 | ja |
| Ereignis speichern (manueller Testeintrag) | 401 | 403 | ja |
| Weitere Benutzer anlegen (`POST /api/auth/register`) | 401* | 403 | ja |
| Eigenes Profil (`GET /api/auth/profile`) | 401 | ja | ja |
| Tuya-Abgleich (`POST /api/tuya/test-device/sync`) | ja | ja | ja |

\* Nur solange es noch keinen Benutzer gibt, ist die Registrierung offen. Der erste Benutzer
wird automatisch Admin.

Umsetzung: Die Regel `AdminOnly` (`Auth/AuthPolicies.cs`, verlangt die Rolle Admin) wird in
`Program.cs` registriert und an den schreibenden Endpunkten mit
`.RequireAuthorization(AuthPolicies.AdminOnly)` angehängt.

**Bewusst noch offen:**

- **Lesen ohne Anmeldung**, damit der n8n-Agent weiterläuft. Der bekommt später einen
  eigenen API-Schlüssel, dann wird auch Lesen geschützt.
- **Tuya-Abgleich.** Die Dashboard-Karte ruft ihn beim Öffnen auf. Er nimmt keine Eingaben an
  und überträgt nur den Zustand aus der Tuya-Cloud. Wird mit dem Frontend-Login auf Admin
  umgestellt.
- **Frontend noch ohne Login.** Bis das Frontend-Login fertig ist, schlagen Anlegen, Ändern und
  Löschen in der Oberfläche mit 401 fehl. Deshalb bleibt alles auf dem Branch
  `feature/authentication`, bis das Frontend nachgezogen ist.

Getestet in `AdminOnlyEndpointTests` (echte API im Arbeitsspeicher über
`WebApplicationFactory`, eigene SQLite-Datei im Temp-Ordner): 401 ohne Token und mit
ungültigem Token, 403 als User, Lesen ohne Anmeldung, Admin darf alles.

## Merksätze

- **Authentifizierung** = Wer bist du? **Autorisierung** = Was darfst du?
  Erst authentifizieren, dann autorisieren.
- **401** = nicht (korrekt) angemeldet: Token fehlt, ist ungültig oder abgelaufen.
- **403** = angemeldet, aber nicht berechtigt (z. B. ein User ruft eine Admin-Funktion auf).
