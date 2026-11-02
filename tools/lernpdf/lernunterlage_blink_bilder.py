"""Erzeugt docs/Lernunterlage-Blink-Kamerabilder.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_blink_bilder.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Blink-Kamerabilder",
    "Lernunterlage: Kamerabilder sicher anzeigen – Proxy, Token, Object-URLs und Datenschutz",
    footer="Home Control · Blink-Kamerabilder · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("Die Übersicht und die Blink-Seite zeigen jetzt pro Kamera Name, Online, Akku und das <b>letzte Vorschaubild</b> "
    "mit „Aufnahme von …“. Kamerabilder sind besonders schützenswert: Sie zeigen das Innere einer Wohnung. "
    "Deshalb gelten strengere Regeln als für Lampen und Steckdosen.")

# ------------------------------------------------------------------ 2
d.h1("2  Proxy statt Direktlink")
d.p("Blink liefert im Homescreen nur einen <b>Pfad</b> zum Bild. Abrufen kann ihn nur, wer ein gültiges "
    "<b>Blink-Token</b> mitschickt. Zwei Wege:")
d.table([
    ["", "Direktlink (nicht gewählt)", "Proxy (gewählt)"],
    ["Wer holt das Bild bei Blink?", "der Browser", "unser Backend"],
    ["Wo liegt das Blink-Token?", "im Browser (JavaScript, Netzwerk-Tab)", "nur im Backend"],
    ["Wer darf das Bild sehen?", "jeder, der Token und Pfad kennt", "nur wer bei Home Control angemeldet ist"],
    ["Rate Limit, Cache, Protokoll", "nicht möglich", "an einer Stelle im Backend"],
], [0.32, 0.34, 0.34])
d.code("""Browser ──JWT──▶ Home Control ──Blink-Token──▶ rest-e006.immedia-semi.com
        ◀── nur das Bild ──      ◀────── Bild ───────""")
d.merksatz("Ein Token im Browser ist wie ein Hausschlüssel unter der Fußmatte: Jedes Skript, jede Erweiterung und "
           "jeder, der über die Schulter schaut, kann ihn mitnehmen. Mit dem Blink-Token könnte man das ganze "
           "Kamerakonto lesen, nicht nur ein Bild.", warn=True)

# ------------------------------------------------------------------ 3
d.h1("3  Der Endpunkt")
d.code("""blink.MapGet("/cameras/{id:long}/thumbnail", GetThumbnail)
    .RequireAuthorization(AuthPolicies.SignedInUser)       // nur JWT
    .RequireRateLimiting(RateLimitPolicies.BlinkThumbnails); // 30 pro Minute

