"""Erzeugt docs/Lernunterlage-Hintergrund-Abgleich.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_hintergrund_abgleich.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Hintergrund-Abgleich",
    "Lernunterlage: BackgroundService, PeriodicTimer und ein Kontingent, das reichen muss",
    footer="Home Control · Hintergrund-Abgleich · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("Bis zum 02.11.2026 hat Home Control Tuya nur gefragt, wenn jemand auf <b>„Mit Tuya abgleichen“</b> "
    "geklickt hat. An diesem Tag wurde die Tür geöffnet, Tuya meldete " + M + "doorcontact_state = true" + E +
    ", die App zeigte aber weiter <b>ZU</b>. Das Ereignis " + M + "door_opened" + E + " fehlte im Verlauf.")
d.p("Lösung: Das Backend fragt selbst regelmäßig nach, auch wenn niemand die App offen hat. "
    "Das nennt man <b>Polling</b> (regelmäßig nachfragen).")

# ------------------------------------------------------------------ 2
d.h1("2  Zuerst rechnen: das Kontingent")
d.p("Das Tuya-Cloud-Projekt hat ein Kontingent an API-Aufrufen pro Monat. Die Produktseite von Tuya IoT Core "
    "nennt für die Testversion <b>26 000 Aufrufe/Monat</b>. Nachsehen: platform.tuya.com → Cloud → My Services → "
    "IoT Core. Bevor ein Takt festgelegt wurde, wurde deshalb gerechnet.")
d.p("Ein Durchlauf kostet <b>1 Status-Aufruf pro Gerät</b> (hier 4) plus <b>1 Aufruf der Geräteliste</b> "
    "(online/offline) = 5 Aufrufe. Dazu etwa alle 2 Stunden ein neues Token.")
d.table([
    ["Takt", "Durchläufe/Tag", "Aufrufe/Tag", "Aufrufe/Monat", "bei 26 000"],
    ["1 min", "1 440", "7 200", "216 000", "8-fach zu viel"],
    ["2 min", "720", "3 600", "108 000", "4-fach zu viel"],
    ["5 min", "288", "1 440", "43 200", "zu viel"],
    ["10 min", "144", "720", "21 600", "kaum Puffer"],
    ["15 min (erste Version)", "96", "480", "14 400", "~11 000 Puffer"],
], [0.22, 0.2, 0.18, 0.2, 0.2])
d.code("""Rechenweg für 15 min:
  Durchläufe pro Tag = 24 h × 60 min ÷ 15 min = 96
  Aufrufe pro Tag    = 96 × (4 Geräte + 1 Liste) = 480
  Aufrufe pro Monat  = 480 × 30 = 14 400""")
d.h2("Nachtrag 02.11. abends: Takt pro Gerät")
d.p("Die Diagnose zeigte: Wichtig ist vor allem die <b>Tür</b>. Deshalb hat jedes Gerät jetzt einen eigenen Takt "
    "(" + M + "Device.TuyaSyncSeconds" + E + ", auf der Geräteseite unter „Abgleich“). Die Tür wird alle "
    "<b>3 Minuten</b> gelesen, alle anderen im Standard-Takt von <b>30 Minuten</b>. Der Dienst wacht jede Minute auf "
    "und fragt nur die fälligen Geräte; die Online-Liste nur alle 30 Minuten.")
d.table([
    ["Lauf", "Läufe/Tag", "Aufrufe je Lauf", "Aufrufe/Monat"],
    ["alle 30 min: 4 Geräte + Online-Liste", "48", "5", "7 200"],
    ["dazwischen alle 3 min: nur Tür", "432", "1", "12 960"],
    ["Token", "", "", "~360"],
    ["Summe", "", "", "~20 500"],
], [0.44, 0.16, 0.2, 0.2])
d.code("""Rechenweg:
  Tür-Läufe pro Tag   = 24 × 60 ÷ 3 = 480, davon 48 zusammen mit den anderen
  nur Tür             = 480 − 48 = 432 Läufe × 1 Aufruf  = 432
  alle 30 min         = 48 Läufe × (4 Geräte + 1 Liste)  = 240
  pro Tag             = 672, pro Monat = 672 × 30 = 20 160 (+ ~360 Token)""")

d.h2("Die versteckten Kosten: Status-LEDs")
d.p("Die Status-LEDs fragen jede Minute " + M + "GET /api/status" + E + ", und der fragte bisher (mit 60-s-Cache) "
    "die Tuya-Geräteliste ab. Ist die App irgendwo dauerhaft offen (Tablet an der Wand), sind das "
    "<b>1 440 Aufrufe/Tag = 43 200/Monat</b>, nur für die LEDs. Lösung: Der Hintergrund-Abgleich füllt den "
    "Speicher (" + M + "TuyaOnlineCache" + E + "), und " + M + "/api/status" + E + " liest ihn nur noch.")
d.table([
    ["Variante (App 24/7 offen)", "LEDs", "Abgleich", "Summe/Monat"],
    ["Nichts geändert", "43 200", "11 520", "~55 000"],
    ["Status-Cache 5 min", "8 640", "11 520–14 400", "~20 500–23 400"],
    ["Abgleich füllt Cache (gewählt)", "0", "14 400", "~14 800"],
], [0.4, 0.15, 0.2, 0.25])
d.merksatz("Erst rechnen, dann programmieren: Ein Takt, der in der Entwicklung harmlos wirkt, kann im Dauerbetrieb "
           "ein Monatskontingent in wenigen Tagen aufbrauchen. Und an ALLE Aufrufer denken, nicht nur an den neuen.",
           warn=True)

# ------------------------------------------------------------------ 3
d.h1("3  BackgroundService und PeriodicTimer")
d.p("Ein <b>BackgroundService</b> ist Code, der neben den HTTP-Anfragen dauerhaft im Backend läuft. "
    "Er startet mit der App und wird beim Beenden gestoppt. Registriert wird er mit " +
    M + "AddHostedService" + E + ".")
d.code("""builder.Services.AddHostedService<TuyaSyncBackgroundService>();

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var interval = options.CurrentValue.GetAutoSyncInterval();   // null = aus
    if (interval is null) return;

    using var timer = new PeriodicTimer(interval.Value, timeProvider);
    do
    {
        failures = await RunOnceAsync(stoppingToken) ? 0 : failures + 1;
        timer.Period = GetDelay(interval.Value, failures);       // Backoff
    }
    while (await timer.WaitForNextTickAsync(stoppingToken));
}""")
d.bullets([
    "<b>PeriodicTimer:</b> „Warte bis zum nächsten Takt“ als " + M + "await" + E + ", ohne Callback. "
    "Läuft ein Durchlauf länger als der Takt, gibt es keine Überschneidung.",
    "<b>do … while:</b> Der erste Durchlauf kommt sofort beim Start, damit Änderungen während eines Neustarts "
    "gleich übernommen werden.",
    "<b>stoppingToken:</b> Wird beim Beenden der App ausgelöst. " + M + "WaitForNextTickAsync" + E + " endet dann, "
    "der Dienst hört sauber auf.",
    "<b>TimeProvider:</b> Die Uhr kommt von außen. Im Test stellt " + M + "FakeTimeProvider.Advance" + E +
    " die Zeit vor, statt 15 Minuten zu warten.",
])

# ------------------------------------------------------------------ 4
d.h1("4  Scopes im Singleton")
d.p("Ein BackgroundService ist ein <b>Singleton</b>: Es gibt ihn einmal, solange die App läuft. "
    + M + "TuyaDeviceSyncer" + E + " und " + M + "ApplicationDbContext" + E + " sind dagegen <b>scoped</b>: "
    "Sie leben eine HTTP-Anfrage lang. Im Hintergrund gibt es keine Anfrage, also baut der Dienst sich pro "
    "Durchlauf selbst einen Scope:")
d.code("""using var scope = scopeFactory.CreateScope();
var syncer = scope.ServiceProvider.GetRequiredService<TuyaDeviceSyncer>();
var results = await syncer.ReadAllAsync(apply: true, cancellationToken);
// Ende von using: DbContext wird freigegeben""")
d.table([
    ["Lebensdauer", "Beispiel", "gilt"],
    ["Singleton", "TuyaSyncBackgroundService, TuyaOnlineCache, TuyaTokenStore", "solange die App läuft"],
    ["Scoped", "ApplicationDbContext, TuyaDeviceSyncer", "eine Anfrage bzw. ein Scope"],
    ["Transient", "TuyaClient (typisierter HttpClient)", "bei jedem Abruf neu"],
], [0.18, 0.52, 0.3])
d.merksatz("Einen scoped Dienst nie direkt in einen Singleton geben (Captive Dependency): Der DbContext lebte dann "
           "ewig, sammelte alle Objekte an und wäre nicht threadsicher. Stattdessen IServiceScopeFactory "
           "und pro Durchlauf ein eigener Scope.")
