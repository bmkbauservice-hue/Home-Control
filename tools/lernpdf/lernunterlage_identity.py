"""Erzeugt docs/Lernunterlage-Identity.pdf (Aufruf aus dem Repo-Hauptordner:
python3 tools/lernpdf/lernunterlage_identity.py)."""
from pathlib import Path

from lernpdf import Doc

d = Doc(
    "ASP.NET Core Identity",
    "Lernunterlage: Benutzer, Passwörter und Rollen nicht selbst bauen",
    footer="Home Control · Identity · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Warum Identity?")
d.p("Benutzerverwaltung ist voller Fallen: Passwörter richtig hashen, doppelte E-Mail-Adressen, Rollen, "
    "Sperre nach Fehlversuchen, Groß- und Kleinschreibung. <b>Sicherheitscode schreibt man nicht selbst</b>, "
    "man nimmt das geprüfte Framework. ASP.NET Core Identity ist genau das.")
d.table([
    ["", "Vorher (selbst gebaut)", "Jetzt (Identity)"],
    ["Benutzerklasse", "Eigene Klasse mit Email, PasswordHash, Role", "<b>User : IdentityUser&lt;Guid&gt;</b>, Felder kommen von Identity"],
    ["Passwort speichern", "PasswordHasher von Hand aufrufen, Hash selbst eintragen", "<b>UserManager.CreateAsync(user, password)</b>"],
    ["Passwortregeln", "Eigene Prüfung im Endpunkt", "Identity-Optionen, Fehler als IdentityResult"],
    ["Rolle", "Textfeld im Benutzer", "Eigene Tabellen (AspNetRoles, AspNetUserRoles)"],
    ["Doppelte E-Mail", "Eigene Abfrage", "RequireUniqueEmail, Identity prüft"],
    ["Fehlversuche", "keine Sperre", "Lockout: 5 Fehler, 5 Minuten gesperrt"],
    ["DbContext", "DbContext", "<b>IdentityDbContext&lt;User, IdentityRole&lt;Guid&gt;, Guid&gt;</b>"],
], [0.2, 0.38, 0.42])
d.merksatz("<b>Merksatz:</b> Identity verwaltet <b>Benutzer</b> (Wer bist du?). Das <b>JWT</b> ist nur der Ausweis, "
           "den Home Control danach ausstellt. Beides arbeitet zusammen, ersetzt sich aber nicht.")

# ------------------------------------------------------------------ 2
d.h1("2  Begriffe")
d.table([
    ["Begriff", "Bedeutung"],
    ["IdentityUser&lt;Guid&gt;", "Fertige Benutzerklasse mit Id (Guid), UserName, Email, PasswordHash, SecurityStamp, Lockout-Feldern, PhoneNumber ..."],
    ["IdentityRole&lt;Guid&gt;", "Fertige Rollenklasse (Name, Id). Bei uns: Admin und User."],
    ["IdentityDbContext", "DbContext, der alle Identity-Tabellen mitbringt. Unser ApplicationDbContext erbt davon."],
    ["UserManager&lt;User&gt;", "Die Schaltzentrale: Benutzer anlegen, suchen, Passwort prüfen, Rollen zuweisen, Fehlversuche zählen."],
    ["RoleManager", "Rollen anlegen und prüfen (RoleExistsAsync, CreateAsync)."],
    ["IdentityResult", "Ergebnis einer Identity-Aktion: Succeeded oder eine Liste von Errors (Code und Beschreibung)."],
    ["AddIdentityCore", "Registriert die Benutzerverwaltung ohne Cookie-Anmeldung und ohne fertige Oberfläche. Wir melden mit JWT an."],
    ["Normalisierung", "Identity speichert zusätzlich NormalizedEmail und NormalizedUserName in Großbuchstaben, damit die Suche Groß-/Kleinschreibung ignoriert."],
    ["SecurityStamp", "Zufallswert pro Benutzer. Ändert sich bei sicherheitsrelevanten Änderungen, z. B. neuem Passwort."],
    ["Lockout", "Zeitweise Sperre des Kontos nach zu vielen falschen Passwörtern."],
    ["RBAC", "Role-Based Access Control: Rechte hängen an Rollen (Admin, User), nicht an einzelnen Personen."],
], [0.27, 0.73])

