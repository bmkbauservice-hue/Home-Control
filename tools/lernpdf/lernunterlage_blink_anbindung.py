"""Erzeugt docs/Lernunterlage-Blink-Anbindung.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_blink_anbindung.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Blink-Anbindung",
    "Lernunterlage: Geheimnisse sicher übergeben, Token-Rotation und ein eigener Client für eine inoffizielle API",
    footer="Home Control · Blink-Anbindung · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("Die Übersicht zeigte „Kameras (Blink): Nicht eingerichtet“. Ursache: Der Erst-Login war auf diesem Rechner "
    "nie gelaufen. Auf dem Weg dorthin kamen drei Themen dazu: Geheimnisse sicher übergeben (REVIEW.md <b>M-4</b>), "
    "ein Login, der an Kleinigkeiten scheiterte, und eine Bibliothek, die Blink nicht mehr akzeptiert (<b>M-5</b>).")

# ------------------------------------------------------------------ 2
d.h1("2  Geheimnisse nie als Argument (M-4)")
d.p("Der Spike hat das Blink-Token früher so gespeichert:")
d.code("""dotnet user-secrets set Blink:InitialRefreshToken <TOKEN> …
                                                 ▲ Token steht in der Prozessliste""")
d.p("Jedes Programm auf dem Rechner kann die Argumente laufender Prozesse lesen (" + M + "ps aux" + E + ", " +
    M + "/proc/*/cmdline" + E + "). Jetzt geht ein JSON-Objekt über <b>stdin</b>, das sieht nur der Empfänger:")
d.code("""using var process = StartUserSecrets("set", secretsId, redirectInput: true);
await process.StandardInput.WriteAsync(JsonSerializer.Serialize(values));
process.StandardInput.Close();     // Ende der Eingabe""")
d.table([
    ["Weg", "Wer kann mitlesen?", "Hier"],
    ["Kommandozeilenargument", "alle Prozesse (Prozessliste), Shell-History", "nie für Geheimnisse"],
    ["stdin (Pipe)", "nur der empfangende Prozess", "Spike, *-einrichten.sh"],
    ["verdeckte Eingabe", "niemand (erscheint als *)", "Blink-Passwort"],
    ["Datei mit Rechten 600", "nur der eigene Benutzer", "User Secrets"],
], [0.28, 0.45, 0.27])
d.p("Außerdem behält der Spike <b>kein</b> eigenes Token mehr: Nach dem ersten Login des Backends tauscht Blink das "
    "Token aus, eine Kopie wäre wertlos und trotzdem ein Geheimnis im Klartext.")

# ------------------------------------------------------------------ 3
d.h1("3  Fehler, die erst im echten Betrieb auffielen")
d.table([
    ["Problem", "Ursache", "Lösung"],
    ["Secrets landeten im falschen Projekt", "relative Pfade, stimmten nur aus tools/BlinkSpike",
     "--id &lt;UserSecretsId&gt; statt --project"],
    ["Jeder Versuch ein neues Gerät", "HardwareId erst nach Erfolg gespeichert", "beim ersten Start sofort speichern"],
    ["E-Mail in der Shell-History", "Anleitung: user-secrets set … E-Mail", "Spike fragt selbst, speichert über stdin"],
    ["Unklar, welcher Code läuft", "keine Versionsangabe", "Versionszeile beim Start"],
], [0.3, 0.37, 0.33])
d.merksatz("Fehlversuche sind bei fremden Diensten teuer: Blink sperrt nach 5 Fehlversuchen für 10 Minuten. "
           "Deshalb erst prüfen (--email-pruefen, Login in der Blink-App), dann einmal versuchen.", warn=True)

# ------------------------------------------------------------------ 4
d.h1("4  Eine Bibliothek, die Blink nicht mehr akzeptiert (M-5)")
d.p("Blink.NET 1.0.9 (die neueste Version) bekam für die Kameraliste " + M + "426 Upgrade Required" + E +
    ". Im dekompilierten Code war zu sehen: alter Endpunkt " + M + "/network/{id}/cameras" + E +
    " und ein alter Android-App-Build im Header. Das aktive Projekt <b>blinkpy</b> liest stattdessen den "
    "<b>Homescreen</b> und schickt keinen App-Build. Optionen:")
d.table([
    ["Option", "Bewertung"],
    ["(a) neuere Version", "gibt es nicht"],
    ["(b) Header per Reflection ändern", "zerbrechlich, nicht erklärbar, löst evtl. nichts"],
    ["(c) eigener schlanker Client (gewählt)", "volle Kontrolle, testbar, weniger fremder Code im Dauerdienst"],
], [0.42, 0.58])
d.code("""1. POST api.oauth.blink.com/oauth/token     Refresh-Token → Access Token (+ neues Refresh-Token)
2. GET  rest-prod…/users/tier_info          → Server (Tier) und Account-Id
3. GET  rest-{tier}…/accounts/{id}/homescreen → alle Kameras in EINER Antwort""")