http.Response.Headers.CacheControl = "no-store";             // nicht zwischenspeichern
return Results.File(thumbnail.Content, thumbnail.ContentType);""")
d.bullets([
    "<b>Nur Menschen:</b> Die Policy " + M + "SignedInUser" + E + " akzeptiert nur das JWT aus dem Login. Der "
    "API-Schlüssel des KI-Agenten bekommt 401, auch wenn Lesen sonst ohne Anmeldung erlaubt ist.",
    "<b>no-store:</b> Das Bild landet nicht im Browser-Cache und nicht in einem Proxy dazwischen.",
    "<b>Kein neues Foto:</b> Nur GET auf das vorhandene Bild. Ein neues Foto würde die Akku-Kamera wecken.",
    "<b>Schutz für Blink:</b> Bilder 60 Sekunden im Server-Cache, dazu ein eigenes Rate Limit.",
    "<b>Aufnahmezeit:</b> im Header " + M + "X-Thumbnail-Taken-At" + E + ", aus dem Zeitstempel im Bildpfad.",
])

# ------------------------------------------------------------------ 4
d.h1("4  Dem Pfad von Blink nicht blind vertrauen")
d.p("Der Pfad kommt aus einer fremden Antwort. Würde das Backend ihn ungeprüft aufrufen, könnte eine manipulierte "
    "Antwort es zu einem fremden Server schicken – mit unserem Token im Header (<b>SSRF</b>, Server-Side Request "
    "Forgery). Deshalb:")
d.bullets([
    "Nur <b>relative</b> Pfade (beginnen mit /), angehängt an " + M + "https://rest-{tier}.immedia-semi.com" + E + ".",
    "Kein " + M + ".." + E + ", kein " + M + "://" + E + ", kein " + M + "//" + E + " am Anfang, keine Leerzeichen.",
    "Nur " + M + "image/jpeg" + E + " oder " + M + "image/png" + E + " weiterreichen, höchstens 5 MB.",
])

# ------------------------------------------------------------------ 5
d.h1("5  Im Browser: fetch und Object-URLs")
d.p("Ein " + M + "&lt;img src=\"/api/…/thumbnail\"&gt;" + E + " kann keinen " + M + "Authorization" + E +
    "-Header mitschicken. Das Bild wird deshalb mit " + M + "fetch" + E + " geladen (unser " + M + "send()" + E +
    " hängt das Access Token an) und als <b>Object-URL</b> angezeigt:")
d.code("""getBlinkThumbnail(camera.id).then(({ image }) => {
  objectUrl = URL.createObjectURL(image)   // "blob:…", zeigt auf die Bytes
  setImageUrl(objectUrl)
})
return () => {                              // Seite verlassen oder neues Bild
  ignore = true
  if (objectUrl !== null) URL.revokeObjectURL(objectUrl)   // Speicher freigeben
}""")
d.bullets([
    "Eine Object-URL gilt nur in diesem Tab und nur, solange sie nicht freigegeben wurde.",
    "Ohne " + M + "revokeObjectURL" + E + " bleiben die Bilddaten im Speicher, bei jedem Neuladen mehr (Speicherleck).",
    "<b>ignore</b>: Kommt die Antwort erst, nachdem die Seite verlassen wurde, wird sie verworfen.",
])

# ------------------------------------------------------------------ 6
d.h1("6  Datenschutz bei Kamerabildern")
d.table([
    ["Grundsatz", "Umsetzung in Home Control"],
    ["Datensparsamkeit", "Nur das letzte Vorschaubild, keine Videos, kein Archiv"],
    ["Speicherbegrenzung", "Kein Bild auf der Festplatte, nur 60 s im Arbeitsspeicher; no-store im Browser"],
    ["Zugriffsschutz", "Nur angemeldete Menschen (JWT), nicht der KI-Agent"],
    ["Keine Spuren im Log", "Nur maskierte Form des Pfads (Ziffern als #), nie Bild, Pfad oder Token"],
    ["Zweckbindung", "Anzeigen, ob zu Hause alles in Ordnung ist. Keine Auswertung, keine Weitergabe"],
], [0.3, 0.7])
d.p("Bei Tests mit echten Daten wird das Bild bewusst nicht angesehen: Geprüft werden nur Größe, Typ und "
    "Aufnahmezeit. Kameras, die öffentliche Bereiche oder Nachbarn erfassen, unterliegen zusätzlichen Regeln (DSGVO).")

# ------------------------------------------------------------------ 7
d.h1("7  Die inoffizielle API als Risiko")
d.bullets([
    "Blink hat keine öffentliche API. Am 02.11.2026 lehnte Blink die Bibliothek Blink.NET mit „426 Upgrade "
    "Required“ ab; Home Control nutzt seitdem einen eigenen Client nach dem Vorbild von blinkpy.",
    "Ändert Blink Adressen oder Felder, gibt es keine Vorwarnung. Die Karte zeigt dann ehrlich "
    "„Blink: derzeit nicht verfügbar“ statt eines Fehlers.",
    "Zwei Bildformate (Zeitstempel oder Pfad) werden unterstützt, weil Blink beide benutzt.",
])

d.h1("8  Grenzen")
d.bullets([
    "Nur das <b>letzte</b> Bild: Es kann Stunden alt sein, deshalb steht die Aufnahmezeit dabei.",
    "Beim alten Bildformat ist die Aufnahmezeit unbekannt.",
    "Online nur bei eindeutigem Status (online/offline), sonst „unbekannt“; Temperatur noch nicht angezeigt.",
    "Ein Prozess pro Datenbank: Beim Entwickeln ist Blink aus.",
])

# ------------------------------------------------------------------ 9
d.h1("9  Tests")
d.table([
    ["Test", "prüft"],
    ["Ohne_Anmeldung_401 / Mit_Agenten_Schluessel_401", "nur Menschen mit JWT"],
    ["Mit_Anmeldung_wird_das_Bild_durchgereicht", "image/jpeg, no-store, Aufnahmezeit-Header"],
    ["Fehler_von_Blink_ergibt_verstaendliche_Meldung_statt_500", "502 mit deutschem Text"],
    ["Altes_/Neues_Bildformat_…", "richtige Adresse, nur GET, unser Token"],
    ["Verdaechtiger_Pfad_… ", "kein Aufruf eines fremden Servers"],
    ["Bild_wird_60_Sekunden_vorgehalten", "Server-Cache"],
    ["Bild_bei_401_einmal_erneuern_dann_Fehler", "keine Schleife"],
    ["Keine_Bilddaten_von_Blink_ergeben_einen_Fehler", "HTML-Fehlerseite wird nicht als Bild ausgegeben"],
], [0.55, 0.45])

d.h1("10  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Warum lädt der Browser das Bild nicht direkt bei Blink?", "Dafür bräuchte er das Blink-Token. Das bleibt im Backend."],
    ["Warum fetch statt img src?", "img kann keinen Authorization-Header schicken."],
    ["Was ist eine Object-URL?", "Eine blob:-Adresse auf Bilddaten im Speicher dieses Tabs. Muss freigegeben werden."],
    ["Warum no-store?", "Kamerabilder sollen nirgends liegen bleiben."],
    ["Warum darf der KI-Agent keine Bilder?", "Bilder sind sensibler als Messwerte. Nur angemeldete Menschen."],
    ["Was ist SSRF?", "Der Server wird dazu gebracht, eine fremde Adresse aufzurufen. Hier verhindert durch Pfadprüfung."],
], [0.42, 0.58])

d.h1("11  Merksätze")
d.merksatz("• Fremde Tokens bleiben im Backend, der Browser bekommt nur das Ergebnis (Proxy).<br/>"
           "• Kamerabilder: nur angemeldet, no-store, nichts speichern, nichts loggen.<br/>"
           "• Adressen aus fremden Antworten prüfen, bevor man sie aufruft (SSRF).<br/>"
           "• Object-URLs immer mit revokeObjectURL freigeben.<br/>"
           "• Bei einer inoffiziellen API ehrlich anzeigen, wenn sie nicht verfügbar ist.")

d.save("docs/Lernunterlage-Blink-Kamerabilder.pdf")
