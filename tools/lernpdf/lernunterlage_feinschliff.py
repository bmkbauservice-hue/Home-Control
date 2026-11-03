"""Erzeugt docs/Lernunterlage-Feinschliff.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_feinschliff.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Feinschliff",
    "Lernunterlage: öffentliches Repo aufräumen, einheitliche Zustände, kleine Fehler finden, README als Produktseite",
    footer="Home Control · Feinschliff · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("Vor der Präsentation sollte das Projekt nicht nur funktionieren, sondern <b>fertig wirken</b>: keine privaten "
    "Daten im öffentlichen Repo, jede Seite verhält sich beim Laden und bei Fehlern gleich, nichts überlappt auf dem "
    "Handy, und die README erklärt in einer Minute, was die App kann. Neue große Funktionen gab es dabei keine.")
d.table([
    ["Schritt", "Was", "Warum"],
    ["REVIEW N-1", "IP, Rechner- und Tailnet-Namen entfernt", "Das Repo ist öffentlich"],
    ["Durchklicken", "Alle Seiten im Demo, Desktop und 390 px", "Fehler sieht man, nicht der Compiler"],
    ["Zustände", "Laden, Fehler, leer überall gleich", "Wiedererkennbar, barrierearm"],
    ["Setup-Endpunkt", "„Erstes Konto anlegen“ nur ohne Benutzer", "Kein toter Link mehr"],
    ["macOS", "Skripte portabel gemacht", "Zweiter Rechner, gleiches Ergebnis"],
    ["README", "Produktseite mit Screenshots", "Erster Eindruck für Lehrer und Besucher"],
], [0.18, 0.44, 0.38])

# ------------------------------------------------------------------ 2
d.h1("2  Öffentliches Repo aufräumen (REVIEW N-1)")
d.p("Im Repo standen eine Tailscale-IP (in " + M + "tuya-rohwerte-anzeigen.sh" + E + ") und der echte Rechnername "
    "(in der Lern-PDF zum Betrieb). Beides verrät Teile des privaten Netzes. Jetzt stehen dort Platzhalter wie "
    + M + "&lt;rechner&gt;.&lt;tailnet&gt;.ts.net" + E + ".")
d.h2("Wie sucht man so etwas?")
d.code("""git grep -nIiE '100\\.[0-9]+\\.[0-9]+\\.[0-9]+|\\.ts\\.net|tailnet'   # Textdateien
python3 pdfsuche.py                                          # Text aus den PDFs (pypdf)""")
d.p("PDFs muss man eigens durchsuchen: " + M + "git grep" + E + " sieht nur den gepackten Inhalt. Die PDF wurde aus "
    "dem korrigierten Python-Skript neu erzeugt, nicht von Hand bearbeitet.")
d.merksatz("<b>Historie nicht umschreiben:</b> In alten Commits steht die IP weiter. Entfernen ginge nur mit "
           "Force-Push, der alle Klone kaputt macht. Die Werte sind nicht geheim (kein Passwort), nur privat. "
           "Bei einem echten Geheimnis gilt dagegen: <b>sofort austauschen</b> (rotieren), das Löschen aus der "
           "Historie allein reicht nie.", warn=True)
d.h2("Nebenbei: Token nicht in der Prozessliste")
d.code("""# vorher: Token als Argument, für andere Prozesse in "ps" sichtbar
curl -H "Authorization: Bearer $TOKEN" "$API/..."
# jetzt: Header über stdin ("-H @-")
printf 'Authorization: Bearer %s\\n' "$TOKEN" | curl -sS -H @- "$API/..." """)

# ------------------------------------------------------------------ 3
d.h1("3  Durchklicken mit einem Skript")
d.p("Ein kleines Python-Skript mit <b>Playwright</b> (steuert einen echten Browser ohne Fenster) meldet sich im "
    "Demo-Modus an, öffnet jede Seite in 1280 px und 390 px Breite, macht ein Foto und sammelt Konsolenfehler. "
    "Außerdem prüft es, ob die Seite breiter als das Fenster ist (horizontales Scrollen = Layoutfehler).")
