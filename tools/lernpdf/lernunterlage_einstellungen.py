"""Erzeugt docs/Lernunterlage-Einstellungen.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_einstellungen.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Einstellungen",
    "Lernunterlage: Benutzerverwaltung, der letzte Admin, Passwort ändern und Sitzungen beenden",
    footer="Home Control · Einstellungen · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("Die Seite <b>Einstellungen</b> (nur Admin) bündelt, wofür man sonst ins Terminal müsste: Benutzer verwalten, "
    "das eigene Passwort ändern, den Zustand der Verbindungen zu Tuya und Blink verstehen, den Abgleich-Takt je "
    "Gerät einstellen und sehen, welche Version mit welcher Datenbank läuft.")
d.table([
    ["Bereich", "Endpunkt", "Wer"],
    ["Benutzer", "GET /api/users, PUT /api/users/{id}/role, DELETE /api/users/{id}", "Admin"],
    ["Benutzer anlegen", "POST /api/auth/register (mit Rolle)", "Admin"],
    ["Mein Passwort", "POST /api/auth/password", "jeder mit JWT"],
    ["Verbindungen", "GET /api/status, GET /api/blink/status", "angemeldet"],
    ["Abgleich", "GET /api/devices, PUT /api/devices/{id}", "Admin"],
    ["Info", "GET /api/system/info", "Admin"],
], [0.2, 0.6, 0.2])

# ------------------------------------------------------------------ 2
d.h1("2  Regeln der Benutzerverwaltung")
d.table([
    ["Regel", "Warum", "Antwort"],
    ["nur Admin", "Rechte vergeben ist die mächtigste Aktion", "401 / 403"],
    ["nicht sich selbst löschen oder herabstufen", "Schutz vor Aussperren, das macht ein anderer Admin", "409"],
    ["der letzte Admin bleibt", "ohne Admin kann niemand mehr verwalten", "409"],
    ["ungültige Rolle", "nur Admin oder User", "400"],
], [0.36, 0.46, 0.18])
d.p("<b>Welcher Statuscode?</b> 400 heißt „die Anfrage ist falsch“ (z. B. Rolle „Chef“). 403 heißt „du darfst das "
    "nicht“. <b>409 Conflict</b> heißt „die Anfrage ist in Ordnung, passt aber nicht zum aktuellen Zustand“: Jemand "
    "zum User machen ist erlaubt, nur nicht, wenn er der letzte Admin ist.")
d.merksatz("Die Oberfläche sperrt Knöpfe nur zur Bequemlichkeit. Ob etwas erlaubt ist, entscheidet immer der Server.")

# ------------------------------------------------------------------ 3
d.h1("3  Der letzte Admin")
d.p("Die Prüfung „gibt es noch einen anderen Admin?“ und das Ändern danach sind zwei Schritte. Laufen zwei "
    "Anfragen genau gleichzeitig, könnten beide „ja, es gibt noch einen“ sehen, und am Ende gibt es keinen mehr. "
    "Deshalb läuft immer nur eine Änderung gleichzeitig:")
d.code("""private static readonly SemaphoreSlim AdminLock = new(1, 1);