# ------------------------------------------------------------------ 3
d.h1("3  Der Benutzer")
d.p("Die Klasse User erbt von IdentityUser&lt;Guid&gt;. Dadurch gibt es Id, UserName, Email und PasswordHash schon. "
    "<b>Felder nie doppelt definieren</b>, sonst hat man zwei E-Mail-Spalten. Hinzu kommen nur "
    "die Dinge, die Identity nicht kennt:")
d.code("""
public class User : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}
""")
d.p("Die Rolle steht <b>nicht mehr</b> im Benutzer. Sie liegt in der Tabelle AspNetUserRoles und wird mit "
    "<b>UserManager.AddToRoleAsync</b> gesetzt und mit <b>GetRolesAsync</b> gelesen.")

# ------------------------------------------------------------------ 4
d.h1("4  Datenbank: IdentityDbContext und Tabellen")
d.code("""
public class ApplicationDbContext
    : IdentityDbContext<User, IdentityRole<Guid>, Guid>
// in OnModelCreating zuerst:  base.OnModelCreating(modelBuilder);
""")
d.table([
    ["Tabelle", "Inhalt"],
    ["AspNetUsers", "Benutzer (E-Mail, PasswordHash, SecurityStamp, Lockout, CreatedAtUtc)"],
    ["AspNetRoles", "Rollen Admin und User"],
    ["AspNetUserRoles", "Wer hat welche Rolle (Verbindungstabelle)"],
    ["AspNetUserClaims, AspNetRoleClaims", "Zusätzliche Angaben zu Benutzer oder Rolle (bei uns ungenutzt)"],
    ["AspNetUserLogins, AspNetUserTokens", "Anmeldung über Google & Co. und Bestätigungstokens (bei uns ungenutzt)"],
    ["RefreshTokens (eigene)", "Unsere Refresh-Tokens, zeigen auf AspNetUsers"],
    ["Rooms, Devices, DeviceEvents, BlinkCredentials (eigene)", "Bleiben unverändert"],
], [0.4, 0.6])
d.p("Das Wichtigste: Alle eigenen DbSets bleiben, und Tuya- und Blink-Daten gehen nicht verloren.")

# ------------------------------------------------------------------ 5
d.h1("5  Registrierung")
d.table([
    ["Schritt", "Wer", "Was passiert"],
    ["1", "Endpunkt", "POST /api/auth/register: E-Mail und Passwort grob prüfen (leer? zu lang?)"],
    ["2", "AuthService", "Erster Benutzer? Dann darf jeder, er wird Admin. Sonst nur ein angemeldeter Admin"],
    ["3", "AuthService", "User-Objekt bauen (UserName = E-Mail, klein geschrieben)"],
    ["4", "UserManager", "<b>CreateAsync(user, password)</b>: Regeln prüfen, Salt erzeugen, PBKDF2-Hash bilden, speichern"],
    ["5", "AuthService", "Rolle anlegen, falls es sie noch nicht gibt, dann <b>AddToRoleAsync</b>"],
    ["6", "Endpunkt", "201 Created, oder bei Fehlern 400 mit ValidationProblem"],
], [0.1, 0.2, 0.7])
d.h2("IdentityResult in ValidationProblem übersetzen")
d.p("CreateAsync wirft bei Fehlern <b>keine Ausnahme</b>, sondern liefert ein IdentityResult. Bei Succeeded = false "
    "stehen in <b>result.Errors</b> Code und Text, zum Beispiel PasswordTooShort oder DuplicateEmail. "
    "Der Endpunkt macht daraus die gewohnte ProblemDetails-Antwort (HTTP 400), damit alle Fehler gleich aussehen. "
    "Die Texte kommen über den GermanIdentityErrorDescriber auf Deutsch.")
d.code("""
var created = await userManager.CreateAsync(user, password);
if (!created.Succeeded)
    return Results.ValidationProblem(ToValidationErrors(created.Errors));
""")

