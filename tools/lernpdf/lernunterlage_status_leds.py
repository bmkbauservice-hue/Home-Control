"""Erzeugt docs/Lernunterlage-Status-LEDs.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_status_leds.py)."""
from lernpdf import Doc

d = Doc(
    "Status-LEDs",
    "Lernunterlage: Online, offline, unbekannt – vom Tuya-Feld bis zur leuchtenden LED",
    footer="Home Control · Status-LEDs · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Was man sieht")
d.bullets([
    "<b>Status-Leiste oben im Dashboard:</b> eine große LED mit einem Satz („Alles in Ordnung“, "
    "„1 Gerät ist offline“, „Server nicht erreichbar“), daneben LEDs für Server, Tuya-Cloud und Blink und "
    "die Zahlen online / offline / ohne Status. Ein Knopf „Aktualisieren“ fragt sofort neu.",
    "<b>Raumkarten:</b> vor jedem Gerätenamen eine kleine LED.",
    "<b>Geräte-Kacheln:</b> oben rechts eine LED mit Text „online“ oder „offline“.",
])
d.table([
    ["Farbe", "Bedeutung"],
    ["grün (pulsiert sanft)", "online: Tuya meldet das Gerät als verbunden bzw. Dienst erreichbar"],
    ["rot", "offline: Tuya meldet das Gerät als nicht verbunden bzw. Dienst nicht erreichbar"],
    ["grau", "unbekannt: nicht mit Tuya verknüpft, Tuya meldet nichts, Tuya nicht eingerichtet/erreichbar"],
    ["gelb (pulsiert)", "Achtung: z. B. Geräte offline, Tuya-Cloud weg oder Blink braucht eine neue Anmeldung"],
], [0.25, 0.75])

# ------------------------------------------------------------------ 2
d.h1("2  Woher kommt „online“?")
d.p("Die Tuya-Geräteliste (<font name='Mono'>GET /v1.0/iot-01/associated-users/devices</font>) enthält pro "
    "Gerät ein Feld <font name='Mono'>online</font>. Unsere Geräte kennen ihr Tuya-Gerät über "
    "<font name='Mono'>TuyaDeviceId</font>. Der neue Endpunkt <font name='Mono'>GET /api/status</font> verbindet "
    "beides.")
d.code("""Datenbank (Devices)         Tuya-Cloud (Geräteliste)        Ergebnis
Lampe   → tuya-abc   ───▶   tuya-abc  online: true    ───▶  online   (grün)
Stecker → tuya-def   ───▶   tuya-def  online: false   ───▶  offline  (rot)
Sensor  → (keins)                                     ───▶  unknown  (grau)""")
d.p("Die Entscheidung trifft <font name='Mono'>DeviceOnlineEvaluator.Evaluate</font>, eine <b>reine Funktion</b>: "
    "Sie bekommt Daten und gibt Daten zurück, ohne Datenbank und ohne HTTP. Deshalb lässt sie sich mit "
    "wenigen Zeilen testen.")

# ------------------------------------------------------------------ 3
d.h1("3  Zwischenspeicher (Cache) im Backend")
d.p("Die Oberfläche fragt jede Minute nach, auf jedem Gerät, das die App offen hat. Ohne Cache wäre jede Frage "
    "ein Aufruf bei der Tuya-Cloud, und das Cloud-Projekt hat ein begrenztes Kontingent.")
d.code("""public async Task<IReadOnlyDictionary<string, bool?>> GetAsync(Func<…> load, CancellationToken ct)
{
    await gate.WaitAsync(ct);                     // nur einer gleichzeitig
    try
    {
        if (cached is not null && now - cachedAtUtc < MaxAge)   // jünger als 60 s?
            return cached;                         // → gespeicherte Antwort
        var devices = await load(ct);              // sonst Tuya fragen
        cached = …; cachedAtUtc = now;             // und merken
        return cached;
    }
    finally { gate.Release(); }
}""")
d.bullets([
    "<b>Singleton:</b> Der Cache muss zwischen den Anfragen erhalten bleiben. Den TuyaClient (kurzlebig) "
    "bekommt er deshalb als Funktion beim Aufruf, nicht im Konstruktor.",
    "<b>SemaphoreSlim:</b> Kommen zwei Anfragen gleichzeitig, fragt nur die erste bei Tuya, die zweite "
    "bekommt danach das gespeicherte Ergebnis.",
    "<b>Fehler werden nicht gespeichert:</b> Ist Tuya kurz weg, versucht es der nächste Aufruf erneut.",
    "<b>TimeProvider:</b> Im Test wird die Uhr vorgestellt (59 s: kein neuer Aufruf, 60 s: neuer Aufruf).",
])
d.merksatz("<b>Immer 200:</b> /api/status meldet „Tuya nicht erreichbar“ als Inhalt (tuya: offline), nicht als "
           "Fehlercode. Für die Oberfläche ist das eine rote LED, kein kaputter Endpunkt.")

