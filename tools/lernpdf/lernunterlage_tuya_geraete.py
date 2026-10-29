"""Erzeugt docs/Lernunterlage-Tuya-Geraete.pdf (Aufruf aus dem Repo-Hauptordner:
python3 tools/lernpdf/lernunterlage_tuya_geraete.py)."""
from pathlib import Path

from lernpdf import Doc

d = Doc(
    "Smart-Home-Geräte verbinden (Tuya)",
    "Lernunterlage: Vom Gerät in der Steckdose bis zur Anzeige in Home Control",
    footer="Home Control · Tuya-Geräte · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Der Weg eines Messwerts")
d.p("Home Control spricht nicht direkt mit deinen Geräten. Jedes Tuya-Gerät meldet seinen Zustand an die "
    "<b>Tuya-Cloud</b>, und dein Backend fragt dort nach. Der Weg:")
d.table([
    ["Schritt", "Was passiert", "Wo in Home Control"],
    ["1  Gerät", "Steckdose, Türkontakt oder Sensor misst oder schaltet", "–"],
    ["2  Hersteller-App", "Das Gerät ist in einer App eingerichtet (z. B. Smart Life) und mit deinem WLAN oder einem Gateway verbunden", "–"],
    ["3  Tuya-Cloud", "Die App-Konten und Geräte liegen in der Cloud des Herstellers", "–"],
    ["4  Cloud-Projekt", "Dein Projekt auf der Tuya Developer Platform ist mit dem App-Konto verknüpft und bekommt Zugriff auf dessen Geräte", "Access ID und Secret in den User Secrets"],
    ["5  Backend", "Signierte Anfragen (HMAC-SHA256) holen Token, Geräteliste und Status", "Tuya/TuyaClient.cs"],
    ["6  Datenbank", "Zustand wird bei einer Änderung als LastValue und Ereignis gespeichert", "Tuya/TuyaDeviceSyncer.cs"],
    ["7  Oberfläche", "Kacheln und Ereignisverlauf zeigen den gespeicherten Zustand", "frontend/src"],
], [0.17, 0.5, 0.33])
d.merksatz("<b>Merksatz:</b> Home Control liest aus der Cloud, nicht aus dem Gerät. Was dort nicht ankommt, "
           "kann Home Control nicht anzeigen, und ein erfolgreicher Abruf heißt nicht, dass das Gerät gerade online ist.")

# ------------------------------------------------------------------ 2
d.h1("2  Begriffe")
d.table([
    ["Begriff", "Bedeutung"],
    ["Tuya-Cloud", "Server des Herstellers Tuya. Sehr viele günstige Smart-Home-Marken laufen darauf, oft unter eigenem App-Namen."],
    ["Cloud-Projekt", "Dein Zugang auf der Tuya Developer Platform (Rechenzentrum Central Europe). Hat Access ID und Access Secret."],
    ["Access ID / Secret", "Zugangsdaten deines Projekts, wie Benutzername und Passwort des Backends. Geheim, nur in den User Secrets."],
    ["Geräte-ID", "Eindeutige Kennung eines Geräts in der Tuya-Cloud. Allein nutzlos, ohne Access ID und Secret."],
    ["Datenpunkt (DP) / Statuscode", "Ein einzelner Wert eines Geräts, z. B. switch_1 (an/aus) oder battery_percentage. Jedes Gerät hat seine eigenen Codes."],
    ["local_key", "Schlüssel für die lokale Steuerung im Heimnetz. Das Backend liest ihn nie und gibt ihn nie aus."],
    ["Gateway / Hub", "Zentrale für Zigbee-Geräte (z. B. der viereckige Silvercrest-Hub). Die angelernten Geräte hängen an ihm."],
], [0.25, 0.75])

# ------------------------------------------------------------------ 3
d.h1("3  Statuscodes und Skalierung")
d.p("Tuya meldet jeden Wert als Boolean, ganze Zahl oder Text. Kommazahlen gibt es nicht. Stattdessen "
    "hat eine Zahl einen <b>Skalierungsfaktor</b> (scale): Die echte Zahl ist Rohwert geteilt durch 10 hoch scale.")
d.table([
    ["Beispiel (typisch, je Gerät prüfen!)", "Typ", "Rohwert", "Bedeutung"],
    ["switch_1", "Boolean", "true", "Steckdose oder Licht ist an"],
    ["doorcontact_state", "Boolean", "true", "Türkontakt meldet: Tür ist offen"],
    ["va_temperature", "Zahl, scale 1", "215", "21,5 °C (215 geteilt durch 10)"],
    ["cur_power", "Zahl, scale 1", "1234", "123,4 W (nur bei Steckdosen mit Messung)"],
    ["battery_percentage", "Zahl, scale 0", "80", "Akku 80 %"],
], [0.32, 0.18, 0.14, 0.36])
d.p("Warum speichert Home Control Temperatur, Watt und Akku noch nicht? Weil der Faktor je Gerät anders sein kann. "
    "Wer „215“ ungeprüft als 215 °C anzeigt, zeigt Unsinn. Deshalb gilt in Home Control: <b>erst die echten Rohwerte "
    "ansehen, dann abbilden. Nichts raten.</b> Genauso wird ein Wert, der kein echter Boolean ist (Text „true“ oder die Zahl 1), "
    "nicht als „an“ gewertet.")
