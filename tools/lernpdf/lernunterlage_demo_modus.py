"""Erzeugt docs/Lernunterlage-Demo-Modus.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_demo_modus.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Demo-Modus",
    "Lernunterlage: Fakes statt echter Clients, Dependency Injection je nach Einstellung, keine echten Daten auf dem Beamer",
    footer="Home Control · Demo-Modus · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("Für die Präsentation gibt es eine <b>zweite Instanz</b> von Home Control: " + M + "./demo-starten.sh" + E +
    ", Adresse " + M + "http://localhost:5300" + E + ", Konto " + M + "demo@example.de" + E + ". Sie zeigt ein "
    "erfundenes Haus mit 5 Räumen und 12 Geräten, Ereignisse der letzten 7 Tage, schwankende Watt, eine Tür, die "
    "ab und zu aufgeht, ein Gerät offline, einen Akku bei 15 % und zwei Kameras mit selbst gezeichneten Bildern.")
d.h2("Warum keine echten Daten auf dem Beamer?")
d.table([
    ["Grund", "Erklärung"],
    ["Datenschutz", "Kamerabilder zeigen die eigene Wohnung, Gerätenamen und Zeiten verraten Gewohnheiten "
                    "(wann jemand zu Hause ist)."],
    ["Sicherheit", "Keine echten Zugangsdaten im Spiel: Die Demo lädt keine User Secrets, ein Fehler kann "
                   "nichts verraten."],
    ["Vorführeffekt", "Schul-WLAN, Tuya-Störung oder Blink-Sperre lassen die echte App leer aussehen. Die Demo "
                      "braucht kein Internet."],
    ["Akku und Kontingent", "„Neues Bild“ kostet bei echten Kameras Akku, jeder Tuya-Abruf Kontingent. Im Demo kostet "
                            "nichts etwas."],
    ["Wiederholbar", "Jeder Start sieht gleich aus. Das Drehbuch der Vorführung passt immer."],
], [0.25, 0.75])

# ------------------------------------------------------------------ 2
d.h1("2  Fakes statt echter Clients")
d.p("Ein <b>Fake</b> ist eine einfache, funktionierende Ersatz-Implementierung, die sich nach außen genauso "
    "verhält wie das Original. Die Tests benutzen solche Fakes schon lange. Der Demo-Modus benutzt dieselbe Idee "
    "im laufenden Programm.")
d.table([
    ["", "Echter Betrieb", "Demo-Modus", "Ersetzt auf Ebene"],
    ["Tuya", "TuyaClient → Internet", "TuyaClient → DemoTuyaHandler", "HTTP (HttpMessageHandler)"],
    ["Blink", "BlinkHttpCameraClient", "DemoBlinkCameraClient", "Interface IBlinkCameraClient"],
    ["Daten", "homecontrol.db", "homecontrol-demo.db", "eigene Datei"],
], [0.12, 0.29, 0.31, 0.28])
d.bullets([
    "<b>Tuya auf der HTTP-Ebene:</b> Der echte " + M + "TuyaClient" + E + " signiert, holt ein Token und fragt "
    "den Status ab, nur antwortet statt der Cloud ein " + M + "HttpMessageHandler" + E + " mit erfundenen Werten im "
    "Tuya-Format. So laufen Abgleich, Messwerte, Skalierung (1234 → 123,4 W) und Online-LEDs als echter Code.",
    "<b>Blink auf der Interface-Ebene:</b> Alles hinter " + M + "IBlinkCameraClient" + E + " wird ersetzt. Endpunkte, "
    "Rate Limit und Oberfläche sehen keinen Unterschied.",
    "<b>Mock vs. Fake:</b> Ein Mock prüft im Test, ob er richtig aufgerufen wurde. Ein Fake <i>funktioniert</i> "
    "einfach, deshalb taugt er auch für eine Vorführung.",
])
d.code("""// Fake-Cloud: beantwortet die Anfragen des echten TuyaClient selbst
public class DemoTuyaHandler(TimeProvider time) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, ...)
    {
        if (path == "/v1.0/token") return Ok(new { access_token = "demo-token", expire_time = 7200 });
        // ... Geräteliste mit online true/false, Status mit switch_1, cur_power, ...
    }
}""")

# ------------------------------------------------------------------ 3
d.h1("3  Dependency Injection je nach Einstellung")
d.p("Program.cs entscheidet <b>einmal beim Start</b>, welche Klasse hinter einem Interface steckt. Wer den Dienst "
    "benutzt (Endpunkt, Abgleich), bekommt ihn per Konstruktor und fragt nie nach, ob es ein Fake ist.")
d.code("""var demoMode = DemoMode.IsEnabled(builder.Configuration);   // HomeControl:DemoMode
if (demoMode)
{
    tuyaHttpClient.ConfigurePrimaryHttpMessageHandler(sp => new DemoTuyaHandler(...));
    services.AddSingleton<IBlinkCameraClient, DemoBlinkCameraClient>();
}
else
{
    services.AddSingleton<IBlinkCameraClient, BlinkHttpCameraClient>();
}""")
d.bullets([
    "<b>Ein Schalter, eine Stelle:</b> Im restlichen Code gibt es kein " + M + "if (demo)" + E + ". Das hält ihn sauber "
    "und verhindert, dass die Demo etwas anderes vorführt als die echte App.",
    "<b>ConfigurePrimaryHttpMessageHandler:</b> Der „letzte“ Handler eines HttpClient ist normalerweise der, der ins "
    "Netzwerk geht. Hier ist es die Fake-Cloud.",
    "<b>Gleiche Lebensdauer:</b> Fake und echter Blink-Client sind beide Singletons. So bleibt z. B. das Bild nach "
    "„Neues Bild“ erhalten.",
])

# ------------------------------------------------------------------ 4
d.h1("4  Sicherheitsnetz: nie versehentlich an")
d.p("Die gefährlichste Panne wäre: Der Dienst läuft mit Fake-Daten, oder die Demo mit echten Zugangsdaten. "
    + M + "DemoMode.EnsureConsistent" + E + " prüft deshalb ganz am Anfang von Program.cs. Passt etwas nicht, "
    "startet die App gar nicht (<b>fail fast</b>):")
d.numbered([
    "DemoMode nur in der Umgebung " + M + "Demo" + E + ". Production oder Development mit DemoMode: Abbruch.",
    "Umgebung Demo nur mit DemoMode.",
    "Im Demo keine User Secrets (dort liegen Tuya-, Blink- und JWT-Schlüssel).",
    "Im Demo muss die Datenbank „demo“ im Namen haben. Die echte homecontrol.db bleibt unberührt.",
    "Demo-Passwort muss gesetzt sein.",
])
d.bullets([
    "<b>Umgebung Demo:</b> ASP.NET Core lädt dann automatisch " + M + "appsettings.Demo.json" + E + ". Der Dienst "
    "(Production) lädt diese Datei nie.",
    "<b>.invalid:</b> Die Tuya-Adresse in der Demo endet auf " + M + ".invalid" + E + ", eine Endung, die es nie geben "
    "wird. Selbst ohne Fake ginge keine Anfrage an Tuya.",
    "<b>Wegwerf-Schlüssel:</b> Das Skript erzeugt bei jedem Start einen zufälligen JWT-Schlüssel.",
])
d.merksatz("Gefährliche Einstellungen nicht nur dokumentieren, sondern beim Start prüfen: Lieber startet die "
           "App gar nicht als mit der falschen Konfiguration.", warn=True)

# ------------------------------------------------------------------ 5
d.h1("5  Werte, die schwanken und trotzdem testbar sind")
d.p("Das Verhalten der Geräte steht in " + M + "DemoBehavior.StateAt(gerät, uhrzeit)" + E + ", einer <b>reinen "
    "Funktion</b>: gleiche Uhrzeit, gleiches Ergebnis. Die Schwankung kommt nicht aus " + M + "Random" + E + ", "
    "sondern aus einer Prüfsumme über Gerät und Minute.")
d.table([
    ["Gerät", "Regel"],
    ["Haustür", "alle 20 Minuten für 5 Minuten offen; der Abgleich fragt alle 3 Minuten, erwischt also jedes Öffnen"],
    ["Kaffeemaschine", "alle 30 Minuten 4 Minuten an, dann ca. 1,2 kW"],
    ["Kühlschrank", "immer an, Kompressor 8 von 20 Minuten (ca. 92 W, sonst 1,5 W)"],
    ["Computer", "tagsüber an, 75–145 W"],
    ["Leselampe", "abends an, tagsüber ab und zu, damit in der Vorführung etwas passiert"],
    ["Drucker", "offline (ausgesteckt): rote LED, keine alten Messwerte"],
], [0.22, 0.78])
d.p("Warum keine echte Zufallszahl? " + M + "string.GetHashCode()" + E + " und " + M + "Random" + E + " liefern bei "
    "jedem Start etwas anderes. Ein Test könnte dann nur „irgendwas“ prüfen. Mit der festen Prüfsumme (FNV-1a) "
    "prüft der Test genau: In einer Stunde ist die Tür 15 Minuten offen.")

# ------------------------------------------------------------------ 6
d.h1("6  Beispielhaus und Verlauf")
d.bullets([
    M + "DemoHouse" + E + " beschreibt Räume und Geräte <b>einmal</b>. Seeder und Fake-Cloud lesen beide von dort, "
    "Namen und erfundene Tuya-Ids passen deshalb immer zusammen.",
    M + "DemoDataSeeder" + E + " legt Ereignisse der letzten 7 Tage nach einem Tagesablauf an (Licht morgens und "
    "abends, Tür 5–9 Mal, Temperatur viermal am Tag). " + M + "new Random(2026)" + E + ": fester Startwert, "
    "also bei jedem Anlegen dieselben Ereignisse.",
    "Das Skript löscht die Demo-Datenbank bei jedem Start. So reichen die „letzten 7 Tage“ immer bis heute.",
    "Das Demo-Konto wird bei jedem Start auf das Passwort aus dem Skript gesetzt, eine Sperre wird aufgehoben.",
])
d.p("<b>Gefundener Fehler:</b> Im ersten Versuch fehlte der Kühlschrank. EF Core speichert nur Objekte, die man "
    "hinzufügt <i>oder</i> die über eine Verknüpfung erreichbar sind. Die Geräte hingen nur an den Ereignissen, "
    "der Kühlschrank hat aber keine (er ist immer an). Lösung: Räume und Geräte ausdrücklich mit "
    + M + "AddRange" + E + " hinzufügen. Ein Test zählt jetzt 12 Geräte.")

# ------------------------------------------------------------------ 7
d.h1("7  Läuft neben dem Dienst")
d.table([
    ["", "Dienst", "Entwicklung", "Demo"],
    ["Start", "dienst-einrichten.sh", "app-starten.sh", "demo-starten.sh"],
    ["Port", "5200", "5111", "5300"],
    ["Umgebung", "Production", "Development", "Demo"],
    ["Datenbank", "homecontrol.db", "homecontrol.db", "homecontrol-demo.db"],
    ["Geheimnisse", "User Secrets", "User Secrets", "keine"],
], [0.19, 0.27, 0.27, 0.27])
d.p("Eigener Ordner, eigene Datenbank, eigener Port: Die Regel „ein Prozess pro Datenbank“ bleibt gewahrt. Die "
    "Demo spricht nie mit Blink, das echte Blink-Token ist also nie in Gefahr.")

# ------------------------------------------------------------------ 8
d.h1("8  Tests")
d.table([
    ["Test", "prüft"],
    ["DemoMode_im_Dienst_oder_beim_Entwickeln_verhindert_den_Start", "Production/Development mit DemoMode: kein Start"],
    ["Demo_mit_echter_Datenbank_oder_User_Secrets_…", "homecontrol.db oder User Secrets: kein Start"],
    ["Dienst_und_Entwicklung_schalten_DemoMode_nirgends_ein", "appsettings.json, …Development.json, dienst-einrichten.sh"],
    ["Demo_startet_mit_5_Raeumen_12_Geraeten_und_7_Tagen_…", "Beispielhaus und Verlauf"],
    ["Abgleich_liest_die_Fake_Cloud_…", "Watt, Akku 15 %, Drucker offline, Status-LEDs"],
    ["Tuer_geht_ab_und_zu_auf_…", "3-Minuten-Takt erwischt jedes Öffnen"],
    ["Neues_Bild_im_Demo_…", "anderes Bild, Aufnahmezeit jetzt"],
], [0.58, 0.42])

# ------------------------------------------------------------------ 9
d.h1("9  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Ist das echt?", "Nein, Beispieldaten. Das Banner sagt es. Der Code ist derselbe wie im echten Betrieb."],
    ["Was ist ein Fake?", "Eine einfache, funktionierende Ersatz-Implementierung mit derselben Schnittstelle."],
    ["Warum nicht einfach die echte App?", "Datenschutz (Wohnung, Gewohnheiten), kein Internet nötig, kein Akku, wiederholbar."],
    ["Wie wird umgeschaltet?", "Dependency Injection: Program.cs registriert je nach HomeControl:DemoMode echt oder Fake."],
    ["Kann der Dienst aus Versehen Demo sein?", "Nein: DemoMode nur in der Umgebung Demo, sonst startet die App nicht. Getestet."],
    ["Warum Tuya auf HTTP-Ebene?", "Damit Abgleich, Messwerte und LEDs echter Code bleiben, nur die Cloud ist erfunden."],
    ["Woher kommen die Bilder?", "Selbst mit Pillow gezeichnet, Aufschrift DEMO-BILD, keine Fotos aus dem Netz."],
    ["Warum schwanken die Werte nicht zufällig?", "Feste Prüfsumme statt Random: sieht lebendig aus und bleibt testbar."],
], [0.38, 0.62])

d.h1("10  Merksätze")
d.merksatz("• Auf den Beamer gehören Beispieldaten, nie echte Kamerabilder, Namen oder Zugangsdaten.<br/>"
           "• Fake = gleiche Schnittstelle, einfache Implementierung. Umschalten nur an EINER Stelle (DI).<br/>"
           "• Gefährliche Schalter beim Start prüfen (fail fast), nicht nur dokumentieren.<br/>"
           "• Eigene Datenbank, eigener Port, keine Secrets: Demo und Dienst stören sich nicht.<br/>"
           "• Reine Funktionen statt Zufall: lebendige Werte, die sich trotzdem testen lassen.")

d.save("docs/Lernunterlage-Demo-Modus.pdf")