d.p("Knopf und Hintergrund rufen dieselbe Methode auf. Die vorhandene Sperre (" + M + "SemaphoreSlim SyncLock" + E +
    ") lässt nur einen Abgleich gleichzeitig zu, sonst könnte dasselbe Ereignis doppelt gespeichert werden.")

# ------------------------------------------------------------------ 5
d.h1("5  Fehler und Backoff")
d.p("Wirft ein BackgroundService eine Exception, beendet .NET standardmäßig die <b>ganze App</b>. "
    "Deshalb fängt " + M + "RunOnceAsync" + E + " jeden Fehler ab, schreibt Typ und Meldung ins Log (nie "
    "Zugangsdaten) und versucht es beim nächsten Takt erneut. Nur das Beenden der App wird durchgereicht.")
d.p("<b>Backoff:</b> Ist die Tuya-Cloud gestört, wäre es sinnlos und unhöflich, sie im selben Takt weiter zu "
    "fragen. Nach jedem Fehler in Folge verdoppelt sich der Abstand, höchstens bis 1 Stunde:")
d.code("""Fehler in Folge:   0      1      2      3      4 …
Prüfabstand:       1 min  2 min  4 min  8 min  16 min … (Obergrenze 1 h)
Erster Erfolg  → wieder jede Minute prüfen""")
d.p("Das nennt man <b>exponentiellen Backoff</b> (Abstand × 2 pro Fehler). Ohne Zugangsdaten gilt das nicht als "
    "Fehler: Tuya ist dann einfach nicht eingerichtet, und es wird gar nicht gefragt.")

