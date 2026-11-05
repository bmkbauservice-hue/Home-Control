"""Erzeugt docs/Lernunterlage-Echtzeit-und-API.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_echtzeit_api.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Echtzeit und API",
    "Lernunterlage: REST, HTTP-GET und -POST, der TuyaClient, Server-Sent Events, Tuya-Push und Docker am eigenen Projekt",
    footer="Home Control · Echtzeit und API · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Das Gesamtbild")
d.p("Ändert sich ein Gerät, soll das Dashboard es möglichst sofort zeigen. Dafür gibt es zwei Abschnitte, die "
    "man auseinanderhalten muss:")
d.code("""Gerät ─► Tuya-Cloud ══ Abschnitt B ══► Backend ══ Abschnitt A ══► Browser
                      heute: Polling            neu: SSE, sofort""")
d.merksatz("Ehrlicher Stand: Abschnitt A ist gebaut und getestet. Abschnitt B ist weiter Polling (Tür alle 3 Minuten). "
           "Echte Tuya-Echtzeit gibt es noch nicht. Der Chip „Live“ meint nur Abschnitt A.", warn=True)

# ------------------------------------------------------------------ 2
d.h1("2  Was ist eine REST-API?")
d.p("Eine API ist eine Schnittstelle, über die Programme miteinander sprechen. <b>REST</b> heißt: Es gibt "
    "<b>Ressourcen</b> mit einer Adresse (Geräte, Räume), und HTTP-Methoden sagen, was damit geschehen soll. Jede "
    "Anfrage steht für sich, der Server merkt sich dazwischen nichts über den Client.")
d.table([
    ["Aufruf bei Home Control", "Bedeutung", "Antwort"],
    ["GET /api/devices", "alle Geräte lesen", "200 und JSON-Liste"],
    ["GET /api/devices/{id}", "ein Gerät lesen", "200 oder 404"],
    ["POST /api/devices", "Gerät anlegen (nur Admin)", "201, sonst 400, 401, 403"],
    ["PUT /api/devices/{id}", "Gerät ändern (nur Admin)", "200, sonst 400, 404"],
    ["DELETE /api/devices/{id}", "Gerät löschen (nur Admin)", "204 oder 404"],
], [0.38, 0.34, 0.28])

# ------------------------------------------------------------------ 3
d.h1("3  HTTP-GET und HTTP-POST")
d.table([
    ["", "GET", "POST"],
    ["Zweck", "lesen", "etwas auslösen oder anlegen"],
    ["Daten", "in der Adresse (?date=2026-11-05)", "im Body, meist JSON"],
    ["Verändert etwas?", "nein", "ja"],
    ["Wiederholbar?", "gefahrlos, Browser und Proxys dürfen es wiederholen", "nicht automatisch"],
    ["Beispiel im Projekt", "GET /api/status", "POST /api/blink/cameras/{id}/snapshot"],
], [0.22, 0.4, 0.38])
d.p("Darum ist „Neues Bild“ ein POST: Es verändert etwas (Akku, neues Bild). Bei Tuya ist es genauso: Status lesen ist "
    "ein GET, ein Schaltbefehl wäre ein POST. Zu jedem Aufruf gehören Header, z. B. "
    + M + "Authorization: Bearer ..." + E + " mit dem Anmelde-Token.")

# ------------------------------------------------------------------ 4
d.h1("4  Wie der TuyaClient arbeitet")
d.p("Das Frontend spricht nie direkt mit Tuya. Das Client-Secret bleibt so im Backend. Ablauf für einen Statusabruf:")
d.numbered([
    "<b>Token holen:</b> " + M + "TuyaTokenStore" + E + " hat ein gültiges Token im Speicher. Sonst holt der Client eines "
    "(" + M + "GET /v1.0/token?grant_type=1" + E + "). Das " + M + "SemaphoreSlim" + E + " sorgt dafür, dass parallele Anfragen "
    "nur einen Abruf auslösen.",
    "<b>Signieren:</b> " + M + "TuyaSignature" + E + " bildet HMAC-SHA256 über Client-Id, Token, Zeit, Nonce, Methode, "
    "Hash des Bodys und Adresse. Tuya rechnet nach und erkennt jede Veränderung.",
    "<b>Senden:</b> " + M + "GET /v1.0/iot-03/devices/{id}/status" + E + " mit den Signatur-Headern.",
    "<b>Auswerten:</b> Tuya antwortet auch bei Fehlern mit HTTP 200, entscheidend ist " + M + "\"success\"" + E + ". Bei einem "
    "Token-Fehler (z. B. 1010) wird genau einmal mit neuem Token wiederholt.",
    "<b>Übertragen:</b> " + M + "TuyaDeviceSyncer.Apply" + E + " rechnet um (1234 → 123,4 W) und speichert. Ein Ereignis gibt "
    "es nur, wenn sich etwas geändert hat.",
])
d.p("<b>Testen ohne Internet:</b> Der HttpClient bekommt einen " + M + "HttpMessageHandler" + E + ". " + M + "FakeTuyaHandler" + E +
    " (Tests) und " + M + "DemoTuyaHandler" + E + " (Demo) antworten statt der Cloud. Alles darüber bleibt echter Code.")

# ------------------------------------------------------------------ 5
d.h1("5  Server-Sent Events (SSE)")
d.p("Normalerweise fragt der Browser, und der Server antwortet einmal. Bei <b>SSE</b> bleibt die Antwort offen, und "
    "der Server schreibt immer wieder etwas hinein. So sieht das auf der Leitung aus:")
d.code("""GET /api/live/devices
Authorization: Bearer eyJ...