d.table([
    ["Gefunden", "Ursache", "Lösung"],
    ["Langer Gerätename überlappt „online“", "LED schwebt absolut oben rechts, Name hat keinen Platz frei",
     "LED als eigene Zeile unter dem Gerätetyp"],
    ["Rollen-Auswahl auf dem Handy nur ein Punkt", "Tabellenspalte wird zusammengedrückt",
     "min-width: 120px, die Tabelle scrollt selbst"],
    ["„sonst alle 15 Minuten“", "Text älter als der Takt pro Gerät", "Verweis auf die Einstellungen"],
    ["„Erstes Konto anlegen“ trotz Konten", "Frontend wusste nicht, ob es Benutzer gibt",
     "GET /api/auth/setup"],
], [0.30, 0.38, 0.32])
d.p("Das Datum „11/03/2026“ im ersten Foto war <b>kein</b> Fehler der App: Der Test-Browser lief auf Englisch. "
    "Mit " + M + "locale=\"de-DE\"" + E + " stand dort 03.11.2026. Erst prüfen, dann ändern.")

# ------------------------------------------------------------------ 4
d.h1("4  Einheitliche Zustände")
d.p("Jede Seite, die Daten lädt, kennt vier Zustände. Vorher sah jeder anders aus (mal grauer Text, mal Kasten, "
    "„Erneut versuchen“ nur auf der Übersicht). Jetzt gibt es zwei kleine Bausteine in "
    + M + "components/StateMessages.tsx" + E + ":")
d.table([
    ["Zustand", "Anzeige", "Screenreader"],
    ["Lädt", "LoadingMessage: gestrichelter Kasten „Räume werden geladen …“", "role=\"status\" (höflich)"],
    ["Fehler beim Laden", "ErrorMessage mit Knopf „Erneut versuchen“", "role=\"alert\" (sofort)"],
    ["Fehler beim Speichern", "ErrorMessage ohne Knopf (Nochmal-Laden hilft da nicht)", "role=\"alert\""],
    ["Leer", "Kasten mit Text je Seite, z. B. „Noch keine Räume“", "–"],
], [0.22, 0.52, 0.26])
d.code("""export function ErrorMessage({ message, onRetry }: ErrorMessageProps) {
  return (
    <div className="alert alert-row" role="alert">
      <span>{message}</span>
      {onRetry && (
        <button type="button" className="button" onClick={onRetry}>Erneut versuchen</button>
      )}
    </div>
  )
}""")
d.h2("Wie funktioniert „Erneut versuchen“?")
d.p("Der useEffect, der lädt, hängt zusätzlich an einem Zähler " + M + "reloadKey" + E + ". Der Knopf zählt ihn hoch, "
    "React führt den Effect noch einmal aus. Kein doppelter Ladecode.")
d.code("""const [reloadKey, setReloadKey] = useState(0)
useEffect(() => { /* getRooms(search) ... */ }, [search, reloadKey])
function retry() { setLoading(true); setLoadError(null); setReloadKey((key) => key + 1) }""")
d.merksatz("Ladefehler und Speicherfehler getrennt halten: Nur beim Laden hilft „Erneut versuchen“.")

# ------------------------------------------------------------------ 5
d.h1("5  Der Setup-Endpunkt")
d.code("""// GET /api/auth/setup – immer offen, verrät nur: gibt es schon einen Benutzer?
private static async Task<SetupResponse> GetSetup(UserManager<User> userManager, CancellationToken ct)
{
    var hasUsers = await userManager.Users.AnyAsync(ct);
    return new SetupResponse(NeedsFirstAccount: !hasUsers);
}""")
d.bullets([
    "<b>AllowAnonymous:</b> Die Anmeldeseite fragt, bevor jemand angemeldet ist, auch mit Leseschutz.",
    "<b>Wenig verraten:</b> nur ein true/false, keine Anzahl, keine E-Mail.",
    "<b>Fehler im Frontend:</b> Antwortet der Server nicht, gilt false. Dann fehlt nur der Link, die Anmeldung geht.",
    "<b>Tests:</b> leere Datenbank → true, nach dem ersten Konto → false, mit Leseschutz trotzdem 200.",
    "<b>Grenze (REVIEW N-4):</b> Die Registrierung selbst bleibt offen, solange es keinen Benutzer gibt. Sicherer "
    "wäre ein erster Admin nur über einen lokalen Weg. Das ist der nächste Schritt.",
])