d.code("""
# Rohwerte aller verknüpften Geräte anzeigen (nur lesend):
./tuya-rohwerte-anzeigen.sh

# Ausgabe sieht zum Beispiel so aus:
# Steckdose Flur (Socket)
#    switch_1 = true
#    cur_power = 1234
""")

# ------------------------------------------------------------------ 4
d.h1("4  Geräte in Home Control verbinden")
d.numbered([
    "<b>Gerät in der App einrichten</b> (Smart Life oder Tuya Smart). Bei Zigbee-Geräten zuerst den Hub, dann die Geräte daran.",
    "<b>Cloud-Projekt mit dem App-Konto verknüpfen</b> (einmalig, siehe docs/TUYA.md). Danach erscheinen die Geräte in der Geräteliste.",
    "<b>Gerät in Home Control anlegen</b> (Raum, Name, Typ) und im Feld „Tuya-Gerät“ das passende Cloud-Gerät auswählen. "
    "Ein Tuya-Gerät kann nur mit einem Gerät verknüpft sein.",
    "<b>Rohwerte ansehen:</b> <font name='Mono'>./tuya-rohwerte-anzeigen.sh</font>. Das zeigt, welche Codes das Gerät wirklich liefert.",
    "<b>Abgleich auslösen:</b> <font name='Mono'>POST /api/tuya/sync</font>. Bekannte Codes (switch_1, doorcontact_state) landen in der Datenbank.",
    "Für Temperatur, Watt und Akku die Codes und den Faktor festlegen und danach die Abbildung ergänzen.",
])

d.h2("Was beim Abgleich passiert")
d.table([
    ["Situation", "Ergebnis"],
    ["Zustand hat sich geändert", "LastValue wird gesetzt, ein Ereignis (z. B. turned_on, door_opened) mit dem Abrufzeitpunkt gespeichert"],
    ["Zustand ist gleich geblieben", "Nichts wird gespeichert, kein doppelter Eintrag im Verlauf"],
    ["Code unbekannt oder kein echter Boolean", "unknownValue: nichts gespeichert, nichts geraten"],
    ["Tuya lehnt ein Gerät ab", "Fehler bei diesem Gerät, die anderen werden trotzdem abgeglichen"],
    ["Tuya-Cloud nicht erreichbar", "Nach dem ersten Fehler Abbruch, statt bei jedem Gerät auf eine Zeitüberschreitung zu warten"],
    ["Zwei Abgleiche gleichzeitig", "Eine Sperre (SemaphoreSlim) lässt nur einen zu, sonst gäbe es doppelte Ereignisse"],
], [0.38, 0.62])
d.p("<b>Warum POST und nicht GET?</b> Ein GET soll nichts verändern. Der Abgleich schreibt in die Datenbank, "
    "also ist er ein POST. Der reine Lese-Endpunkt <font name='Mono'>GET /api/tuya/linked-devices/status</font> speichert nichts.")
d.p("<b>Warum nur für angemeldete Benutzer?</b> Jedes Gerät ist ein Aufruf bei Tuya, und das Cloud-Projekt hat "
    "ein Kontingent. Ohne Anmeldung könnte jeder im Netz dein Kontingent aufbrauchen. Es gibt auch kein automatisches "
    "Polling: Zwischen zwei Abgleichen verpasste Wechsel (kurz auf, wieder zu) sieht Home Control nicht.")

# ------------------------------------------------------------------ 5
d.h2("Der Knopf „Mit Tuya abgleichen“ (Geräteseite)")
d.table([
    ["Schritt", "Was passiert"],
    ["1  Klick", "Das Frontend ruft POST /api/tuya/sync auf (mit Bearer-Token, nur angemeldete Benutzer)."],
    ["2  Backend", "TuyaDeviceSyncer fragt jedes verknüpfte Gerät einzeln bei Tuya ab und überträgt bekannte Codes."],
    ["3  Antwort", "Pro Gerät: changed, unchanged, unknownValue … oder ein Fehlertext, falls nur dieses Gerät nicht antwortet."],
    ["4  Anzeige", "Kurzmeldung (X abgeglichen, Y geändert) und die Geräteliste wird neu geladen."],
], [0.2, 0.8])
d.p("Der Abgleich läuft <b>nur auf Knopfdruck</b>, nie automatisch: Jedes Gerät ist ein Aufruf im Tuya-Kontingent. "
    "Ein einzelner Fehler stoppt die anderen Geräte nicht.")

