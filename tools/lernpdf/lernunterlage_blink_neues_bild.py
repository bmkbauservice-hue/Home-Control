"""Erzeugt docs/Lernunterlage-Blink-Neues-Bild.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_blink_neues_bild.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Blink: Neues Bild",
    "Lernunterlage: eine bewusste Ausnahme von „nur lesend“ – Admin-Recht, Rate Limit pro Kamera, Befehl abfragen",
    footer="Home Control · Blink Neues Bild · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("Blink erneuert das Vorschaubild nur bei einer Bewegung. Ohne Bewegung zeigt die Kamerakarte ein Bild, das "
    "Tage alt sein kann. Neu ist deshalb ein Knopf <b>„Neues Bild“</b>: Die Kamera wacht auf und nimmt ein "
    "Standbild auf, wie „Bild aktualisieren“ in der Blink-App.")
d.p("Bisher galt streng: Tuya und Blink sind <b>nur lesend</b>. Der Knopf ist die <b>einzige bewusste Ausnahme</b>. "
    "Er schaltet nichts (nicht scharf/unscharf, kein Clip, keine Einstellung), kostet aber <b>Akku</b>, weil die "
    "Kamera aufwacht und über WLAN sendet. Deshalb ist er eng begrenzt und dokumentiert (BLINK.md, CLAUDE.md).")
d.merksatz("Eine Ausnahme von einer Regel ist in Ordnung, wenn sie bewusst entschieden, eng begrenzt und "
           "aufgeschrieben ist. Sonst wird aus der Ausnahme schleichend die neue Regel.", warn=True)

# ------------------------------------------------------------------ 2
d.h1("2  Drei Schranken")
d.table([
    ["Schranke", "Umsetzung", "Warum"],
    ["Wer?", "nur Admin (Policy AdminOnly, JWT)", "User 403, KI-Agent 401: Akku ist teuer"],
    ["Wann?", "nur auf Knopfdruck", "kein Hintergrunddienst löst Bilder aus"],
    ["Wie oft?", "1× pro Minute und Kamera (429)", "auch ein Doppelklick leert den Akku nicht"],
], [0.18, 0.42, 0.40])
d.code("""blink.MapPost("/cameras/{id:long}/snapshot", RequestSnapshot)
    .RequireAuthorization(AuthPolicies.AdminOnly)            // nur Admin
    .RequireRateLimiting(RateLimitPolicies.BlinkSnapshot);    // 1× pro Minute und Kamera""")
d.p("<b>POST statt GET:</b> GET heißt in HTTP „nur lesen, beliebig oft wiederholbar“. Ein neues Bild verändert "
    "etwas (Akku, neues Bild bei Blink), deshalb POST. Browser und Proxys wiederholen POST nie von selbst.")

# ------------------------------------------------------------------ 3
d.h1("3  Rate Limit pro Kamera statt pro Gerät")
d.p("Die anderen Regeln zählen pro Client-IP (Login, Bilder) oder gemeinsam (Tuya). Hier geht es um den Akku "
    "<b>einer Kamera</b>. Also wird nach der Kamera-Id aus der Adresse gezählt:")
d.code("""options.AddPolicy(RateLimitPolicies.BlinkSnapshot, context =>
{
    var limit = GetOptions(context).BlinkSnapshot;                 // Standard 1 pro 60 s
    var camera = context.Request.RouteValues["id"]?.ToString() ?? "unbekannt";
    return RateLimitPartition.GetFixedWindowLimiter(camera, _ => CreateFixedWindow(limit));
});""")
d.bullets([
    "<b>Partition:</b> Jede Kamera bekommt ihren eigenen Zähler. Wohnzimmer gesperrt heißt nicht Flur gesperrt.",
    "<b>Egal von wo:</b> Handy und Laptop teilen sich den Zähler derselben Kamera. Pro IP wäre das nicht so.",
    "<b>429 mit Retry-After:</b> Die Oberfläche zeigt „Neues Bild für diese Kamera höchstens einmal pro Minute. "
    "Bitte in … erneut versuchen.“",
])

# ------------------------------------------------------------------ 4
d.h1("4  Befehl abfragen statt blind warten")
d.p("Blink antwortet auf den POST sofort mit einer <b>Befehls-Id</b>. Das Bild ist da noch nicht fertig: Die "
    "Kamera muss erst aufwachen. Das Backend fragt deshalb den Befehlsstatus ab (Polling):")
