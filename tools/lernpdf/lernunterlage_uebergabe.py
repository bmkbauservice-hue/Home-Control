"""Erzeugt docs/Lernunterlage-Uebergabe.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_uebergabe.py)."""
from lernpdf import Doc

d = Doc(
    "Übergabe und Arbeitsweise",
    "Lernunterlage: Stand 31.10.2026, offene Punkte und wie man sauber weiterarbeitet",
    footer="Home Control · Übergabe · Lernunterlage",
)

d.h1("1  Projekt auf einen Blick")
d.table([
    ["Schicht", "Technik", "Was sie macht"],
    ["Frontend", "React, TypeScript, Vite, CSS-Variablen", "Oberfläche, Login, Status-LEDs, PWA"],
    ["API", "ASP.NET Core 10 Minimal API", "REST-Endpunkte, ProblemDetails, OpenAPI/Scalar"],
    ["Sicherheit", "Identity, JWT, Refresh-Token, API-Schlüssel", "Wer bist du? Was darfst du?"],
    ["Daten", "EF Core, SQLite, Migrationen", "Räume, Geräte, Ereignisse, Benutzer, Tokens"],
    ["Anbindungen", "Tuya-Cloud (HMAC), Blink (inoffiziell)", "Zustände und Online-Status lesen, nie schalten"],
    ["Betrieb", "systemd, Tailscale serve", "Dienst mit Neustart, HTTPS-Link fürs iPhone"],
], [0.18, 0.4, 0.42])

d.h1("2  Der Arbeitsablauf (Git)")
d.code("""git switch main && git pull                 # aktuellen Stand holen
git switch -c feature/<thema>               # eigener Branch für die Aufgabe
# ... Code ändern ...
./pruefen.sh                                # Build, Migrationen, Tests, Frontend, Lint
git add <dateien> && git commit -m "Was und warum"
git push -u origin feature/<thema>          # dann auf GitHub einen Pull Request öffnen
# Review lesen, PR mergen (Merge-Commit), lokal: git switch main && git pull""")
d.bullets([
    "<b>Branch:</b> main bleibt immer lauffähig. Neue Arbeit passiert daneben.",
    "<b>Pull Request:</b> Der Schnappschuss zum Zeitpunkt des Mergens zählt. Später gepushte Commits "
    "landen nicht automatisch in main (das ist bei PR #5 passiert und wurde mit PR #6 nachgeholt).",
    "<b>Kleine Commits:</b> Ein Commit, eine Idee. Die Nachricht sagt, was und warum.",
    "<b>Ehrlichkeit:</b> Was nicht gebaut oder getestet wurde, steht in der Commit-Nachricht.",
])

d.h1("3  Was am 31.10. dazukam")
d.table([
    ["Pull Request", "Inhalt"],
    ["#15 Stand geprüft", "pruefen.sh, Migration IdentityUmgestellt, Startskript nutzt die echte Datenbank, 103/103 Tests grün"],
    ["#16 Betrieb, Status-LEDs, Unterricht",
     "Dienst + Tailscale-HTTPS, GET /api/status mit LEDs im Dashboard, Blink-Login-Schutz, AddValidation, "
     "GetUserId(), OpenAPI-Tags, Übergabe-Dokument, vier Lern-PDFs"],
], [0.3, 0.7])

d.h1("4  Offene Punkte")
d.numbered([
    "PR #16 mit <font name='Mono'>./pruefen.sh</font> prüfen und mergen.",
    "Dauerbetrieb einrichten: <font name='Mono'>./dienst-einrichten.sh</font>, "
    "<font name='Mono'>./tailscale-https-einrichten.sh</font>, iPhone/iPad.",
    "KI-Agent: API-Schlüssel und https-Adresse in n8n, dann Lesen schützen.",
    "Weitere Gerätetypen (Temperatur, Watt, Akku) nach den echten Tuya-Rohwerten.",
    "Blink-Seite mit echter Kameraliste.",
    "TypedResults in allen Endpunkten (wie im Unterricht).",
    "Endpunktfilter gegen leere GUIDs.",
    "Variante B: Betrieb auf dem OVH-Server mit Docker.",
])

d.h1("5  Mit einer KI weiterarbeiten")
d.p("Eine KI kennt dein Projekt nicht. Gib ihr deshalb immer denselben Einstieg: "
    "<font name='Mono'>docs/UEBERGABE.md</font> lesen lassen, Regeln nennen, eine klar begrenzte Aufgabe geben. "
    "Die fertige Vorlage steht in UEBERGABE.md, Abschnitt 5.")
d.table([
    ["Gut", "Schlecht"],
    ["„Lies docs/UEBERGABE.md. Aufgabe: Punkt 5, Blink-Seite mit Kameraliste.“", "„Mach mal die App fertig.“"],
    ["„Vor dem Commit ./pruefen.sh, Ergebnis zeigen.“", "„Passt schon, wird schon laufen.“"],
    ["„Erkläre mir, warum du das so machst.“", "Code übernehmen, ohne ihn zu verstehen"],
], [0.55, 0.45])
d.merksatz("<b>Merksatz:</b> Die KI schreibt Vorschläge. Ob sie stimmen, entscheiden der Compiler, die Tests "
           "und du. Erst wenn <font name='Mono'>./pruefen.sh</font> grün ist und du es erklären kannst, ist es fertig.")

d.h1("6  Alle Lernunterlagen")
d.table([
    ["PDF", "Thema"],
    ["Lernunterlage-Authentifizierung.pdf", "JWT, Refresh-Token, Rotation, Rollen, XSS/CSRF"],
    ["Lernunterlage-Identity.pdf", "UserManager, IdentityUser, Rollen, Lockout"],
    ["Lernunterlage-API-Schluessel.pdf", "Zugang für den KI-Agenten"],
    ["Lernunterlage-Tuya-Geraete.pdf", "Tuya-Cloud, Statuscodes, Abgleich"],
    ["Lernunterlage-Pruefen-und-Migration.pdf", "pruefen.sh, Exit-Codes, Migrationen"],
    ["Lernunterlage-Betrieb.pdf", "systemd, dotnet publish, Tailscale-HTTPS"],
    ["Lernunterlage-Status-LEDs.pdf", "/api/status, Cache, Context, CSS-LEDs"],
    ["Lernunterlage-Validierung-und-Unterricht.pdf", "AddValidation, GetUserId, OpenAPI, Vergleich mit dem Unterricht"],
    ["Lernunterlage-Uebergabe.pdf", "diese Unterlage"],
], [0.5, 0.5])

d.save("docs/Lernunterlage-Uebergabe.pdf")