# ------------------------------------------------------------------ 6
d.h1("6  Passwort-Hashing")
d.bullets([
    "Das Passwort wird <b>nie gespeichert</b>, nur ein Hash.",
    "Identity nutzt <b>PBKDF2</b> mit zufälligem <b>Salt</b> und vielen Durchläufen. Das ist absichtlich langsam.",
    "Im PasswordHash-Feld stehen Verfahren, Salt und Hash in einem Text.",
    "Beim Login hasht Identity das eingegebene Passwort mit demselben Salt und vergleicht. Zurückrechnen geht nicht.",
    "Ist ein Hash veraltet (weniger Durchläufe), hasht Identity beim nächsten erfolgreichen Login automatisch neu.",
])
d.merksatz("<b>Merksatz:</b> Nie selbst hashen, nie SHA-256 für Passwörter. SHA-256 ist schnell, und schnell ist bei Passwörtern schlecht. "
           "(Für unsere Refresh-Tokens ist SHA-256 dagegen richtig: Das sind lange Zufallswerte, keine Passwörter.)")

# ------------------------------------------------------------------ 7
d.h1("7  Rollen (RBAC)")
d.table([
    ["Rolle", "Darf"],
    ["Admin", "Räume, Geräte und Ereignisse anlegen, ändern, löschen; neue Benutzer anlegen"],
    ["User", "Alles ansehen (lesen)"],
], [0.2, 0.8])
d.bullets([
    "Rollen entstehen beim ersten Gebrauch (RoleManager.CreateAsync).",
    "Die Rolle kommt als Claim <b>role</b> ins JWT. Die Regel AdminOnly (RequireAuthorization) prüft sie.",
    "<b>401</b> = nicht angemeldet. <b>403</b> = angemeldet, aber keine Berechtigung.",
])

# ------------------------------------------------------------------ 8
d.h1("8  Sperre nach Fehlversuchen (Lockout)")
d.p("Ohne Sperre könnte ein Angreifer beliebig viele Passwörter durchprobieren (Brute Force). Mit Identity:")
d.numbered([
    "Benutzer über FindByEmailAsync suchen.",
    "IsLockedOutAsync: schon gesperrt? Dann sofort Antwort 429, ohne das Passwort zu prüfen.",
    "CheckPasswordAsync: stimmt das Passwort?",
    "Falsch: AccessFailedAsync zählt hoch. Beim fünften Fehler sperrt Identity für 5 Minuten.",
    "Richtig: ResetAccessFailedCountAsync setzt den Zähler zurück, dann werden die Tokens ausgestellt.",
])

# ------------------------------------------------------------------ 9
d.h1("9  Identity und JWT zusammen")
d.table([
    ["Frage", "Antwort in Home Control"],
    ["Wer verwaltet Benutzer und Passwörter?", "Identity (UserManager)"],
    ["Woher weiß die API bei jeder Anfrage, wer ruft?", "Aus dem JWT (Access Token, 15 Minuten)"],
    ["Wie kommt man an ein neues JWT, ohne Passwort?", "Refresh-Token im HTTP-only Cookie, mit Rotation"],
    ["Wo steht, was das JWT enthält?", "sub (Id), email, role. Kein Passwort, keine Tuya- oder Blink-Daten"],
    ["Warum nicht Cookie-Anmeldung von Identity?", "Wir haben ein getrenntes React-Frontend und die API wird auch vom Agenten benutzt, das passt besser zu Tokens"],
], [0.4, 0.6])
d.merksatz("<b>Nicht verwechseln:</b> Das <b>Blink-Refresh-Token</b> gehört zum Blink-Konto (wird verschlüsselt gespeichert). "
           "Das <b>Home-Control-Refresh-Token</b> gehört zu unseren Benutzern (nur der Hash wird gespeichert).", warn=True)

# ------------------------------------------------------------------ 10
d.h1("10  Code-Landkarte")
d.table([
    ["Datei", "Inhalt"],
    ["Models/User.cs", "User : IdentityUser&lt;Guid&gt;, Konstanten Roles.Admin und Roles.User"],
    ["Data/ApplicationDbContext.cs", "IdentityDbContext, base.OnModelCreating, eigene Tabellen"],
    ["Auth/IdentityRegistration.cs", "AddIdentityCore, AddRoles, AddEntityFrameworkStores, Regeln, deutsche Fehlertexte"],
    ["Auth/AuthService.cs", "RegisterAsync, LoginAsync, RefreshAsync, LogoutAsync"],
    ["Auth/AuthUser.cs", "Kleines Ergebnisobjekt: Id, E-Mail, Rolle"],
    ["Auth/TokenService.cs", "JWT erzeugen, Refresh-Token erzeugen und hashen"],
    ["Endpoints/AuthEndpoints.cs", "HTTP-Endpunkte, Cookie, IdentityResult in ValidationProblem"],
    ["Program.cs", "builder.Services.AddHomeControlIdentity()"],
    ["Tests: AuthServiceTests, TokenServiceTests", "Register, Login, Sperre, Refresh, Rotation, JWT"],
], [0.38, 0.62])