HTTP/1.1 200 OK
Content-Type: text/event-stream

event: connected
data: null

event: device-change
data: {"deviceIds":["3f2c..."],"source":"tuya-abgleich","atUtc":"2026-11-05T13:06:00Z"}

event: ping
data: null""")
d.bullets([
    "<b>Nur Ids, keine Werte:</b> Der Browser lädt danach über die normalen Endpunkte neu. Es gibt nur einen Weg zu den "
    "Daten, und eine verlorene Meldung schadet nicht.",
    "<b>ping alle 25 s:</b> hält die Verbindung durch Proxys offen.",
    "<b>Abbruch:</b> Der Browser verbindet sich neu (1, 2, 4 … 30 s) und lädt danach einmal alles.",
    "<b>Tab im Hintergrund:</b> Verbindung zu, das spart Akku und eine der wenigen Verbindungen des Browsers.",
    "<b>Rückfallebene:</b> Alle Seiten laden weiter jede Minute neu.",
])
d.h2("Warum SSE und nicht SignalR?")
d.table([
    ["", "SSE", "SignalR"],
    ["Richtung", "Server → Browser, genau unser Fall", "beide Richtungen"],
    ["Abhängigkeit", "keine, in .NET 10 eingebaut", "eigene Client-Bibliothek"],
    ["Token", "im Header (fetch)", "im Browser meist in der URL"],
    ["Erklärung", "eine HTTP-Antwort, die nicht aufhört", "Hub, Gruppen, Protokoll"],
], [0.2, 0.42, 0.38])
d.p("<b>Warum fetch statt EventSource?</b> Das eingebaute " + M + "EventSource" + E + " kann keinen Authorization-Header "
    "schicken. Das Token müsste in die URL und stünde dann in Logs.")
d.h2("Publish/Subscribe im Backend")
d.code("""// DeviceChangeHub: je Verbindung eine eigene Warteschlange
var channel = Channel.CreateBounded<DeviceChangeEvent>(new BoundedChannelOptions(32)
{
    FullMode = BoundedChannelFullMode.DropOldest,   // voll? Älteste Meldung fällt weg
});