# ------------------------------------------------------------------ 6
d.h1("6  Linux und macOS")
d.p("Auf dem MacBook lief " + M + "./pruefen.sh" + E + " sofort. Zwei Stellen waren aber nur für Linux geschrieben:")
d.table([
    ["Stelle", "Linux (GNU)", "Portabel"],
    ["Zufallsschlüssel in einer Zeile", "base64 -w0", "base64 | tr -d '\\n'"],
    ["Schrift für die Lern-PDFs", "/usr/share/fonts/truetype/dejavu/", "mehrere Ordner prüfen, macOS: ~/Library/Fonts"],
], [0.32, 0.30, 0.38])
d.p("GNU-Werkzeuge (Linux) und BSD-Werkzeuge (macOS) heißen gleich, haben aber teils andere Optionen. "
    "Typische Fallen: " + M + "sed -i" + E + ", " + M + "readlink -f" + E + ", " + M + "date -d" + E + ".")

# ------------------------------------------------------------------ 7
d.h1("7  README als Produktseite")
d.p("Die README ist die Startseite auf GitHub. Sie beantwortet jetzt in dieser Reihenfolge: <b>Was ist das?</b> "
    "(Satz + großes Bild) → <b>Was kann es?</b> (Funktionen) → <b>Wie sieht es aus?</b> (Screenshots aus dem Demo) "
    "→ <b>Wie probiere ich es?</b> (Demo in einer Minute) → Architektur, Installation (Docker, Dienst, Entwickeln), "
    "Sicherheit, Grenzen, Ausblick (Push über Pulsar, MCP).")
d.bullets([
    "<b>Screenshots nur aus dem Demo:</b> keine echten Geräte, Namen oder Kamerabilder im öffentlichen Repo.",
    "<b>Grenzen offen nennen:</b> nur lesend, Polling, inoffizielle Blink-API. Das wirkt ehrlicher als nur Highlights.",
    "<b>Mermaid:</b> GitHub zeichnet Diagramme aus Text. Ändert sich die Architektur, ändert man eine Zeile.",
])

# ------------------------------------------------------------------ 8
d.h1("8  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Warum die IP nicht aus der Historie löschen?", "Force-Push zerstört alle Klone, und die IP ist kein Geheimnis. "
     "Bei einem echten Geheimnis: austauschen."],
    ["Warum ein eigener Baustein für Fehler?", "Gleiches Aussehen, gleiche Barrierefreiheit (role=alert), "
     "eine Stelle zum Ändern."],
    ["Was ist role=\"status\" vs. role=\"alert\"?", "status liest der Screenreader vor, wenn er Zeit hat; alert "
     "unterbricht sofort."],
    ["Warum Playwright?", "Man sieht die echte Seite in echten Breiten, auch Fehler, die kein Test findet."],
    ["Verrät /api/auth/setup zu viel?", "Nur, ob es Benutzer gibt. Das sieht man auch daran, ob Registrieren klappt."],
], [0.38, 0.62])

d.h1("9  Merksätze")
d.merksatz("• Öffentliches Repo: keine IPs, Hostnamen, Tailnet-Namen; PDFs mit durchsuchen.<br/>"
           "• Geleakte Geheimnisse rotieren, nicht nur löschen.<br/>"
           "• Jede Seite: Laden, Fehler, leer, Daten – überall gleich anzeigen.<br/>"
           "• Ladefehler mit „Erneut versuchen“, Speicherfehler ohne.<br/>"
           "• Erst auf Desktop und 390 px ansehen, dann „fertig“ sagen.")

d.save("docs/Lernunterlage-Feinschliff.pdf")
