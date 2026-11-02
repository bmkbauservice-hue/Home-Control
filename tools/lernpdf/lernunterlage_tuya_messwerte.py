"""Erzeugt docs/Lernunterlage-Tuya-Messwerte.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_tuya_messwerte.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Tuya-Messwerte",
    "Lernunterlage: Watt, Volt und Akku – von der rohen Tuya-Zahl bis zur Anzeige in der Kachel",
    footer="Home Control · Tuya-Messwerte · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Was neu ist")
d.bullets([
    "<b>Geräte-Kachel:</b> neue Zeilen „Messwerte“ (z. B. „0,0 W · 227 V“ oder „Akku 83 %“) und „Gemessen“ "
    "mit Uhrzeit. Ist das Gerät offline, steht dahinter „(Gerät offline, letzter Stand)“.",
    "<b>Raumkarte auf der Übersicht:</b> Zustand und Messwerte in einer Zeile, z. B. „ZU · Akku 83 %“.",
    "<b>Warnung:</b> Ab 20 % Akku wird der Wert gelb. So tauscht man die Batterie, bevor der Türsensor ausfällt.",
    "<b>Ausgelöst</b> wird alles durch „Mit Tuya abgleichen“ auf der Geräteseite (" + M + "POST /api/tuya/sync" + E + ").",
])

# ------------------------------------------------------------------ 2
d.h1("2  Rohwerte und Skalierung")
d.p("Tuya schickt Messwerte als <b>ganze Zahlen</b>. Wie viele Nachkommastellen gemeint sind, sagt ein "
    "<b>Skalierungsfaktor</b>. Das spart Datenmenge und vermeidet Rundungsfehler, die bei Kommazahlen in JSON "
    "entstehen können. Umrechnen muss der Empfänger, also wir.")
d.p("Die Tabelle zeigt die echten Rohwerte unserer Geräte vom 02.11.2026 (" + M + "./tuya-rohwerte-anzeigen.sh" + E + "):")
d.table([
    ["Code", "Rohwert", "Rechnung", "Anzeige", "Wie sicher?"],
    ["cur_voltage", "2271", "2271 ÷ 10", "227 V", "geprüft: passt zur Netzspannung"],
    ["cur_power", "0", "Wert ÷ 10", "0,0 W", "Tuya-Konvention, mit bekannter Last prüfen"],
    ["battery_percentage", "83", "unverändert", "Akku 83 %", "geprüft"],
    ["cur_current", "0", "–", "nicht angezeigt", "Milliampere, für uns ohne Mehrwert"],
    ["va_temperature", "–", "–", "–", "kein Gerät liefert ihn bisher"],
], [0.22, 0.12, 0.16, 0.16, 0.34])
d.merksatz("Den Faktor nie raten, sondern an echten Daten prüfen. Ein falscher Faktor zeigt 2271 V oder "
           "22,7 V an, und keiner merkt es, solange niemand hinschaut.", warn=True)

# ------------------------------------------------------------------ 3
d.h1("3  Der Weg eines Messwerts")
d.code("""Steckdose ──WLAN──▶ Tuya-Cloud          (merkt sich den letzten Stand)
                       │
POST /api/tuya/sync    ▼
TuyaDeviceSyncer  ── GET /v1.0/devices/{id}/status ──▶  [{code:"cur_voltage", value:2271}, …]
       │
       ├─ Online? (Geräteliste aus dem 60-s-Cache, derselbe wie für die LEDs)
       ├─ ApplyMeasurements: 2271 ÷ 10 → VoltageVolt = 227.1, MeasuredAtUtc = jetzt
       └─ SaveChanges → Tabelle Devices
                       │
