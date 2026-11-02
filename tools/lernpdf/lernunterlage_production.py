"""Erzeugt docs/Lernunterlage-Production-Modus.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_production.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Production-Modus",
    "Lernunterlage: Umgebungen in ASP.NET Core – warum der Dauerdienst nicht im Development laufen soll",
    footer="Home Control · Production-Modus · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("ASP.NET Core kennt <b>Umgebungen</b> (Environments). Welche gilt, steht in der Umgebungsvariable "
    + M + "ASPNETCORE_ENVIRONMENT" + E + ". Üblich sind <b>Development</b>, <b>Staging</b> und <b>Production</b>. "
    "Ohne Angabe ist es Production, also die sichere Voreinstellung.")
d.p("Bis zum 02.11.2026 lief der Dauerdienst als Development, nur damit die User Secrets gelesen werden. "
    "Das Security-Review (REVIEW.md, Finding <b>M-1</b>) hat das bemängelt: Sicherheitsentscheidungen hingen an "
    "einem Schalter, der für die Entwicklung gedacht ist.")

# ------------------------------------------------------------------ 2
d.h1("2  Was sich je nach Umgebung ändert")
d.table([
    ["Verhalten", "Development (app-starten.sh)", "Production (Dauerdienst)"],
    ["User Secrets", "liest ASP.NET Core automatisch", "nur mit Schalter HomeControl:UseUserSecrets"],
    ["Refresh-Cookie", "ohne Secure (läuft auch über http)", "Secure: Browser schickt es nur sicher"],
    ["API-Doku /scalar, /openapi", "an, ohne Anmeldung", "aus (404), keine Angriffskarte"],
    ["Ohne Jwt:SigningKey", "Zufallsschlüssel + Warnung im Log", "App startet nicht (Exception)"],
    ["Demo-Daten (SeedDemoData)", "erlaubt", "nie"],
    ["appsettings.Development.json", "wird geladen", "wird nicht geladen"],
], [0.28, 0.36, 0.36])
d.merksatz("Faustregel: Development ist bequem, Production ist sicher. Was dauerhaft läuft und von "
           "anderen Geräten erreichbar ist, läuft als Production.")

# ------------------------------------------------------------------ 3
d.h1("3  Das Problem: Wo kommen die Geheimnisse her?")
d.p("JWT-Schlüssel, Tuya-Zugang, Blink und Agenten-Schlüssel liegen in den <b>User Secrets</b> "
    "(" + M + "~/.microsoft/usersecrets/&lt;id&gt;/secrets.json" + E + ", Rechte 600). Die liest ASP.NET Core "
    "von sich aus <b>nur im Development</b>. Zwei Wege standen zur Wahl:")
d.table([
    ["Weg", "Vorteil", "Nachteil"],
    ["A: User Secrets ausdrücklich einbinden (gewählt)", "Geheimnisse an einer Stelle, alle *-einrichten.sh "
     "funktionieren weiter", "Nur sinnvoll auf dem eigenen Rechner"],
    ["B: Umgebungsvariablen-Datei für systemd", "Üblicher Weg auf Servern und in Docker",
     "Geheimnisse doppelt, müssten bei jedem Update abgeglichen werden"],
], [0.34, 0.36, 0.30])
d.p("Für den OVH-Server (Variante B in BETRIEB.md) kommt später Weg B: Dort gibt es keine User Secrets.")

# ------------------------------------------------------------------ 4
d.h1("4  Der Code")
d.h2("Program.cs: ausdrücklich einbinden, nur mit Schalter")
d.code("""var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("HomeControl:UseUserSecrets"))
{
    builder.Configuration.AddUserSecrets<Program>(optional: false);
    builder.Configuration.AddEnvironmentVariables();   // wieder nach hinten
}""")
d.bullets([
    "<b>Ganz am Anfang:</b> Gleich danach liest Program.cs den Connection String und den JWT-Schlüssel. "
    "Die Quelle muss vorher da sein.",
    "<b>optional: false:</b> Fehlt die Datei, bricht der Start ab. Lieber ein klarer Fehler als ein Dienst ohne "
    "Zugangsdaten.",
    "<b>Reihenfolge der Quellen:</b> Die zuletzt hinzugefügte Quelle gewinnt. AddUserSecrets hängt hinten an, "
    "damit würden die Secrets sogar Umgebungsvariablen überstimmen. Deshalb die Umgebungsvariablen noch "
    "einmal anhängen: Der Connection String aus dem Dienst gilt weiter.",
])
d.code("""appsettings.json → appsettings.Production.json → Umgebungsvariablen → Kommandozeile
                                    → User Secrets (neu) → Umgebungsvariablen (nochmal)
                                                             ▲ gewinnt""")
d.h2("Der Schalter im Dienst (dienst-einrichten.sh)")
d.code("""Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=HomeControl__UseUserSecrets=true""")
d.p("Zwei Unterstriche " + M + "__" + E + " in einer Umgebungsvariable entsprechen dem Doppelpunkt in der "
    "Konfiguration: " + M + "HomeControl__UseUserSecrets" + E + " = " + M + "HomeControl:UseUserSecrets" + E + ".")
d.h2("Vorher prüfen statt hinterher suchen")
d.code("""SECRETS="$(dotnet user-secrets list --project backend/HomeControl.Api 2>/dev/null || true)"
if ! grep -q '^Jwt:SigningKey = ' <<<"$SECRETS"; then
    echo "Fehler: In den User Secrets fehlt Jwt:SigningKey." >&2
    exit 1