d.h1("5  Nicht alles ist Tuya")
d.table([
    ["Gerät", "Kann Home Control es lesen?", "Warum / nächster Schritt"],
    ["WLAN- oder Zigbee-Geräte in Smart Life", "Ja, über die Tuya-Cloud", "Verknüpfen und abgleichen, wie oben"],
    ["Lidl-Geräte mit Silvercrest-Gateway", "Vermutlich, je nach App", "Lidl-Geräte laufen auf Tuya-Technik. Taucht ein Gerät nicht in der Geräteliste auf, ist es oft in einer Hersteller-App (z. B. Lidl Home) angelernt, die sich nicht mit dem Cloud-Projekt verknüpfen lässt. Dann hilft meist, es in Smart Life neu anzulernen. Das zeigt die echte Geräteliste."],
    ["Funksteckdosen (433 MHz)", "Nein", "Sie empfangen nur Befehle und melden keinen Zustand zurück. Ohne Rückmeldung gibt es nichts zu lesen."],
    ["Geräte nur in Google Home", "Noch nicht", "Eigener Anbindungsweg über Google, ein eigener späterer Schritt."],
    ["Blink-Kameras", "Eigene Anbindung", "Läuft nicht über Tuya, siehe docs/BLINK.md."],
], [0.28, 0.2, 0.52])

# ------------------------------------------------------------------ 6
d.h1("6  Grenzen")
d.bullets([
    "<b>Nur lesend:</b> Home Control schaltet nichts. Es gibt keinen Endpunkt dafür.",
    "<b>Kein Echtzeit:</b> Der Zustand ist so aktuell wie der letzte Abgleich.",
    "<b>Kontingent:</b> Pro Gerät ein Aufruf; höchstens 50 Geräte pro Abgleich.",
    "<b>Cloud-Abhängigkeit:</b> Ist die Tuya-Cloud oder dein Internet weg, gibt es keine neuen Werte.",
    "<b>Ein Backend-Prozess:</b> Die Sperre gegen doppelte Ereignisse gilt nur innerhalb einer laufenden API.",
])

# ------------------------------------------------------------------ 7
d.h1("7  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort in einem Satz"],
    ["Wie kommen die Werte in Home Control?", "Die Geräte melden an die Tuya-Cloud, das Backend fragt dort mit signierten Anfragen nach und speichert Änderungen."],
    ["Was ist ein Statuscode?", "Der Name eines einzelnen Messwerts oder Schalters eines Geräts, z. B. switch_1."],
    ["Was heißt scale?", "Skalierungsfaktor: Der echte Wert ist der Rohwert geteilt durch 10 hoch scale (215 mit scale 1 = 21,5)."],
    ["Warum werden Temperatur und Watt noch nicht gespeichert?", "Der Faktor ist je Gerät verschieden; erst die echten Rohwerte ansehen, damit nichts falsch angezeigt wird."],
    ["Warum erzeugt „Aktualisieren“ keine doppelten Einträge?", "Ein Ereignis entsteht nur, wenn sich der Zustand ändert."],
    ["Wozu die Sperre (SemaphoreSlim)?", "Zwei gleichzeitige Abgleiche würden dieselbe Änderung doppelt speichern."],
    ["Warum liest Home Control den local_key nicht?", "Er erlaubt die lokale Steuerung der Geräte; er wird nie gespeichert oder ausgegeben."],
    ["Warum nur lesend?", "Sicherheit und Einfachheit: Ohne Schalten kann niemand über Home Control Geräte bedienen."],
    ["Was ist mit Geräten, die nicht in der Liste sind?", "Meist in einer Hersteller-App angelernt, die nicht mit dem Cloud-Projekt verknüpfbar ist; in Smart Life neu anlernen."],
], [0.38, 0.62])

# ------------------------------------------------------------------ 8
d.h1("8  Merksätze")
d.bullets([
    "Home Control liest aus der <b>Tuya-Cloud</b>, nicht aus dem Gerät.",
    "Jedes Gerät hat seine eigenen <b>Statuscodes</b>. Zuerst die Rohwerte ansehen, dann abbilden.",
    "Zahlen aus Tuya haben einen <b>Skalierungsfaktor</b>: Rohwert geteilt durch 10 hoch scale.",
    "Nur ein <b>echter Boolean</b> zählt als an/aus. Alles andere ist „unbekannt“, nichts wird geraten.",
    "Ein <b>Ereignis</b> entsteht nur bei einer Änderung.",
    "Ein Abgleich schreibt, deshalb <b>POST</b>; lesen allein ist <b>GET</b>.",
    "Jeder Aufruf kostet <b>Kontingent</b>, deshalb nur für angemeldete Benutzer und ohne Polling.",
    "Funksteckdosen ohne Rückmeldung kann man nicht auslesen.",
])

out = Path(__file__).resolve().parents[2] / "docs" / "Lernunterlage-Tuya-Geraete.pdf"
d.save(str(out))
print(out)