# ------------------------------------------------------------------ 6
d.h1("6  Warum Polling und nicht Push?")
d.table([
    ["", "Polling (gewählt)", "Push (Tuya Message Service)"],
    ["Wie", "Backend fragt nach (Tür alle 3, Rest alle 30 min)", "Tuya schickt jede Änderung sofort über eine dauerhafte Verbindung"],
    ["Zeitpunkt", "bis zu 3 bzw. 30 min zu spät", "genau"],
    ["Kurze Änderungen", "Tür kurz auf und zu: nicht gesehen", "jede Änderung kommt an"],
    ["Aufwand", "ein BackgroundService, vorhandener Code", "eigene Bibliothek, dauerhafte Verbindung, "
     "Entschlüsselung, Wiederverbinden"],
    ["Kontingent", "API-Aufrufe (hier 14 400/Monat)", "eigenes Nachrichten-Kontingent"],
    ["Erklärbar", "in zwei Sätzen", "eigenes Thema"],
], [0.18, 0.37, 0.45])
d.p("Für ein Umschulungsprojekt mit 4 Geräten ist Polling der richtige erste Schritt: wenig Code, gut testbar, "
    "gut erklärbar. Push wäre der Weg, wenn genaue Zeitpunkte wichtig werden (z. B. Alarm bei offener Tür).")

# ------------------------------------------------------------------ 7
d.h1("7  Was die Oberfläche macht")
d.p("Übersicht und Geräteseite laden die Geräteliste jede Minute und beim Zurückkehren in die App neu "
    "(Hook " + M + "useAutoRefresh" + E + ", Muster wie die Status-LEDs). Das kostet <b>kein</b> "
    "Tuya-Kontingent, denn gelesen wird nur die eigene Datenbank.")
d.code("""const timer = window.setInterval(() => latest.current(), 60_000)
document.addEventListener('visibilitychange', onVisible)   // iPhone entsperrt → sofort""")

