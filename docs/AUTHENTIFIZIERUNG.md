# Authentifizierung und Autorisierung

> **In Arbeit** auf dem Branch `feature/authentication`. Die ausführliche Erklärung
> (JWT, Refresh-Token-Rotation, Passwort-Hashing, XSS, CSRF, Auth-Flow) folgt, sobald der
> Code steht.

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

## Merksätze

- **Authentifizierung** = Wer bist du? **Autorisierung** = Was darfst du?
  Erst authentifizieren, dann autorisieren.
- **401** = nicht (korrekt) angemeldet: Token fehlt, ist ungültig oder abgelaufen.
- **403** = angemeldet, aber nicht berechtigt (z. B. ein User ruft eine Admin-Funktion auf).