GET /api/devices       ▼
Frontend  measurementsOf(device) → "227 V"  → Kachel und Raumkarte""")

# ------------------------------------------------------------------ 4
d.h1("4  Der Code im Backend")
d.h2("Zahlen sicher lesen")
d.code("""public static long? GetNumber(List<TuyaStatusItem> status, string code)
{
    var item = status.FirstOrDefault(s => s.Code == code);
    if (item is null || item.Value.ValueKind != JsonValueKind.Number)
        return null;                       // fehlt, Text "83" oder true → nichts raten
    return item.Value.TryGetInt64(out var number) ? number : null;   // 12.5 → null
}""")
d.h2("Messwerte übernehmen")
d.code("""if (GetNumber(status, VoltageCode) is { } voltage && voltage >= 0)
{
    device.VoltageVolt = voltage / VoltageScale;     // 2271 / 10.0 = 227.1
    found = true;
}
if (GetNumber(status, BatteryCode) is { } battery && battery is >= 0 and <= 100)
{
    device.BatteryPercent = (int)battery;
    found = true;
}
if (found) device.MeasuredAtUtc = timestamp;""")
d.bullets([
    "<b>" + M + "is { } voltage" + E + "</b> ist ein Muster (Pattern Matching): „ist nicht null, und nenne den "
    "Wert voltage“. Spart eine eigene Zeile mit " + M + ".Value" + E + ".",
    "<b>" + M + "battery is >= 0 and <= 100" + E + "</b> prüft den Bereich in einem Ausdruck. Ein Akku mit 150 % "
    "ist ein Fehler, kein Messwert.",
    "<b>Fester Faktor als Konstante</b> (" + M + "PowerScale = 10" + E + "), nicht als Zahl mitten im Code. So "
    "steht an einer Stelle mit Kommentar, woher er kommt.",
])

# ------------------------------------------------------------------ 5
d.h1("5  Zustand oder Ereignis?")
d.table([
    ["", "Ereignis (DeviceEvents)", "Letzter Stand (Spalten am Gerät)"],
    ["Beispiel", "Licht an, Tür auf", "123,4 W, Akku 83 %"],
    ["Wann gespeichert", "nur bei einem Wechsel", "bei jedem Abgleich überschrieben"],
    ["Verlauf", "ja, mit Zeitpunkt", "nein, nur der neueste Wert"],
    ["Warum so", "Wechsel sind die Aussage („wann war die Tür offen?“)", "Watt schwankt ständig, der Verlauf "
     "wäre voller Einträge ohne Aussage"],
], [0.18, 0.41, 0.41])
d.p("Ein Verlauf für Diagramme („Verbrauch pro Tag“) ist bewusst <b>nicht</b> gebaut. Dafür bräuchte es eine "
    "eigene Tabelle und einen Hintergrunddienst, der regelmäßig abfragt. Das ist ein eigener Schritt.")

# ------------------------------------------------------------------ 6
d.h1("6  Offline-Geräte: der letzte Stand ist nicht neu")
d.p("Ist eine Steckdose offline, antwortet die Tuya-Cloud trotzdem, nur eben mit dem <b>zuletzt bekannten</b> "
    "Stand. Würden wir den übernehmen, stünde „gemessen um 11:53“ an einem Wert von gestern.")
d.code("""var online = apply ? await LoadOnlineAsync(ct) : null;      // eine Abfrage für alle Geräte
...
var measured = online != false && ApplyMeasurements(device, status, timestamp);""")
d.bullets([
    "<b>" + M + "online != false" + E + "</b> bei " + M + "bool?" + E + ": true und null (unbekannt) gehen durch, "
    "nur ein sicheres „offline“ sperrt. Ist die Geräteliste nicht abrufbar, wird gespeichert wie bisher.",
    "<b>Kein Zusatz-Kontingent:</b> Der Online-Zustand kommt aus dem " + M + "TuyaOnlineCache" + E + " der "
    "Status-LEDs (60 Sekunden). Zwei Funktionen teilen sich einen Aufruf.",
    "<b>Die Oberfläche</b> zeigt bei roter LED „(Gerät offline, letzter Stand)“. Ehrlich statt hübsch.",
])

# ------------------------------------------------------------------ 7
d.h1("7  Migration: neue Spalten")
d.p("Neue Eigenschaften am Modell " + M + "Device" + E + " brauchen neue Spalten. Erzeugt mit "
    + M + "dotnet ef migrations add TuyaMesswerteErgaenzt" + E + ", angewendet beim Start:")
d.code("""ALTER TABLE "Devices" ADD "BatteryPercent" INTEGER NULL;
ALTER TABLE "Devices" ADD "MeasuredAtUtc"  TEXT    NULL;
ALTER TABLE "Devices" ADD "PowerWatt"      REAL    NULL;
ALTER TABLE "Devices" ADD "VoltageVolt"    REAL    NULL;""")
d.bullets([
    "<b>Alle nullable</b> (" + M + "double?" + E + ", " + M + "int?" + E + "): Bestehende Geräte bekommen "
    "einfach null. Kein Datenverlust, keine Standardwerte, die wie echte Messungen aussehen.",
    "<b>Abwärtskompatibel:</b> Der alte Dienst lief weiter, obwohl die Datenbank schon die neuen Spalten hatte. "
    "EF Core liest nur die Spalten, die es kennt.",
    "<b>SQLite</b> kennt kein DateTimeOffset, EF Core speichert es als Text (" + M + "TEXT" + E + ").",
    "<b>Neu verknüpft</b> = Messwerte zurück auf null (" + M + "DeviceEndpoints" + E + "), damit kein Wert vom "
    "falschen Gerät stehen bleibt.",
])

# ------------------------------------------------------------------ 8
d.h1("8  Frontend: Zahlen auf Deutsch")
d.code("""const wattFormat = new Intl.NumberFormat('de-DE',
  { minimumFractionDigits: 1, maximumFractionDigits: 1 })