# ------------------------------------------------------------------ 8
d.h1("8  Grenzen")
d.bullets([
    "<b>Bis zu 3 (Tür) bzw. 30 Minuten Verzögerung.</b> Das Ereignis bekommt den Zeitpunkt des Abrufs, nicht den "
    "der echten Änderung.",
    "<b>Kurze Änderungen gehen verloren.</b> Tür auf und nach 2 Minuten wieder zu: Beide Abrufe sehen „ZU“.",
    "<b>Online/Offline bis zu 30 Minuten alt</b>, weil die LEDs die Online-Liste des letzten 30-Minuten-Laufs lesen.",
    "<b>Jedes Gerät kostet.</b> Ein weiteres Gerät im 30-Minuten-Takt +1 440/Monat, im 3-Minuten-Takt +14 400.",
    "<b>Takt ändern erfordert einen Neustart</b> des Dienstes.",
    "<b>Eine Backend-Instanz.</b> Bei mehreren Instanzen würde jede selbst abgleichen (doppeltes Kontingent). "
    "Deshalb ist der Hintergrund-Abgleich beim Entwickeln aus (" + M + "Tuya:AutoSyncSeconds = 0" + E +
    " in appsettings.Development.json): Dienst und app-starten.sh teilen sich dieselbe Datenbank. Einschalten nur "
    "bei gestopptem Dienst.",
])

# ------------------------------------------------------------------ 9
d.h1("9  Tests")
d.table([
    ["Test", "prüft"],
    ["Durchlauf_ruft_den_Abgleich_auf_…", "Tür OFFEN gespeichert, Ereignis door_opened, Cache gefüllt"],
    ["Ohne_Zugangsdaten_wird_Tuya_nicht_gefragt", "kein Aufruf, kein Fehler"],
    ["Tuya_nicht_erreichbar_wird_geloggt_statt_geworfen", "Warnung im Log, kein Secret im Log"],
    ["Unerwarteter_Fehler_wird_geloggt_statt_geworfen", "Fehler im Log, Dienst läuft weiter"],
    ["Backoff_verdoppelt_bis_hoechstens_eine_Stunde", "60 → 120 → 240 … → 3600 s"],
    ["Null_heisst_aus", "AutoSyncSeconds 0: Dienst endet sofort"],
    ["Laeuft_im_Takt_wird_bei_Fehlern_langsamer_…", "Takt, Backoff, sauberes Stoppen mit Test-Uhr"],
    ["StatusCacheModeTests (3)", "10 × /api/status: 0 Tuya-Aufrufe mit Abgleich, 1 ohne"],
], [0.5, 0.5])
d.p("Die Testfabrik schaltet den Hintergrund-Abgleich fest aus (" + M + "Tuya:AutoSyncSeconds = 0" + E + "). "
    "Sonst würde er beim Start der Test-API mit den echten User Secrets die echte Tuya-Cloud fragen.")

# ------------------------------------------------------------------ 10
d.h1("10  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Was ist ein BackgroundService?", "Code, der dauerhaft neben den HTTP-Anfragen im Backend läuft, mit der App "
     "startet und stoppt."],
    ["Warum PeriodicTimer statt Task.Delay?", "Fester Takt, als await lesbar, und mit TimeProvider testbar."],
    ["Warum ein eigener Scope?", "Der Dienst ist Singleton, DbContext und Syncer sind scoped."],
    ["Warum Tür 3, Rest 30 Minuten?", "Die Tür ist am wichtigsten. Zusammen ~20 500 von 26 000 Aufrufen/Monat."],
    ["Was ist Backoff?", "Bei Fehlern seltener fragen (× 2, höchstens 1 h), damit eine gestörte Cloud nicht "
     "geflutet wird."],
    ["Warum stürzt der Dienst nie ab?", "Jeder Fehler wird gefangen und geloggt; eine Exception würde sonst die "
     "ganze App beenden."],
    ["Warum kein Push?", "Mehr Aufwand (dauerhafte Verbindung, eigene Bibliothek). Polling reicht für den Anfang."],
    ["Kosten die LEDs noch Kontingent?", "Nein, sie lesen den Speicher, den der Abgleich füllt."],
], [0.38, 0.62])

d.h1("11  Merksätze")
d.merksatz("• Erst das Kontingent rechnen, dann den Takt festlegen, und dabei an alle Aufrufer denken.<br/>"
           "• BackgroundService + PeriodicTimer = regelmäßige Arbeit im Backend.<br/>"
           "• Scoped Dienste im Singleton nur über IServiceScopeFactory, ein Scope pro Durchlauf.<br/>"
           "• Ein Hintergrund-Dienst darf nie abstürzen: Fehler fangen, loggen, Backoff.<br/>"
           "• Polling ist einfach, aber verzögert und sieht kurze Änderungen nicht. Push ist genau, aber aufwendig.")

d.save("docs/Lernunterlage-Hintergrund-Abgleich.pdf")