fi""")
d.bullets([
    "<b>Vor dem Anhalten des Dienstes:</b> Fehlt der Schlüssel, läuft der alte Dienst einfach weiter.",
    "<b>In eine Variable statt per Pipe:</b> Mit " + M + "set -o pipefail" + E + " könnte " + M + "grep -q" + E +
    " die Pipe früh schließen. Der erste Befehl bekäme dann ein Signal, und die Prüfung schlüge fälschlich "
    "fehl. Die Werte erscheinen so auch nicht auf dem Bildschirm.",
])

# ------------------------------------------------------------------ 5
d.page_break()  # Codeblock passt nicht mehr auf Seite 2, Überschrift nicht allein lassen
d.h1("5  Tests: ProductionModeTests")
d.code("""public class ProductionApiFactory : HomeControlApiFactory
{
    protected override Dictionary<string, string?> Settings => new()
        { ["Jwt:SigningKey"] = SigningKey };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Production");   // später = gewinnt
    }
}""")
d.table([
    ["Test", "prüft"],
    ["Api_Dokumentation_ist_aus", "/scalar und /openapi/v1.json antworten mit 404"],
    ["Refresh_Cookie_ist_Secure", "Set-Cookie für hc_refresh enthält Secure und HttpOnly"],
    ["Ohne_JWT_Schluessel_startet_die_App_nicht", "Start wirft eine Exception, die Jwt:SigningKey nennt"],
], [0.45, 0.55])
d.p("<b>HandleCookies = false</b> beim Test-Client: Sonst verarbeitet der Client das Cookie selbst, und man "
    "sieht die Eigenschaften im Header nicht mehr so gut.")

# ------------------------------------------------------------------ 6
d.h1("6  Und http://127.0.0.1:5200?")
d.p("Ein Secure-Cookie schickt der Browser nur über sichere Verbindungen. Der Zugriff über Tailscale ist HTTPS, "
    "das passt. Für " + M + "localhost" + E + " und " + M + "127.0.0.1" + E + " machen Browser eine Ausnahme: "
    "Diese Adressen gelten als „potenziell vertrauenswürdig“, denn die Daten verlassen den Rechner nie. "
    "Deshalb funktioniert die Anmeldung dort weiter.")

# ------------------------------------------------------------------ 7
d.h1("7  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Was ist der Unterschied zwischen Development und Production?", "Development ist bequem (API-Doku, Secrets, "
     "Ersatzschlüssel), Production ist sicher (Secure-Cookie, keine Doku, harter Abbruch)."],
    ["Warum User Secrets im Production?", "Damit die Geheimnisse an einer Stelle bleiben. Nur auf dem eigenen "
     "Rechner und nur mit ausdrücklichem Schalter."],
    ["Was passiert ohne JWT-Schlüssel?", "Die App startet nicht. Ein Zufallsschlüssel würde nach jedem "
     "Neustart alle Tokens ungültig machen und Fehler verdecken."],
    ["Wo ist jetzt die API-Doku?", "Nur noch beim Entwickeln: app-starten.sh oder dotnet run, dann /scalar."],
    ["Wie macht man das auf einem Server?", "Geheimnisse als Umgebungsvariablen oder Docker-Secrets, "
     "Umgebung Production, Schalter aus."],
], [0.4, 0.6])

d.h1("8  Merksätze")
d.merksatz("• Umgebung über ASPNETCORE_ENVIRONMENT, Standard ist Production.<br/>"
           "• Was dauerhaft läuft, läuft als Production.<br/>"
           "• Konfiguration: Die zuletzt hinzugefügte Quelle gewinnt.<br/>"
           "• __ in Umgebungsvariablen = : in der Konfiguration.<br/>"
           "• Fehlende Geheimnisse früh und laut melden, nicht mit Ersatzwerten verdecken.")

d.save("docs/Lernunterlage-Production-Modus.pdf")