# ------------------------------------------------------------------ 4
d.h1("4  Frontend: ein Provider, viele LEDs")
d.table([
    ["Datei", "Aufgabe"],
    ["api/status.ts", "Typen (LedState, OnlineState, SystemStatus) und getStatus(), getBlinkStatus()"],
    ["status/StatusProvider.tsx", "Fragt alle 60 s und beim Zurückkehren in die App (visibilitychange); Context für alle"],
    ["status/statusContext.ts", "useStatus(): status, blink, serverUnreachable, refresh(), stateOf(id)"],
    ["components/StatusLed.tsx", "Die LED selbst: farbiger Punkt, optional mit Text"],
    ["components/StatusBar.tsx", "Leiste oben im Dashboard mit Gesamtzustand"],
], [0.33, 0.67])
d.bullets([
    "<b>Context statt Prop-Drilling:</b> Status-Leiste, Raumkarten und Kacheln brauchen dieselben Daten. Ein "
    "Provider fragt einmal, alle lesen mit <font name='Mono'>useStatus()</font>. Dasselbe Muster wie "
    "<font name='Mono'>useAuth()</font>.",
    "<b>Aufräumen im useEffect:</b> clearInterval und removeEventListener in der Cleanup-Funktion. Sonst liefen "
    "nach dem Abmelden Abfragen weiter.",
    "<b>useMemo:</b> Die Nachschlagetabelle Geräte-Id → Zustand wird nur bei einer neuen Antwort gebaut.",
    "<b>Blink seltener:</b> nur beim Start und auf Knopfdruck, nicht jede Minute (keine offizielle API, Sperrrisiko).",
])

d.h2("Lint-Warnung: setState direkt im Effekt")
d.p("oxlint meldete <font name='Mono'>react(set-state-in-effect)</font>. Der Effekt rief "
    "<font name='Mono'>loadStatus()</font> auf, und die Funktion setzte State. Der erste Versuch, nur "
    "<font name='Mono'>setRefreshing(true)</font> zu entfernen, reichte nicht: Bei einer "
    "<font name='Mono'>async</font>-Funktion erkennt der Linter nicht sicher, dass das setState erst nach dem "
    "<font name='Mono'>await</font> passiert. Erst das Muster aus der React-Doku hat geholfen: State nur in "
    "<font name='Mono'>.then()</font> bzw. <font name='Mono'>.catch()</font> setzen.")
d.code("""// vorher (Warnung): async-Funktion mit setState
const loadStatus = useCallback(async () => {
  setRefreshing(true)                       // synchron → sofort neuer Render
  try { setStatus(await getStatus()) } ...
}, [])

// nachher (0 Warnungen): State erst, wenn die Antwort da ist
const loadStatus = useCallback(() =>
  getStatus()
    .then((next) => { setStatus(next); setServerUnreachable(false) })
    .catch(() => setServerUnreachable(true))
    .finally(() => setRefreshing(false)), [])

const refresh = useCallback(() => {
  setRefreshing(true)                       // Klick = Ereignis, hier ist setState richtig
  loadStatus(); loadBlink()
}, [loadStatus, loadBlink])""")
d.merksatz("<b>Merksatz:</b> Effekte synchronisieren mit der Außenwelt (Server, Timer). State aus einer "
           "Benutzeraktion gehört in den Event-Handler, State aus einer Server-Antwort in den Promise-Callback. "
           "Und: Erst prüfen (Linter erneut laufen lassen), dann „behoben“ sagen.")

# ------------------------------------------------------------------ 5
d.h1("5  CSS: die LED leuchtet")
d.code(""".led { --led: var(--offline); width: 10px; height: 10px; border-radius: 50%;
        background: var(--led);
        box-shadow: 0 0 0 3px color-mix(in srgb, var(--led) 18%, transparent),
                    0 0 10px color-mix(in srgb, var(--led) 70%, transparent); }
.led-online  { --led: var(--online); }
.led-offline { --led: var(--danger); }
@media (prefers-reduced-motion: no-preference) {
  .led-online { animation: led-pulse 2.6s ease-in-out infinite; }
}""")
d.bullets([
    "<b>Eine Variable pro LED</b> (<font name='Mono'>--led</font>): Jede Farbvariante setzt nur die Farbe, "
    "Größe und Leuchten bleiben gleich.",
    "<b>color-mix()</b> erzeugt den halbdurchsichtigen Lichthof aus derselben Farbe.",
    "<b>Barrierefreiheit:</b> Farbe allein reicht nicht (Rot-Grün-Schwäche). Jede LED hat Text (sichtbar oder "
    "als aria-label), und das Pulsieren entfällt bei „Bewegung reduzieren“.",
])

# ------------------------------------------------------------------ 6
d.h1("6  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Woher weiß die App, ob ein Gerät online ist?", "Aus dem Feld online der Tuya-Geräteliste, verbunden über TuyaDeviceId."],
    ["Belastet das die Tuya-Cloud?", "Höchstens ein Aufruf pro Minute dank 60-Sekunden-Cache im Backend."],
    ["Warum grau statt rot bei fehlenden Daten?", "Rot hieße „sicher offline“. Ohne Daten wissen wir es nicht, also grau."],
    ["Warum ein Context?", "Mehrere Komponenten brauchen dieselben Daten. Ein Provider fragt einmal, alle lesen mit."],
    ["Kann der KI-Agent das nutzen?", "Ja, GET /api/status mit dem API-Schlüssel: „Welche Geräte sind offline?“"],
], [0.4, 0.6])

d.h1("7  Merksätze")
d.merksatz("• Reine Funktion = leicht testbar (Daten rein, Daten raus).<br/>"
           "• Cache vor teuren oder begrenzten Aufrufen, Fehler nicht cachen.<br/>"
           "• Gemeinsame Daten → Context + Provider, Abfragen im useEffect mit Aufräumen.<br/>"
           "• Unbekannt ist nicht offline: grau statt rot.<br/>"
           "• Farbe nie allein: immer auch Text, Bewegung nur wenn erlaubt.")

d.save("docs/Lernunterlage-Status-LEDs.pdf")
