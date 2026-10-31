"""Erzeugt docs/Lernunterlage-Validierung-und-Unterricht.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_validierung.py)."""
from lernpdf import Doc

d = Doc(
    "Validierung und Unterricht",
    "Lernunterlage: Was aus dem Unterricht vom 30.10. in Home Control steckt, und wie",
    footer="Home Control · Validierung und Unterricht · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Eingebaute Validierung (.NET 10)")
d.p("Minimal APIs nehmen ohne Validierung jedes JSON an. Ein leeres <font name='Mono'>{}</font> ergibt ein "
    "Objekt mit <font name='Mono'>null</font>-Feldern, obwohl <font name='Mono'>string</font> nicht nullable ist: "
    "Nullable-Referenztypen prüft nur der Compiler, nicht der JSON-Deserialisierer.")
d.code("""// Contracts/AuthContracts.cs – die Regeln stehen am DTO
public record LoginRequest(
    [Required(ErrorMessage = "Bitte eine E-Mail-Adresse eingeben.")]
    [EmailAddress(ErrorMessage = "Bitte eine gültige E-Mail-Adresse eingeben.")]
    [StringLength(256, ErrorMessage = "...")]
    string Email,
    [Required(ErrorMessage = "Bitte ein Passwort eingeben.")]
    [StringLength(128, ErrorMessage = "...")]
    string Password);

// Program.cs – einschalten
builder.Services.AddProblemDetails();
builder.Services.AddValidation();""")
d.bullets([
    "<b>Vor dem Handler:</b> Ist die Eingabe ungültig, antwortet die API mit <b>400</b> und ProblemDetails "
    "(<font name='Mono'>errors</font> je Feld). Der Endpunkt läuft gar nicht erst.",
    "<b>Deutsche Meldungen:</b> über <font name='Mono'>ErrorMessage</font>. Standard wäre Englisch "
    "(„The Email field is required.“).",
    "<b>Rolle:</b> <font name='Mono'>[RegularExpression(\"^(Admin|User)$\")]</font> statt eigener if-Abfrage.",
    "<b>Was bleibt im Code:</b> Regeln, die Daten brauchen (E-Mail schon vergeben) und die Passwortregeln. "
    "Die prüft Identity beim Anlegen.",
])
d.merksatz("<b>Beim Login keine Passwortregeln</b>, nur Pflicht und Obergrenze. Sonst verrät die Antwort etwas "
           "über die Regeln statt nur „E-Mail oder Passwort ist falsch“.", warn=True)
d.h2("Vorher und nachher")
d.table([
    ["Vorher", "Jetzt"],
    ["Eigene Methode ValidateEmailAndPassword (35 Zeilen) im Endpunkt", "Attribute am DTO, AddValidation prüft automatisch"],
    ["if (request.Role is not null && ...) errors[\"role\"] = ...", "[RegularExpression(\"^(Admin|User)$\")]"],
    ["Jeder Endpunkt ruft die Prüfung selbst auf", "Gilt für jeden Endpunkt, der das DTO annimmt"],
], [0.5, 0.5])
d.h2("Testen")
d.p("<font name='Mono'>ValidationTests</font> schickt ungültige Anfragen an die echte API (WebApplicationFactory) und "
    "prüft 400 und die deutschen Meldungen. Fallstrick: System.Text.Json schreibt Umlaute im JSON als "
    "<font name='Mono'>\\u00FC</font>. Deshalb liest der Test die Meldungen mit JsonDocument aus, statt im Text zu suchen.")

# ------------------------------------------------------------------ 2
d.h1("2  GetUserId(): Wer fragt an?")
d.code("""public static Guid? GetUserId(this ClaimsPrincipal user) =>
    Guid.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

// im Endpunkt
if (user.GetUserId() is not { } userId)
    return Results.Problem(statusCode: 401, title: "Ungültiges Token.");""")
d.bullets([
    "<b>Erweiterungsmethode</b> (<font name='Mono'>this ClaimsPrincipal</font>): liest sich wie eine eingebaute "
    "Methode und steht an einer Stelle statt in jedem Endpunkt.",
    "<b>Grundsatz aus dem Unterricht:</b> Wer anfragt, steht im Token (<font name='Mono'>sub</font>), nie im Body. "
    "Sonst könnte jeder im Namen anderer handeln.",
    "<b>null statt Ausnahme:</b> Der KI-Agent meldet sich per API-Schlüssel an und hat keine Benutzer-Id. "
    "Dann gibt es 401 und keinen Absturz.",
])