export function measurementsOf(device: Device): Measurement[] {
  const result: Measurement[] = []
  if (device.powerWatt !== null)
    result.push({ text: `${wattFormat.format(device.powerWatt)} W`, warning: false })
  ...
  if (device.batteryPercent !== null)
    result.push({ text: `Akku ${device.batteryPercent} %`,
                  warning: device.batteryPercent <= lowBatteryPercent })
  return result
}""")
d.bullets([
    "<b>Intl.NumberFormat('de-DE')</b> schreibt 123,4 statt 123.4. Das Objekt wird einmal außerhalb der "
    "Funktion erzeugt, nicht bei jedem Render neu.",
    "<b>Reine Funktion ohne React</b> in " + M + "components/measurements.ts" + E + ": Kachel und Raumkarte "
    "benutzen dieselbe Logik, Formatierung steht nur an einer Stelle.",
    "<b>!== null statt Wahrheitswert:</b> " + M + "if (device.powerWatt)" + E + " wäre bei 0 W falsch, weil 0 in "
    "JavaScript als false gilt. Die Steckdose liefert aber gerade 0 W.",
])
d.merksatz("Falle in JavaScript: 0, '' und NaN sind „falsy“. Wer auf vorhandene Werte prüft, schreibt "
           "!== null (oder != null), sonst verschwindet die Anzeige genau bei 0.", warn=True)

# ------------------------------------------------------------------ 9
d.h1("9  Tests und ein Fehler, der lehrreich war")
d.p("Die neuen Tests benutzen genau die echten Rohwerte, z. B. Barlicht mit " + M + "cur_voltage = 2271" + E
    + " → 227,1 V und nur einem Ereignis (" + M + "turned_off" + E + "). Unplausible Werte laufen als "
    + M + "[Theory]" + E + " mit mehreren " + M + "[InlineData]" + E + ": " + M + "\"83\"" + E + " als Text, 150, "
    "-1, 12.5, true.")
d.h2("Warum drei alte Tests plötzlich rot waren")
d.p("Die Testfabrik startet die App im Development-Modus. Dort liest ASP.NET Core die <b>User Secrets des "
    "Entwicklers</b> mit. Am Morgen wurde im Dienst der Leseschutz eingeschaltet "
    "(" + M + "Auth:RequireLoginForReading = true" + E + "), und schon bekamen Tests, die „Lesen ohne Anmeldung“ "
    "erwarten, eine 401.")
d.code("""private static readonly Dictionary<string, string?> FixedSettings = new()
{
    ["Auth:RequireLoginForReading"] = "false",
    ["Agent:ApiKey"] = "",
};
builder.ConfigureAppConfiguration((_, config) => config
    .AddInMemoryCollection(FixedSettings)     // feste Werte für alle Tests
    .AddInMemoryCollection(Settings));        // Unterklasse darf überschreiben""")
d.merksatz("Tests müssen reproduzierbar sein: gleiches Ergebnis auf jedem Rechner. Alles von außen "
           "(Uhrzeit, Konfiguration, Netzwerk) legt der Test selbst fest. Bei der Konfiguration gewinnt die "
           "zuletzt hinzugefügte Quelle.")

# ------------------------------------------------------------------ 10
d.h1("10  Grenzen (ehrlich)")
d.bullets([
    "<b>Watt-Faktor</b> ist noch nicht mit einer eingeschalteten Last bestätigt (Barlicht war offline).",
    "<b>Kein Verlauf</b> der Messwerte, nur der letzte Stand.",
    "<b>Kein automatischer Abgleich:</b> Neue Werte gibt es nur nach „Mit Tuya abgleichen“.",
    "<b>An/Aus und Tür auf/zu</b> werden auch bei Offline-Geräten übernommen (dann der letzte Stand). Nur die "
    "Messwerte sind gegen alte Werte geschützt.",
    "<b>Temperatur</b> fehlt: Die Sensoren hängen nicht im Tuya-Cloud-Projekt.",
])

# ------------------------------------------------------------------ 11
d.h1("11  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Warum schickt Tuya 2271 statt 227,1?", "Ganze Zahlen plus Faktor: kleiner, ohne Rundungsfehler. Der Empfänger rechnet um."],
    ["Woher kennt ihr den Faktor?", "Aus den echten Rohwerten: 2271 kann nur 227,1 V Netzspannung sein."],
    ["Warum kein Ereignis pro Messwert?", "Ereignisse sind Wechsel. Watt schwankt dauernd, das wäre Rauschen."],
    ["Was passiert bei Offline-Geräten?", "Tuya liefert den alten Stand. Wir übernehmen ihn nicht und zeigen „letzter Stand“."],
    ["Warum sind die Spalten nullable?", "„Kein Wert“ ist etwas anderes als 0 W. null heißt: Gerät misst das nicht."],
    ["Warum !== null im Frontend?", "Weil 0 falsy ist. Sonst verschwände die Anzeige bei 0 W."],
], [0.4, 0.6])

d.h1("12  Merksätze")
d.merksatz("• Rohwert + Faktor = echter Wert. Faktor an echten Daten prüfen, nie raten.<br/>"
           "• Ereignis = Wechsel mit Zeitpunkt. Messwert = letzter Stand.<br/>"
           "• Offline heißt: Die Cloud kennt nur die Vergangenheit.<br/>"
           "• null ist nicht 0: nullable Spalten und !== null im Frontend.<br/>"
           "• Tests legen ihre Umgebung selbst fest, sonst sind sie nicht reproduzierbar.")

d.save("docs/Lernunterlage-Tuya-Messwerte.pdf")