# ------------------------------------------------------------------ 5
d.h1("5  Token-Rotation und der Schutz vor Schleifen")
d.bullets([
    "<b>Rotation:</b> Bei jeder Erneuerung gibt Blink ein neues Refresh-Token, das alte ist dann ungültig. "
    "Wird das neue nicht gespeichert, ist nach einem Neustart ein Login mit 2FA nötig. Deshalb wird es "
    "<b>sofort</b> verschlüsselt in die Datenbank geschrieben (Data Protection), bevor irgendetwas anderes passiert.",
    "<b>Nur eine Erneuerung gleichzeitig</b> (" + M + "SemaphoreSlim" + E + "): Zwei parallele Erneuerungen mit "
    "demselben Refresh-Token würden sich gegenseitig ungültig machen.",
    "<b>401 beim Abruf:</b> einmal erneuern und wiederholen. Ein zweites 401 ist ein Fehler, keine Schleife.",
    "<b>Abgelehntes Refresh-Token:</b> wird nie wieder benutzt. Erst ein neues Token (BlinkSpike) löst einen neuen "
    "Versuch aus, ohne Neustart.",
    "<b>426 oder Netzwerkfehler</b> sind kein Login-Problem: Das Token bleibt gültig.",
    "<b>Der Tier</b> landet im Hostnamen. Er wird geprüft (nur a–z, 0–9, Bindestrich), damit eine seltsame Antwort "
    "uns nie an einen fremden Server schickt.",
])
d.merksatz("Token, Bild-Adressen und Seriennummern kommen nie ins Log. Beim ersten Abruf stehen dort nur die "
           "NAMEN der Felder, damit sich die Zuordnung an echten Daten prüfen lässt, ohne zu raten.")

d.h2("Grenze: ein Prozess pro Datenbank")
d.p("Dienst (Port 5200) und " + M + "app-starten.sh" + E + " (Port 5111) teilen sich " + M + "homecontrol.db" + E +
    ". Die Sperre gilt nur innerhalb EINES Prozesses. Erneuern beide, benutzt der zweite ein Token, das der erste "
    "schon verbraucht hat, und Blink verlangt einen neuen Login mit 2FA.")
d.bullets([
    "<b>Beim Entwickeln ist Blink aus</b> (" + M + "Blink:Enabled = false" + E + " in appsettings.Development.json). "
    "Die Übersicht zeigt „Blink in der Entwicklung aus“.",
    "<b>Vor jeder Erneuerung</b> wird das Token frisch aus der Datenbank gelesen. Hat ein anderer Prozess erneuert, "
    "wird dessen neueres Token übernommen. Gegen genau gleichzeitige Erneuerungen hilft das nicht.",
])
d.merksatz("Eine Sperre im Arbeitsspeicher (lock, SemaphoreSlim) schützt nur den eigenen Prozess. Teilen sich "
           "mehrere Prozesse eine Ressource (Datei, Datenbank, Token), braucht es eine Regel darüber hinaus.", warn=True)

# ------------------------------------------------------------------ 6
d.h1("6  Tests")
d.p("12 Tests mit " + M + "FakeBlinkHandler" + E + " (spielt die Blink-Cloud, ohne Internet): Kameras aus dem "
    "Homescreen, kein App-Build-Header, rotiertes Token verschlüsselt in der DB und nicht im Log, nur eine "
    "Erneuerung bei fünf parallelen Aufrufen, 2-Minuten-Cache, einmal erneuern bei 401, kein Kreisel bei "
    "zweimal 401, neuer Startwert ohne Neustart, 426 ist kein Login-Problem, kein fremder Server, "
    "ohne HardwareId nicht eingerichtet, ausgeschaltet fragt nichts ab, zweiter Prozess übernimmt das neuere "
    "Token aus der Datenbank. Dazu Endpunkt-Tests: in der Entwicklung „disabled“.")
d.p("Danach einmal gegen das echte Konto: Status <b>connected</b>, zwei Kameras, Token verschlüsselt in der Datenbank.")

d.h1("7  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Warum stdin statt Argument?", "Argumente stehen in der Prozessliste, stdin sieht nur der Empfänger."],
    ["Warum ein eigener Client?", "Die Bibliothek wird von Blink abgelehnt, eine neuere gibt es nicht."],
    ["Was ist Token-Rotation?", "Jede Erneuerung liefert ein neues Refresh-Token, das alte wird ungültig."],
    ["Warum keine Schleife bei 401?", "Wiederholte Fehlversuche führen bei Blink zur Sperre."],
    ["Was ist das Risiko?", "Inoffizielle API: Blink kann sie jederzeit ändern. Dann muss der Client angepasst werden."],
], [0.36, 0.64])

d.h1("8  Merksätze")
d.merksatz("• Geheimnisse nie in Argumente, Shell-History oder Logs.<br/>"
           "• Rotierte Tokens sofort speichern, nur eine Erneuerung gleichzeitig.<br/>"
           "• Bei 401 einmal erneuern, dann aufhören.<br/>"
           "• Fremden Code ansehen, bevor man ihm Zugangsdaten gibt (hier: dekompiliert).<br/>"
           "• Bei inoffiziellen APIs nichts raten: Feldnamen an echten Daten prüfen.")

d.save("docs/Lernunterlage-Blink-Anbindung.pdf")
