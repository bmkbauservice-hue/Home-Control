"""Erzeugt docs/Lernunterlage-Pruefen-und-Migration.pdf (Aufruf aus dem Repo-Hauptordner:
python3 tools/lernpdf/lernunterlage_pruefen_migration.py)."""
from lernpdf import Doc

d = Doc(
    "Prüfen und Migrieren",
    "Lernunterlage: Ein Befehl prüft alles, und die Datenbank wandert auf Identity um",
    footer="Home Control · Prüfskript und Identity-Migration · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("Mehrere Änderungen (Admin-Schutz, Frontend-Login, API-Schlüssel, Tuya-Abgleich, PWA, Auslieferung "
    "des Frontends, Umstellung auf ASP.NET Core Identity) wurden ohne .NET SDK geschrieben und waren "
    "<b>nie gebaut und nie getestet</b>. Bevor man darauf aufbaut, muss der Stand einmal vollständig "
    "geprüft werden. Dazu gibt es jetzt <b>ein</b> Skript: <font name='Mono'>./pruefen.sh</font>.")
d.merksatz("<b>Merksatz:</b> Ein Schritt gilt erst als erledigt, wenn Build und Tests wirklich gelaufen sind. "
           "Geschriebener, aber nie kompilierter Code ist nur eine Vermutung.")

# ------------------------------------------------------------------ 2
d.h1("2  Das Prüfskript pruefen.sh")
d.table([
    ["Schritt", "Befehl", "Was er prüft"],
    ["Werkzeuge", "dotnet tool restore", "Holt dotnet-ef in der Version aus dotnet-tools.json"],
    ["Backend bauen", "dotnet build", "Kompiliert API, Tests und BlinkSpike: Tippfehler, falsche Typen, fehlende using"],
    ["Migrationen", "dotnet ef migrations has-pending-model-changes", "Hat sich das Datenmodell geändert, ohne dass es eine Migration gibt?"],
    ["Tests", "dotnet test --no-build", "Alle xUnit-Tests, ohne ein zweites Mal zu bauen"],
    ["Pakete", "npm ci", "Installiert genau die Versionen aus package-lock.json"],
    ["Frontend bauen", "npm run build", "tsc -b prüft die Typen, vite build baut die fertigen Dateien"],
    ["Lint", "npm run lint", "oxlint sucht typische Fehler und schlechten Stil"],
], [0.18, 0.37, 0.45])
d.h2("So ist es gebaut")
d.code("""set -uo pipefail                      # -u: unbekannte Variable = Fehler
                                      # pipefail: Fehler in einer Pipe zählt
schritt() {
    local name="$1"; shift
    "$@" 2>&1 | tee -a "$LOG"         # anzeigen UND ins Log schreiben
    local status=${PIPESTATUS[0]}     # Exit-Code des Befehls, nicht von tee
    if [ "$status" -eq 0 ]; then ergebnisse+=("OK      $name")
    else ergebnisse+=("FEHLER  $name"); fehler=1; fi
}
schritt "dotnet build" dotnet build --nologo
...
exit "$fehler"                        # 0 = alles grün, 1 = mindestens ein Fehler""")
d.bullets([
    "<b>Ohne <font name='Mono'>set -e</font>:</b> Das Skript soll nach einem Fehler weiterlaufen, damit man alle "
    "Probleme auf einmal sieht und nicht eins nach dem anderen.",
    "<b>Exit-Code:</b> 0 heißt Erfolg, alles andere Fehler. So kann später auch ein anderes Programm "
    "(z. B. eine CI-Pipeline auf GitHub) das Skript aufrufen und auswerten.",
    "<b>PIPESTATUS:</b> Bei <font name='Mono'>befehl | tee</font> wäre <font name='Mono'>$?</font> der "
    "Exit-Code von tee (fast immer 0). <font name='Mono'>${PIPESTATUS[0]}</font> ist der des ersten Befehls.",
    "<b>pruefen.log:</b> Die ganze Ausgabe landet in einer Datei, die in .gitignore steht. Man kann sie "
    "weitergeben, ohne Text aus dem Terminal zu kopieren.",
])
d.h2("Ergebnis am 31.10.2026")
d.table([
    ["Schritt", "Ergebnis"],
    ["dotnet build", "OK, 0 Warnungen, 0 Fehler"],
    ["Migrationen vollständig", "OK (nach dem Anlegen von IdentityUmgestellt)"],
    ["dotnet test", "OK, 103 von 103 Tests bestanden"],
    ["npm run build", "OK (264 kB JavaScript, 80 kB gezippt)"],
    ["npm run lint", "OK, 0 Warnungen, 0 Fehler"],
], [0.4, 0.6])

# ------------------------------------------------------------------ 3
d.h1("3  Migrationen: Datenbank und Code im Gleichschritt")
d.p("EF Core beschreibt das Datenmodell im Code (Klassen, <font name='Mono'>OnModelCreating</font>). "
    "Eine <b>Migration</b> ist die Anleitung, wie die echte Datenbank von einem Stand zum nächsten kommt. "
    "Home Control wendet fehlende Migrationen beim Start automatisch an "
    "(<font name='Mono'>db.Database.Migrate()</font> in Program.cs).")
d.table([
    ["Datei", "Inhalt"],
    ["2026…_Name.cs", "Up() und Down(): was beim Anwenden bzw. Zurücknehmen passiert"],
    ["2026…_Name.Designer.cs", "Das vollständige Modell zu diesem Zeitpunkt (von EF erzeugt, nicht anfassen)"],
    ["ApplicationDbContextModelSnapshot.cs", "Der aktuelle Stand. Daran erkennt EF, was sich seit der letzten Migration geändert hat"],
], [0.38, 0.62])
d.merksatz("<b>Regel:</b> Alte Migrationen werden nie verändert. Jede Änderung am Modell bekommt eine neue "
           "Migration mit sprechendem Namen.")
d.h2("Was IdentityUmgestellt macht")
d.p("Aus der eigenen Klasse <font name='Mono'>User</font> wurde <font name='Mono'>User : IdentityUser&lt;Guid&gt;</font>, "
    "der DbContext erbt von <font name='Mono'>IdentityDbContext</font>. Deshalb erzeugt EF:")
d.bullets([
    "Tabelle <b>Users</b> wird in <b>AspNetUsers</b> umbenannt, die Spalte <b>Role</b> fällt weg.",
    "Neue Spalten von Identity: NormalizedEmail, NormalizedUserName, SecurityStamp, ConcurrencyStamp, "
    "AccessFailedCount, LockoutEnabled, LockoutEnd, EmailConfirmed, PhoneNumber, TwoFactorEnabled.",
    "Neue Tabellen: AspNetRoles, AspNetUserRoles (wer hat welche Rolle), AspNetUserClaims, "
    "AspNetUserLogins, AspNetUserTokens, AspNetRoleClaims.",
    "Der Fremdschlüssel der RefreshTokens zeigt jetzt auf AspNetUsers.",
])
d.merksatz("<b>„An operation was scaffolded that may result in the loss of data“</b>: EF warnt, weil die "
           "Spalte Role gelöscht wird. Hier ist das gewollt. Die Rollen stehen jetzt in AspNetUserRoles, und "
           "die alten Benutzer hat <font name='Mono'>identity-vorbereiten.sh</font> vorher bewusst entfernt. "
           "Bei so einer Warnung immer die Migration lesen, bevor man sie anwendet.", warn=True)
d.h2("SQLite und Tabellenumbau")
d.p("SQLite kann viele Änderungen nicht direkt (Primärschlüssel ändern, Spaltentyp ändern). EF Core baut "
    "die Tabelle dann automatisch neu auf: neue Tabelle anlegen, Daten kopieren, alte löschen, umbenennen. "
    "Das passiert unsichtbar beim Anwenden der Migration.")

# ------------------------------------------------------------------ 4
d.h1("4  Die Reihenfolge, die funktioniert hat")
d.code("""git fetch origin
git switch feature/fertigstellen        # aktueller main + Prüfskript
./identity-vorbereiten.sh               # Datenbank sichern, alte Benutzer löschen
dotnet tool restore
dotnet ef migrations add IdentityUmgestellt --project backend/HomeControl.Api
./pruefen.sh                            # alles prüfen
git add backend/HomeControl.Api/Migrations
git commit -m "Migration IdentityUmgestellt ergänzt"
git push
./app-starten.sh                        # wendet die Migration auf die echte DB an""")
d.bullets([
    "<b>Warum lokal?</b> <font name='Mono'>migrations add</font> muss das Projekt bauen und braucht das .NET SDK. "
    "Die Migration ist danach normaler Code und gehört ins Repo.",
    "<b>Warum zuerst sichern?</b> Eine Migration verändert die echte Datenbank. "
    "Die Sicherung liegt als <font name='Mono'>homecontrol.db.vor-identity.bak</font> neben der Datenbank.",
    "<b>Warum ein eigener Branch?</b> main bleibt lauffähig. Erst wenn alles grün ist, kommt der Pull Request.",
])

# ------------------------------------------------------------------ 5
d.h1("5  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Woher weißt du, dass der Stand funktioniert?", "./pruefen.sh: Build, 103 Tests, Frontend-Build und Lint laufen in einem Durchgang grün."],
    ["Was ist eine Migration?", "Eine versionierte Anleitung, wie die Datenbank zum aktuellen Datenmodell passt. Sie liegt als C#-Code im Repo."],
    ["Was passiert, wenn eine Migration fehlt?", "has-pending-model-changes schlägt fehl, und Migrate() würde beim Start eine Ausnahme werfen."],
    ["Warum testen die Endpunkt-Tests auch die Migrationen?", "WebApplicationFactory startet die echte App auf einer frischen SQLite-Datei. Program.cs wendet dabei alle Migrationen an."],
    ["Warum npm ci statt npm install?", "npm ci hält sich exakt an package-lock.json und ändert sie nie. So baut jeder Rechner dieselben Versionen."],
], [0.4, 0.6])

# ------------------------------------------------------------------ 6
d.h1("6  Merksätze")
d.merksatz("• Erst prüfen, dann weiterbauen. Ein Befehl für alles: ./pruefen.sh.<br/>"
           "• Exit-Code 0 = Erfolg. Bei Pipes zählt ${PIPESTATUS[0]}.<br/>"
           "• Modell geändert → neue Migration. Alte Migrationen nie anfassen.<br/>"
           "• Warnung „loss of data“ → Migration lesen, Datenbank vorher sichern.<br/>"
           "• Migrationen gehören ins Repo, die Datenbank (homecontrol.db) nicht.")

d.save("docs/Lernunterlage-Pruefen-und-Migration.pdf")