# ------------------------------------------------------------------ 11
d.h1("11  Die Umstellung und ihre Grenzen")
d.bullets([
    "Die Migration muss auf dem eigenen Rechner erzeugt werden: <b>dotnet ef migrations add IdentityUmgestellt</b>. "
    "Alte Migrationen werden nie verändert.",
    "Vorher sichert und leert ./identity-vorbereiten.sh nur die alten Benutzer und Refresh-Tokens. Alte Benutzer werden nicht übernommen, "
    "das Konto wird neu angelegt (erster Benutzer = Admin).",
    "Der Code wurde ohne .NET SDK geschrieben und ist erst nach <b>dotnet build</b> und <b>dotnet test</b> als funktionierend zu betrachten.",
    "Noch nicht dabei: E-Mail-Bestätigung, Passwort-Zurücksetzen, Zwei-Faktor-Anmeldung.",
    "Eine Rolle pro Benutzer (Identity kann mehrere).",
    "Identity misst die Sperrzeit mit der echten Uhr, der Ablauf ist deshalb nicht im Unit-Test geprüft.",
])

# ------------------------------------------------------------------ 12
d.h1("12  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort in einem Satz"],
    ["Warum Identity statt selbst bauen?", "Sicherheitscode schreibt man nicht selbst. Identity ist geprüft und bringt Hashing, Regeln, Rollen und Sperre mit."],
    ["Was macht UserManager?", "Er legt Benutzer an, prüft Passwörter, setzt Rollen und zählt Fehlversuche."],
    ["Was ist ein IdentityResult?", "Das Ergebnis einer Identity-Aktion: Erfolg oder eine Liste von Fehlern."],
    ["Warum erbt der DbContext von IdentityDbContext?", "Damit die Identity-Tabellen in unserer Datenbank mit angelegt werden."],
    ["Was ist ein Salt?", "Ein Zufallswert pro Passwort, damit gleiche Passwörter verschiedene Hashes ergeben."],
    ["Warum PBKDF2 und nicht SHA-256 für Passwörter?", "PBKDF2 ist absichtlich langsam, SHA-256 ist schnell und damit leichter zu erraten."],
    ["Was ist der Unterschied zwischen 401 und 403?", "401: nicht angemeldet. 403: angemeldet, aber nicht erlaubt."],
    ["Wofür ist das JWT, wenn Identity die Benutzer hat?", "Identity kennt die Benutzer, das JWT ist der Ausweis für jede weitere Anfrage."],
    ["Was passiert bei fünf falschen Passwörtern?", "Das Konto ist 5 Minuten gesperrt, die Antwort ist 429."],
    ["Was ist mit Tuya und Blink?", "Nicht betroffen: eigene Tabellen und Klassen."],
], [0.38, 0.62])

# ------------------------------------------------------------------ 13
d.h1("13  Merksätze")
d.bullets([
    "Passwörter <b>nie selbst hashen</b>: UserManager.CreateAsync(user, password).",
    "Identity = <b>Benutzerverwaltung</b>, JWT = <b>Ausweis</b>.",
    "User erbt von IdentityUser&lt;Guid&gt;, Felder <b>nicht doppelt</b> definieren.",
    "Der DbContext erbt von IdentityDbContext, <b>base.OnModelCreating zuerst</b>.",
    "Fehler kommen als <b>IdentityResult</b>, bei uns als ValidationProblem (400).",
    "Rollen liegen in <b>eigenen Tabellen</b>, gesetzt mit AddToRoleAsync.",
    "<b>401</b> nicht angemeldet, <b>403</b> keine Berechtigung, <b>429</b> zu viele Fehlversuche.",
    "Alte Migrationen <b>nie verändern</b>, neue Migration anlegen.",
])

out = Path(__file__).resolve().parents[2] / "docs" / "Lernunterlage-Identity.pdf"
d.save(str(out))
print(out)