d.numbered([
    "POST an Blink: " + M + "/network/{n}/camera/{id}/thumbnail" + E + " (Mini: " + M + "…/owls/{id}/thumbnail" + E +
    ", Türklingel: " + M + "…/doorbells/{id}/thumbnail" + E + ", wie im Projekt blinkpy).",
    "Antwort lesen: " + M + "{ \"id\": 555 }" + E + ".",
    "Jede Sekunde " + M + "GET /network/{n}/command/555" + E + ", bis " + M + "complete: true" + E +
    ", höchstens 40-mal.",
    "Homescreen und Bild-Cache verwerfen, neu holen: neuer Bildpfad, neue Aufnahmezeit.",
])
d.code("""for (var i = 0; i < MaxCommandPolls; i++)                  // höchstens 40-mal
{
    // ... GET /network/{n}/command/{id}, complete == true -> fertig
    await Task.Delay(CommandPollInterval, timeProvider, ct);  // im Test: TimeSpan.Zero
}""")
d.bullets([
    "<b>Obergrenze:</b> Ohne die 40 würde eine hängende Kamera die Anfrage ewig offen halten.",
    "<b>completed: false</b> ist kein Absturz: Blink hat den Befehl angenommen, nur nicht rechtzeitig gemeldet. "
    "Die Karte sagt das ehrlich.",
    "<b>Gescheitert:</b> Meldet Blink einen anderen " + M + "status_code" + E + " als 908 („läuft noch“), wird nicht "
    "weiter gewartet.",
    "<b>Token:</b> Lehnt Blink den POST mit 401 ab, wird einmal erneuert, dann gibt es einen Fehler statt einer "
    "Schleife. Dieselbe Regel wie bei den anderen Blink-Aufrufen.",
])

# ------------------------------------------------------------------ 5
d.h1("5  Im Frontend")
d.bullets([
    "Den Knopf sehen nur Admins (" + M + "isAdmin" + E + " aus " + M + "useAuth()" + E + "). Das ist nur Bequemlichkeit: "
    "<b>Die echte Prüfung macht das Backend.</b>",
    "Während der Aufnahme steht „Kamera nimmt auf …“ und der Knopf ist gesperrt (kein Doppelklick).",
    "Danach zählt ein Zähler (" + M + "snapshotCount" + E + ") hoch. Er steht in den Abhängigkeiten des Effekts, der "
    "das Bild lädt. So lädt React das neue Bild, ohne eigenen Sonderweg.",
    "Die <b>Aufnahmezeit</b> steht groß unter dem Bild, <b>gelb</b>, wenn sie älter als einen Tag ist. Ein "
    "Vorschaubild ist kein Livebild.",
])

# ------------------------------------------------------------------ 6
d.h1("6  Tests")
d.table([
    ["Test", "prüft"],
    ["Neues_Bild_loest_POST_aus_wartet_auf_den_Befehl_…", "richtige Adresse, POST, unser Token, neue Aufnahmezeit"],
    ["Neues_Bild_fuer_eine_Mini_nimmt_die_owls_Adresse", "Mini-Kamera bekommt die andere Adresse"],
    ["Neues_Bild_gibt_nach_40_Abfragen_auf", "keine Endlosschleife"],
    ["Neues_Bild_fuer_unbekannte_Kamera_loest_nichts_aus", "kein Aufruf bei Blink"],
    ["Neues_Bild_als_User_403_mit_Agenten_Schluessel_401", "nur Admin"],
    ["Neues_Bild_als_Admin_hoechstens_einmal_pro_Minute_…", "zweiter Klick 429 mit Retry-After, andere Kamera 200"],
], [0.55, 0.45])
d.p("Die Blink-Cloud wird im Test nie aufgerufen: Ein Fake-" + M + "HttpMessageHandler" + E + " spielt POST und "
    "Befehlsstatus. Live ausprobiert wird erst nach dem Merge im Dienst, weil jeder Versuch Akku kostet.")

# ------------------------------------------------------------------ 7
d.h1("7  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Ist das nicht Schalten?", "Nein: nichts wird ein- oder ausgeschaltet. Es kostet nur Akku, deshalb begrenzt."],
    ["Warum POST?", "Es verändert etwas. GET muss gefahrlos wiederholbar sein."],
    ["Warum pro Kamera zählen?", "Geschützt wird der Akku einer Kamera, egal von welchem Gerät geklickt wird."],
    ["Warum nur Admin?", "Teure Aktion; der KI-Agent und normale User dürfen nur lesen."],
    ["Was ist Polling?", "Wiederholt nachfragen, bis etwas fertig ist, mit Pause und Obergrenze."],
    ["Warum Knopf verstecken, wenn das Backend prüft?", "Nur Bequemlichkeit. Sicherheit kommt immer vom Server."],
], [0.40, 0.60])

d.h1("8  Merksätze")
d.merksatz("• Ausnahmen bewusst entscheiden, eng begrenzen, aufschreiben.<br/>"
           "• Verändernde Aufrufe sind POST, nie GET.<br/>"
           "• Rate Limit nach dem zählen, was geschützt wird (hier: die Kamera).<br/>"
           "• Polling immer mit Pause und Obergrenze.<br/>"
           "• Knöpfe ausblenden ist Komfort, die Rechte prüft der Server.")

d.save("docs/Lernunterlage-Blink-Neues-Bild.pdf")