await AdminLock.WaitAsync(cancellationToken);
try
{
    if (isAdmin && await CountAdminsAsync() <= 1) return UserChangeStatus.LastAdmin;
    // ... Rolle wechseln
}
finally { AdminLock.Release(); }""")
d.p("<b>Altes Token:</b> Das JWT enthält die Rolle und gilt bis zu 15 Minuten. Wer gerade herabgestuft wurde, "
    "hat also noch kurz ein Token mit „Admin“. Ein Test prüft genau das: Auch mit so einem Token lässt sich der "
    "letzte Admin nicht löschen, weil der Server in der Datenbank zählt und nicht dem Token glaubt.")

# ------------------------------------------------------------------ 4
d.h1("4  Passwort ändern")
d.bullets([
    "Das <b>bisherige Passwort</b> muss stimmen. Wer nur kurz an einem fremden, angemeldeten Rechner sitzt, kann "
    "das Passwort so nicht ändern.",
    "Die Regeln für das neue Passwort prüft Identity: " + M + "userManager.ChangePasswordAsync(user, alt, neu)" + E + ".",
    "Wer, steht im Token (" + M + "caller.GetUserId()" + E + "), nie im Body. Sonst könnte jeder das Passwort eines "
    "anderen ändern.",
    "<b>Rate Limit</b> wie beim Login: Mit einem gestohlenen Access Token soll niemand das alte Passwort beliebig oft "
    "durchprobieren.",
    "Danach sind <b>alle anderen Geräte abgemeldet</b>, z. B. ein vergessenes Tablet. Das aktuelle bleibt: Der "
    "Endpunkt liegt unter " + M + "/api/auth" + E + ", dorthin schickt der Browser das Refresh-Cookie mit, und "
    "genau dieses Token bleibt erhalten.",
])

# ------------------------------------------------------------------ 5
d.h1("5  Der Fehler mit der Reuse Detection")
d.p("Zur Erinnerung: Wird ein schon <b>widerrufenes</b> Refresh-Token noch einmal benutzt, hält der Server es für "
    "gestohlen und beendet vorsichtshalber <b>alle</b> Sitzungen des Benutzers (Reuse Detection).")
d.table([
    ["Schritt", "Erste Fassung (widerrufen)", "Jetzt (löschen)"],
    ["Laptop ändert Passwort", "Handy-Token wird widerrufen", "Handy-Token wird gelöscht"],
    ["Handy meldet sich", "widerrufenes Token → Reuse Detection", "unbekanntes Token → 401"],
    ["Folge", "auch der Laptop ist abgemeldet", "nur das Handy ist abgemeldet"],
], [0.26, 0.4, 0.34])
d.p("Gefunden hat das ein Test mit zwei Sitzungen (Handy und Laptop), nicht das Ausprobieren von Hand. Lehre: "
    "Neue Funktionen immer zusammen mit den <b>vorhandenen</b> Sicherheitsmechanismen testen.")

# ------------------------------------------------------------------ 6
d.h1("6  Verbindungen und Info")
d.bullets([
    "<b>Verbindungen</b> lesen dieselben Werte wie die Status-LEDs. Es gibt keine zusätzlichen Aufrufe bei Tuya "
    "oder Blink. Jeder Zustand hat einen Satz „was heißt das, was tun?“. Bei Blink „Neu anmelden nötig“ stehen die "
    "drei Schritte mit BlinkSpike direkt auf der Seite.",
    "<b>Abgleich-Tabelle:</b> Takt direkt in der Tabelle ändern. Gespeichert wird über denselben Endpunkt wie beim "
    "Bearbeiten eines Geräts (" + M + "deviceToInput" + E + " gibt es nur einmal, in api/devices.ts).",
    "<b>Version und Commit:</b> Das .NET-SDK schreibt beim Bauen " + M + "1.0.0+&lt;Commit&gt;" + E + " in die "
    "Assembly (InformationalVersion). Ohne Git, z. B. im Docker-Build, fehlt der Commit.",
    "<b>Datenbank:</b> nur Dateiname und Größe, <b>nie der Pfad</b>: Der enthielte den Benutzernamen des Rechners, "
    "und die Seite steht vielleicht auf dem Beamer.",
])

# ------------------------------------------------------------------ 7
d.h1("7  Tests")
d.table([
    ["Test", "prüft"],
    ["Benutzerliste_nur_fuer_Admin", "403 für User, Liste mit Rollen"],
    ["Rolle_aendern_und_zuruecksetzen", "204, ungültige Rolle 400"],
    ["Sich_selbst_loeschen_oder_herabstufen_geht_nicht", "409"],
    ["Der_letzte_Admin_bleibt_auch_mit_altem_Token_…", "409, Server zählt in der Datenbank"],
    ["Geloeschter_Benutzer_kann_sich_nicht_mehr_anmelden", "Löschen wirkt, zweites Löschen 404"],
    ["Passwort_aendern_prueft_das_alte_und_die_Regeln", "Fehler an currentPassword bzw. newPassword"],
    ["Neues_Passwort_gilt_und_andere_Sitzungen_werden_beendet", "Handy 401, Laptop bleibt angemeldet"],
    ["Info_nennt_Umgebung_und_Datenbank_ohne_Pfad_…", "kein / im Dateinamen"],
], [0.6, 0.4])

# ------------------------------------------------------------------ 8
d.h1("8  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Kann ich mich aussperren?", "Nein: Das eigene Konto lässt sich nicht löschen oder herabstufen, der letzte Admin bleibt."],
    ["Warum 409 und nicht 400?", "Die Anfrage ist richtig, passt aber nicht zum Zustand (letzter Admin)."],
    ["Was ist mit offenen Sitzungen?", "Nach Passwort- oder Rollenwechsel werden sie beendet, das JWT läuft nach 15 min ab."],
    ["Warum das alte Passwort abfragen?", "Damit niemand an einem fremden, angemeldeten Rechner das Passwort ändert."],
    ["Was hat die Reuse Detection damit zu tun?", "Widerrufene Tokens lösen sie aus, deshalb werden sie hier gelöscht."],
    ["Woher kommt der Commit?", "Aus der InformationalVersion, die das .NET-SDK beim Bauen setzt."],
    ["Warum kein Pfad zur Datenbank?", "Er verriete den Benutzernamen des Rechners."],
], [0.36, 0.64])

d.h1("9  Merksätze")
d.merksatz("• Rechte prüft der Server, die Oberfläche blendet nur aus.<br/>"
           "• Immer einen Admin übrig lassen, und das Prüfen und Ändern gemeinsam absichern (Sperre).<br/>"
           "• Wer etwas tut, steht im Token, nie im Body.<br/>"
           "• 400 falsch, 403 verboten, 409 passt nicht zum Zustand.<br/>"
           "• Neue Funktionen zusammen mit den vorhandenen Schutzmechanismen testen (Reuse Detection).")

d.save("docs/Lernunterlage-Einstellungen.pdf")