# ------------------------------------------------------------------ 3
d.h1("3  OpenAPI-Angaben für Scalar")
d.code("""var rooms = app.MapGroup("/api/rooms").WithTags("Räume");
auth.MapPost("/login", Login).AllowAnonymous()
    .WithSummary("Anmelden: Access Token im JSON, Refresh-Token im HTTP-only Cookie");""")
d.p("<font name='Mono'>WithTags</font> gruppiert die Endpunkte in der API-Doku (/scalar): Räume, Geräte, Ereignisse, "
    "Reports, Tuya, Blink, Anmeldung, Status. <font name='Mono'>WithSummary</font> schreibt einen Satz dazu. "
    "Das ist praktisch für die Präsentation: Die Doku erklärt sich selbst.")

# ------------------------------------------------------------------ 4
d.h1("4  Vergleich mit dem Unterricht (Stand 31.10.2026)")
d.table([
    ["Thema", "Unterricht (Blog-API)", "Home Control", "Ergebnis"],
    ["Benutzer", "ASP.NET Core Identity", "ASP.NET Core Identity", "gleich"],
    ["Passwort-Hash", "PBKDF2 über Identity", "PBKDF2 über Identity", "gleich"],
    ["Sperre nach Fehlversuchen", "nein (CheckPasswordAsync zählt nicht)", "ja: 5 Fehler, 5 Minuten (AccessFailedAsync)", "Home Control weiter"],
    ["Validierung", "AddValidation + DataAnnotations", "AddValidation + DataAnnotations, deutsch", "gleich"],
    ["User-Id aus Token", "GetUserId()", "GetUserId()", "gleich"],
    ["OpenAPI-Angaben", "WithTags, WithSummary", "WithTags, WithSummary", "gleich"],
    ["Token-Laufzeit", "60 Minuten, kein Refresh", "15 Minuten + Refresh-Token mit Rotation", "Home Control weiter"],
    ["Rechte", "Besitz (nur der Autor)", "Rolle (Admin) + API-Schlüssel nur lesend", "anderer Ansatz"],
    ["Registrierung", "offen", "erster Benutzer Admin, danach nur Admin", "Home Control weiter"],
    ["Rückgabetypen", "TypedResults + Results&lt;…&gt;", "IResult + Results.…", "Unterricht weiter"],
    ["Endpunktfilter", "RejectEmptyIdFilter, TimingFilter", "keine", "Unterricht weiter"],
    ["Tests", "Postman von Hand", "113 xUnit-Tests, ein Befehl: ./pruefen.sh", "Home Control weiter"],
], [0.2, 0.27, 0.33, 0.2])
d.p("<b>Noch offen:</b> TypedResults (Umbau aller Endpunkte, gut für einen eigenen Pull Request) und ein "
    "Endpunktfilter für leere GUIDs. Beides steht im Übergabe-Dokument.")

# ------------------------------------------------------------------ 5
d.h1("5  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Wann läuft die Validierung?", "Nach dem Lesen des Bodys, vor dem Endpunkt. Bei Fehlern läuft der Endpunkt nicht."],
    ["Warum [Required], obwohl string nicht nullable ist?", "Nullable prüft nur der Compiler. Fehlt das Feld im JSON, ist es zur Laufzeit trotzdem null."],
    ["Was gehört nicht in Attribute?", "Regeln, die Daten brauchen (E-Mail vergeben?). Die prüft der Service, Antwort ebenfalls als ValidationProblem."],
    ["Warum GetUserId() als Erweiterung?", "Einmal geschrieben, überall gleich benutzt, und der Endpunkt bleibt lesbar."],
], [0.4, 0.6])

d.h1("6  Merksätze")
d.merksatz("• Regeln ans DTO, AddValidation prüft vor dem Endpunkt. Ungültig → 400.<br/>"
           "• Meldungen für Benutzer auf Deutsch (ErrorMessage).<br/>"
           "• Regeln mit Datenbankzugriff im Service, nicht in Attributen.<br/>"
           "• Wer anfragt, steht im Token: user.GetUserId().<br/>"
           "• WithTags/WithSummary: Die API-Doku erklärt sich selbst.")

d.save("docs/Lernunterlage-Validierung-und-Unterricht.pdf")