// TuyaDeviceSyncer: erst speichern, dann melden
await db.SaveChangesAsync(ct);
changeHub.Publish(new DeviceChangeEvent(changedIds, DeviceChangeSources.TuyaSync, now));""")
d.p("Der Abgleich weiß nicht, wer zuhört, und die Browser wissen nicht, wer die Änderung ausgelöst hat. Das ist die Idee von "
    "<b>Publish/Subscribe</b>. Ein späterer Tuya-Push wäre nur ein weiterer Absender.")
d.p("<b>Sicherheit:</b> Die Anmeldung wird nur beim Aufbau geprüft. Deshalb endet der Strom beim Ablauf des Access Tokens "
    "(höchstens 15 Minuten). Der Browser verbindet sich neu, und dabei wird wieder geprüft.")

# ------------------------------------------------------------------ 6
d.h1("6  Wie Push-Nachrichten von Tuya zum Backend kämen")
d.p("<b>Noch nicht gebaut.</b> Tuya bietet dafür den <b>Message Service</b> an, technisch eine Warteschlange auf Basis von "
    "<b>Apache Pulsar</b>.")
d.table([
    ["Polling (heute)", "Push (geplant)"],
    ["Backend fragt regelmäßig: „Hat sich etwas geändert?“", "Tuya legt jede Meldung in eine Warteschlange, das Backend liest mit"],
    ["Tür alle 3 Minuten, kostet Kontingent, auch ohne Änderung", "sofort, kostet je Nachricht"],
    ["Kurzes Öffnen zwischen zwei Abfragen wird verpasst", "jede Meldung kommt an, mit eigener Uhrzeit"],
], [0.5, 0.5])
d.bullets([
    "<b>Die Verbindung geht vom Backend aus</b> (" + M + "pulsar+ssl://mqe.tuyaeu.com:7285" + E + "). Kein offener Port, keine "
    "öffentliche Adresse. Das geht auch hinter Tailscale und im Container.",
    "<b>Bestätigung (Ack):</b> Pulsar hebt eine Nachricht auf, bis das Backend sie bestätigt. War es offline, kommt sie danach. "
    "Doppelte Zustellung ist möglich, die Verarbeitung muss das aushalten.",
    "<b>Reihenfolge:</b> Die Doku nennt keine Garantie. Jede Meldung hat aber eine Zeit, damit erkennt man Veraltetes.",
    "<b>Polling bleibt</b> als Rückfallebene, nur seltener.",
])
d.merksatz("Offene Frage: Laut Doku ist für Gerätenachrichten ein Client über OEM-App oder App SDK vorgesehen. Ob unsere "
           "Geräte aus dem verknüpften Smart-Life-Konto Nachrichten liefern, ist nicht belegt. Das klärt erst ein Spike.", warn=True)

# ------------------------------------------------------------------ 7
d.h1("7  Wie Docker dazugehört")
d.bullets([
    "<b>An der API ändert sich nichts:</b> Dieselben Endpunkte, derselbe Code. Nur die Umgebung ist eine andere.",
    "<b>Port:</b> Im Container hört die App auf 8080, nach außen ist es " + M + "127.0.0.1:5400" + E + " (Betrieb) oder 5300 (Demo). "
    "SSE ist normales HTTP und geht durch diese Weiterleitung.",
    "<b>Ausgehend:</b> Aufrufe an Tuya, später auch Pulsar, gehen aus dem Container hinaus, dafür muss nichts geöffnet werden.",
    "<b>Volumes:</b> Datenbank und Schlüssel liegen außerhalb des Containers und überleben ein Update.",
    "<b>Geheimnisse:</b> kommen beim Start über " + M + ".env" + E + ", nie ins Image.",
    "<b>Grenze:</b> Blink und der Tuya-Abgleich dürfen nur an einer Stelle laufen. Dienst und Container gleichzeitig mit echten "
    "Zugangsdaten verbrauchen doppelt Kontingent, bei Blink melden sie sich gegenseitig ab.",
])

# ------------------------------------------------------------------ 8
d.h1("8  Selbst ausprobieren")
d.code("""./demo-starten.sh       # Beispielhaus auf http://localhost:5300, keine echten Daten""")
d.p("Im Browser anmelden. Die Befehle für das Umschalten der simulierten Tür stehen in docs/ECHTZEIT.md. Die Haustür "
    "wechselt dann sofort, ohne Neuladen. Das läuft durch den echten Code, nur die Cloud ist erfunden.")

# ------------------------------------------------------------------ 9
d.h1("9  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Ist das Echtzeit von Tuya?", "Nein. Vom Server zum Browser ja, von Tuya zum Server weiter Polling."],
    ["Was ist SSE?", "Eine HTTP-Antwort, die offen bleibt und in die der Server Ereignisse schreibt."],
    ["Warum nicht SignalR?", "Nur eine Richtung nötig, SSE ist eingebaut, einfacher, Token bleibt im Header."],
    ["Warum stehen keine Werte im Ereignis?", "Nur ein Weg zu den Daten, und eine verlorene Meldung schadet nicht."],
    ["Was passiert bei Abbruch?", "Neu verbinden mit wachsendem Abstand, danach einmal alles laden. Jede Minute lädt die Seite sowieso."],
    ["Wie ist der Kanal geschützt?", "Nur mit JWT, und er endet beim Ablauf des Tokens."],
    ["Unterschied GET und POST?", "GET liest und ist gefahrlos wiederholbar, POST löst etwas aus."],
    ["Warum geht das Frontend nicht direkt zu Tuya?", "Das Secret muss geheim bleiben, und unsere API prüft die Rechte."],
], [0.38, 0.62])

d.h1("10  Merksätze")
d.merksatz("• Den ganzen Weg betrachten: Ein schneller letzter Abschnitt macht die Daten nicht frischer.<br/>"
           "• Nur behaupten, was getestet ist. „Live“ meint den Weg vom Server zum Browser.<br/>"
           "• Ereignis sagt: „Es gibt Neues“. Die Daten kommen über den normalen Weg.<br/>"
           "• Immer eine Rückfallebene: Polling bleibt.<br/>"
           "• Lange Verbindungen brauchen ein Ende: Token-Ablauf, Abmelden, Herunterfahren.")

d.save("docs/Lernunterlage-Echtzeit-und-API.pdf")
